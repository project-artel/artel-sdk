# 2026-10-23 — SDK 오디오 테스트 재생 상태 안정화

- Date: 2026-10-23
- Jira: ARTEL-1224
- Status: Implemented

## Goal
PR #155와 이를 포함한 #156의 PlayMode CI 실패를 수정한다.

## Non-goals
AudioPulse의 런타임 동작과 프로토콜은 변경하지 않는다.

## Context / Constraints
Linux CI에서 시작을 관측한 직후 다음 관측의 playing이 0이다. 테스트 씬에 AudioListener가 없다는 로그가 있다.

## Approach (Checklist)
- [x] **Step 0: Recon** 두 CI에서 같은 테스트 실패 확인.
- [x] **Step 1: Implementation** fixture에 AudioListener를 두고 재생 지속을 검증하는 source는 loop로 유지한다.
- [ ] **Step 2: Tests** Unity PlayMode 전체 실행과 두 PR의 Linux CI 확인.
- [x] **Step 3: Rollout / Rollback** #155에 커밋하고 #156에 merge하여 반영한다.

## Validation
- **Commands to run:** Unity 2022.3.34f1 PlayMode, git diff --check, GitHub Actions.
- **Expected output:** 재생 중 중복 시작 없음, Stop 이후 playing=0, 전체 테스트 통과.

- **Local result:** Unity 2022.3.34f1 PlayMode 74/74 통과. 독립 pair review PASS. Linux CI는 push 후 확인한다.

## Risks & Rollback
- **Risks:** Linux와 Windows의 audio backend 차이는 CI에서 확인해야 한다.
- **Rollback steps:** 테스트 수정 커밋을 revert한다.

## Open Questions
없음.
