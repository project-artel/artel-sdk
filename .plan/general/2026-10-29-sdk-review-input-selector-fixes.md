# 2026-10-29 — SDK 입력 해제와 persistent selector 수정

- Date: 2026-10-29
- Jira: None (기존 PR #176 리뷰 수정)
- Status: Verified

## Goal
일시 정지 중 release가 거절돼도 기존 hold를 해제하고, persistent scene scan의 selector를 실행 경로에서도 정확히 해석한다.

## Non-goals
새 입력 방식, 기존 active scene selector 변경, 사용자 게임 재시작, merge.

## Context / Constraints
공유 폴더를 보존한 독립 worktree. 기존 PR에 일반 push만 수행한다. 부모 에이전트가 계획과 최종 diff를 검토한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 두 review의 실제 실패 경로 확인.
- [x] **Step 1: Implementation** validation 실패 중 mouse release는 기존 ReleaseAllVirtualInput으로 정리한다. 실패 응답은 유지한다. persistent scene은 @persistent/ prefix로 분리하고 기존 scene 탐색 helper로 해석한다.
- [x] **Step 2: Tests** mouse_up·mouse alias key_up의 paused Fixed Update release, 실제 persistent scan→move_mouse 회귀를 PlayMode에서 실행한다.
- [ ] **Step 3: Rollout / Rollback** 부모 diff 검토 후 기존 PR에 commit/push. revert로 rollback.

## Validation
- **Commands to run:** setup-unity-test-project.sh로 별도 프로젝트 구성 후 Unity 2022.3.34f1 PlayMode 테스트, git diff --check.
- **Expected output:** 실패 응답에도 hold가 남지 않고 persistent selector는 지정한 scene의 객체를 가리킨다.

## Risks & Rollback
- **Risks:** Unity scene 구분과 실행 중 입력 정리 부작용. 기존 active scene 및 release tests 함께 실행한다.
- **Rollback steps:** 수정 commit revert.

## Open Questions
없음.

## 검증 결과
Unity 2022.3.34f1, Both input + Input System 1.7.0 전용 프로젝트에서 baseline PlayMode 29/29, 수정 후 동일 suite와 신규 회귀를 포함해 32/32 통과(실패·skip 0). 부모 에이전트의 계획·독립 diff 검토 PASS. git diff --check 통과. 실제 게임 프로젝트나 실행 중 Unity는 변경하지 않았다.
