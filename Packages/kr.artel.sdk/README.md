# Artel SDK

## WebGL player 제외

WebGL player에서는 `Artel.Runtime`을 컴파일하지 않는다. SDK의 native WebSocket·입력 경로는
WebGL에서 지원하지 않는다. 영상 전송은 WebSocket JPEG를 사용하며 `Unity.WebRTC` 의존성은 제거했다.
입력 ILPostProcessor도 WebGL player에는 `ArtelInput` 참조를 추가하지 않는다.
WebGL을 Build Target으로 선택한 Unity Editor에서는 기존 SDK 기능을 사용할 수 있다.
별도 affordance 메타데이터·scan 어셈블리는 유지한다.

게임 코드에서 `Artel.Runtime` 타입을 직접 참조한다면 해당 코드에도
`#if UNITY_EDITOR || !UNITY_WEBGL` 조건이 필요하다. WebGL에서도 필요한 게임 로직은
SDK 참조와 분리해야 한다.

## Runtime connection

Install the package. Nothing else is required — in the editor and in
development builds `ArtelManager` spawns itself after the first scene loads, and
it adds `ArtelOverlayController`, `CursorController` and `KeyboardStatusController`
on its own. It ships pointing at the Artel servers.

Release builds carry none of this. The spawn and the scan are compiled out
(`#if UNITY_EDITOR || DEVELOPMENT_BUILD`), so a game shipped to players holds no
subscription and no callback of ours.

기본 서버는 `https://stage-orch.artel.kr`이며 주소 입력 없이 로그인한다.
다른 서버를 사용하려면 로그인 화면의 **고급 설정**을 열고 **서버 주소**에
`https://artel.example.com` 또는 `http://localhost:8080`을 입력한 뒤 **적용**을 누른다.
**ARTEL 서버로 복원**으로 기본 서버를 다시 선택할 수 있다. 고급 메뉴의 **서버 변경**으로 다른 서버를 선택할 수 있다.
Inspector에서는 `Server URL` 하나를 설정한다. 기존 씬의 `secure`·`host`·`port` 값은
새 URL이 없을 때 읽으며, HTTP와 WebSocket 주소는 같은 URL에서 만든다.
경로·query·fragment·사용자 정보가 붙은 URL은 지원하지 않는다.

console 주소는 서버의 `ARTEL_HOME_URL`에서 관리한다. 로그인 버튼은
`<server>/api/auth/sdk/login`을 열고 서버가 console의 `/sdk-login`으로 redirect한다.
기존 loopback callback과 PKCE code 교환은 유지한다. SDK는 console 주소를 저장하지 않는다.

인증 상태는 정규화된 서버 주소별로 분리된다. 다른 서버를 선택하면 그 서버의 토큰과
프로젝트를 불러오며, 원래 서버로 돌아오면 저장된 로그인을 다시 쓸 수 있다.
서버 정보가 없는 이전 버전의 로그인은 재사용하지 않으므로 한 번 다시 로그인해야 한다.

The JWT says who you are, not which instance you are. Instance identity is
carried by `sdkUuid`, the SDK's per-installation UUID. After login the person
picks a project once, and from then on the server finds or creates the instance
by `(projectId, sdkUuid)`:

```
POST /api/sdk/registrations
Authorization: Bearer <sdk token>
{ "projectId": "<id>", "sdkUuid": "<uuid>", "instanceName": "<name>", "gameVersion": "1.2.3" }
```

`gameVersion` is `Application.version` from Player Settings. The response
carries `instanceId`, `projectId`, `instanceName`, `gameBuildId` and
`gameVersion`. The SDK then connects to
`/ws/sdk?token={SDK_TOKEN}&instanceId={INSTANCE_ID}`. Every later launch
registers and connects with no interaction.

The two tokens — access and refresh — live in the OS secret store
(`ArtelSecretStore`; DPAPI on Windows, Keychain on macOS). Expiry, display name
and project sit in `PlayerPrefs` under `Artel.*`, because none of them opens
anything on its own. The per-installation UUID and the instance id of each
server live in `Application.persistentDataPath/artel/identity.json`, so a game
that calls `PlayerPrefs.DeleteAll()` keeps registering as the same instance.
Values an older SDK left in `PlayerPrefs` (`Artel.SdkId`, `Artel.InstanceId.*`)
move into that file the first time they are read.

Close code `4001` on the socket means the token or the instance was refused.
The SDK does not retry that one; the overlay's `연결` button registers again,
which refreshes the token and the instance id.

## Local PoC

This is the one case that still wants an `ArtelManager` in the scene. The SDK
spawns its own manager on a `GameObject` of its own, and `ArtelTestPageManager`
looks for the manager on its own object — so put both on the same scene object,
or assign the manager to the test page manager's field. The scene's manager
keeps the spot and the spawn steps aside. The test page manager replaces the
default client transport with its local WebSocket server and manages both test
servers:

- WebSocket URL: `ws://127.0.0.1:17311/ws`
- Scan request: `{ "jsonrpc": "2.0", "id": 1, "method": "scan_scene", "params": [] }`
- Action message: `ACTION` with `button_click`, `enter_text`, `key_click`, `key_down`,
  `key_up`, `move_mouse`, `mouse_down`, `mouse_up`, `scan_scene`, and `scan_all_scenes`

## Ordering a scan against actions

A top-level `scan_scene` answers as soon as the message arrives, so it can report
the scene while a preceding `button_click` is still moving the cursor. Put
`scan_scene` inside the `ACTION` batch instead and it runs on the same queue,
after every action ahead of it has finished:

```json
{
  "type": "ACTION",
  "id": 9,
  "actions": [
    { "id": 1, "method": "button_click", "params": [12345] },
    { "id": 2, "method": "scan_scene", "params": [] }
  ]
}
```

The batched scan sends its own `GAME_STATE` message and leaves
`{ "id": 2, "success": true }` in the `ACTION_RESULT` that follows. The top-level
request keeps working so existing clients can migrate at their own pace.

`GAME_STATE` is answered on request and nothing else produces it. The SDK used
to push one every second from a scene poller; ARTEL-400 removed that, so a run
that never sends `scan_scene` never sees a `GAME_STATE`. What changes while the
game runs is on the pulse channel — see `## State and action tracking`.

## Scanning every scene in the build

`scan_all_scenes` walks Build Settings from index 0 upward, scanning each scene,
and answers with a single `ALL_SCENES` message. It is a batch method only —
the walk spans many frames, so there is no top-level form.

```json
{ "type": "ACTION", "id": 10, "actions": [{ "id": 1, "method": "scan_all_scenes", "params": [] }] }
```

```json
{
  "type": "ALL_SCENES",
  "id": 11,
  "scenes": [
    { "buildIndex": 0, "path": "Assets/Scenes/Lobby.unity", "scene": { "id": -1234, "type": "scene", "name": "Lobby", "children": [] } }
  ]
}
```

### Full mode

`params: ["full"]` widens the same walk. It reads every field Unity would
serialize on the MonoBehaviours the game itself wrote — public fields and
`[SerializeField]` private ones — it walks into inactive objects, which come back
as blocks with `"active": false`, and it reports what each button is wired to
call. Everything else about the walk is unchanged.

**This is the only mode that reads field values.** The default scan reads none,
so its components carry no `states` key at all.

```json
{ "type": "ACTION", "id": 10, "actions": [{ "id": 1, "method": "scan_all_scenes", "params": ["full"] }] }
```

The built-in cheat defaults to `clearPlayerPrefs: false` and `resetStatics: true`.
A game can register `[ArtelOverrideCheat("reset_game", "...")]` on a static method
that takes both boolean options. The cheat registry selects the override in
Editor and development builds.

The SDK's own `Artel.*` entries are read out before the wipe and written back
after it, so a reset does not log the game out of the server that ordered it.
Everything else in the store goes — including the entries Unity itself keeps
there, such as `Screenmanager Resolution Width` / `Height`,
`Screenmanager Fullscreen mode`, and the `unity.*` analytics keys. A reset with
this flag therefore also reverts the player's window size and fullscreen choice
on the next launch. Those names are tied to the Unity version, so the SDK does
not try to preserve them: a stale allowlist would claim a protection it no longer
gives.

A cleared store is all this promises, and not that the game is in a first-run
state: a manager destroyed by the reload can write its keys back from
`OnDestroy`.

It is a batch method only — the reload spans frames. The result arrives once the
new scene has loaded and settled, so a `scan_scene` after it in the same batch
reads the fresh scene. Every target id from before is dead by then; a
`button_click` queued behind a reset must come from a scan taken after it.

A pause left by `pause_time` is lifted and every held key and mouse button is
released before the load, so the new scene starts with the clock running and
nothing pressed.

The game's `DontDestroyOnLoad` objects go with it. A manager that survives scene
loads is usually where the run's progress is kept, so leaving it would defeat the
reset; the reloaded scene builds its own through the same singleton guard that
let the old one live. The SDK is the one persistent object kept, since it is
running the reset. A game whose managers are created by a bootstrap scene the run
did not start in loses them for good — the SDK logs every object it drops, by
name, for exactly that case.

What no reload can reach: static fields and save files on disk. A game that keeps
its progress in one of those comes back holding it, whether or not
`clearPlayerPrefs` was set. The action fails, changing nothing — the store
included — when the scene the game started in is not in Build Settings, or when
the params are malformed; there is no index to return to, and a refused reset
must leave the game exactly as it found it.

## State and action tracking

### State: the pulse channel

**Reading state needs no change to the game.** There is no attribute to add and
no member to mark. The SDK samples what the game holds ten times a second and
sends a `PULSE` message on the same WebSocket on the tick a value moved, no
sooner than 0.1 s after the previous one. The two actions `start_readings` and
`stop_readings` turn it on and off, so a QA run reads only the part of the
session it is actually driving; `start_readings` takes the three rates as
options when a run wants to measure with different ones.

A member the evidence did not name can still be asked for by name while the game
runs: `watch [["Game.Player::hp", "Progress::stage"]]` resolves each name against
the loaded assemblies and adds it to the watch list, and `read_member` reads one
once. A field or auto-property is enough; a property whose getter runs code is
refused unless the game marks it `[ArtelObserve]` — name the field instead. A
member on a scene component is read where the walk meets it; a static is
read directly; anything else — a ScriptableObject, a plain object behind a static
or a container — is found by following references from static fields and scene
components, four levels deep and within a time budget, and the action's result
says which names resolved, where each is read from, and which did not and why.

A game can also say so in its own code: `[ArtelObserve]` on a field or property
puts that member on the watch list at startup, with no build step involved. A
property's getter then runs on every reading, which is the one place the pulse
runs game code, and only because the game asked for it.

Two kinds of state no member holds ride each object entry as well: `ui` carries
what uGUI accepts (`interactable`, `isOn`, slider and dropdown `value`, `fill`,
`CanvasGroup` alpha and flags) and `engine` carries what the engine is doing
(the current `Animator` state, whether an `AudioSource` or `ParticleSystem` is
playing, whether a `SpriteRenderer` is enabled and how opaque). `flush` reads and sends one
reading right now, for a check that cannot wait for the next tick. A UI Toolkit
screen has no object per element, so the object carrying the `UIDocument` lists
its named and textual elements under `uitk`.

`wait_until` waits inside the game: `["Game.Player::hp <= 0", 5]` yields every
frame until the condition holds or the seconds run out, and answers with
`{held, waitedFrames, timedOut}`. Conditions are `exists(selector)`,
`active(selector)`, `Type::member op literal`, `and`, `or` and parentheses; no
expressions. A timeout is a value, not a failure — only an unreadable condition
fails the action.

What happened, as opposed to what is, rides `events` on the same reading: the
engine calls the game made since the previous reading — a sound played, an
animator trigger set, a prefab instantiated or destroyed, a scene load, a
`PlayerPrefs` write. These are caught at the call site: the same IL
post-processor that redirects `Input` redirects those calls to `ArtelEngine`,
which makes the original call and records it. Repeats within one reading fold
into a `count`, so an `Update` that sets an animator float every frame is one
line, not sixty.

What a pulse carries is decided by the evidence document baked at build time —
the members the analysed conditions and effects name — plus the objects that
carry text on screen. Each reading names the `instanceId` of the object it came
from, which is the same number `button_click`, `move_mouse`, and the rest take,
so a reader that sees a value change can act on the thing that changed it.

`tools/watch-readings.py` reads the stream locally. The SDK also writes it to
`artel-pulse.jsonl` under the player's `Application.persistentDataPath`.

This replaced `[ArtelState]`, which required the game's own source to carry an
attribute on every member worth reading, and was removed in ARTEL-400. Nothing
in this SDK reads member values from a scan any more; `scan_all_scenes ["full"]`
reads serialized fields, but that is a build-time discovery walk, not a live
channel.

### Actions: `[ArtelAction]`

Action results are captured by IL post-processing without changing the source
class:

```csharp
using Artel.Tracking;
using UnityEngine;

public sealed class PlayerStatus : MonoBehaviour
{
    [ArtelAction("attack")]
    public int Attack(int damage)
    {
        return damage * 2;
    }
}
```

`[ArtelAction]` records method tag/name, success, return value, timestamp, and
exception type/message on failure. The original exception is rethrown.

Scan only snapshots pending actions. It does not consume them. After the
WebSocket send succeeds, the SDK removes actions included in that snapshot.
Actions recorded between scan and send remain for the next message. Failed
sends leave the entire snapshot pending.

Current limits:

- `[ArtelAction]` supports synchronous instance methods on `Component` classes.
- Async methods, iterators, and coroutines are rejected during compilation.
- Method parameters are captured as `args`: boxed at entry, Unity objects by name,
  anything Newtonsoft cannot serialize by type name; `ref`/`out` slots are null.
- Return values must be serializable by Newtonsoft.Json.
- Each component keeps at most 256 pending actions; overflow drops the oldest.

- HTTP URL: `http://127.0.0.1:17310/`
- WebSocket URL: `ws://127.0.0.1:17311/ws`

## Cheats and behaviors for the QA agent

새 메서드는 `[ArtelGameFunction]`으로 등록합니다. `ReadOnly`는 게임 상태를
바꾸지 않고 `string`을 반환하고, `Setup`은 테스트 사전조건을 준비합니다.
`Behavior`는 `IEnumerator`로 동작을 계속 실행합니다. QA Agent는 세 종류를
`call_game_function`으로 호출하며, `Behavior`는 중지와 연장에 쓸 실행 ID를 반환합니다.
기존 `[ArtelCheat]`와 `[ArtelBehavior]` 등록도 지원합니다.

```csharp
public static class DebugCheats
{
    [ArtelGameFunction("Reads current gold.", ArtelGameFunctionMode.ReadOnly, when: nameof(HasWallet))]
    static string GetGold() => $"gold={Wallet.Gold}";

    [ArtelGameFunction("Adds gold to the player's wallet.", ArtelGameFunctionMode.Setup, when: nameof(HasWallet))]
    static string GiveGold([CheatRange(1, 1_000_000)] int amount)
    {
        Wallet.Add(amount);
        return $"gold={Wallet.Gold}";
    }

    // null means available; any other string is the reason it is not.
    static string HasWallet() => Wallet.Instance != null ? null : "no wallet loaded";
}

public class PlayerController : MonoBehaviour
{
    [ArtelGameFunction("Attacks the nearest enemy until none are in range.", ArtelGameFunctionMode.Behavior, group: "combat")]
    IEnumerator AutoAttack([CheatRange(1, 20)] float range, ArtelBehaviorContext context)
    {
        try
        {
            while (true)
            {
                var enemy = Enemies.Nearest(transform.position, range);
                if (enemy == null)
                {
                    context.Complete("no enemy in range");
                    yield break;
                }

                Attack(enemy);
                context.Report($"target={enemy.name}");
                yield return null;
            }
        }
        finally
        {
            StopAttackAnimation(); // runs however the behavior ends
        }
    }
}
```

- Parameters may be `int`, `float`, `bool`, `string` or an enum. A cheat returns `void`, `string` or
  `IEnumerator`; a behavior returns `IEnumerator`. Anything else is a compile error.
- Names are the method name in snake_case (`GiveGold` → `give_gold`) unless `name:` overrides it.
- Instance methods must live on a `MonoBehaviour`; the agent picks the object with a `target`
  selector when more than one is live.
- Registration is generated at compile time in the editor and development builds. In a release
  build the attributes do nothing and the actions are refused.
- `ArtelCheats.Register` / `Unregister` add and remove cheats at runtime, for cheats built from data.

### Keep the game compiling without the SDK: `ARTEL_GAME_FUNCTIONS`

Wrap every file that uses `[ArtelCheat]`, `[ArtelBehavior]`, `[CheatRange]` or `ArtelBehaviorContext`
in `#if ARTEL_GAME_FUNCTIONS`:

```csharp
#if ARTEL_GAME_FUNCTIONS
using Artel;

public static class DebugCheats
{
    // ...
}
#endif
```

That is the only step on the game side. The package owns the define:

- Every time the editor loads scripts, `Artel.GameFunctions.Editor.ArtelDefineSync` adds
  `ARTEL_GAME_FUNCTIONS` to the scripting define symbols of Standalone, the active build target,
  and every other build target that already has defines. It writes Player Settings only when the
  list changes.
- When the package is removed or changes version through the Package Manager while the editor is
  open, it removes every `ARTEL_` define before the change is applied, so the wrapped code drops
  out and the next compile succeeds. The new version, if any, adds its own defines back.
- An SDK without cheats, or a project without the SDK, never defines it, so the wrapped files are
  skipped and the game builds.

`ARTEL_` is reserved for this package. On every load it also removes `ARTEL_` defines that this
version does not know, which a newer version may have left behind. Do not define your own
`ARTEL_*` symbols; defines without the prefix are never touched.

The define lives in `ProjectSettings/ProjectSettings.asset`, and Unity runs no package code while
a script has compile errors. Two cases therefore need a manual fix: the SDK removed by editing
`Packages/manifest.json` with the editor closed, and a committed `ARTEL_GAME_FUNCTIONS` reaching a
machine whose SDK has no cheats. Delete `ARTEL_GAME_FUNCTIONS` once in **Project Settings > Player >
Other Settings > Scripting Define Symbols**, or update the SDK.

Games that put this code in their own assembly definition can use the asmdef's **Version
Defines** (`kr.artel.sdk`) and **Define Constraints** instead; `Assembly-CSharp` has no asmdef, so
the scripting define is what covers it.

The wire shapes and failure messages are in [`docs/protocol.md`](../../docs/protocol.md); the
reasoning is in [ADR 0007](../../docs/adr/0007-game-registered-cheats-and-behaviors.md).

## Running while the window is not focused

**The SDK changes one of the host game's global settings.** From the moment it
opens its own connection to the orchestration server until that connection is
stopped, it sets `Application.runInBackground` to `true`, then puts the previous
value back. That covers the time the socket is down or retrying, not only the
time it is up. A build that never connects to Artel is left with whatever its
own Player Settings say.

QA 실행에 필요한 JPEG capture·전송 상태 확인과 수신 메시지 처리는 `Update()`에서 돈다.
Player Settings 기본값으로는 게임 창이 focus를 잃을 때 이 작업들이 멈출 수 있다.
브라우저로 전환해도 영상과 제어가 계속되도록 연결을 소유하는 동안 background 실행을 유지한다.

Performance reports also continue while the window is not focused. Their frame
statistics include the background frames, which may reflect platform
throttling; `status.isFocused` remains available so consumers can identify
those reports.

The setting is a desktop one. On mobile the OS suspends the app regardless, so
the stream stops there while the game is in the background. What the SDK
guarantees on the way back is that the session survives the resume: the stream
lease is spent frame by frame and refuses to charge a single frame more than a
second, so a stretch in which the process was not running is not mistaken for a
viewer who went away. A viewer who really did go away still stops the stream —
once the app is running again, the lease runs out normally and the session is
torn down.

## Included dependencies

The SDK vendors `websocket-sharp` under `Runtime/Plugins`. It uses Unity's
`com.unity.nuget.newtonsoft-json` for protocol serialization and
`com.unity.nuget.mono-cecil` for Editor-only IL post-processing.
