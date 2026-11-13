# 2026-11-13 — SDK 씬 전환 후 가상 마우스 연결 유지 수정

- Date: 2026-11-13
- Jira: ARTEL-1448
- Status: Implemented

## Goal
메뉴에서 QA 마우스를 사용한 후 PlayerInput이 있는 전투 씬으로 넘어가도 Look action이 이동량을 받게 한다.

## Non-goals
키보드 변경, 상대 좌표 API, 게임 카메라 우회, SDK 연결 끊김 수정.

## Context / Constraints
InputSystemMouseBackend.PairMouseUser는 물리 마우스가 없는 사용자의 기존 QA 마우스 연결을 해제한다. PlayerInput은 이미 존재하는 QA 마우스만 선택할 수 있다. 실제 Living Dead PlayMode 재현: 씬 진입 후 활성화는 18.01186도, 씬 진입 전 활성화는 Look callback 없음과 0도.

## Approach (Checklist)
- [x] **Step 0: Recon** 재현 테스트와 장치 연결 코드 확인.
- [x] **Step 1: Implementation** PairMouseUser에서 기존 virtual-only pairing을 해제하지 않는다. 새 pairing은 기존대로 physical mouse 사용자에만 추가한다. MouseUserCount는 QA 마우스를 포함한 모든 Mouse 연결을 사용자별 한 번 센다. virtual-only 사용자와 별도 physical 사용자 조합도 다중 사용자로 거부한다. Dispose의 RemoveDevice 정리는 유지한다.
- [x] **Step 2: Tests** PlayerInput을 QA 마우스 생성 후 활성화하고 delta action 수신 및 Release 후 장치 정리를 검증한다. virtual-only 사용자와 별도 physical 사용자 조합의 거부 테스트 및 기존 single-user, multiple-user, UI/drag tests를 실행한다. 실제 게임 전환 테스트도 재실행한다.
- [ ] **Step 3: Rollout / Rollback** SDK draft PR, 검증한 SDK commit으로 샘플 게임 재빌드. 되돌릴 때 기존 SDK SHA로 복귀.

## Validation
- **Commands to run:** Unity 2022.3.62f3 PlayMode NewInputMouseTests 및 LivingDead.Tests.MouseLookTests.
- **Expected output:** 기존 SDK에서 회귀 테스트 실패, 수정 SDK에서 통과. 카메라 각도 변화 >1도.

## Risks & Rollback
- **Risks:** virtual-only 사용자가 gamepad로 전환해도 기존 연결이 남을 수 있다. control scheme의 binding mask와 InputUser lifecycle이 장치 사용을 제한하며 새 virtual-only 사용자에게 임의 pairing을 추가하지 않는다. Dispose에서 RemoveDevice로 정리한다.
- **Rollback steps:** 수정 commit revert, 샘플 SDK pin을 465e18c로 복귀.

## Open Questions
없음.

## Plan review
Fast PASS. Medium finding accepted: QA mouse 제외 집계가 virtual-only 사용자 + physical 사용자 조합을 놓치므로 MouseUserCount와 혼합 사용자 회귀 테스트를 추가한다.

Heavy plan review PASS. Pair review PASS. 실제 게임 전환 회귀는 수정 후 18.01186도 및 Look performed/canceled 수신 확인. SDK 추가 delta 테스트는 glide 중간값을 한 번의 delta와 혼동하지 않도록 ArtelInput.MoveMouse + WaitFrames(2)를 사용한다.

최종 검증: Unity 2022.3.62f3 PlayMode 16/16 PASS (NewInputMouseTests 15개 및 LivingDead MouseLookTests 1개).
