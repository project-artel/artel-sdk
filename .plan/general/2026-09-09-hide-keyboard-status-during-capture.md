# 2026-09-09 — screen capture 하는 프레임에만 keyboard status 패널을 끈다

- Date: 2026-09-09
- Jira: ARTEL-881
- Status: Approved (plan-review fast/medium/heavy 통과)

## Goal

`ScreenCapturer` 가 back buffer 를 읽는 프레임에서 `Artel Keyboard Status Canvas` 를 그리지 않는다. grab 이 끝나면 패널은 곧바로 다시 보인다. `capture_screen` 이 올리는 이미지와 evidence scan 이 모으는 scene thumbnail 두 경로 모두에 적용된다.

## Non-goals

- virtual cursor 는 그대로 캡처에 남긴다. `ScreenCapturer` 주석이 일부러 넣었다고 적어 둔 것이다.
- 우상단 Artel 토글 버튼과 패널, window label 패널, run status line 은 건드리지 않는다.
- `PointerEventDispatcher` 가 `Instrument` 표시된 것을 raycast 에서 건너뛰게 하는 것은 별개 이슈다. 이 패널은 `raycastTarget` 이 꺼져 있어 클릭을 먹지 않으므로 여기서는 문제가 되지 않는다.
- 패널을 아예 안 그리는 선택지, 실행 인자로 끄는 선택지는 이번 범위가 아니다. 사용자가 셋 중 "캡처에서만 뺀다"를 골랐다.
- `Instrument` 표시된 것을 전부 끄는 일반 규칙은 만들지 않는다. 이번에 빼는 것은 패널 하나다.

## Context / Constraints

- 패널은 `KeyboardStatusController.CreateGui` 가 만드는 `Artel Keyboard Status Canvas` 하나에 다 들어 있다. 화면 하단 중앙 720x96 이고 `PRESSED KEYS` 와 포인터 좌표를 그린다.
- `ScreenCapturer.Capture` 는 `WaitForEndOfFrame` 뒤에 `ScreenCapture.CaptureScreenshotIntoRenderTexture` 로 back buffer 를 잡는다. 그러니 canvas 를 끄는 것은 `yield return endOfFrame` **앞** 이어야 한다. 뒤에서 끄면 그 프레임은 이미 그려진 뒤다.
- 추가로 한 프레임을 더 기다릴 필요는 없다. coroutine 이 `Update` 에서 시작하면 렌더가 뒤에 오고, 직전 캡처의 end-of-frame 에서 이어서 시작하면 `WaitForEndOfFrame` 이 다음 프레임 끝까지 기다린다. 두 경우 모두 grab 하는 프레임은 canvas 를 끈 뒤에 그려진다.
- `Instrument` 는 이 패널을 이미 표시해 두었지만 그것은 scene scan 보고에서 빼는 표시이고, 컴포넌트 주석이 "보고의 문제이지 렌더링의 문제가 아니다" 라고 못 박아 두었다. 이번 변경은 그 문장의 예외를 하나 만든다. 이유는 캡처가 back buffer 를 통째로 읽기 때문이다 — 글자 보고는 표시를 보고 거를 수 있지만, back buffer grab 에는 거를 자리가 없다. 안 그리는 것이 패널을 이미지에서 빼는 유일한 방법이다. 예외는 이 패널 하나로 한정하고, `Instrument` 주석에 그 사실을 적는다.
- `ScreenVideoSource` 도 같은 back buffer 를 읽는다. 캡처 한 번마다 사람이 보는 WebRTC stream 에서 한 프레임 패널이 사라진다. 10fps stream 이면 100ms 다. 사용자가 이 값을 알고 "캡처에서만 뺀다"를 골랐으므로 열린 질문이 아니라 받아들인 비용이다.
- 런타임에 `FindObjectOfType` 을 쓰는 자리가 하나도 없다. Unity 6 에서 폐기된 API 라 버전 분기가 필요해지므로 쓰지 않고, `ArtelManager` 가 이미 들고 있는 참조를 `ScreenCapturer` 에 넘긴다.
- static 참조도 두지 않는다. 테스트가 공유 상태를 지나게 된다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — `ScreenCapturer`, `KeyboardStatusController.CreateGui`, `ArtelManager.EnsureRuntime` 의 현재 계약을 확인한다. 완료.
- [ ] **Step 1: 컨트롤러가 끄고 켜는 것과 그 상태를 함께 갖는다** — `KeyboardStatusController` 는 지금 `canvasObject` 만 들고 있으므로 `CreateGui` 에서 `Canvas` 를 필드에 함께 담는다. 그 위에 `internal void HideForCapture()` 와 `internal void ShowAfterCapture()` 를 넣고, 자기가 껐는지를 private bool 하나로 기억한다. `HideForCapture` 는 canvas 가 살아 있고 `enabled` 일 때만 끄고 그 bool 을 세운다. 원래 꺼져 있었으면 아무것도 하지 않는다. `ShowAfterCapture` 는 그 bool 이 서 있을 때만 다시 켠다. 호출자는 지켜야 할 규약이 없다. `GameObject.SetActive` 가 아니라 `Canvas.enabled` 를 쓴다 — 계층과 `Text` 값을 그대로 두고 렌더만 멈추는 것이 가장 싸고, `OnEnable`/`OnDisable` 이 돌지 않는다.
- [ ] **Step 2: 캡처가 그 두 메서드를 부른다** — `ScreenCapturer` 가 생성자로 `KeyboardStatusController` 를 받는다 (null 허용). `Application.isBatchMode` 조기 반환 뒤, `yield return endOfFrame` 앞에서 `HideForCapture()` 를 부르고, `finally` 에서 `ShowAfterCapture()` 를 부른다. 이때 기존 `try` 를 위로 늘리지 않고 **바깥 `try`/`finally` 를 하나 더 둔다**. 기존 `try` 를 늘리면 `var previous = RenderTexture.active;` 도 `yield return endOfFrame` 앞으로 올라가고, 그러면 end-of-frame 의 active target 대신 Update phase 의 값을 복원하게 된다. `ScreenVideoSource.CaptureFrame` 주석이 바로 그 복원에 기대고 있으므로 건드리면 안 된다. 참조 검사는 `?.` 가 아니라 `!= null` 로 쓴다 — `?.` 는 파괴된 `UnityEngine.Object` 의 수명 검사를 건너뛴다.
- [ ] **Step 3: 배선** — `ArtelManager.EnsureRuntime` 이 `KeyboardStatusController` 를 지역 변수로 잡아 `new ScreenCapturer(keyboardStatus)` 두 자리에 넘긴다. `ActionExecutor` 용과 `WalkedEvidenceScan` 용이다.
- [ ] **Step 4: Tests** — `KeyboardStatusControllerTests` 에 네 개를 더한다. `HideForCapture` 가 canvas 를 끈다, `ShowAfterCapture` 가 다시 켠다, 이미 꺼져 있던 canvas 는 `HideForCapture`/`ShowAfterCapture` 뒤에도 꺼진 채다, 겹친 캡처를 흉내낸 Hide·Hide·Show·Show 뒤에 canvas 가 켜져 있다. 기존 테스트처럼 reflection 으로 `Awake` 를 먼저 불러야 한다 — 안 부르면 canvas 가 없어 네 개가 전부 무의미하게 통과한다. 픽셀 경로와 `ScreenCapturer` 안의 호출 순서는 화면이 있어야 하므로 CI 가 아니라 Step 5 의 수동 검증이 맡는다. 이 한계를 PR 에 적는다.
- [ ] **Step 5: Manual verification** — 로컬 QA run 에서 `capture_screen` 결과 이미지에 패널이 없고, 게임 창에는 패널이 그대로 있는 것을 확인한다. 연속 캡처 10회에서 패널이 꺼진 채 남는 경우가 없는지 본다.
- [ ] **Step 6: Rollout / Rollback** — feature flag 없이 기본 동작이다. 문제가 있으면 PR 을 revert 한다.

## Validation

- **Commands to run:** Windows 쪽 Unity 에디터로 `/mnt/c` 경로의 테스트 프로젝트에서 EditMode 전체와 PlayMode 전체.
- **Expected output:** 기존 테스트가 그대로 통과하고 새 테스트 4개가 통과한다. 수동 검증 결과는 PR 에 적는다.

## Risks & Rollback

- **Risks:** coroutine 이 중간에 멈추면 `finally` 가 돌지 않아 패널이 꺼진 채 남을 수 있다. 같은 위험을 지금도 `RenderTexture` 해제가 지고 있으므로 새로 생기는 위험은 아니다. Unity 2023.1 이후로는 비활성 MonoBehaviour 의 coroutine 이 `WaitForEndOfFrame` 에서 깨어나지 않으므로, hide 와 grab 사이에 `ArtelManager` 가 disable 되면 패널이 꺼진 채 남는다. 같은 위험군이다. 캡처 두 개가 겹치면 (`capture_screen` 과 evidence scan) 먼저 끝난 쪽이 패널을 다시 켜서 뒤쪽 이미지에 패널이 한 번 찍힐 수 있다. 상태가 컨트롤러 하나에 있으므로 패널이 꺼진 채 남지는 않는다. `ActionExecutor` 가 action 을 차례로 돌리므로 겹칠 일 자체가 드물다.
- **Rollback steps:** `git revert` 로 PR 커밋을 되돌린다. 프로토콜과 서버 계약은 바뀌지 않는다.

## Rejected feedback

- **`IDisposable` scope 로 hide/show 를 묶자.** 호출부가 하나뿐이고 그 자리에 `finally` 가 이미 있다. 상태를 컨트롤러로 옮긴 것으로 규약 문제는 사라지므로, 타입을 하나 더 만드는 값이 맞지 않는다.
- **`ScreenCapturer` 에 delegate seam 을 두어 호출 순서를 테스트하자.** `Capture` 는 framebuffer 없이는 돌지 않고 CI 는 `-batchmode` 라 조기 반환한다. seam 을 넣어도 그 순서를 CI 에서 확인할 수 없으므로 간접 층만 남는다.
- **`ActionExecutor` 에서 끄자.** 그러면 `capture_screen` 만 덮이고 `WalkedEvidenceScan` 쪽 thumbnail 은 같은 코드를 한 번 더 써야 한다. 두 경로가 모두 `IScreenCapturer` 를 지나므로 `ScreenCapturer` 안이 중복 없는 유일한 자리다.
- **stream 한 프레임 결손을 열린 질문으로 올리자.** 사용자가 이 비용을 듣고 세 선택지 중에서 골랐다. 결정된 값이라 열린 질문이 아니다.
- **grab 전에 `yield return null` 을 한 번 더 두자.** heavy 검토가 두 호출 사슬을 따라가 확인했다. `capture_screen` 은 `ArtelManager.Update` 에서 시작하고, scene walk 도 Update phase 에서 이어진다. 렌더 뒤에 주어지는 resume point 는 `WaitForEndOfFrame` 하나뿐이고 그것은 다음 프레임 끝에 깨어나므로, 어느 쪽이든 grab 하는 프레임은 hide 뒤에 그려진다.

## Open Questions

- 없음.
