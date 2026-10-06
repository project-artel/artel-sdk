# 2026-09-15 — `polling` 기반 상태 추적과 `[ArtelState]` 를 지운다

- Date: 2026-09-15
- Jira: ARTEL-400
- Status: Implemented

## Goal

`pulse` 가 상태 채널을 맡았으므로 attribute 기반 경로를 지운다. 지우는 것은 넷이다 —
`SceneStatePoller`, `SceneStateHashTracker`, `StateReader`, `ArtelStateAttribute` — 그리고
`GAME_STATE` 가 싣던 `states` 배열이다.

실질적 성과는 **게임 코드를 고치라는 요구가 사라지는 것**이다. 상태를 보려면 게임의
`MonoBehaviour` 에 `[ArtelState]` 를 붙여야 했다. `samples/WordVenture` 전체에서 그 attribute 를
붙인 자리는 `Assets/Scenes/Test/TrackingTest.cs` 한 파일, 멤버 둘뿐이다. 즉 이 채널이 실제 게임에서
읽어 낸 값은 테스트 씬의 두 개가 전부였다.

부수적으로 씬 DTO 전체를 SHA256 으로 해싱해 하나만 바뀌어도 씬을 통째로 다시 보내던 것도
없어진다.

## Non-goals

- `[ArtelAction]` 폐기. ARTEL-429 의 일이고 이 이슈의 명시된 non-goal 이다.
  `ArtelActionAttribute`, `ArtelActionRecorder`, `ActionInvocationBuffer`,
  `AffordanceILPostProcessor` 는 손대지 않는다.
- wire 에서 `GAME_STATE` 타입을 없애는 것. `artel-agent-server`(`app/qa/envelope.py`)와
  `artel-orchestration-server`(`GameStateMessageHandler.kt`, `SdkWebSocketHandler.kt`)가 그
  철자를 알고 있고, 등록되지 않은 타입은 프레임째로 거절된다. envelope 폐기는 별도 교차 저장소
  작업이다.
- `SerializedFieldReader` 제거. `scan_all_scenes --full` 이 그것을 쓴다.
- `StateDto.tag` / `TrackedState.Tag` 제거. `[ArtelState]` 가 사라지면 이 값은 언제나 빈
  문자열이지만, orchestration 의 `SdkState.tag` 는 기본값 없는 non-null 이라 필드를 빼면 그쪽
  파싱이 깨진다. 후속 과제로 남긴다.
- `docs/` 와 저장소 루트 `README.md`. 다른 branch 가 동시에 다시 쓰고 있다.

## Context / Constraints

- **`GAME_STATE` 는 남고 `states` 만 빠진다.** ARTEL-400 의 acceptance criterion 은
  "`GAME_STATE` 에 `states` 가 더 이상 실리지 않는다" 와 "로컬 test page 가 `states` 없이
  그린다" 다. 둘 다 `GAME_STATE` 가 계속 만들어진다는 것을 전제한다. 그래서 요청에 답하는
  생산자(`scan_scene` · `SCAN_SCENE` · `GET_GAME_STATE`)는 남기고, 1초 `polling` 과 해시 게이트만
  지운다.
- **`SendsGameState` 스위치는 함께 사라진다.** 그 doc comment 가 "실제로 지우는 것은
  ARTEL-400 이고, 그때 이 속성도 함께 사라진다" 라고 적고 있다. 스위치가 막던 것은 전송이 아니라
  `Update` 안의 씬 순회였고(`PollSceneState` 가 `sceneStatePoller` 앞에서 막는다), 그 순회가
  사라지면 막을 것이 없다. 스위치가 빠지면 `scan_scene` 은 ERROR 대신 다시 씬으로 답한다 —
  agent 는 ARTEL-516 이 꼬리 `scan_scene` 을 뺀 뒤로 그것을 보내지 않으므로 QA 런의 트래픽은
  달라지지 않고, 로컬 test page 의 Scan 버튼만 되살아난다.
- **`SerializedFieldReader` 가 `scan_all_scenes --full` 을 떠받친다.** 확인했다:
  `ArtelManager.TryReadScanOptions` → `SceneScanOptions.Full` → `SceneScanner.CreateComponents`
  의 `readAllFields` → 지금은 `StateReader.Read(component, true)` 를 거쳐 그 reader 에 닿는다.
  `StateReader` 를 지우면 `ScannedTarget` 이 `SerializedFieldReader` 를 직접 들어야 한다.
- **`ISceneSnapshotScanner` 는 `SceneStatePoller.cs` 안에 산다.** `poller` 가 사라지면 이 인터페이스도
  사라지고, `SceneScanner` 는 그것을 구현하지 않는다. 구현체도 구독자도 그 하나뿐이다.
- **`Aiming.Known.Holds` 는 이 저장소에 없다.** ARTEL-400 의 acceptance criterion 이 그 이름을
  대지만, `Runtime`·`Editor`·`Tests`·`samples`·git 전체 이력에 그 심볼은 한 번도 나타나지 않는다.
  ARTEL-417 은 자기 description 에서 `AimableTargets` 를 포함한 그 스택이 742줄에서 90줄로
  줄면서 "전부 빠졌다" 고 적고 있다. 실제로 "세 번째 근거" 에 해당하는 것은
  `SceneScanner.CreateComponents` 의 3항 조건이다:
  `actionSource == null && !readAllFields && !stateReader.HasTrackedState(...)`. 그 세 번째 항이
  `[ArtelState]` 태그이고, 이 작업이 그것을 뺀다.
- **`GAME_STATE` 소비자는 이미 0 이다.** `app/qa/scene.py:386` 의 실측 주석 — `GAME_STATE` 0장,
  `PULSE` 14,489장. agent 쪽 `observables` 는 orchestration 의 `GameStateTransformer` 가
  `component.states` 에서 만드는데, 그 프레임이 오지 않으므로 이미 비어 있다.
- 이 저장소는 Unity 프로젝트가 아니다. 테스트는
  `.github/scripts/setup-unity-test-project.sh` 가 만드는 임시 프로젝트에서 돈다. 이 기기의
  editor 는 Windows 쪽이므로 대상 경로를 `/mnt/c` 아래에 둔다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 확인 완료.
  - `Packages/kr.artel.sdk/Runtime/Tracking/SceneStatePoller.cs` — `poller`, `ISceneSnapshotScanner`,
    `ScenePollResult` 가 한 파일에 있다.
  - `Packages/kr.artel.sdk/Runtime/Tracking/SceneStateHashTracker.cs` — SHA256 게이트.
  - `Packages/kr.artel.sdk/Runtime/Tracking/StateReader.cs:87` — `HasTrackedState` 가 스캔의
    3항 필터 중 세 번째 항이다.
  - `Packages/kr.artel.sdk/Runtime/ArtelManager.cs:50` — `SendsGameState`.
  - `Packages/kr.artel.sdk/Runtime/ArtelManager.cs:443` — `NoticeNewConnection` 은 해시를 비우는
    일만 한다.
  - `Packages/kr.artel.sdk/Runtime/SceneScanner.cs:328` — 3항 필터.
  - `Packages/kr.artel.sdk/Runtime/Protocol/Dto/SceneComponentDto.cs:16` — `states` 가 빈
    목록으로라도 언제나 실린다.

- [x] **Step 1: attribute 와 그 reader 를 지운다**
  - `Runtime/Tracking/ArtelStateAttribute.cs` 와 `Runtime/Tracking/StateReader.cs` 를 meta 와
    함께 지운다.
  - `SceneScanner`/`ScannedTarget` 이 `SerializedFieldReader` 를 직접 든다. 컴포넌트 필터는
    2항이 된다: `actionSource == null && !readAllFields` 이면 건너뛴다.
  - `--full` 의 상태 읽기는 `StateReader` 가 하던 그대로 — 이름순 정렬, 태그는 빈 문자열,
    읽기 실패는 경고 한 줄 — `ScannedTarget` 안으로 옮긴다.
  - `Tests/Fixtures/TrackedFixtureBehaviour.cs` 에서 `[ArtelState("hp")]` 만 뗀다. `Hp` 필드는
    public 이라 `--full` 이 그대로 읽는다.

- [x] **Step 2: `polling` 과 해시를 지운다**
  - `Runtime/Tracking/SceneStatePoller.cs`, `Runtime/Tracking/SceneStateHashTracker.cs` 를 meta 와
    함께 지운다.
  - `ArtelManager` 에서 `sceneStatePoller` 필드, 생성, 네 군데 `Reset` 호출,
    `SceneScanIntervalSeconds`, `PollSceneState`, `NoticeNewConnection`, `transportWasConnected`
    를 지운다.
  - `SendsGameState` 를 지운다. `ReplyWithGameState` 와 `SendGameState` 는 `scanner.Scan()` 을
    직접 부르고 `SceneSnapshotMapper.ToDto` 로 내린다.
  - `Runtime/Diagnostics/ArtelProfilerMarkers.cs` 에서 `ManagerPollSceneState`, `SceneScanMap`,
    `SceneScanHash`, `StateReadTagged` 를 지운다. `StateReadSerializedFields` 는 남는다 —
    `--full` 의 필드 읽기가 계속 그 marker 아래에서 돈다.

- [x] **Step 3: `GAME_STATE` 에서 `states` 를 뺀다**
  - `SceneComponentDto.States` 를 `NullValueHandling.Ignore` 로 두고 기본값을 `null` 로 바꾼다.
    `ButtonComponentDto.OnClick` 이 이미 쓰는 규칙과 같다 — 빈 목록과 "수집하지 않았다" 를
    가르지 않는다.
  - `SceneSnapshotMapper` 는 상태가 없으면 `null` 을 싣는다. 기본 스캔(= `GAME_STATE`)은 상태를
    만들지 않으므로 키 자체가 사라지고, `--full` 은 종전대로 싣는다.
  - orchestration 의 `SdkComponent.states` 는 `= emptyList()` 기본값이라 키가 없어도 파싱된다.
    확인했다(`sdk/dto/GameStateDto.kt:82`).

- [x] **Step 4: 주석과 test page 를 사실에 맞춘다**
  - `SceneSnapshotMapper` 의 `WorldDecimals` 주석이 "`poller` 가 해시해서 보낼지 정한다" 고 적고
    있다. 양자화가 남는 이유는 이제 payload 가 흔들리지 않는 것이다.
  - `SceneScanOptions` 의 doc 이 `ArtelStateAttribute` 와 `poller` 를 가리킨다.
  - `Runtime/ArtelTestPage.cs` — `states` 렌더링은 그대로 둔다. `component.states || []` 가
    키 없는 기본 스캔에서 빈 배열이 되어 details 블록을 그리지 않고, `--full` 스냅샷에서는
    종전대로 그린다. 고칠 것은 "`poller` 가 1초 안에 GAME_STATE 를 민다" 고 적은 주석 셋뿐이다 —
    live 씬은 이제 Scan 을 누를 때만 갱신된다.

- [x] **Step 5: Tests**
  - 지운다: `Tests/Runtime/SceneStatePollerTests.cs`, `Tests/Runtime/SceneStateHashTrackerTests.cs`,
    `Tests/PlayMode/GameStateSwitchTests.cs`. 셋째 파일은 자기 doc 에 "폐기는 ARTEL-400 이고
    그때 이 파일도 함께 사라진다" 고 적혀 있다.
  - `Tests/Runtime/SceneScannerTests.cs` —
    `Scan_Full_KeepsTaggedStateTagsInsteadOfReadingTheFieldTwice` 를 지우고,
    `Scan_Default_ReadsOnlyTaggedState` 를 `Scan_Default_SkipsAGameBehaviourWithNoActions` 로
    바꾼다(본문은 그대로 유효하다).
  - `Tests/Runtime/BlockTransformTests.cs` —
    `Map_RoundsCoordinatesSoAStillSceneHashesTheSame` 가 해시 트래커를 쓴다. 같은 성질을 트래커
    없이 — 직렬화한 DTO 두 개가 글자까지 같은지로 — 확인하도록 다시 쓴다.
  - `Tests/PlayMode/ActionBatchTests.cs` — `SendsGameState` 를 켜고 끄던 SetUp/TearDown 을
    없앤다. `BatchScan_*` 과 `TopLevelScan_*` 은 스위치 없이 그대로 통과해야 한다.
  - `Tests/Runtime/SerializedFieldReaderTests.cs` — **바꿀 것이 없다.** 그 파일의 일곱 테스트는
    전부 `SerializedFieldReader` 자신을 보고, `[ArtelState]` 를 한 번도 쓰지 않는다.
    acceptance criterion 이 이름을 대지만 그 경로를 덮는 부분은 없다.
  - `Tests/Runtime/SceneJsonContractTests.cs` — `states` 가 빠진 wire 모양을 확인하는 단언을
    맞춘다.
  - `--full` 이 여전히 직렬화 필드를 싣는지 보는 기존 테스트
    (`Scan_Full_ReadsEverySerializedFieldOfAGameBehaviour`)가 이 작업의 회귀 방지선이다.

- [x] **Step 6: README**
  - `Packages/kr.artel.sdk/README.md` 의 `## State and action tracking` 에서 `[ArtelState]`
    예제와 설명을 뺀다. 상태는 이제 `pulse` 채널이고, `[ArtelAction]` 문서는 그대로 남는다.
  - `### Full mode` 의 "whether or not they carry `[ArtelState]`" 와 "`GAME_STATE` and the
    poller are never affected by this mode" 를 고친다.
  - `## Ordering a scan against actions` 의 "the same shape the poller pushes" 를 고친다.

- [x] **Step 7: Rollout / Rollback**
  - flag 없이 나간다. 되돌리기는 `git revert` 한 번.

## Validation

- **Commands run** (이 기기의 Unity 는 Windows 쪽이라 대상 경로를 `/mnt/c` 아래에 둔다):
  ```bash
  .github/scripts/setup-unity-test-project.sh /mnt/c/temp/artel-400
  "/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1/Editor/Unity.exe" \
    -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath 'C:\temp\artel-400' \
    -testResults 'C:\temp\artel-400\results-editmode.xml' \
    -logFile 'C:\temp\artel-400\unity-editmode.log'
  python3 .github/scripts/summarize-test-results.py \
    /mnt/c/temp/artel-400/results-editmode.xml EditMode
  ```
  `-testPlatform PlayMode` 로 한 번 더 돌린다.

- **Result:**

  | | merge-base `52bcb02` | 이 branch |
  | --- | --- | --- |
  | EditMode | 440 passed · 0 failed | 435 passed · 0 failed |
  | PlayMode | 50 passed · 0 failed | 46 passed · 0 failed |

  EditMode 가 다섯 줄어든 것은 지운 테스트 여덟(`SceneStatePollerTests` 2 ·
  `SceneStateHashTrackerTests` 5 · 태그 테스트 1)에서 새로 넣은
  둘을 뺀 값이다. PlayMode 가 넷 줄어든 것은 `GameStateSwitchTests` 의 테스트 넷이다.

- **Static check:** 지운 것들을 `Packages/kr.artel.sdk/{Runtime,Editor,Tests}` 와 `samples/` 에
  대고 `grep` 했다. `SceneStatePoller`, `SceneStateHashTracker`, `StateReader`,
  `ArtelStateAttribute`, `ISceneSnapshotScanner`, `ScenePollResult`, `SendsGameState`,
  `PollSceneState` 전부 0건이다. `[ArtelState]` 를 실제로 붙인 자리도 0건이고, 남은 언급 여덟은
  전부 "무엇이 왜 사라졌는가" 를 적은 산문이다.

### 숫자로 대조하는 acceptance criterion

"pulse 로 읽히는 값의 수가 기존 `[ArtelState]` 로 읽히던 것 이상이다 — 숫자로 대조한다" 는
**이 작업에서 절반만 잰다.**

- **잰 쪽.** `[ArtelState]` 가 붙은 멤버는 `samples/WordVenture` 의 C# 파일 62개 전체에서
  **둘**이다 — `Assets/Scenes/Test/TrackingTest.cs` 의 `trackingInt` 와 `trackingString` 이고,
  그 파일은 이름 그대로 테스트 씬의 것이다. SDK 안에는
  `Tests/Fixtures/TrackedFixtureBehaviour.cs` 의 `Hp` 하나뿐이었다. 즉 이 채널이 실제 게임에서
  읽어 낼 수 있었던 값은 **한 컴포넌트의 두 개**가 전부다.
- **재지 않은 쪽.** 한 런에서 `pulse` 가 실제로 읽는 값의 개수. 그것을 세려면 WordVenture 를
  빌드하고 evidence 를 스캔해 watch list 를 구운 뒤 스택을 띄워야 한다. 이 작업에서는 하지
  않았다.
- **닫으려면 무엇을 돌려야 하는가.** `qa-run-local` skill 로 로컬 스택과 WordVenture 빌드를
  띄우고, `[ArtelState]` 가 아직 살아 있는 `develop` 빌드에서 `GAME_STATE` 한 장의
  `states` 개수와 같은 런의 `PULSE` 한 장이 싣는 멤버 개수를 세어 견준다. 이 PR 이 머지된 뒤에는
  `GAME_STATE` 쪽에 셀 것이 남지 않으므로, 그 대조는 **머지 전에** 해야 한다.
- **인용할 수 있지만 이 측정이 아닌 것.** `artel-agent-server/app/qa/scene.py:386` 의 실측
  주석 — 한 런에서 `GAME_STATE` 0장, `PULSE` 14,489장. 채널이 도는지를 말하지, 값의 개수를
  말하지 않는다.

## Risks & Rollback

- **Risks:**
  - `GAME_STATE` 를 요청에만 답하게 두면서 스위치를 빼면, `scan_scene` 을 보내는 클라이언트가
    ERROR 대신 씬을 받는다. agent 는 ARTEL-516 이후 보내지 않고, 보내는 것은 로컬 test page
    하나다.
  - `states` 키가 사라지면 그 키를 필수로 읽는 소비자가 깨진다. orchestration 의
    `SdkComponent.states` 는 기본값이 있어 안전하다. `ALL_SCENES` 는 로컬 test page 말고 파싱하는
    곳이 없다.
  - `--full` 의 상태 읽기가 `StateReader` 에서 `ScannedTarget` 으로 옮겨 간다. 정렬 순서와 태그
    값이 달라지면 `--full` 을 읽는 쪽이 조용히 달라진다. 기존 `--full` 테스트가 잡는다.
- **Rollback steps:** `git revert`.

## WordVenture 가 함께 고쳐져야 한다

`samples/WordVenture` 는 submodule 이고 다른 저장소다(`project-artel/word-venture`).
`Assets/Scenes/Test/TrackingTest.cs` 가 `[ArtelState]` 를 둘 쓰므로, 이 PR 이 머지되면 그
파일은 SDK 에 대고 컴파일되지 않는다. 그 저장소에서 두 줄을 지우고 submodule 포인터를 올리는
것이 후속 작업이다. 이 PR 은 저장소 밖을 건드리지 않으므로 포인터를 그대로 둔다.

## Open Questions

- `StateDto.tag` 는 이제 언제나 빈 문자열이다. wire 에서 빼려면 orchestration 의
  `SdkState.tag` 에 기본값을 주는 교차 저장소 변경이 필요하다. 후속 이슈로 낸다.
- `GameStateTransformer` 와 agent 쪽 `observables` 는 이제 영영 비어 있다. 그 둘과
  `MessageType.GAME_STATE` 의 폐기는 이 저장소 밖의 일이다.
