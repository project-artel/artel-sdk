# SDK PR 9개 통합 기록

2026-10-28 `dev-yunseong`의 아래 PR만 `codex/pr-bundle-20261028`에 통합했다. `origin/develop`의 `b3c3eb225de49c8b736670f6111d1c6de5e650ce`에서 시작했고, 원본 branch와 commit author를 유지한 merge를 사용했다. 원본 branch 삭제·force push·develop merge는 하지 않는다.

## 원본 PR과 commit 포함 확인

| PR | 제목 | head SHA | 기존 base |
| --- | --- | --- | --- |
| [#161](https://github.com/project-artel/artel-sdk/pull/161) | fix(sdk): evidence scan 전후 PlayerPrefs 백업과 복원 추가 | `1139d38bf914e81f45e7c9cea2c45c5d4669510f` | `develop` |
| [#162](https://github.com/project-artel/artel-sdk/pull/162) | feat(sdk): collider 위 OnMouse handler 를 click 대상으로 기록 | `6300e3e621de58c2a4c2a2a3548236d588fe0480` | `fix/sdk-scan-playerprefs-복원-ARTEL-1231` |
| [#163](https://github.com/project-artel/artel-sdk/pull/163) | fix(sdk): 상속된 handler 의 capability 를 실제 instance 의 scene 에 귀속 | `b094e9444e4e3a5fdc1a17d7e09bca7242ed2d3c` | `feat/sdk-collider-onmouse-handler-click-capability-생성-ARTEL-1227` |
| [#164](https://github.com/project-artel/artel-sdk/pull/164) | fix(sdk): 버튼 handler 기록에 다른 진입점의 기록이 묶이는 문제 수정 | `70065835c086029ec41c05ac967833dcf4cb7976` | `fix/sdk-상속-handler-capability-scene-귀속-수정-ARTEL-1228` |
| [#165](https://github.com/project-artel/artel-sdk/pull/165) | feat(sdk): dictionary 초기화 식에서 입력 key 상수 추적 추가 | `094dce157b6fce97d67442513b099ca607b4d81b` | `fix/sdk-버튼-evidence-join-다른-진입점-effect-제외-ARTEL-1229` |
| [#166](https://github.com/project-artel/artel-sdk/pull/166) | feat(sdk): evidence scan 에 ScriptableObject asset 값 수집 추가 | `3af166185ee3f348907da1d78e597fe4b2ab3984` | `feat/sdk-dictionary-초기화-입력-key-상수-추적-추가-ARTEL-1230` |
| [#168](https://github.com/project-artel/artel-sdk/pull/168) | feat(input): 선택적 New Input System 마우스 주입 추가 | `ba0817b286351e9d060f51ad7eea29af9577a9c7` | `develop` |
| [#169](https://github.com/project-artel/artel-sdk/pull/169) | feat(sdk): 시연 제어권과 입력 만료 처리 추가 | `2898a87bff2d7af8b43520ff146888f5bacf96c4` | `develop` |
| [#174](https://github.com/project-artel/artel-sdk/pull/174) | fix(stream): SDK 영상 전송을 orch WebSocket으로 이동 | `57114f4f9b7ccf6a7cb936b932b6dd1ea3f47448` | `develop` |

선택 조건은 명시된 9개 PR 번호와 `author.login == dev-yunseong`의 교집합이다. 각 원격 head를 `refs/artel-pr-bundle/20261028/<PR 번호>`로 fetch하고 snapshot의 `headRefOid`와 같은지 확인했다. 위 9개 pinned head에 대해 `git merge-base --is-ancestor <SHA> HEAD`를 전수 검사했다. 모두 통합 branch의 ancestor이며, squash·rebase로 원본 commit을 교체하지 않았다.

`origin/develop..HEAD`에서 통합 전 원본 비-merge commit 20개의 author를 확인했고 모두 `dev-yunseong`이다. 다른 작성자의 미병합 commit을 추가 기반으로 가져온 경우는 없다. 목록 밖의 다른 작성자 SDK PR head는 merge하거나 종료하지 않는다. 원본 source branch와 그 branch를 기반으로 둔 팀원의 PR도 변경하지 않는다.

## 충돌 해결과 통합 수정

- PR #174와 #169가 겹친 `IArtelWebSocketTransport.cs`, `ArtelWebSocketClient.cs`, `ArtelWebSocketServer.cs`에서 시연 제어의 `IArtelConnectionEpoch`와 영상 전송의 `IArtelMediaWebSocketTransport`를 함께 유지했다. 연결 교체 시 오래된 시연 입력을 거절하고, JPEG 송신은 하나로 제한한다.
- PR #168의 `ActionExecutor.ExecuteCore` 추출과 #169의 `glideMouse` 옵션이 자동 merge된 결과에서 scope 오류가 발생했다. 실제 Unity compile에서 확인했고, wrapper가 `glideMouse`를 core에 전달하도록 수정했다. 일반 QA의 glide와 시연 pointer의 즉시 이동을 모두 유지한다.
- New Input System 전용 PlayMode에서 기존 `DemonstrationPointer_BurstCompletesWithoutGlideBacklog`가 35건 중 19건만 완료되는 동작 충돌을 확인했다. 시연 `move_mouse`는 native input 처리를 기다린 뒤 ACK하되, 클릭 결과에만 필요한 추가 `EventSystem.Update` 대기를 생략하도록 수정했다. 일반 QA와 `mouse_down`의 대기는 유지하고 테스트의 기대값을 낮추지 않았다. 수정 후 New Input 전용 PlayMode 112개가 모두 통과했다.
- PR #174의 JPEG_WS·ACK·송신 deadline·H.264 저장 계약을 바꾸지 않았다. 화면 방향 보정과 JPEG 상한 초과 시 해상도 축소도 유지했다.
- `git diff --check`가 보고한 신규 `.meta` 3개의 줄 끝 공백만 정리했다.

## 통합 검증

별도 `.work/qa-bundle-sdk-unity` 프로젝트에 통합 package를 embed하고 Windows Unity 2022.3.62f3에서 실행한다. 실행 중인 사용자의 WordVenture, 기존 검증 프로젝트와 로컬 stack은 변경하지 않았다.

Windows Unity 2022.3.62f3에서 아래 네 가지 입력 구성의 EditMode·PlayMode를 모두 실행했고, 최종 결과는 전부 통과했다. Input System package의 유무와 `activeInputHandler` 조합은 저장소 CI matrix와 같다. 운영체제와 Editor patch 버전까지 CI와 같다고 해석하지 않는다.

| Input System package | activeInputHandler | EditMode | PlayMode |
| --- | --- | --- | --- |
| 없음 | 0 (Legacy) | 620 / 620 | 103 / 103 |
| 1.7.0 | 1 (New Input 전용) | 620 / 620 | 112 / 112 |
| 1.7.0 | 2 (Both) | 620 / 620 | 112 / 112 |
| 1.7.0 | 0 (Legacy) | 620 / 620 | 102 / 102 |

각 구성은 compile-time define에 따라 발견하는 테스트 수가 다르다. 반복 실행 수를 고유 테스트 수로 합산하지 않는다. `-batchmode -runTests -testPlatform EditMode`와 `PlayMode`를 사용했으며 GPU가 필요한 JPEG 축소 테스트를 위해 `-nographics`는 사용하지 않았다. 테스트는 입력 시연·제어권 만료·pointer burst·대화형 UI·JPEG 크기/방향·ACK·송신 deadline·evidence scan 회귀를 포함한다.

원본 head 9개 ancestry 검사와 `git diff --check`도 통과했다. 결과 XML과 원본 Unity 로그는 gitignored 로컬 검증 프로젝트에 남겼으며, 계정·라이선스 정보가 섞일 수 있는 Unity 원본 로그는 문서나 PR에 업로드하지 않는다.

## 의존성과 남은 확인

- JPEG_WS 영상은 orchestration-server PR #383과 home PR #228의 대응 계약이 필요하다. 이 PR들도 저장소별 통합 대상이며, 새 통합 PR에서 대체 링크를 제공한다.
- 시연 제어 API·frame ACK·좌표 계약은 원본 #169와 #174의 설명을 아래에 보존했다. 다른 저장소의 통합 검증과 CI 상태는 새 통합 PR의 결과를 따른다.
- 원본 설명의 테스트 수치와 실제 Unity 화면·영상 증거는 당시 commit의 기록이다. 아래 수치를 이번 통합 branch를 재검증한 결과로 합산하지 않는다. 이번 작업에서는 사용자의 실제 게임 QA를 새로 시작하지 않는다.

## 원본 PR 설명 보존

<details>
<summary>PR #161 — fix(sdk): evidence scan 전후 PlayerPrefs 백업과 복원 추가</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/161](https://github.com/project-artel/artel-sdk/pull/161) · snapshot head `1139d38bf914e81f45e7c9cea2c45c5d4669510f`

## Why

`scan_all_scenes` 의 scene walk 는 scene 을 하나씩 load 하면서 게임 코드를 실행한다. 게임 코드가 그 사이에 `PlayerPrefs` 를 써서, gyemong 에서는 scan 한 번으로 `MaxStageId=4`, `CurrentStageId=3`, `TutorialFlag=1` 이 남아 새 플레이어의 타이틀에 이어하기 버튼이 나타났다 (`.work/eval-feedback-20261022/F-gyemong/results.md` 문제 4).

## What Changed

- walk 시작 전에 `PlayerPrefs` 를 담고, 원래 scene 으로 돌아온 뒤 그 값으로 되돌린다. walk 중에 새로 생긴 key 는 지운다. 취소된 walk 도 같다.
- key 목록은 Windows 에서 registry(`HKCU\Software\<company>\<product>`, editor 는 `...\Unity\UnityEditor\...`)의 value 이름에서 읽는다. 값은 플랫폼과 관계없이 `PlayerPrefs` API 로 읽고 쓴다.
- macOS 와 그 밖의 플랫폼은 저장소를 나열하지 않고 evidence 의 `saved` effect 가 적은 key 만 다룬다. 이때 gap `player-prefs-keys-from-evidence-only` 를 남긴다. 이슈의 macOS plist 경로는 구현하지 않았다.
- `Artel.` 접두사 key 는 담지도 되돌리지도 않는다.
- walk 중 게임 코드가 던진 예외 수를 scene 별 gap `game-exceptions:N` 과 문서 gap `game-exceptions-during-walk:N` 에 적는다. 되돌린 key 수는 `player-prefs-restored:N` 이다. 문서 모양은 그대로이고 `gaps[]` 에 문자열이 늘 뿐이다.
- scan 결과 DTO(`EvidenceScanResultDto`)에는 적지 않았다. track A 소유 파일이라 건드리지 않았다.

## Example

WordVenture 개발 빌드(productName `WordVentureTrackB`)를 server 없이 walk 시키는 평가용 harness 로 돌리고, 실행 전후 `reg query HKCU\Software\Team6203\WordVentureTrackB` 를 비교했다 (실측, 2026-10-23).

| | walk 전후로 바뀐 게임 key |
| --- | --- |
| develop | `SeenSlimes0_h2659072101 REG_DWORD 0x1` 이 새로 생김 |
| 이 branch | 없음. 로그 `[Artel] Restored 1 PlayerPrefs key(s) the walk changed.`, 문서 gaps `game-exceptions-during-walk:0`, `player-prefs-restored:1` |

두 경우 모두 `unity.player_session_count`, `unity.player_sessionid` 는 실행할 때마다 Unity 가 바꾸므로 비교에서 뺐다.

이 예시가 증명하지 않는 것: gyemong 은 batchmode harness 로 walk 하면 develop 에서도 `MaxStageId` 같은 key 를 쓰지 않았다 (scene 마다 2 frame 만 주므로 해당 스크립트가 쓰기 전에 다음 scene 으로 간다). 그래서 gyemong 의 실제 증상은 이 branch 로 재현하지 못했다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/PlayerPrefsSnapshot.cs:25` — `PlayerPrefsSnapshot` 이 key 마다 type(string/int/float)과 값을 담고 `Restore()` 로 되돌린다. type 은 기본값을 바꿔 두 번 물어 가린다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/PlayerPrefsSnapshot.cs:250` — `PlayerPrefsKeys` 가 Windows registry 의 value 이름에서 `_h<hash>` 를 떼어 key 를 나열한다(advapi32 P/Invoke).
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/SceneWalk.cs:320` — `BeforeWalk` 가 snapshot 을 뜨고 `Application.logMessageReceived` 로 예외를 세기 시작한다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/SceneWalk.cs:344` — `AfterWalk` 가 원래 scene 을 다시 load 한 뒤 되돌리고 gap 을 적는다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/AffordanceReport.cs:371` — walk 한 번에 대한 gap 을 따로 들고, walk 이 시작할 때 비운다. 앞선 walk 의 수가 남지 않게 하려는 것이다.
- `Packages/kr.artel.sdk/Tests/Runtime/PlayerPrefsSnapshotTests.cs:16` — 값 되돌리기, 새 key 지우기, 지운 key 되살리기, type 복원, `Artel.` key 보존, registry 이름 해석, evidence key 추출 8 개.

## Validation

- [x] EditMode `Unity 2022.3.34f1 -runTests -testPlatform EditMode` (`.github/scripts/setup-unity-test-project.sh` 로 만든 throwaway project): 555 passed, 0 failed. 새 테스트 8 개 포함
- [x] WordVenture 개발 빌드(Unity 2022.3.62f3) walk 전후 registry 비교 (위 Example)
- [ ] PlayMode: 이 branch 단독으로는 돌리지 않았다. stack 맨 위(ARTEL-1226)에서 68 passed, 0 failed
- [ ] gyemong 에서 증상 재현: 위 이유로 재현하지 못했다

## Risks

- registry 를 나열할 수 없는 플랫폼(macOS, Linux, 모바일)에서는 evidence 가 적지 않은 key(문자열 상수가 아닌 key)를 되돌리지 못한다.
- 메모리의 static 값은 되돌리지 않는다. walk 중 게임이 static 필드를 바꿨으면 그 값은 남는다.
- 게임이 walk 중에 `PlayerPrefs.DeleteAll()` 을 부르면 SDK 자신의 `Artel.` key 도 사라지는데, 그것은 되살리지 않는다 (gyemong 문제 5, 범위 밖).

## Rollback

이 PR 을 revert 하면 walk 이 `PlayerPrefs` 를 건드리지 않던 이전 동작으로 돌아간다. 데이터 이전은 없다.

Jira: ARTEL-1231

🤖 Generated with [Claude Code](https://claude.com/claude-code)


</details>

<details>
<summary>PR #162 — feat(sdk): collider 위 OnMouse handler 를 click 대상으로 기록</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/162](https://github.com/project-artel/artel-sdk/pull/162) · snapshot head `6300e3e621de58c2a4c2a2a3548236d588fe0480`

## Why

`Card.OnMouseDown/Up` 과 `SelectableObject.OnMouseDown` 은 evidence 는 있지만 누를 자리가 문서에 없어서, 서버가 interaction 을 `none` 으로 두고 not-a-step 으로 적재했다. 그래서 카드 drag(C10)와 적 클릭(C12)이 TC step 이 되지 못했고, 생성 TC 90 건 중 둘을 다루는 TC 가 0 건이었다 (`.work/eval-feedback-20261022/C-content-map/results.md` 3절, 5절). 발동 버튼 뒤에 적 클릭이 필요한 경로(`CastSpell` 의 `while (target == null)`)도 step 에 그 입력이 없었다(부분 판정 13 개 중 11 개).

## What Changed

- scan 이 `Collider` 또는 `Collider2D` 가 있는 객체 위의 `OnMouseDown`, `OnMouseUp`, `OnMouseUpAsButton`, `OnMouseDrag` handler 를 그 객체의 `calls[]` 항목으로 적는다. `event` 는 메시지 이름, `targetType` 은 컴포넌트의 runtime type, `method` 는 메시지 이름이다. 버튼 `m_OnClick` 과 같은 모양이라 서버의 `SceneWiringIndex` 가 그대로 handler 기록에 잇고, 그 객체의 selector 가 control 이 된다. 문서 모양은 그대로다.
- evidence 가 있는 type 에만 적는다. `OnMouseEnter`/`OnMouseOver` 처럼 누르지 않아도 불리는 메시지는 적지 않는다.
- 분석기가 coroutine `MoveNext` 안에서 마우스 handler 가 쓰는 필드를 검사하면, 같은 `MoveNext` 에서 그 검사 offset 뒤에 effect 나 호출이 있는 기록에 gap `waits-for-input:<type>::<message>` 를 붙인다. 서버는 모르는 gap 토큰도 `capability_evidence.gaps` 로 통과시킨다.

## Example

WordVenture 개발 빌드를 harness 로 walk 한 문서(실측). 서버 적재는 하지 않았고, 서버 `SceneWiringIndex.keysOf` 규칙을 Python 으로 옮겨 묶이는 기록을 셌다.

| handler | develop: `calls[]` 항목 | 이 branch: `calls[]` 항목 | 이 handler 에 이어지는 기록 (key gesture 없음 = click) |
| --- | --- | --- | --- |
| `Cards.Card.OnMouseDown` (C10) | 0 | 2 (`Card(Clone)[17]`, `[18]`, TurnBattleScene) | 22 개, 그중 click 9 |
| `Cards.Card.OnMouseUp` (C10) | 0 | 2 | 20 개, 전부 click |
| `Combat.Enemies.SelectableObject.OnMouseDown` (C12) | 0 | 2 (`Word[13]` 등) | 10 개, 전부 click |

develop 에서는 control 이 0 개라 위 기록이 전부 `interaction=none`, not-a-step 으로 적재된다. 이 branch 에서는 binding 이 생겨 `RecordTranslation.interactionOf` 가 `click` 을 준다.

`waits-for-input:Combat.Enemies.SelectableObject::OnMouseDown` gap 이 붙은 기록은 14 개이고, 전부 `CombineZone/<CastSpell>d__35::MoveNext()` 의 `while (target == null)` 뒤 기록이다(공격 animation, `magicEffectSource` 재생 등).

이 예시가 증명하지 않는 것: 서버에 적재해 `capability.status` 를 확인하지 않았다. 서버 `Interaction` 에 `drag` 가 없어서 `OnMouseDrag`/카드 drag 도 `click` 으로 적재된다. `binding_event` 가 `OnMouseDown`/`OnMouseUp`/`OnMouseDrag` 로 남으므로 서버가 이것을 보고 drag 로 옮기는 follow-up 이 필요하다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/MouseMessageReader.cs:22` — collider 유무와 handler 선언(부모 class 포함)을 보고 `PersistentCall` 을 만든다. collider 는 type 이름으로 봐서 physics module 에 기대지 않는다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/SceneEvidenceScan.cs:636` — evidence 가 있는 컴포넌트를 쓸 때 persistent call 옆에 마우스 handler 를 더한다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/AwaitedInputs.cs:20` — 마우스 진입점이 쓰는 필드 → coroutine 안의 그 필드 검사 → 검사 뒤 기록 순서로 gap 을 붙인다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/AffordanceILPostProcessor.cs:233` — 중복 기록을 접은 뒤에 위 pass 를 부른다.
- `Packages/kr.artel.sdk/Tests/Runtime/MouseMessageReaderTests.cs:11`, `Packages/kr.artel.sdk/Tests/Editor/Affordance/AwaitedInputsTests.cs:10` — 3D/2D collider, 상속된 handler, collider 없음, hover 만 있는 handler, 기다림 앞뒤 기록, coroutine 이 아닌 검사, 마우스가 쓰지 않는 필드 8 개.

## Validation

- [x] EditMode (Unity 2022.3.34f1 throwaway project): 563 passed, 0 failed. 새 테스트 8 개 포함
- [x] WordVenture 개발 빌드 walk 문서 비교 (위 Example)
- [ ] PlayMode: 이 branch 단독으로는 돌리지 않았다. stack 맨 위(ARTEL-1226)에서 68 passed, 0 failed
- [ ] 서버 적재 후 C10, C12 의 status 확인: 하지 않았다

## Risks

- collider 가 있는 객체의 `OnMouseUp` 은 drag 를 놓는 자리이기도 하다. 카드는 눌러서 끌어 놓아야 하는데 지금 서버 어휘로는 click 하나로 보인다.
- `waits-for-input` 은 같은 `MoveNext` 안의 IL offset 순서로 판단한다. 검사 뒤에 있지만 기다림과 무관한 분기도 표시될 수 있다.

## Rollback

이 PR 을 revert 하면 `calls[]` 에 마우스 handler 가 빠지고 gap 이 사라진다. 서버 쪽 변경이나 데이터 이전은 없다.

Jira: ARTEL-1227

🤖 Generated with [Claude Code](https://claude.com/claude-code)


</details>

<details>
<summary>PR #163 — fix(sdk): 상속된 handler 의 capability 를 실제 instance 의 scene 에 귀속</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/163](https://github.com/project-artel/artel-sdk/pull/163) · snapshot head `b094e9444e4e3a5fdc1a17d7e09bca7242ed2d3c`

## Why

WordVenture 의 `TutorialController` 는 `StoryController` 를 상속한다. evidence 에서 `TutorialController.Update` → `StoryController.IsAdvanceKeyDown` 기록이 `StoryController` 에 실려 StoryScene 과 EndingScene 에 들어갔고, `TutorialController` 의 capability 는 0 개였다. 그 prefab 은 MapScene 에 놓여 `Awake` 에서 `DontDestroyOnLoad` 로 scene 을 떠나기 때문에 scan 에 잡히지 않았다. 생성 TC 30 건 중 오류 8 건의 주 원인이다 (`.work/eval-feedback-20261022/C-content-map/results.md` 3절, 5절).

## What Changed

- 분석기: 자식 class 진입점이 같은 객체 위에서(또는 static 으로) 부른 부모 class 메서드의 기록은 자식 type 을 `owner` 로 싣는다.
- 분석기: 부모 class 에 선언된 진입점의 기록을, 그 메서드를 같은 이름과 parameter 로 다시 선언하지 않은 같은 assembly 의 자식 behaviour 에도 싣는다. `owner` 만 자식 이름이다. 서버는 `owner` 로 기록을 scene 컴포넌트에 잇는데 scene 에는 runtime type 만 놓이므로, 부모의 `Start` 를 그대로 쓰는 자식은 그 기록을 받지 못했다.
- scan: walk 이 scene 을 load 하는 동안 `DontDestroyOnLoad` 로 나간 root 를 그 scene 의 객체로 적고 `"persistent": true` 를 붙인다.
- **문서 모양**: `objects[]` 항목에 선택 필드 `persistent`(bool, 참일 때만 씀)가 더해졌다. 서버 `EvidenceParser` 는 `readTree` 로 아는 필드만 읽으므로 무시된다. 나머지는 내용만 바뀐다.

## Example

WordVenture 개발 빌드를 harness 로 walk 한 문서(실측).

| | develop + 1231/1227 | 이 branch |
| --- | --- | --- |
| `Tutorial.TutorialController` 가 놓인 자리 | 없음 (`types` 에도 없음) | MapScene `TutorialController`, `persistent: true` |
| `types["Tutorial.TutorialController"]` 기록 수 | 0 | 206 |
| `Story.StoryController` 기록 중 진입점이 `TutorialController.Update` 인 것 (StoryScene, EndingScene 에 놓임) | 2 | 0 |
| `persistent: true` 객체 | 0 | 6 (`StageDataSingleton`, `TutorialController` 와 그 아래 5 개, 전부 MapScene) |
| 부모 기록을 자식에 함께 실은 수 (분석기 로그) | 0 | 979, 대부분 `Combat.Enemies.Enemy` 의 자식 10 개(70 개씩) |

이 예시가 증명하지 않는 것: `Story.ChatWindowController` 에 실린 기록 32 개는 진입점이 여전히 `TutorialController.Update` 이고 StoryScene, GameClearScene, EndingScene 에 놓인다. `tutorialChatWindow.UpdateChatStream()` 처럼 다른 객체(`TutorialChatWindow`, `ChatWindowController` 의 자식)에 대고 부른 호출이라, 그 수신 객체의 정적 type 을 경로 끝까지 나르지 않으면 고칠 수 없다. eval 의 cap 389-394 가 이 모양이다. 서버 적재 후 S8, C8, R18 위치는 확인하지 않았다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/VariantBuilder.cs:236` — `OwnerOf` 가 자식 진입점이 부른 부모 메서드의 기록을 자식 type 에 싣는다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/InheritedEvidence.cs:20` — 같은 module 의 상속 관계를 모으고, 다시 선언하지 않은 자식 type 을 기록마다 돌려준다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/AffordanceWriter.cs:133` — 그 자식 type 에도 `owner` 를 바꾼 기록을 굽는다. 진단 메시지에 수를 적는다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/EvidenceJson.cs:47` — `owner` 를 받는 `Write` overload.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/StraySpawnTracker.cs:52` — `Strays` 가 방문 뒤 다른 scene 에 새로 생긴 root 를 옮기지 않고 돌려준다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/SceneEvidenceScan.cs:70` — `Capture(scene, carriedOff)` 가 그 root 들을 방문 scene 이름과 `persistent: true` 로 쓴다. selector 는 실제로 사는 DontDestroyOnLoad scene 의 root 순서로 만든다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/SceneWalk.cs:227` — build scene 을 읽을 때 위 목록을 넘긴다.
- `Packages/kr.artel.sdk/Tests/Editor/Affordance/InheritedEvidenceTests.cs:14`, `Packages/kr.artel.sdk/Tests/Runtime/SceneEvidenceScanCarriedOffTests.cs:23`, `Packages/kr.artel.sdk/Tests/PlayMode/StraySpawnTrackerTests.cs:112` — 상속 5 개, 내보낸 root 2 개, `Strays` 1 개.

## Validation

- [x] EditMode (Unity 2022.3.34f1 throwaway project): 570 passed, 0 failed. 새 테스트 7 개 포함
- [x] PlayMode: 68 passed, 0 failed. 새 테스트 1 개 포함
- [x] WordVenture 개발 빌드 walk 문서 비교 (위 Example)
- [ ] 서버 적재 후 S8, C8, R18 의 scene 확인: 하지 않았다

## Risks

- 부모 기록을 자식에 복사하면 문서가 커진다. WordVenture 의 evidence resource 가 282,307 bytes 에서 314,098 bytes 로 늘었다.
- `persistent: true` 객체는 서버가 아직 따로 다루지 않는다. 처음 생긴 scene 에만 놓인 것으로 적재되고, 그 뒤 scene 에서도 남는다는 사실은 쓰이지 않는다.
- 다른 assembly 에 선언된 부모 class 는 다루지 않는다.

## Rollback

이 PR 을 revert 하면 기록이 다시 메서드의 선언 type 에 실리고 `persistent` 필드가 사라진다. 서버 쪽 변경은 없다.

Jira: ARTEL-1228

🤖 Generated with [Claude Code](https://claude.com/claude-code)


</details>

<details>
<summary>PR #164 — fix(sdk): 버튼 handler 기록에 다른 진입점의 기록이 묶이는 문제 수정</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/164](https://github.com/project-artel/artel-sdk/pull/164) · snapshot head `70065835c086029ec41c05ac967833dcf4cb7976`

## Why

WordVenture 발동 버튼(`CombineSystem/CombineZone/Button`)이 step capability 오류 11 개 중 7 개였다. 버튼 → handler join 이 `OnButtonClick` 말고도 같은 `CombineZone` 의 다른 진입점(카드 drop 의 `AddCard`, 매 frame `Update`, `Prs..ctor`)의 기록까지 이 버튼에 묶었다 (`.work/eval-feedback-20261022/C-content-map/results.md` 4절).

## What Changed

서버 `SceneWiringIndex` 는 기록의 진입점, `owner`+메서드, `alsoReachedBy`, `handles` 네 key 로 버튼에 잇는다. 잘못 묶이던 기록이 그 key 를 갖지 않도록 SDK 분석기가 기록을 다르게 만든다.

- `UnityEvent.AddListener` 로 넘긴 메서드가 그 자체로 분석 진입점(인스펙터에서 부를 수 있는 public 메서드)이면, 넘긴 쪽 진입점에서 delegate 를 따라간 기록을 지운다. 같은 본문이 이미 제 진입점으로 적혀 있고, 넘긴 쪽 기록은 `Start` 나 카드 drop 의 조건을 버튼 click 에 붙인다.
- behaviour 가 아닌 type 의 생성자가 자기 필드에 쓰기만 하는 기록(`Prs..ctor`)을 지운다.
- 중복 기록을 접을 때, 진입점이 inspector 에서 부를 수 있는 메서드인 기록(`triggerKind: unity-event`)은 진입점까지 같을 때만 접는다. 예전에는 버튼 handler 의 기록이 다른 진입점 기록 밑 `alsoReachedBy` 로 들어가, 서버가 버튼을 다른 진입점의 `callPath` 와 조건에 이었다.
- 문서 모양은 그대로다.

## Example

WordVenture 개발 빌드를 harness 로 walk 한 문서(실측). 서버 `SceneWiringIndex.keysOf` 규칙을 Python 으로 옮겨 `(Combat.UI.CombineZone, OnButtonClick)` 에 묶이는 기록을 셌다.

| | ARTEL-1228 까지 | 이 branch |
| --- | --- | --- |
| 버튼에 묶이는 기록 | 46 | 40 |
| 그중 진입점이 `OnButtonClick` 이 아닌 것 | 12 | 1 |
| 그중 `.ctor` 기록 | 1 | 0 |

ARTEL-1228 까지 진입점이 다르던 12 개: `CombineZone.Start` 에서 delegate 로 따라간 `OnButtonClick` 5, `alsoReachedBy` 로 묶인 `CardManager.ReturnCardsToHand`/`AddCard`/`CardMouseUp`, `CombineZone.SetTarget` 기록 6, `handles` 를 가진 `Start` 기록 1. 남은 1 개가 그 `Start` 기록이다 — 서버의 HANDLE 경로가 `AddListener` 를 부른 기록 자체를 버튼에 잇는다. SDK 가 아니라 서버 규칙이라 이 PR 에서 다루지 않았다.

분석기 로그: 기록 3,290 → 3,843, 접힌 기록 1,013 → 185, 지운 기록 241.

이 예시가 증명하지 않는 것: `C-content-map/scores.json` 판정으로 step capability 80 개를 다시 채점하지 않았다. 그 판정은 WordVenture 20623b1 기준이고 지금 submodule 은 v1.4.0(09c48a9)이라 cap 번호가 맞지 않는다. 이슈의 두 번째 항목(저장이 없을 때 숨겨진 이어하기 버튼 cap 274, 358 과 `InitPlayerData` 가 먼저 -1 을 쓰는 cap 359 같은 도달할 수 없는 분기)은 구현하지 않았다. 버튼의 활성 조건과 click 기록의 조건을 대조하거나 `PlayerPrefs` 값을 호출 경로를 따라 전파해야 해서 따로 다뤄야 한다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/ButtonJoin.cs:22` — 진입점인 listener 를 넘긴 쪽에서 따라간 기록과, 값 type 생성자 기록을 가린다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/AffordanceILPostProcessor.cs:236` — 접기 전에 위 두 거르기를 부르고, 지운 수를 진단 메시지에 적는다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/DuplicateVariants.cs:72` — `unity-event` 진입점 기록의 접기 key 에 진입점을 넣는다.
- `Packages/kr.artel.sdk/Tests/Editor/Affordance/ButtonJoinTests.cs:8` — 넘긴 listener, 값 type 생성자, 접기 규칙 3 개.

## Validation

- [x] EditMode (Unity 2022.3.34f1 throwaway project): 573 중 572 passed, 1 failed. 실패한 `SecretFileTests.EveryWriterFinishesWhenTheyAllWriteAtOnce` 는 임시 폴더에 Windows 가 남긴 `~RF*.TMP` 파일을 센 것이고, 그 클래스만 다시 돌리니 5 passed, 0 failed. 이 PR 이 건드리지 않는 파일이다
- [x] WordVenture 개발 빌드 walk 문서 비교 (위 Example)
- [ ] PlayMode: 이 branch 단독으로는 돌리지 않았다. stack 맨 위(ARTEL-1226)에서 68 passed, 0 failed
- [ ] `scores.json` 재채점: 위 이유로 하지 않았다

## Risks

- 접기가 줄어 기록이 553 개 늘었다(3,290 → 3,843). 서버가 만드는 capability 행도 늘어난다.
- `AddListener` 로 넘긴 lambda 나 private 메서드는 진입점이 아니라서 그대로 남는다. 그 기록은 여전히 넘긴 쪽 조건을 단다.

## Rollback

이 PR 을 revert 하면 지웠던 기록과 예전 접기 규칙이 돌아온다. 서버 쪽 변경은 없다.

Jira: ARTEL-1229

🤖 Generated with [Claude Code](https://claude.com/claude-code)


</details>

<details>
<summary>PR #165 — feat(sdk): dictionary 초기화 식에서 입력 key 상수 추적 추가</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/165](https://github.com/project-artel/artel-sdk/pull/165) · snapshot head `094dce157b6fce97d67442513b099ca607b4d81b`

## Why

gyemong 의 `GyeMong.InputSystem.InputManager` 는 `Input.GetKeyDown(_keyMappings[action])` 처럼 key 를 `Dictionary<ActionCode, KeyCode>` 에서 꺼내 읽는다. evidence 에는 `(not a literal)` 로 남아서 agent 가 이동과 상호작용 key 를 몰라 A, LeftArrow, E, F, KeypadEnter 를 차례로 눌러 봤다 (`.work/eval-feedback-20261022/F-gyemong/results.md` 문제 8).

## What Changed

- 분석기가 `Dictionary<?, KeyCode>` 필드를 채우는 상수를 그 필드가 선언된 class 의 모든 메서드에서 모은다. collection initializer(`new Dictionary { { k, v } }` 를 필드에 대입), 필드에 대고 부른 `Add`, indexer 대입을 읽는다. gyemong 은 `Start` 가 부르는 `SetDefaultKey` 에서 채우므로 `.cctor`/생성자/`Awake` 로 좁히지 않았다.
- `GetKey*(dict[x])` 에서 x 가 enum 상수나 문자열 상수면 표에서 찾은 KeyCode 를 key 로 적는다.
- x 가 상수가 아니면 dictionary 값 전체를 후보로 두고, 조건에는 후보 key 제스처의 `either` 로 넣는다. 눌리지 않아야 하는 갈래는 후보 전부의 `every` 다. `inputs[]` 에도 후보마다 하나씩 들어간다. 문서 모양은 그대로다 — 서버 `ConditionBranches` 가 `either` 를 갈래로 나눠 key 마다 capability 를 만든다.

## Example

gyemong 사본(`C:\artel-eval-20261022\track-b\gyemong`, 원본은 건드리지 않음)의 `Assembly-CSharp.dll` 에서 evidence resource 를 꺼내 비교했다(실측).

| | develop | 이 branch (stack 아래 1231-1229 포함) |
| --- | --- | --- |
| `InputManager.Update()` 기록의 조건 제스처 | `key:(not a literal) (down)`, `mouse:0 (down)` | `key:W`, `A`, `S`, `D`, `Space`, `Return`, `Escape`, `LeftShift`, `I`, `U`, `Q`, `E`, `R`, `Mouse0`, `Mouse1` (모두 `down`), `mouse:0 (down)` |
| 문서 전체 `inputs[]` 의 key 이름 수 | 10 (`(not a literal)` 포함) | 18 (`(not a literal)` 없음) |

`Update` 는 `foreach (action in _keyMappings.Keys)` 안에서 읽어 x 가 상수가 아니므로 후보 전체가 들어갔다. 기록 수는 2,727 → 3,630 이다. 이 차이에는 stack 아래 ARTEL-1228(상속 기록 복사)과 ARTEL-1229(접기 규칙)의 몫이 섞여 있다.

이 예시가 증명하지 않는 것: 서버에 적재해 `capability.input_key` 를 확인하지 않았다. agent run 은 하지 않았다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/DictionaryKeys.cs:23` — dictionary 필드의 key → KeyCode 표를 만들고, `get_Item` 호출에서 상수 key 또는 후보 목록을 돌려준다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/VariantBuilder.cs:935` — key 인자를 읽을 때 위 표를 먼저 본다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/Condition.cs:67` — 후보가 있는 제스처를 `either`(눌리지 않음이면 `every`)로 편다.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/Variant.cs:33` — `InputRead.Candidates` 와 후보마다 하나씩 돌려주는 `Each()`.
- `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/AffordanceILPostProcessor.cs:129` — assembly 마다 표를 비운다.
- `Packages/kr.artel.sdk/Tests/Editor/Affordance/DictionaryKeysTests.cs:11` — 상수 key, 변수 key, static 문자열 key dictionary, `either`/`every` 5 개.

## Validation

- [x] EditMode (Unity 2022.3.34f1 throwaway project): 578 passed, 0 failed. 새 테스트 5 개 포함
- [x] gyemong 개발 빌드(Unity 2022.3.34f1) evidence 비교 (위 Example)
- [ ] PlayMode: 이 branch 단독으로는 돌리지 않았다. stack 맨 위(ARTEL-1226)에서 68 passed, 0 failed

## Risks

- 표는 상수만 담는다. 게임이 실행 중에 `SetKey` 로 바꾼 key 나 저장된 설정에서 불러온 key 는 모른다.
- x 가 상수가 아닌데 실제로는 하나로 정해지는 경우(메서드 인자 `action`)에도 후보 전체가 들어가, 그 기록이 key 마다 capability 로 나뉜다.

## Rollback

이 PR 을 revert 하면 dictionary key 는 다시 `(not a literal)` 로 적힌다. 서버 쪽 변경은 없다.

Jira: ARTEL-1230

🤖 Generated with [Claude Code](https://claude.com/claude-code)


</details>

<details>
<summary>PR #166 — feat(sdk): evidence scan 에 ScriptableObject asset 값 수집 추가</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/166](https://github.com/project-artel/artel-sdk/pull/166) · snapshot head `3af166185ee3f348907da1d78e597fe4b2ab3984`

## Why

scan 은 asset 의 참조 필드만 2 hop 까지 따라가고 값은 읽지 않았다 (`SerializedReferences.cs`). WordVenture 정답 rule 20 개의 재현율이 6/20 이었고, 빠진 것이 속성 상성표(`Magic Affinity Table.asset`), 웨이브 구성과 적 HP(`*BattleWaveData.asset`, `EnemyDataContainer.asset`), 보스 HP 80 이다 (`.work/eval-feedback-20261022/C-content-map/results.md` 3절, R10, R19, R20).

## What Changed

- evidence 가 있는 컴포넌트의 `refs[]` 가 가리키는 `ScriptableObject` 중 게임 assembly(분석기가 evidence 를 구운 assembly)의 type 을 읽어 문서 최상위 `assets` 에 싣는다. TextMeshPro font asset 같은 엔진·패키지 asset 은 읽지 않는다.
- 값은 숫자, bool, 문자열, enum 이름, 그것들의 배열/List 와 `[Serializable]` class/struct 두 단계까지다. `Vector2/3`, `Color` 는 문자열로 쓴다. 다른 `UnityEngine.Object` 는 `{ "ref": 이름, "id": id }` 로 쓰고, 그것이 게임 `ScriptableObject` 이면 그 값도 읽는다(`StageData` → `BattleScriptContainer`).
- 상한: asset 하나에 값 200 개(넘으면 거기서 멈추고 `truncated: true`), `assets` 전체 2MB(넘는 asset 은 건너뛰고 gap `asset-values-omitted:N`).
- `capabilities` 에 `asset-values-v1` 을 더한다. 기존 필드는 그대로이고 schema 번호는 7 그대로다.
- 서버 적재(`asset_value` 테이블, TC 생성기와 scene context 노출)는 이 PR 범위가 아니다. 아래 모양을 근거로 orchestration-server follow-up 이 필요하다.

### 더해진 문서 모양

```json
{
  "capabilities": ["build-info-v1", "selector-v1", "visual-roles-v1", "persistent-objects-v1", "asset-values-v1"],
  "objects": [
    { "path": "CombineSystem/CombineZone", "components": [
      { "type": "Combat.UI.CombineZone",
        "refs": [ { "field": "magicAffinityTable", "type": "Combat.MagicAffinityTable", "name": "Magic Affinity Table", "id": 8300, "asset": true } ] } ] }
  ],
  "assets": {
    "8300": {
      "name": "Magic Affinity Table",
      "type": "Combat.MagicAffinityTable",
      "values": { "table": { "rows": [ { "targets": [1, 1.5, 0.7, 1, 1, 0.7] }, { "targets": [-1, -1, -1, -2, -2, 3] } ] } },
      "truncated": false
    }
  }
}
```

- `assets` 의 key 는 `refs[].id` 와 같은 값의 문자열이다. id 는 실행마다 Unity 가 새로 주는 instance id 라, 같은 문서 안에서만 뜻이 있다.
- `values` 는 asset 의 직렬화 필드 이름을 key 로 하는 객체다. 필드 순서는 이름순이다.
- 위 예시는 `refs[]` 항목의 `path`, `carries` 와 `rows` 일부를 줄였다. 실제 값은 아래 Example 에 있다.

## Example

WordVenture 개발 빌드를 harness 로 walk 한 문서(실측). develop 에는 `assets` 가 없다. 이 branch 는 asset 28 개, `assets` 25,739 bytes (문서 전체 6.86MB).

- R10 상성표: `assets["8300"]` (`Magic Affinity Table`) — `rows` 5 줄 × `targets` 6 개: `[1,1.5,0.7,1,1,0.7]`, `[1,1,1.5,0.7,1,0.7]`, `[1,1,1,1.5,0.7,0.7]`, `[1,1,1,1,1.5,0.7]`, `[-1,-1,-1,-2,-2,3]`. `CombineZone.magicAffinityTable`(TurnBattleScene)과 `MapMove.magicAffinityTable`(MapScene)의 `refs[].id` 가 8300 이다.
- R19 보스 HP: `EnemyDataContainer` 의 `enemyDatas` 에 `{"id":15,"name":"SlimeKing2","displayName":"언데드 슬라임 마왕","maxHp":80,"type":"Undead",...}`. `BossBattleWaveData` 의 마지막 웨이브가 `[{"enemyId":15,"spawnPositionX":7}]` 이다.
- R20 웨이브 구성: `PlainBattleWaveData`, `SeaBattleWaveData`, `HighlandBattleWaveData`, `RainstageBattleWaveData`, `BossBattleWaveData` 등 `BattleScriptContainer` 10 개와 `StageData` 10 개(`stageName`, `waveData.enemyWaves`).

이 예시가 증명하지 않는 것: `EnemyDataContainer` 는 적 하나에 필드 10 개라 200 개 상한에서 23 번째 적까지만 실리고 `truncated: true` 다. 이 게임 v1.4.0 의 뒤쪽 스테이지 적은 빠진다. 서버 적재와 TC 생성은 하지 않았다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/AssetValues.cs:28` — asset 값을 걷고 상한을 세며 `assets` 를 쓴다. 참조된 게임 `ScriptableObject` 를 queue 에 넣어 한 번씩 읽는다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/SceneEvidenceScan.cs:839` — asset 참조를 따라갈 때 값도 읽는다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/AffordanceReport.cs:785` — `persistentObjects` 와 `gaps` 사이에 `assets` 를 쓴다. 건너뛴 asset 수를 gap 에 적는다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/AffordanceReport.cs:585` — `asset-values-v1` 을 capabilities 에 더한다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/AffordanceCatalog.cs:224` — `Carries` 가 evidence 를 구운 assembly 인지 답한다. 게임 type 을 가리는 기준이다.
- `Packages/kr.artel.sdk/Runtime/Affordance/Scan/SerializedReferences.cs:469` — 직렬화 필드 목록을 `AssetValues` 와 함께 쓰려고 `FieldsOf` 를 internal 로 열었다.
- `Packages/kr.artel.sdk/Tests/Runtime/AssetValuesTests.cs:12` — 중첩 표, enum 과 참조된 asset, 200 개 상한, 게임 밖 asset, 문서와 capability 5 개.

## Validation

- [x] EditMode (Unity 2022.3.34f1 throwaway project): 583 passed, 0 failed. 새 테스트 5 개 포함
- [x] PlayMode: 68 passed, 0 failed
- [x] WordVenture 개발 빌드 walk 문서 확인 (위 Example). walk 전후 `PlayerPrefs` 도 게임 key 변화 없음 (ARTEL-1231)
- [ ] 서버 적재: 범위 밖

## Risks

- 문서 크기: WordVenture 에서 26KB 늘었다. 상한 2MB 는 `assets` 에만 걸리고 문서 전체 크기에는 걸리지 않는다.
- 200 개 상한이 큰 표를 자른다(`EnemyDataContainer`). 상한을 올릴지, 필드별 요약을 둘지 서버 쪽 소비 방식과 함께 정해야 한다.
- docs/adr/0004 의 capabilities 목록은 track A 소유라 고치지 않았다. `asset-values-v1` 을 거기 더해야 한다.

## Rollback

이 PR 을 revert 하면 `assets` 와 `asset-values-v1` 이 빠진다. 더하기만 한 필드라 소비자 쪽 변경 없이 되돌릴 수 있다.

Jira: ARTEL-1226

🤖 Generated with [Claude Code](https://claude.com/claude-code)


</details>

<details>
<summary>PR #168 — feat(input): 선택적 New Input System 마우스 주입 추가</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/168](https://github.com/project-artel/artel-sdk/pull/168) · snapshot head `ba0817b286351e9d060f51ad7eea29af9577a9c7`

## Why

New Input System 전용 게임은 기존 QA의 VirtualMouseState 변경만으로 InputAction이나 InputSystemUIInputModule에 마우스 입력이 전달되지 않았다. 기존 QA 명령을 가상 Mouse의 이벤트로 전달한다. Input System 패키지가 없는 Legacy 프로젝트는 추가 설치 없이 기존 입력 경로를 유지한다.

선행 PR #158 (ARTEL-1233)은 병합됐다. 최신 develop의 uGUI 입력 결과와 WebGL 경계를 통합했고, 현재 PR은 develop을 대상으로 한다.

## What Changed

- VirtualMouseState를 유일한 버튼/소유권 상태로 유지하고 선택적 InputSystemMouseBackend가 위치, delta, 세 버튼을 InputSystem.QueueStateEvent로 전달한다.
- Dynamic/Fixed input update가 이벤트를 실제로 처리하고 UI가 읽은 뒤 명령을 완료한다. 짧은 key_click도 press가 처리되기 전에 release되지 않는다.
- InputSystemUIInputModule에는 native 이벤트를 사용해 직접 PointerEventDispatcher와의 중복 클릭을 막는다. Legacy dispatcher 및 collider 경로는 유지한다.
- QA 소유 중 실제 Mouse 이벤트를 격리하고 기존 포인터 반환 정책과 Ctrl+Shift+M을 연결한다. 단일 Mouse InputUser에 가상 장치를 pairing한다.
- 종료 시 버튼을 즉시 reset하고 다음 rendered frame에 장치를 정리한다. QA 중 복제한 InputSettings로 focus 정책을 적용하고 종료 시 원본을 복원한다.
- Input System은 선택적 의존성이다. 공통 Runtime에는 Unity.InputSystem 참조가 없으며, 패키지 1.7.0 이상과 ENABLE_INPUT_SYSTEM을 모두 만족할 때만 별도 Artel.InputSystem assembly가 등록된다. SDK 설치 시 패키지를 자동 설치하지 않는다.
- 패키지 없는 Legacy와 패키지가 설치된 Legacy/New/Both의 네 조합을 CI에서 각각 EditMode/PlayMode로 실행한다.

## Example

PlayMode fixture의 (100, 100) 위치에서 move_mouse → mouse_down → mouse_up을 실행하면 가상 Mouse.position은 (100, 100)이 되고 InputAction과 native uGUI가 클릭을 받는다. uGUI 테스트는 클릭 1회를 검증한다. FixedUpdate 간격을 0.5초로 늘린 테스트에서도 짧은 Mouse0 key_click의 InputAction performed와 UI click은 각각 1회다.

이는 실제 Unity InputAction/InputSystemUIInputModule을 사용하는 fixture 결과다. QA 서버부터 대상 게임까지의 전체 통합 실행을 입증하지는 않는다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Runtime/UnityEngine/NewInputMouse.cs:6` — 패키지 참조 없는 facade로 optional backend를 호출하며, 미등록 시 Legacy 경로를 유지한다.
- `Packages/kr.artel.sdk/Runtime/InputSystem/InputSystemMouseBackend.cs:13` — 가상 장치, input update 확인, 실제 입력 격리, pairing과 settings 복원을 관리한다. SubsystemRegistration에서 instance를 등록하고 Editor ExitingPlayMode에서 정리한다.
- `Packages/kr.artel.sdk/Runtime/InputSystem/Artel.InputSystem.asmdef:1` — package versionDefines와 defineConstraints로 optional assembly를 분리하고 공통 Runtime과 같은 WebGL player 제외 조건을 적용한다.
- `Packages/kr.artel.sdk/Runtime/InputSystem/NewInputMouseLifetime.cs:6` — FixedUpdate가 중단되어도 release 이후 장치를 정리한다.
- `Packages/kr.artel.sdk/Runtime/UnityEngine/Input.cs:200` — 공통 VirtualMouseState를 adapter에 노출하고 물리 위치/포인터 소유권을 연결한다.
- `Packages/kr.artel.sdk/Runtime/ActionExecutor.cs:121` — 지원 조건과 처리 완료를 확인한다. native UI 직접 dispatch를 생략하고 native reached는 null로 반환한다.
- `Packages/kr.artel.sdk/Runtime/ArtelManager.cs:897` — New-only에서도 실제 Keyboard의 제어권 반환 단축키를 읽는다. 기존 overlay 안내와 LegacyInput 설명은 마우스 지원 범위에 맞춘다.
- `Packages/kr.artel.sdk/Tests/InputSystem/PlayMode/NewInputMouseTests.cs:18` — 버튼/alias/delta/drag, 단일 click, FixedUpdate, paused cleanup, settings 복원, 물리 입력 격리 및 pairing을 검증한다. 기존 UI fixture는 New-only에서도 native input module을 사용한다.
- `Packages/kr.artel.sdk/Tests/Runtime/UnityEngine/VirtualAxisStateTests.cs:1` — New-only는 선행 guard의 축 입력 미지원 결과를 검증하고 Legacy/Both의 기존 축 테스트는 유지한다.
- `Packages/kr.artel.sdk/Tests/PlayMode/OptionalInputDependencyTests.cs:8` — 무패키지 실행에서 Input System assembly가 로드되지 않고 공통 Runtime이 참조하지 않는지 검증한다.
- `Packages/kr.artel.sdk/Tests/PlayMode/InputModuleFixture.cs:7` — 공통 UI fixture의 native module 생성에만 reflection을 사용해 공통 테스트의 필수 참조를 제거한다.
- `.github/workflows/unity-tests.yml:87` — test project의 package 유무 및 설정 0/1/2를 나누어 EditMode/PlayMode를 각각 실행한다.
- `Packages/kr.artel.sdk/README.md:1` — 지원 모드, 결과 필드, 장치 제약 및 정리 동작을 설명한다.

## Validation

Unity 2022.3.34f1에서 `-batchmode -nographics -runTests -testPlatform EditMode/PlayMode`로 실행했다.

| Input System package | Active Input Handling | EditMode | PlayMode |
| --- | --- | --- | --- |
| 없음 (fresh import) | Legacy (0) | 566/566 | 93/93 |
| 1.7.0 | Legacy (0) | 566/566 | 92/92 |
| 1.7.0 | New (1) | 566/566 | 102/102 |
| 1.7.0 | Both (2) | 566/566 | 102/102 |

- [x] 선행 commit e2077e3 Legacy baseline: 565/565, 87/87.
- [x] 무패키지 프로젝트의 packages-lock.json, Library/PackageCache 및 ScriptAssemblies에서 Input System 부재 확인. 기존 UI/마우스 테스트도 모두 실행했다.
- [x] 최신 원격 변경을 보존해 통합한 후 위 네 조합 전체 테스트를 다시 실행했다.
- [x] 원격 PR에 추가된 coroutine 테스트를 실제 press 이후 결과 반환 전 제거 조건으로 맞추고, 수신 경로 보존 assertion을 유지했다.
- [x] 테스트 assembly 분리 후 기존 bootstrap manager를 보존하도록 fixture를 수정하고 위 네 조합 전체 회귀 통과.
- [x] plan review 및 pair review PASS. FixedUpdate 짧은 click과 paused cleanup 지적 수정 후 실행 검증.
- [x] 별도 Editor 프로세스에서 domain reload 없이 두 번 진입: 위치/클릭 전달, 장치/lifetime GameObject/복제 InputSettings 제거와 원본 settings 복원 통과. 재현 script와 결과는 `.plan/assets/new-input-system-mouse/`에 포함했다.
- [x] git diff --check 및 setup script의 bash -n 문법 검사.
- [ ] 원격 GitHub Actions 결과 확인.

## Risks

- New Input System 키보드/gamepad 입력 주입은 포함하지 않는다.
- New/Both에서 Input System 1.7.0 미만이거나 adapter가 등록되지 않았다면 마우스 명령은 명시적으로 실패한다.
- Manual update, timeScale=0인 Fixed update, 복수 Mouse InputUser는 명령을 명시적으로 거절한다.
- 게임이 고정한 device ID나 별도 binding/device 필터는 변경하지 않는다.
- native UI의 실제 handler 수신을 public API로 추적할 수 없어 reached는 null이다. pointerHeldByPerson은 포인터 소유권으로 판단한다.
- QA 활성 중 focus 정책과 runInBackground를 조정한다. InputSettings는 복제본에 적용하고 종료 시 복원한다.
- 종료는 기존 ReleaseAll과 같은 release 의미이므로 동일 UI 대상에서 버튼을 놓으면 click이 발생할 수 있다.

## Rollback

이 PR의 commit을 revert한다. 선행 LegacyInput guard는 유지한다.

Jira: ARTEL-1263

---

<sub>Stack created with <a href="https://github.com/github/gh-stack">GitHub Stacks CLI</a> • <a href="https://gh.io/stacks-feedback">Give Feedback 💬</a></sub>


</details>

<details>
<summary>PR #169 — feat(sdk): 시연 제어권과 입력 만료 처리 추가</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/169](https://github.com/project-artel/artel-sdk/pull/169) · snapshot head `2898a87bff2d7af8b43520ff146888f5bacf96c4`

## Why

사람이 streaming 화면에서 TC 검증을 시연할 때 연결이 끊기거나 제어권이 끝나도 누른 키와 대기 입력이 남을 수 있다. SDK가 session/generation과 lease를 직접 확인하고 만료 후 입력을 중단하도록 한다.

## What Changed

- protocol v1 capability와 ACQUIRE/RENEW/RELEASE ACK를 추가했다. control은 action queue 밖에서 처리하고 monotonic clock으로 lease를 검사한다.
- 만료·연결 epoch 변경·수동 pointer 반환 시 held input과 시연이 시작한 readings를 해제한다. 이미 실행 중인 중첩 coroutine과 대기 action도 이전 generation이면 진행하지 않는다.
- 시연 move_mouse는 QA용 glide 없이 즉시 적용해 연속 pointermove가 보간 queue에 쌓이지 않게 한다. smoothMovement 설정과 무관하게 기존 virtual input·PointerEvents callback을 호출한다. QA glide 기본값은 유지한다.
- 시연 move_mouse는 좌상단 원점 normalized 좌표를 현재 게임 해상도로 변환한다. 기존 QA의 픽셀 좌표와 optional demonstration 필드가 없는 요청은 기존 계약을 유지한다.

## Example

실제 로컬 Unity 버튼 카운터 Development build(version 1.0.1258, gameInstance 1/build 1)를 backend와 연결했다. 브라우저 streaming에서 Increment를 한 번 클릭하자 Count: 0 → Count: 1로 바뀌었다. ACQUIRE 이후 readings가 활성화되고 pulse에 Counter text와 SDK count가 반영됐다. 이후 승인 절차를 사용한 ORDERED QA도 reset → Count: 0 → 단일 click → Count: 1을 현재 실행에서 확인해 1/1 PASS로 완료했다.

PlayMode fixture에서는 session `one`, generation `1`, leaseMilliseconds `8000`으로 ACQUIRE한 뒤 key_down을 적용했다. monotonic clock을 10초에서 18초로 옮기면 별도 메시지 없이 키·pointer·시연 readings가 해제되고 같은 generation의 RENEW는 거절된다. 시연 중 legacy ACTION도 거절되며 해제 후에는 기존 QA 경로로 돌아간다.

이는 실제 로컬 연결과 단순 버튼 fixture의 확인이다. 여러 게임 입력 시스템, 장시간 네트워크 장애, 서비스 환경 latency를 검증한 결과는 아니다. SDK는 시연 원본이나 판정을 저장하지 않으며 backend가 ACTION_RESULT와 pulse를 기록한다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Runtime/DemonstrationLease.cs:6` — session/generation, monotonic 만료, 지연된 ACQUIRE/RENEW 거절을 한곳에서 검사한다.
- `Packages/kr.artel.sdk/Runtime/ArtelManager.cs:818` — queue와 무관한 control 처리, 입력 해제, readings 수명 관리와 실행 중 coroutine 중단을 담당한다.
- `Packages/kr.artel.sdk/Runtime/DemonstrationPointer.cs:6` — 시연 좌표만 실행 시점 Screen 크기로 변환해 streaming 해상도 차이를 보정한다.
- `Packages/kr.artel.sdk/Runtime/ArtelWebSocketClient.cs:10`, `Packages/kr.artel.sdk/Runtime/ArtelWebSocketServer.cs:23`, `Packages/kr.artel.sdk/Runtime/IArtelWebSocketTransport.cs:5` — 연결 epoch와 메시지 유효성 검사를 추가하고 capability를 알린다.
- `Packages/kr.artel.sdk/Runtime/Protocol/Dto/DemonstrationDtos.cs:5`, `Packages/kr.artel.sdk/Runtime/Protocol/Dto/ArtelRequestDto.cs:1` — control/capability와 optional demonstration 참조의 wire 계약을 선언한다.
- `Packages/kr.artel.sdk/Tests/Runtime/DemonstrationLeaseTests.cs:8`, `Packages/kr.artel.sdk/Tests/PlayMode/DemonstrationControlTests.cs:14` — lease 경계·재연결·실행 및 대기 입력·기존 QA 호환·좌표 보정을 검증한다.
- `.plan/general/2026-10-23-sdk-demonstration-control.md:1` — 구현, 검증 결과와 한계를 기록한다. 공통 계획·계약도 함께 반영했다.

## Validation

- [x] Unity 2022.3.34f1 `-batchmode -nographics -runTests -testPlatform EditMode`: 562/562 PASS
- [x] 같은 throwaway project의 `-testPlatform PlayMode`: 78/78 PASS
- [x] 실제 브라우저 streaming 단일 클릭 Count: 0 → 1 및 readings/pulse 확인
- [x] smoothMovement=true/10초 설정의 즉시 도착, 35개 연속 move, direct uGUI drag/drop 검사
- [x] 독립 SDK diff 검토 PASS(최초 변경); direct pointer 추가 변경은 Unity 회귀로 검증
- [x] `git diff --cached --check`

결과 XML은 로컬 `.tmp/unity-tests/EditMode-direct-final.xml`, `PlayMode-direct-final.xml`로 확인했다. `.tmp`, 게임 build와 로컬 설정은 커밋하지 않았다.

## Risks

**backend ARTEL-1259보다 이 SDK PR을 먼저 merge하고 게임 build에 포함해야 한다.** capability가 없는 기존 SDK에는 backend가 시연 시작을 허용하지 않는다. lease 기본 8초, 허용 상한 30초이며 Unity main thread가 멈춘 동안에는 입력 해제 처리가 진행되지 않는다. 아직 merge하지 않은 draft이며 사람의 검토와 수동 ready-for-review 전환이 필요하다.

## Rollback

backend에서 신규 시연 시작을 중단하고 활성 lease 종료를 기다린 뒤 SDK 변경을 되돌린다. 공통 protocol을 사용하는 backend 반영 순서를 함께 조정한다.

Jira: ARTEL-1260


</details>

<details>
<summary>PR #174 — fix(stream): SDK 영상 전송을 orch WebSocket으로 이동</summary>

원본: [https://github.com/project-artel/artel-sdk/pull/174](https://github.com/project-artel/artel-sdk/pull/174) · snapshot head `57114f4f9b7ccf6a7cb936b932b6dd1ea3f47448`

## Why

실제 Windows Unity와 WSL orch 사이의 WebRTC 연결에서 영상 frame이 도착하지 않아 실시간 화면과 QA 녹화가 비었다. 영상도 기존 인증된 WebSocket으로 보내 orch가 중계·H.264 변환·S3 저장을 처리하도록 바꾼다.

[서버 PR #383](https://github.com/project-artel/artel-orchestration-server/pull/383), [home PR #228](https://github.com/project-artel/artel-home/pull/228)의 `JPEG_WS` 계약과 함께 배포해야 한다. 별도 TURN·media-worker·미디어 포트는 추가하지 않는다.

## What Changed

- `STREAM_FRAME`과 `STREAM_FRAME_ACK`로 송신과 ACK 대기를 각각 하나로 제한한다. SDK main thread에서 네트워크 송신을 기다리지 않는다.
- 기본 1280×720·10fps·quality65, JPEG 128KiB 상한을 적용한다. 복잡한 화면은 품질 재시도 후 해상도를 절반씩 낮추고 surface를 재사용한다.
- 10초 동안 송신이 끝나지 않으면 기존 소켓을 실제 종료한 뒤 backoff로 다시 연결한다. 늦은 ACK·timer·callback은 새 session과 다음 frame에 적용하지 않는다.
- D3D 캡처의 상하 방향을 보정한다. `Unity.WebRTC` 참조·package 의존성과 pump를 제거하고 로컬 `ArtelTestPage`도 같은 계약으로 바꾼다.

## Example

실제 Unity→orch viewer WebSocket에서 정상 방향의 1280×720 JPEG 40개를 약 9.37fps로 수신했다. 평균 63,367 bytes·최대 63,711 bytes였다. 같은 실제 QA 6의 S3 MP4는 H.264·1,350,436 bytes·472 frames·약 54.57초였으며 다운로드·decode해 글자 방향을 확인했다.

## Code Walkthrough

- `Packages/kr.artel.sdk/Runtime/Streaming/ArtelStreamSession.cs` — ACK window·timeout·session 수명 관리
- `Packages/kr.artel.sdk/Runtime/Streaming/ScreenVideoSource.cs` — JPEG 캡처·축소·D3D 방향 보정·surface 해제
- `Packages/kr.artel.sdk/Runtime/ArtelWebSocketClient.cs` — bounded 비동기 송신·deadline·물리 소켓 종료와 복구
- `Packages/kr.artel.sdk/Runtime/Streaming/ArtelStreamHost.cs`, `StreamSignalSender.cs`, `Runtime/Protocol/Dto/StreamFrameMessageDto.cs` — frame·ACK·transport 계약
- `Packages/kr.artel.sdk/Runtime/ArtelTestPage.cs`, `ArtelWebSocketServer.cs` — 기존 로컬 viewer 유지
- `Packages/kr.artel.sdk/Tests/Runtime/MediaSendDeadlineTests.cs`, `WebSocketStreamSessionTests.cs`, `ScreenVideoSourceTests.cs` — 정지·늦은 callback·noise 축소·UV 방향 회귀
- `docs/websocket-streaming.md` — 전송·자원 상한과 검증·배포 제약

## Validation

- [x] 최종 GPU Unity EditMode 578개·PlayMode 92개 통과, skip 0
- [x] 실제 socket의 callback 미반환→Closed→새 socket 송신과 같은 socket의 늦은 callback 회귀
- [x] GPU noise 입력의 JPEG 상한·자동 축소·surface 재사용과 UV 방향 분기
- [x] 실제 Windows Unity→WSL orch 영상 수신과 S3 H.264 다운로드·decode·정상 방향 확인
- [x] `git diff --check`

## Risks

JPEG 전송은 WebRTC보다 대역폭과 main-thread encode 비용이 클 수 있다. 한도와 ACK로 누적을 제한하지만 운영 장시간 부하와 encode p95는 별도 측정이 남아 있다. 같은 SDK socket의 TCP 전송이므로 느린 연결의 control 지연도 확인해야 한다. 기존 SDK와 새 서버의 혼합 배포는 SDK 업데이트 오류로 표시한다.

## Rollback

SDK·서버·home을 함께 이전 버전으로 되돌리거나 스트리밍만 비활성화한다. 저장한 판정·평가·issue·영상은 보존한다.

Jira: ARTEL-1286


</details>
