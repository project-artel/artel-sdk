# 2026-11-11 — SDK run별 OS 입력 선택 추가

- Date: 2026-11-11
- Jira: ARTEL-1421 (상위 ARTEL-1420)
- Status: Draft PR

## Goal

기존 가상 입력을 기본값으로 유지하고, run 시작 시 선택한 `os` 입력을 Windows, macOS, Linux에서 실행한다. CLI와 MCP는 backend에 `inputMode`를 전달한다. 빌드 설정은 추가하지 않는다.

## Non-goals

게임의 axis·button 이름을 물리 키로 추정하거나 OS 권한을 자동 변경하지 않는다. OS 모드에서 Unity 직접 조작을 섞지 않는다.

## Context / Constraints

backend는 `run_config.input_mode`를 소유하고 Agent 스냅샷으로 덮어쓰지 않는다. OS run은 `configure_input`의 실제 응답을 확인한 뒤 시작한다. 모든 QA `ACTION`은 `inputMode`를 명시하며 SDK는 연결 변경 뒤에도 이 값을 검사한다. 누락 값은 `virtual`이다. `move_mouse`는 기존 게임 화면 좌표와 selector를 유지하고 대상 창의 실제 좌표로 변환한다. Linux X11은 XTest를 사용하며 native Wayland는 portal 구현 없이 지원된다고 표시하지 않는다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 기존 입력·연결 해제·run 상태 처리와 backend 설정 보존 경로 확인.
- [x] **Step 1: Implementation** — `configure_input` preflight, typed 요청, OS 입력 session, 플랫폼별 backend, ACTION 모드 전달, 종료·reset 해제 추가.
- [x] **Step 2: Tests** — fake native backend로 전달·실패·해제·가상 입력 회귀와 wire 기본값 검증. Unity EditMode·PlayMode 실행.
- [ ] **Step 3: Rollout / Rollback** — SDK와 backend PR을 먼저 병합하고 CLI·MCP를 뒤에 병합. 기본 모드는 virtual.

## Validation

- Unity 2022.3.34f1 throwaway project EditMode 720 통과·기존 skip 1, PlayMode 125 통과.
- Unity Roslyn의 `UNITY_STANDALONE_WIN`, `UNITY_STANDALONE_OSX`, `UNITY_STANDALONE_LINUX` 분기 컴파일과 `git diff --check` 통과.
- 실제 OS 입력 주입·다중 모니터·DPI 이동과 전체 QA run은 미검증.

## Risks & Rollback

- **Risks:** 실제 커서·포커스에 영향을 주며 macOS 권한, X11 라이브러리, 창 좌표·DPI에 의존한다. Editor의 정확한 Game view 경계를 구할 수 없으면 포인터 입력을 거절한다. native Wayland는 명시적 미지원 오류를 반환한다.
- **Rollback steps:** run을 virtual로 시작한다. 변경 PR을 revert한다.

## Open Questions

없음. native Wayland portal 지원은 구현 여부와 검증 환경을 별도로 기록한다.

## Plan Review

후속 수정에서는 `IQaInputActions`로 키보드·마우스 실행을 통일한다. `ActionExecutor`에서 화면 좌표, selector, 키 코드와 클릭 시간을 해석하고 선택한 구현에 전달한다. 각 구현에서 기존 입력 결과와 프레임 대기를 처리한다.

FAST REVIEW에서 설정 덮어쓰기·preflight 응답 대기·재연결·혼합 입력·좌표·Wayland 구분을 반영했다. MEDIUM REVIEW: PASS. 수정 계약 HEAVY REVIEW: PASS.
