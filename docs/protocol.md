# Protocol reference

- SDK 와 orchestration 이 `/ws/sdk` 위에서 주고받는 것의 표
- 왜 이런 모양인지는 [`docs/adr/`](adr/README.md)
- 핸드셰이크는 `wss://<host>/ws/sdk?token=<sdk token>&instanceId=<instance id>`

## Actions

- `ACTION` 메시지 하나가 `actions` 배열을 나름
- 배열은 한 큐에서 직렬로 돎
- 항목 하나의 모양은 `{ "id": <int>, "method": <string>, "params": [...] }`

```mermaid
sequenceDiagram
    participant O as orchestration
    participant M as ArtelManager
    participant X as ActionExecutor

    O->>M: ACTION · actions 배열
    M->>M: EnqueueAction → ProcessActions coroutine
    loop 배열의 action 하나마다
        M->>X: Execute · id, method, params
        X-->>M: ActionResultDto
        Note over M,X: action 마다 yield return — 최소 한 프레임
    end
    M-->>O: ACTION_RESULT · 배치 하나에 한 번
```

- `ACTION_RESULT` 의 `Frame` 은 배치를 받은 프레임이 아니라 마지막 action 이 끝난 프레임 (ARTEL-620)
- `RequestId` 로 어느 `ACTION` 에 대한 답인지 알림. `Id` 는 이 메시지 자신의 번호라 그 자리에 못 씀

`ActionExecutor` 가 처리하는 21 개.

| method | params | 비고 |
| --- | --- | --- |
| `button_click` | `[targetId]` | **deprecated.** `onClick` 을 `EventSystem` 없이 직접 부른다. 이 저장소 밖에서 아직 보내므로 남아 있다 |
| `enter_text` | `[targetId, value]` | `InputField` 와 `TMP_InputField` |
| `move_mouse` | `[x, y]` · `[instanceId]` · `[selector]` | 좌표는 스캔이 보고한 좌상단 원점 픽셀 |
| `mouse_down` | `[]` 또는 `[button]` (0·1·2) | 한 프레임 뒤 `returnValue` 에 `reached` 와 `pointerHeldByPerson` |
| `mouse_up` | `[]` 또는 `[button]` | |
| `key_click` | `[keyCode, positiveDurationSeconds]` | |
| `key_down` | `[keyCode]` | `KeyCode.Mouse0`~`Mouse2` 는 마우스 경로로 간다 |
| `key_up` | `[keyCode]` | |
| `set_axis` | `[axisName, valueBetweenMinusOneAndOne]` | Input Manager 축 이름 |
| `set_button` | `[axisName, pressed]` | 놓으면 실제 입력에 축을 돌려준다 |
| `pause_time` | `[]` | `Time.timeScale = 0`. SDK 자신은 unscaled 로 돈다 |
| `resume_time` | `[]` | `pause_time` 이 없었으면 실패 |
| `reset_game` | `[]` 또는 `[{ "clearPlayerPrefs": bool }]` | 시작 씬을 다시 연다 |
| `start_readings` | `[]` | `pulse` 를 켠다. 여러 번 불러도 같음 |
| `stop_readings` | `[]` | 돌고 있었든 아니든 성공 |
| `capture_screen` | `[]` · `[targetId]` · `[targetId, options]` | 전체는 JPEG, 잘라내기는 PNG |
| `scan_evidence` | `[]` | 출시 빌드에서는 거절. `returnValue` 는 아래 |
| `invoke_cheat` | `[{ "name", "args"?, "target"? }]` | 게임이 등록한 cheat. 출시 빌드에서는 거절. 아래 |
| `start_behavior` | `[{ "name", "args"?, "target"?, "maxSeconds"? }]` | 게임이 등록한 behavior 를 시작한다. 아래 |
| `stop_behavior` | `[{ "behaviorId", "reason"? }]` · `[{ "all": true, "reason"? }]` | |
| `extend_behavior` | `[{ "behaviorId", "seconds" }]` | |

`ArtelManager` 가 배치 안에서 직접 처리하는 둘.

| method | params | 비고 |
| --- | --- | --- |
| `scan_scene` | `[]` | `GAME_STATE` 를 보내고 성공을 적는다 |
| `scan_all_scenes` | `[]` 또는 `["full"]` | 씬을 하나씩 load·unload 한다. `ALL_SCENES` 를 보낸다 |

- `scan_scene` 은 최상위 메시지로도 옴. 그 경로는 도착 즉시 답하므로, 앞선 조작이 아직 끝나지
  않았는데 씬을 보고할 수 있음 — 배치 안에 넣으면 큐에서 제 차례를 기다림

### capture_screen 기본값

| 값 | 상수 | 크기 |
| --- | --- | --- |
| 전체 화면 최장변 | `FullScreenMaxEdge` | 1024 |
| 잘라내기 최장변 | `CropMaxEdge` | 512 |
| 잘라내기 여백 | `DefaultPadding` | 8 px |
| JPEG 품질 | `JpegQuality` | 70 |

### scan_evidence 의 returnValue

| 필드 | 뜻 |
| --- | --- |
| `objectKey` | 올라간 문서의 키 |
| `evidenceDigest` | 문서 내용의 digest |
| `byteSize` | 바이트 수 |
| `schemaVersion` | 문서의 schema version |
| `sceneCount` | 스캔이 본 씬 수 |
| `sceneCapturesRegistered` | 등록에 실린 씬 화면 수. 못 찍은 것과 못 올린 것도 사실로 세므로 씬 수보다 작을 수 있다 |
| `alreadyRegistered` | 같은 문서가 이미 등록돼 있었는가 |

### cheat 와 behavior

- 게임 개발자가 `[ArtelCheat]`·`[ArtelBehavior]` 를 단 메서드, 또는 `ArtelCheats.Register` 로 더한 cheat 다.
  왜 ADR 0002 의 예외인지는 [ADR 0007](adr/0007-game-registered-cheats-and-behaviors.md)
- 모양의 기준은 워크스페이스의 `.plan/general/2026-10-14-game-functions-wire-contract.md` (ARTEL-1051) 이고,
  아래는 SDK 가 그것을 어떻게 지키는지다

| method | `returnValue` |
| --- | --- |
| `invoke_cheat` | `{ "message": string \| null }` — cheat 가 돌려준 문자열, coroutine cheat 면 마지막으로 `yield return` 한 문자열 |
| `start_behavior` | `{ "behaviorId": int, "report": string \| null }` — 첫 `MoveNext()` 뒤의 report |
| `stop_behavior` | `{ "stopped": [int] }` |
| `extend_behavior` | `{ "maxSeconds": number }` |

검사 순서와 실패 문장. 앞부분은 계약이 정한 그대로다.

| 순서 | 실패 |
| --- | --- |
| 1. 빌드 | `cheats are not available in this build` · `behaviors are not available in this build` |
| 2. 이름 | `unknown cheat: <name>` · `unknown behavior: <name>` |
| 3. 대상 | `<name> is static and takes no target` · `Nothing at selector: <selector>` · `<Component> is not on <selector>` · `no active <Component>` · `<n> <Component> objects; pass target as one of: ...` (5 개까지) |
| 4. 인자 | `bad argument <param>: <why>` — 타입, 범위, 모르는 키, 빠진 필수 인자. `maxSeconds` 도 여기 |
| 5. `when` | `unavailable: <reason>` |
| 6. 상한 | `too many behaviors running: <names>` (4 개. 같은 `group` 이라 바뀔 것은 세지 않는다) |
| 실행 중 | `<ExceptionType>: <message>` (cheat 가 던짐) |

- `target` 을 빼면 static 함수는 그대로, instance 함수는 살아 있는 instance 가 하나일 때만 그 위에서 돈다
- 인자: `int` 는 정수와 소수 부분 없는 수, `float` 는 유한한 수, `bool` 은 JSON boolean, `string` 은 JSON
  문자열, enum 은 멤버 이름 그대로. C# 기본값이 있는 parameter 만 생략할 수 있다
- `maxSeconds` 는 1–300, 기본 30. `extend_behavior` 는 시작부터 300 초를 넘기지 못한다
- `stop_behavior` 의 `reason` 은 `stopped`(기본) 또는 `not_allowed` 이고, 그대로 `BEHAVIOR_ENDED.reason` 이 된다

behavior 가 끝나는 이유.

| `reason` | 언제 |
| --- | --- |
| `stopped` · `not_allowed` | `stop_behavior` |
| `completed` | 메서드가 끝났거나 `ctx.Complete` |
| `failed` | `ctx.Fail` 이나 예외 |
| `timeout` | 게임 시간으로 `maxSeconds` 가 지남 |
| `replaced` | 같은 `group` 의 behavior 가 시작함 |
| `target_gone` | 대상 객체가 파괴됨 (씬이 내려간 경우 포함) |
| `unavailable` | `when` 이 false 가 됨. 1 초에 한 번 확인 |
| `run_ended` | `reset_game`, `RUN_STATUS` `FINISHED`, 연결 끊김 |

- 어떻게 끝나든 SDK 가 iterator 를 `Dispose()` 하므로 메서드 안의 `finally` 가 돈다
- `yield return null` 은 한 프레임, `WaitForSeconds` 는 게임 시간 그 초, 그 밖의 값은 한 프레임과 경고 한 번
- `pause_time` 중에는 걸음도 시간도 멈춘다

## Outbound messages (SDK → orchestration)

| type | 언제 |
| --- | --- |
| `ACTION_RESULT` | `ACTION` 배치 하나를 다 돈 뒤. 항목은 `{id, success, error?, action?, returnValue?}`. 배치 중 예외가 찍혔으면 `errors` 도 실린다 |
| `GAME_STATE` | 스냅샷 해시가 달라졌을 때, 그리고 `scan_scene` 에 대한 답으로. **`ArtelManager.SendsGameState` 가 기본으로 `false` 다** |
| `ALL_SCENES` | `scan_all_scenes` 를 마친 뒤 |
| `DEVICE_CONTEXT` | 연결이 열린 뒤 한 번. 끊기면 표시를 내려 다음 연결에서 다시 보낸다 |
| `GAME_FUNCTION_CATALOG` | `DEVICE_CONTEXT` 바로 뒤에 한 번, 그 뒤 목록이 바뀌면 1 초에 한 번까지. `{type, revision, cheats, behaviors}`. 출시 빌드는 보내지 않는다 |
| `BEHAVIOR_ENDED` | behavior 하나가 끝날 때 한 번. `{type, behaviorId, name, target, reason, detail, seconds, frame}`. 연결이 없을 때 끝난 것은 버린다 |
| `PERFORMANCE` | 1 초마다 (`PerformanceReportIntervalSeconds`) |
| `PULSE` | live readings 가 켜져 있고 감시 대상 값이 움직였을 때. `functions`·`behaviors`·`errors` 는 아래 |
| `STREAM_STATE` | 스트림 상태가 바뀔 때. 값은 `CONNECTING`·`LIVE`·`FAILED`·`STOPPED` |
| `WEBRTC_OFFER` | 스트림 세션이 offer 를 만들었을 때 |
| `WEBRTC_ICE` | ICE candidate 가 모였을 때 |
| `ERROR` | 알 수 없는 메시지에 대한 답 |

- `PULSE.functions`: 함수가 지금 쓸 수 있는지. `whole` 인 `pulse` 에는 전량, 그 밖에는 1 초에 한 번 평가해 바뀐
  항목과 사라진 키(`removed: true`)만 싣는다. 키는 `(kind, name, target)` 이고, instance 가 없는 instance 함수는
  `target: null` 항목 하나로 선다
- `PULSE.behaviors`: 도는 behavior 전부. behavior 가 하나라도 등록돼 있으면 모든 `pulse` 에 싣는다. 감시 대상 값이
  가만히 있어도 behavior 가 도는 동안은 1 초에 한 번 `pulse` 를 보낸다
- `PULSE.errors`: 직전 `pulse` 이후 새로 생기거나 `count` 가 늘어난 예외·Error 로그 묶음. 아래 「예외와 Error 로그」
- `PERFORMANCE` 가 나르는 지표군 이름은 서버가 정한 계약
- 지금 `MetricGroupNames.Collected()` 가 선언하는 것은 `frameTiming` 과 `editorRender` 둘

### 예외와 Error 로그

- `Application.logMessageReceived` 로 `LogType.Exception`·`Error`·`Assert` 만 모은다. `Warning` 과 `Log` 는 버린다
- 게임이 잡아서 `Debug.LogException` 으로 찍은 예외도 `Exception` 으로 들어온다
- type, message, stack 첫 줄이 같으면 한 묶음이다. 매 frame 같은 예외를 던지는 게임도 묶음 하나로 온다
- 메인 스레드에서 찍힌 로그만 모은다

항목 하나의 모양. `PULSE.errors` 와 `ACTION_RESULT.errors` 가 같다.

| 필드 | 뜻 |
| --- | --- |
| `type` | `Exception` · `Error` · `Assert` |
| `message` | 로그 문장. 200 자에서 자른다 |
| `stackHead` | stack trace 첫 줄. 비어 있으면 `null` (release build 의 `Debug.LogError` 가 대개 그렇다) |
| `firstFrame` · `lastFrame` | 이 묶음이 처음과 마지막으로 찍힌 `Time.frameCount` |
| `count` | 프로세스가 시작한 뒤의 누적 횟수 |
| `truncated` | `message` 가 잘렸을 때만 `true` 로 실린다 |

| 어디 | 무엇을 싣나 | 상한 |
| --- | --- | --- |
| `PULSE.errors` | 직전 `pulse` 이후 새로 생기거나 `count` 가 늘어난 묶음. 실을 것이 있으면 감시 대상 값이 가만히 있어도 `pulse` 가 나간다 | `pulse` 하나에 20 개. 넘친 묶음은 다음 `pulse` 로 밀린다 |
| `ACTION_RESULT.errors` | 배치를 꺼낸 frame 이후 한 번이라도 찍힌 묶음. 없으면 필드가 빠진다 | 20 개 |

- 묶음은 100 개까지 둔다. 넘치면 이미 `pulse` 로 보낸 것 중 가장 오래전에 찍힌 것부터 버리고, 그것이 다시 찍히면 새
  묶음으로 `count` 1 부터 센다
- 전량 `pulse` 에도 이미 보낸 묶음을 다시 싣지 않는다. 예외는 상태가 아니라 일어난 일이라서다. 그래서 전달에 실패한
  `pulse` 에 실렸던 묶음은 다시 찍히기 전까지 다시 오지 않는다
- live readings 가 꺼져 있는 동안 찍힌 묶음은 쌓여 있다가 켜진 뒤 첫 `pulse` 부터 나간다

## Inbound messages (orchestration → SDK)

| type | 무엇 |
| --- | --- |
| `ACTION` | action 배치 |
| `RUN_STATUS` | 이 창에서 지금 무엇이 도는지. 답을 기다리는 요청이 아니라 통지다 |
| `SCAN_SCENE` · `GET_GAME_STATE` | `GAME_STATE` 를 요청한다. JSON-RPC `method: "scan_scene"` 도 같은 자리로 온다 |
| `STREAM_START` | 스트림 세션을 연다. 살아 있는 세션이 있으면 앞선 `STREAM_STOP` 없이 갈아치운다 |
| `STREAM_RENEW` | lease 를 늘린다 |
| `STREAM_STOP` | 세션을 닫는다 |
| `WEBRTC_ANSWER` | offer 에 대한 답 |
| `WEBRTC_ICE` | 상대의 ICE candidate |

## 소비자가 빠지는 함정 둘

### `onScreen=true` 는 보인다는 뜻이 아니다

- `true` 가 말하는 것은 "블록이 카메라 앞에 투영되고 프레임 안 어딘가에 떨어진다" 까지
- 아래 셋도 전부 `true`

| 실제 상태 | 보고되는 값 |
| --- | --- |
| `RectMask2D`·`ScrollRect` 에 잘려 나감 | `true` |
| 다른 것에 완전히 가려짐 | `true` |
| `CanvasGroup.alpha=0` 으로 그려짐 | `true` |

- 답하려면 블록마다 mask walk 나 raycast 가 붙는데 이 코드는 폴링 경로에서 돎
- `false` 는 두 모양

| `false` 의 모양 | `rect` 값 | 왜 |
| --- | --- | --- |
| 정상적으로 투영됐는데 프레임 밖에 떨어짐 | 잰 값 그대로. 0..1 밖도 그대로 | 얼마나 벗어나 있는지 자체가 정보 |
| 카메라 뒤에 있거나 투영할 카메라가 없음 | 0 으로 채움 | 잴 값이 없음 |

### 좌표는 양자화돼 있다

| 필드 | 단위 | 자리 |
| --- | --- | --- |
| `rect.x/y/w/h` | 픽셀. 좌상단 원점, y 가 아래로 증가 | 정수 |
| `world.x/y/z` | 게임 자신의 단위 | 소수점 4 자리 (`WorldDecimals`) |

- `SceneStatePoller` 가 스냅샷 전체를 해시해 변화를 감지함
- raw float 을 그대로 실으면 숨 쉬는 idle 애니메이션 하나가 매 폴링 `GAME_STATE` 를 다시 내보냄
- `rect` 는 정수 픽셀이라 이 문제를 비켜 가고, `world` 는 반올림할 자리가 필요해 4 로 정함
- **스트림이 축소돼 있으면 `rect` 를 프레임 좌표로 바꿔야 함** — `rect` 는 `scene.screen` 에 대고 잰
  값이고, 그것이 독자가 보고 있는 비디오 프레임의 크기와 같다는 보장이 없음

```
x_frame = rect.x * frame.w / screen.w
y_frame = rect.y * frame.h / screen.h
```

- 축소가 없는 환경은 이 변환을 빼먹어도 그냥 맞음
- 그래서 빼먹은 채로 오래 굴러가다 축소를 켜는 순간 전부 어긋남

## evidence 문서

| 무엇 | 값 |
| --- | --- |
| schema version | 7 (`AffordanceReport.SchemaVersion`) |
| 어디 실리나 | 게임 어셈블리 안 deflate 리소스 `kr.artel.affordance.evidence` |
| 함께 실리는 것 | watch list 리소스 `kr.artel.affordance.watch`, 타입마다 `AffordanceAttribute(schemaVersion, anchor)` |
| 어떻게 나가나 | `scan_evidence` 가 스캔해 `POST /api/sdk/game-builds/{buildId}/content-map` 으로 올린다 |
| 누가 읽나 | agent-server 의 `specs_v2` |
| `capture` 필드 값 | `editor` · `editor-play` · `player` |

- 버전 6 은 `label` 의 뜻을 **좁힘**, 버전 7 은 `createdBy` 항목의 타입을 문자열에서 객체로 **바꿈**
- 5 까지는 전부 더하기
- 이력과 이유는 [ADR 0004](adr/0004-evidence-payload.md)
