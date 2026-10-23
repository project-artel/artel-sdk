# 2026-10-23 — QA New Input System 마우스 입력 주입 추가

- Date: 2026-10-23
- Jira: ARTEL-1263
- Status: Implementation and local validation complete; draft PR submission pending

## Goal

기존 QA 명령으로 Legacy와 New Input System 게임에 위치, 좌/우/가운데 버튼, 드래그를 전달한다. InputAction과 uGUI에서 클릭을 한 번만 받고 종료 후 눌린 입력이 남지 않는다.

## Non-goals

New Input System 키보드/gamepad 주입, 게임별 binding 변경, OS 커서 이동, QA 프로토콜 변경.

## Context / Constraints

ARTEL-1233 / PR #158의 LegacyInput guard 위에 쌓는다. 기존 작업 checkout은 수정하지 않는다. Input System 1.7.0을 package와 runtime asmdef에 명시해 Unity 2022.3에서 컴파일하고 ENABLE_INPUT_SYSTEM으로 runtime 사용을 분리한다. Legacy만 활성화한 프로젝트에서는 기존 동작을 유지한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — ActionExecutor, ArtelInput, PointerEventDispatcher, VirtualMouseMessenger, RUN_STATUS 종료 경로 확인.
- [x] **Step 1: Implementation** — NewInputMouse adapter를 ArtelInput에 연결한다. 하나의 가상 Mouse를 사용하고 전체 MouseState에서 위치/버튼과 이동 delta를 보존한다. press/release가 같은 input update에 사라지지 않게 순서와 프레임을 보장한다. New Input System 이벤트가 UI에서 처리될 때 직접 PointerEventDispatcher를 사용하지 않는다. Legacy StandaloneInputModule은 기존 dispatcher를 유지한다. Collider OnMouse 경로도 유지한다.
- [x] NewInputMouse는 실제 Mouse 이벤트를 관찰해 물리 위치를 따로 보관하고, QA가 포인터를 소유할 때 실제 Mouse 이벤트가 가상 Mouse를 덮지 않게 막는다. 기존 HeldForRun 및 4px 제어권 반환 정책을 유지한다. Ctrl+Shift+M은 New-only에서도 실제 Keyboard를 읽는다.
- [x] Mouse를 소유한 InputUser가 하나인 경우만 가상 Mouse를 추가 pairing한다. 복수 사용자 환경은 명령을 명시적으로 거절한다. 매 input update에 사용자 변화를 확인하며 게임의 bindings/control scheme은 변경하지 않는다.
- [x] ReleaseAllVirtualInput/reset/disconnect/destroy에서 가상 버튼 release와 장치/이벤트 구독 정리를 보장한다. UI release가 처리되기 전에 장치를 제거하지 않는다. 재사용/즉시 재시작도 검증한다.
- [x] PressOutcome은 native UI 처리가 지난 뒤 포인터 소유 상태를 읽으며 native 경로의 reached는 null로 반환한다. InputAction-only 클릭은 대상이 없을 수 있고, 이를 사용자 제어권 반환으로 오판하지 않는다.
- [x] **Step 2: Tests** — 실제 InputAction과 InputSystemUIInputModule을 사용한 PlayMode 테스트로 이동/세 버튼/alias/drag/delta/단일 click/cleanup/물리 입력 격리/pairing 검증. Legacy, Both, New-only 설정을 별도 test project로 실행하고 CI matrix에도 반영한다. 선행 commit baseline을 먼저 실행해 기존 실패를 분리한다.
- [x] **Step 3: Rollout / Rollback** — 지원 범위와 dependency를 문서화하고 ARTEL-1233 위 draft PR로 제출한다. 이 PR의 commit revert로 원복한다.

## Validation

- **Commands to run:** setup-unity-test-project.sh로 throwaway project를 준비한 뒤 Unity 2022.3.34f1 -batchmode -nographics -runTests -testPlatform EditMode/PlayMode. Input handling 0/1/2를 별도 project로 구성한다.
- **Expected output:** Legacy 회귀와 New/Both 입력 경계 테스트가 통과하고 새 컴파일 오류가 없다. baseline 실패가 있으면 동일 테스트 비교 및 PR에 기록한다.

## Risks & Rollback

- **Risks:** PlayerInput pairing, 실제 장치 이벤트 충돌, input update와 EventSystem.Update 순서, 정리 중 drag/click 중복, 외부 장치 제한이 있는 게임.
- **Rollback steps:** 이 PR commit을 revert한다. 선행 guard PR은 유지한다.

## Open Questions

- 없음. 지원하지 않는 게임별 binding 구성과 실제 플랫폼 실행 범위는 테스트 결과에 맞춰 문서화한다.

## Review decisions

- 버튼과 소유권의 유일한 기준은 기존 VirtualMouseState다. adapter는 onBeforeUpdate에서 해당 frame 상태를 MouseState로 변환한다. 별도 버튼 queue나 press/release 타이머를 만들지 않는다.
- Dynamic/Fixed update를 지원한다. Manual Update 및 timeScale=0인 Fixed 모드는 마우스 명령을 명시적으로 거절한다. 처리 완료는 onAfterUpdate의 frame 확인 뒤 다음 rendered frame까지 기다리며, 업데이트가 중단되면 2초 unscaled timeout으로 실패하고 입력을 해제한다.
- native UI의 실제 handler 수신 결과를 public API로 안정적으로 추적할 수 없으므로 native 경로의 reached는 null이다. 후속 raycast를 수신 증거로 보고하지 않는다. pointerHeldByPerson은 포인터 소유권으로 판정한다. Legacy 경로의 수신 대상은 유지한다.
- 종료 정리는 기존 ReleaseAll과 같은 release 의미다. 같은 대상에서 놓으면 click이 발생할 수 있다. adapter는 ResetDevice로 즉시 action을 취소하고 다음 rendered frame의 LateUpdate에서 장치를 제거하며, 반복 release/즉시 재시작은 같은 장치에서 pending cleanup을 취소해 처리한다. 도메인 초기화에서는 즉시 dispose한다.
- 실제 Mouse state는 QA 시작 시 reset하여 이미 눌린 물리 버튼이 InputAction에 남지 않게 하고, 소유 중 이벤트는 물리 위치만 별도로 기록한 뒤 차단한다. 반환 때 실제 장치 sync를 요청한다.
- FAST/MEDIUM의 프레임 정책, 결과 판정, 단일 사용자, lifecycle 지적을 반영했다.

- 실행 검증에서 Editor 기본 focus 정책이 synthetic Mouse 이벤트도 게임에 전달하지 않는 것을 확인했다. QA Mouse 활성 중 복제한 InputSettings로 IgnoreFocus 및 AllDeviceInputAlwaysGoesToGameView를 사용하고 종료 시 원본 settings를 복원한다. 이벤트를 실제로 관찰한 input update만 완료로 인정한다.
- pair review의 FixedUpdate 짧은 click 및 paused cleanup 지적을 반영했다. cleanup은 ResetDevice로 즉시 action을 취소하고 전용 lifetime의 LateUpdate에서 UI 처리 후 dispose한다.

## Final validation

Unity 2022.3.34f1 batch mode에서 실제 InputAction과 InputSystemUIInputModule fixture를 실행했다. 실제 QA 서버부터 게임까지의 통합 실행은 이번 검증에 포함하지 않았다.

| Active Input Handling | EditMode | PlayMode |
| --- | --- | --- |
| Legacy (0) | 565/565 | 87/87 |
| New (1) | 565/565 | 97/97 |
| Both (2) | 565/565 | 97/97 |

- 선행 commit e2077e3의 Legacy baseline: EditMode 565/565, PlayMode 87/87.
- settings 원본과 runInBackground 복원 assertion 추가 후 Both의 NewInputMouseTests 10/10 재통과.
- New-only VirtualAxisStateTests는 선행 guard의 명시적 unsupported 결과를 검증한다. Legacy/Both의 축 주입 assertion은 유지한다.
- plan review PASS, pair review PASS. FixedUpdate의 짧은 click 및 paused cleanup 지적을 수정하고 테스트했다.
- CI matrix에 0/1/2 × EditMode/PlayMode를 추가했다. 원격 CI 결과는 PR에서 확인한다.
