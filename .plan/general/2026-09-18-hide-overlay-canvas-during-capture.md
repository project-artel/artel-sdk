# 2026-09-18 — screen capture 하는 프레임에 Artel Overlay Canvas 도 끈다

- Date: 2026-09-18
- Jira: ARTEL-905
- Status: Implemented

## Goal

`ScreenCapturer` 가 back buffer 를 읽는 프레임에서 `Artel Overlay Canvas` (우상단 토글 버튼, 패널, window
label, run status 줄, 로그인 게이트) 도 그리지 않는다. [ARTEL-881](https://artel-asm.atlassian.net/browse/ARTEL-881)
이 `Artel Keyboard Status Canvas` 에 해 둔 것과 같은 모양을 이 캔버스에도 적용한다. `capture_screen` 이
올리는 이미지와 evidence scan 이 모으는 scene thumbnail 두 경로 모두에 적용된다.

## Non-goals

- virtual cursor 는 그대로 캡처에 남긴다 (`ScreenCapturer.cs` 주석).
- `Instrument` 표시된 것을 전부 끄는 일반 규칙은 만들지 않는다. 끄는 것은 캔버스 두 개(키보드 상태,
  Artel Overlay)다.
- 사람이 보는 WebRTC stream 에서 캡처 한 번마다 한 프레임 캔버스가 사라지는 것은 ARTEL-881 이 이미
  받아들인 비용이고, 여기서 다시 열지 않는다.
- `Affordance/Scan/SceneEvidenceScan.cs`, `Affordance/Scan/Instrument.cs`, `KeyboardStatusController.cs`
  는 건드리지 않는다. 다른 작업이 그 파일들을 쓰고 있고, `KeyboardStatusController` 의 hide/show 는
  이미 동작하는 것을 그대로 둔다.

## Context / Constraints

- `ArtelOverlayController.CreateGui`가 `Artel Overlay Canvas`를 만들고 그 아래 `Artel Button`,
  `Artel Panel`, `Artel Overlay Cover`, `Artel Window Label`, `Artel Run Status`를 매단다. 전부 같은
  캔버스의 자식이므로, 그 캔버스 하나의 `Canvas.enabled`를 끄면 전부 함께 안 그려진다.
- `ScreenCapturer.Capture`는 `WaitForEndOfFrame` 뒤에 `ScreenCapture.CaptureScreenshotIntoRenderTexture`로
  back buffer를 잡는다. 끄는 것은 `yield return endOfFrame` **앞**이어야 한다.
- 기존 `try`/`finally`를 위로 늘리지 않는다. `RenderTexture.active` 복원이 end-of-frame의 값에 기대고
  있고, `ScreenVideoSource.CaptureFrame`의 주석이 그 복원에 기댄다. ARTEL-881과 같은 이유로 바깥
  `try`/`finally`를 그대로 재사용한다.
- 런타임에 `FindObjectOfType`을 쓰지 않는다. `ArtelManager.EnsureRuntime`이 `ArtelOverlayController`를
  이미 `AddComponent`로 들고 있으므로, 그 참조를 지역 변수로 잡아 `ScreenCapturer`에 넘긴다.
- 참조 검사는 `?.`가 아니라 `!= null`이다. 파괴된 `UnityEngine.Object`의 수명 검사를 건너뛰지 않기
  위해서다.

## Approach (Checklist)

- [x] **Step 1: `ArtelOverlayController`가 끄고 켜는 것과 그 상태를 함께 갖는다** — `CreateGui`가 만드는
      `Canvas`를 필드(`canvas`)로 저장하고, `KeyboardStatusController`와 같은 모양의
      `internal void HideForCapture()` / `internal void ShowAfterCapture()`를 추가한다. 자기가 껐는지를
      `hiddenForCapture` bool로 기억한다.
- [x] **Step 2: `ScreenCapturer`가 두 캔버스 모두를 부른다** — 생성자가 `KeyboardStatusController`에 더해
      `ArtelOverlayController`도 받는다(둘 다 nullable). `yield return endOfFrame` 앞에서 둘 다
      `HideForCapture()`를 부르고, 기존 바깥 `finally`에서 둘 다 `ShowAfterCapture()`를 부른다.
      두 concrete 타입을 그대로 유지하고 인터페이스나 리스트로 묶지 않는다 — 인터페이스 타입 참조는
      파괴된 `UnityEngine.Object`에 대해 `!= null`이 기대하는 수명 검사를 우회하기 때문이다.
- [x] **Step 3: 배선** — `ArtelManager.EnsureRuntime`이 `ArtelOverlayController`를 지역 변수로 잡아
      `new ScreenCapturer(keyboardStatus, overlayController)` 두 자리(`ActionExecutor`용,
      `WalkedEvidenceScan`용)에 넘긴다.
- [x] **Step 4: Tests** — `Tests/Runtime/WebSocketTransportTests.cs`의 기존 `WithOverlay` 헬퍼(Awake·Start를
      reflection으로 부르는 시임)를 재사용해 네 개를 더한다: `HideForCapture`가 캔버스를 끈다,
      `ShowAfterCapture`가 다시 켠다, 이미 꺼져 있던 캔버스는 뒤에도 꺼진 채다, 겹친 캡처(Hide·Hide·
      Show·Show) 뒤에 캔버스가 켜져 있다.
- [ ] **Step 5: Manual verification** — 로컬 QA run에서 `capture_screen` 결과 이미지와 evidence scan
      thumbnail에 캔버스가 없고, 게임 창에는 그대로 있는 것을 확인한다. 이 worktree는 `/home` 아래라
      QA 런을 띄우는 씬 실행 자체가 닿지 않아 이번 세션에서는 못 했다. PR의 "확인하지 못한 것"에
      적는다.

## Validation

- **Commands run:** `.github/scripts/setup-unity-test-project.sh /mnt/c/tmp/artel-unity-test-905` 로
  `/mnt/c` 아래 테스트 프로젝트를 만들고, Windows 쪽 `Unity.exe -batchmode -nographics -runTests`로
  EditMode 와 PlayMode 를 각각 돌렸다.
- **Result:** EditMode 448/448 (새 테스트 4개 포함), PlayMode 50/50 모두 통과.
- **Not run:** Step 5의 손 검증(로컬 QA run에서 실제 캡처 이미지 확인). 이 worktree 에서는 QA 런을
  띄울 수 없어 이 이슈에서는 하지 못했다.

## Risks & Rollback

- **Risks:** ARTEL-881과 같은 위험군이다 — coroutine이 hide와 grab 사이에서 멈추면 캔버스가 꺼진 채
  남을 수 있고, 겹친 캡처는 먼저 끝난 쪽이 캔버스를 다시 켜 뒤쪽 이미지에 한 번 찍힐 수 있다(패널이
  꺼진 채 남지는 않는다).
- **Rollback:** `git revert`로 이 PR의 커밋을 되돌린다. 프로토콜과 서버 계약은 바뀌지 않는다.

## Rejected feedback

- **`ScreenCapturer`에 `ICaptureHideable` 같은 인터페이스나 리스트를 두어 두 캔버스를 하나로
  묶자.** 인터페이스 타입으로 들고 있으면 `!= null` 이 `UnityEngine.Object`의 오버로드가 아니라
  일반 참조 비교로 풀려, 파괴된 오브젝트를 산 것으로 잘못 읽을 수 있다 — 이 이슈가 `?.` 대신
  `!= null`을 못 박은 것과 같은 위험을 인터페이스로 다시 들여오는 셈이다. 두 개뿐인 concrete 타입
  필드가 더 안전하다.
- **`IDisposable` scope로 hide/show를 묶자.** ARTEL-881에서 이미 기각됐다 — 호출부가 하나뿐이고 그
  자리에 `finally`가 이미 있다. 이 변경도 그 자리를 그대로 쓴다.

## Open Questions

- 없음.
