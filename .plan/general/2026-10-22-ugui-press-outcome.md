# 2026-10-22 — uGUI 입력 결과 판정 보완

- Date: 2026-10-22
- Jira: None
- Status: Implemented

## Goal
uGUI 수신 대상이 있는 입력을 pointerHeldByPerson으로 오판하지 않고, 입력 후 대상이 제거되어도 reached 경로를 보존한다.

## Non-goals
macro 문법, 입력 전달 방식, 기존 raycast target 기록 정책 변경.

## Context / Constraints
최신 develop에는 ARTEL-1175의 uGUI reached 반영이 이미 있다. 이 PR은 그 위에 결과 판정과 경로 보존을 보완한다. 기존 작업 폴더의 다른 변경은 포함하지 않는다.

## Approach (Checklist)
- [x] **Step 0: Recon** — develop의 기존 수정과 차이를 확인한다.
- [x] **Step 1: Implementation** — 이벤트 실행 전 경로를 저장하고 프레임 대기 전에 복사한다. uGUI 수신 경로가 있으면 pointerHeldByPerson=false로 반환한다.
- [x] **Step 2: Tests** — 기존 테스트 6개를 유지하고 결과 반환, Button.onClick, 빈 공간, 부모 핸들러, 프레임 대기 중 대상 제거를 검증한다.
- [x] **Step 3: Rollout / Rollback** — SDK 갱신 빌드에 반영하며 문제가 있으면 이 커밋만 되돌린다.

## Validation
- **Commands to run:** Unity 2022.3.34f1 `-batchmode -runTests -testPlatform PlayMode -testFilter Artel.Tests.PointerEventDispatcherTests`.
- **Expected output:** 11개 테스트 통과.
- 독립 diff 검토: PASS. 실제 macro QA run은 실행하지 않았다.

## Risks & Rollback
- **Risks:** 입력 전달을 기록하며 게임 로직의 성공까지 보장하지 않는다.
- **Rollback steps:** 이 PR 커밋을 revert한다.

## Open Questions
- 없음.
