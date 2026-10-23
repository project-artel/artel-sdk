# 2026-10-23 — QA New Input System 마우스 입력 주입 추가

- Date: 2026-10-23
- Jira: ARTEL-1263
- Status: 구현 및 로컬 검증 완료

## Goal

기존 QA 명령으로 Legacy와 New Input System 게임에 위치, 좌/우/가운데 버튼과 드래그를 전달한다. Input System 패키지가 없는 Legacy 프로젝트에서도 SDK만 설치해 컴파일과 QA 입력이 동작한다.

## Non-goals

New Input System 키보드/gamepad 주입, 게임별 binding 변경, OS 커서 이동, QA 프로토콜 변경.

## Context / Constraints

ARTEL-1233 / PR #158의 LegacyInput guard 위에 쌓는다. 기존 작업 checkout은 수정하지 않는다. 사용자 후속 요청에 따라 Input System을 필수 dependency에서 제거하고 선택적 assembly로 분리한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — ActionExecutor, ArtelInput, PointerEventDispatcher, VirtualMouseMessenger와 종료 경로를 확인했다.
- [x] **Step 1: Implementation** — VirtualMouseState를 버튼/소유권의 유일한 상태로 유지한다. 공통 NewInputMouse facade는 Input System assembly를 참조하지 않는다. optional backend가 없는 Legacy 설정에서는 기존 경로를 사용한다.
- [x] 실제 구현과 lifetime은 Artel.InputSystem assembly에 둔다. com.unity.inputsystem >=1.7.0의 versionDefine와 ENABLE_INPUT_SYSTEM을 모두 요구한다. optional -> Runtime으로만 참조하며 필요한 internal 경계만 InternalsVisibleTo로 공유한다.
- [x] SubsystemRegistration에서 이전 backend를 dispose하고 새 instance를 등록한다. reset은 등록을 지우지 않아 초기화 callback 순서에 의존하지 않는다. AlwaysLinkAssembly로 bootstrap을 보존한다.
- [x] 가상 Mouse 하나로 위치/delta/세 버튼을 전달한다. 실제 이벤트가 처리된 update만 완료로 인정하고 다음 rendered frame까지 기다린다. 짧은 FixedUpdate click도 press 처리 후 놓는다.
- [x] native UI 직접 dispatch를 생략해 중복 클릭을 막는다. native reached는 null, pointerHeldByPerson은 실제 포인터 소유권이다. Legacy dispatcher와 collider 경로는 유지한다.
- [x] 실제 Mouse 이벤트 격리, HeldForRun/4px 제어권 반환, 실제 Keyboard의 Ctrl+Shift+M, 단일 Mouse InputUser pairing을 유지한다.
- [x] release는 버튼을 즉시 reset하고 다음 rendered frame에 장치를 정리한다. lifecycle은 생성한 backend instance만 참조한다. Editor ExitingPlayMode에서 명시적으로 dispose하고 edit mode의 임시 객체는 DestroyImmediate로 정리한다.
- [x] **Step 2: Tests** — native 입력 테스트는 별도 optional assembly에 둔다. 공통 UI fixture의 native module 생성만 reflection helper로 모아 공통 테스트도 패키지 없이 컴파일한다. 기존 bootstrap manager는 테스트 중 비활성화 후 복원한다.
- [x] setup script와 CI에 무패키지 Legacy, 패키지 설치 Legacy/New/Both 네 조합을 추가했다. 기본 manifest는 패키지를 포함하지 않고 present 옵션만 추가한다. cache도 패키지 유무로 분리한다.
- [x] **Step 3: Rollout / Rollback** — PR #168과 ARTEL-1263에 최종 검증 결과를 반영한다. 후속 commit revert로 이전 구성으로 돌아간다.

## Validation

Unity 2022.3.34f1의 batch mode에서 실제 InputAction과 InputSystemUIInputModule fixture를 실행했다. QA 서버부터 대상 게임까지의 전체 통합 실행은 포함하지 않았다.

| Input System package | Active Input Handling | EditMode | PlayMode |
| --- | --- | --- | --- |
| 없음 (fresh import) | Legacy (0) | 565/565 | 88/88 |
| 1.7.0 | Legacy (0) | 565/565 | 87/87 |
| 1.7.0 | New (1) | 565/565 | 97/97 |
| 1.7.0 | Both (2) | 565/565 | 97/97 |

- 명령: setup-unity-test-project.sh <dest> <0|1|2> <absent|present>로 준비하고 Unity -batchmode -nographics -runTests -testPlatform EditMode/PlayMode로 실행한다. Windows 로컬 준비는 같은 template/package를 복사해 구성했다.
- 무패키지 프로젝트의 packages-lock.json, Library/PackageCache, ScriptAssemblies에서 Input System 부재를 확인했다. runtime 참조 및 로드 부재 assertion과 기존 마우스/UI 테스트를 모두 실행했다.
- 선행 e2077e3 Legacy baseline: EditMode 565/565, PlayMode 87/87.
- New-only 축 테스트는 선행 guard의 unsupported 결과를 검증하며 Legacy/Both 축 assertion은 유지한다.
- plan review FAST/MEDIUM/HEAVY PASS, pair review PASS. FixedUpdate 짧은 click, paused cleanup, optional registration 및 fixture 격리를 검토했다.
- 별도 Editor 프로세스에서 domain reload 없이 Play Mode 두 번 재진입 통과. 입력 전달, 장치/lifetime/복제 settings 제거와 원본 settings 복원을 검증했다. 재현 script와 결과는 `.plan/assets/new-input-system-mouse/`에 보관한다.
- git diff --check 및 bash -n 문법 검사 통과. 원격 CI는 PR에서 확인한다.

## Risks & Rollback

- Manual update, timeScale=0인 Fixed update, 복수 Mouse InputUser는 명령을 명시적으로 거절한다. 업데이트 중단은 2초 unscaled timeout으로 실패하고 입력을 해제한다.
- 게임이 고정한 device ID/binding 필터는 바꾸지 않는다. New/Both에서 Input System 1.7.0 미만이거나 adapter 미등록이면 명령을 실패시킨다.
- QA 중 복제한 InputSettings로 focus 정책을 적용하고 종료 시 원본을 복원한다. 실제 Mouse의 기존 눌림은 시작 시 reset하고 반환 시 sync를 요청한다.
- 종료는 기존 ReleaseAll과 같은 release 의미이므로 같은 UI 대상에서 놓으면 click이 발생할 수 있다.
- Rollback: 이 PR commit을 revert한다. 선행 guard PR은 유지한다.

## Open Questions

없음.
