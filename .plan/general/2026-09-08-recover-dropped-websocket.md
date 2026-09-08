# 2026-09-08 — 끊긴 웹소켓이 재시도로도 연결 버튼으로도 돌아오게 한다

- Date: 2026-09-08
- Jira: ARTEL-842
- Status: Approved (pair-review 1회 반영)

## Goal

소켓이 한 번 끊긴 뒤 SDK 가 스스로, 또는 사람이 overlay 의 연결 버튼을 눌러 반드시 돌아오게
한다. ARTEL-599 가 자동 재연결을 넣은 뒤에도 남아 있는 네 구멍을 함께 막는다.

1. 자동 재시도가 8회(약 121초)에서 영영 멈춘다.
2. `Connecting` 에서 굳은 소켓이 재시도와 연결 버튼을 둘 다 막는다.
3. 재연결이 생성자에서 굳은 URL 을 다시 써서, 토큰을 refresh 하거나 인스턴스를 다시 등록해도
   낡은 자격증명으로 건다.
4. 연결 버튼이 실제 결과와 무관하게 성공을 알리고, overlay 는 소켓이 끊긴 것을 그리지 않는다.

## Non-goals

- orchestration-server 와 proxy 설정 변경.
- 끊긴 동안 보내려던 메시지의 queue 와 재전송.
- 스트리밍(`ArtelStreamHost`) 재협상.
- 로컬 테스트 페이지(`ArtelWebSocketServer`) 의 연결 동작 변경. 새 인터페이스 멤버를 채우는
  것까지만 한다.
- 자동 재등록. 4001 뒤의 재등록은 사람이 연결 버튼을 누르는 경로로만 둔다.

## Context / Constraints

- `OnClose` 와 재시도 타이머는 websocket-sharp 수신 스레드와 타이머 스레드다. Unity API 를
  부르면 안 된다. 반대로 overlay 는 Unity 메인 스레드이므로, 전송 상태는 main thread 가 읽어
  가는 값 하나로 건네야 한다.
- `WebSocket.Ping()` 과 `CloseAsync` 는 `WaitTime`(기본 5초) 만큼 막힌다. `gate` 를 쥔 채 부르지
  않는다.
- websocket-sharp 의 handshake 에는 마감이 없다. TCP 가 열린 뒤 101 이 오지 않으면 소켓은
  `Connecting` 에 머물고 `OnClose` 도 오지 않는다.
- 서버는 (project, sdkUuid) 로 인스턴스를 찾거나 만든다(`SdkRegistrationService.findOrCreateInstance`).
  재등록은 idempotent 이므로 돌고 있는 런의 instanceId 를 잃지 않는다.
- 서버는 `sceneScan` 이 없는 등록에서 저장된 scan 을 지우지 않는다
  (`SdkRegistrationService.findOrCreateBuild` 의 `sceneScan == null -> existing`). 그래서 연결
  버튼의 재등록은 씬 walk 를 건너뛸 수 있다 — 그 walk 는 씬을 하나씩 load 하고 unload 하므로
  돌고 있는 게임 위에서 다시 걸으면 안 된다.
- SDK access token 의 수명은 30일(`AuthProperties.sdkTokenTtl`), refresh token 은 90일이다.
- 이 저장소는 Unity 프로젝트가 아니다. 테스트는 `.github/scripts/setup-unity-test-project.sh` 가
  만드는 임시 프로젝트에서 돈다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 확인 완료.
  - `Packages/kr.artel.sdk/Runtime/ArtelWebSocketClient.cs` — `TryReconnectDelay` 의 8회 상한,
    생성자에서 굳는 `url`, `Start()` 의 `IsLive` 조기 반환.
  - `Packages/kr.artel.sdk/Runtime/ArtelManager.cs:449` — `StartTransport` 가 실패를 로그로만 알린다.
  - `Packages/kr.artel.sdk/Runtime/ArtelOverlayViewModel.cs:471` — `Connect` 가 예외가 없으면
    Connected 로 올린다.
  - `Packages/kr.artel.sdk/Runtime/ArtelOverlayController.cs:348` — 연결 버튼이 `StartTransport`
    만 부른다.
  - `Packages/kr.artel.sdk/Tests/Runtime/WebSocketTransportTests.cs:310` — 기존 backoff 테스트가
    8회 상한을 값으로 검증한다. 함께 고친다.

- [ ] **Step 1: 전송이 자기 상태를 말한다**
  - `Packages/kr.artel.sdk/Runtime/ArtelTransportPhase.cs` 를 새로 만든다. 값은 넷:
    `Idle`, `Connecting`, `Connected`, `Refused`.
  - `IArtelWebSocketTransport` 에 `ArtelTransportPhase Phase { get; }` 를 더한다. 구현은
    `ArtelWebSocketClient`, `ArtelWebSocketServer`, 그리고 테스트의 fake 여섯이다. 전송이 아닌
    쪽에서 `as ArtelWebSocketClient` 로 내려다보지 않으려면 인터페이스가 맞다.
  - `ArtelWebSocketClient` 는 `gate` 아래에서 쓰고 잠금 없이 읽는 필드 하나로 들고 있는다.
    overlay 가 프레임마다 읽으므로, 그 읽기가 dial 뒤에서 막히면 안 된다.

- [ ] **Step 2: 재시도가 멈추지 않는다**
  - `TryReconnectDelay` 에서 시도 횟수 상한을 없앤다. 4001 은 그대로 false — 같은 자격증명으로
    다시 걸어 봐야 같은 대답이고, 그 회복은 연결 버튼의 재등록이다.
  - backoff 수열(1, 2, 4, 8, 16, 30 …)과 30초 상한은 그대로 둔다.

- [ ] **Step 3: 굳은 handshake 를 버린다**
  - `Connect()` 가 dial 마다 마감 타이머를 건다. 20초 안에 `OnOpen` 이 오지 않으면 그 소켓을
    버리고 다시 건다. `OnOpen` 과 `HandleClose` 가 그 타이머를 끈다.
  - 재시도 타이머 콜백을 try/catch 로 감싼다. 콜백에서 예외가 새면 그 뒤로 다시 걸 일이 없다.

- [ ] **Step 4: dial 마다 지금 자격증명을 읽는다**
  - `ArtelWebSocketClient` 생성자를 `Server` 와 자격증명 provider 둘(`Func<string>`) 로 바꾼다.
    `Connect()` 가 `BuildEndpoint` 를 그때 부른다.
  - provider 가 읽는 것은 `ArtelSdkSession` 이 아니라 `ArtelManager` 가 들고 있는 스냅샷 두 개다.
    `ArtelSdkSession` 을 재연결 타이머 안에서 바로 읽으면 `PlayerPrefs` 를 메인 스레드 밖에서
    만지고, 만료된 토큰을 만나면 그 자리에서 `Clear` 가 세션을 지우며, macOS 의 비밀 저장소는
    `/usr/bin/security` 를 최대 15초 기다린다(그동안 전송의 자물쇠가 잡혀 `StopTransport` 까지
    멈춘다). 스냅샷은 `StartTransport` 안에서만, 즉 메인 스레드에서만 다시 뜬다 — 토큰과
    instanceId 가 달라지는 자리는 로그인과 등록 둘뿐이고 그 둘은 끝에서 반드시 그것을 부른다.
  - 자격증명이 비어 dial 을 만들 수 없으면 `Refused` 로 두고 로그를 남긴다. 재시도로는 나아지지
    않고, 연결 버튼의 재등록이 그 값을 채운다.

- [ ] **Step 5: 연결 버튼이 등록부터 다시 한다**
  - `ArtelManager.StartTransport` 를 `bool` 로 바꾼다. dial 을 시작하지 못한 두 경로(자격증명
    없음, 남의 전송)가 false 다.
  - `ArtelOverlayViewModel.Connect` 가 `Func<bool>` 을 받는다. false 면 실패로 적고, true 면
    Connected 가 아니라 `Connecting` 으로 둔다. 실제 Connected 는 Step 6 이 올린다.
  - `ArtelOverlayController.ConnectWebSocket` 이 씬 walk 없는 재등록 coroutine 을 돈다.
    `viewModel.Register(...)` 에 캐시된 scan(없으면 null)을 그대로 넘긴다. 등록이 토큰 refresh 와
    instanceId 를 새로 하고, 그 끝에서 `StartTransport` 를 부른다.

- [ ] **Step 6: overlay 가 실제 상태를 그린다**
  - `ArtelOverlayViewModel.NoticeTransport(ArtelTransportPhase)` 를 더한다. 등록을 마친 뒤
    (`Connecting`/`Connected`) 에만 쓰고, 상태가 실제로 달라졌을 때만 `Changed` 를 올린다.
  - `ArtelOverlayController.Update` 가 프레임마다 `artelManager.TransportPhase` 를 넘긴다.
  - 끊김은 gate 를 올리지 않는다. 게임 위에 전체 화면 덮개를 씌우지 않고 패널 문구와 색으로만
    알린다.

- [ ] **Step 7: Tests**
  - `WebSocketTransportTests` 의 `ReconnectDelay_GivesUpAfterEightAttempts` 를 "상한 없이 계속
    다시 건다" 로 바꾼다. 4001 거절과 backoff 수열 테스트는 그대로 둔다.
  - `ArtelOverlayViewModel` 의 새 경로를 EditMode 로 덮는다: `Connect` 가 false 를 실패로
    적는 것, `NoticeTransport` 가 끊김을 그리는 것, 다시 붙었을 때 Connected 로 돌아오는 것.
  - handshake 마감과 무한 재시도는 시계에 기대므로 단위 테스트하지 않는다. PR 에 그대로 적는다.

- [ ] **Step 8: Rollout / Rollback**
  - flag 없이 나간다. 되돌리기는 `git revert` 한 번.

## Validation

- **Commands to run:**
  ```bash
  .github/scripts/setup-unity-test-project.sh /mnt/c/temp/artel-unity-test
  "/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1/Editor/Unity.exe" \
    -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath 'C:\temp\artel-unity-test' \
    -testResults 'C:\temp\artel-unity-test\results-editmode.xml' \
    -logFile 'C:\temp\artel-unity-test\unity-editmode.log'
  python3 .github/scripts/summarize-test-results.py /mnt/c/temp/artel-unity-test/results-editmode.xml EditMode
  ```
  `-testPlatform PlayMode` 로 한 번 더 돌린다.
- **Expected output:** 두 스위트 모두 green. 변경 전 merge-base 에서 기준선을 먼저 잡는다.

## Risks & Rollback

- **Risks:**
  - 상한 없는 재시도가 서버를 두드린다. 30초 상한이 인스턴스당 분당 두 번으로 묶는다.
  - handshake 마감이 너무 짧으면 느린 네트워크에서 멀쩡한 연결을 우리 손으로 버린다. 20초는
    Windows 의 SYN 재시도(약 21초)보다 짧지 않게 두어, TCP 가 스스로 포기하는 경우를 앞지르지
    않는다.
  - `IArtelWebSocketTransport` 에 멤버를 더하면 fake 여섯이 함께 깨진다. 컴파일이 잡아 준다.
  - 연결 버튼의 재등록이 등록 화면 덮개를 잠깐 띄운다. 사람이 직접 누른 자리이므로 그대로 둔다.
- **Rollback steps:** `git revert`.

## Pair review

`pair-review-critic` 가 NONPASS 를 내고 넷을 지적했다. 넷 다 반영했다.

1. **must-fix — 자격증명을 타이머 스레드에서 읽는다.** provider 가 `ArtelSdkSession` 을 바로
   읽고 있었고, 그 자리는 재연결 타이머이며 전송의 자물쇠를 쥔 채였다. Step 4 를 스냅샷으로
   고쳤다.
2. **should-fix — `AbandonStalledHandshake` 에 재시도도 없고 놓아주지도 않는 출구가 있었다.**
   마감이 왔을 때 `Connecting` 이 아닌 소켓을 그냥 두고 나갔다. `Open` 일 때만 물러서고 나머지는
   전부 버리고 다시 걸도록 바꿨다.
3. **should-fix — 실패한 연결이 게이트를 올리고, 그 뒤로 내려오지 못했다.** `Fail` 이
   `ChoosingProject` 로 보내면 전체 화면 덮개가 서는데, `NoticeTransport` 가 그 상태에서
   물러서기만 해서 전송이 스스로 다시 붙어도 덮개가 남았다. 소켓이 실제로 열렸고 로그인 세션도
   그대로면 그 화면이 물러나도록 `DrawsTransport` 를 두었다.
4. **should-fix — 새 dial 경로에 테스트가 없었다.** 자격증명 없이 `Start` 하면 `Refused` 가
   되는 것과, 세션 없는 `StartTransport` 가 false 를 내는 것을 EditMode 로 덮었다.

비차단 지적 둘도 받아들였다: `Math.Pow` 지수의 clamp 는 `Math.Min` 이 이미 무한대를 상한으로
접으므로 지웠고, `Connect` 가 `noticedPhase` 를 직접 쓰는 이유를 주석으로 남겼다.

## Open Questions

- 없음.
