# 2026-10-29 — SDK 연결 종료와 runtime 상태 유실 후 복구 추가

- Date: 2026-10-29
- Jira: None
- Status: Complete

## Goal
script reload, 연결 교체, action 예외, 종료 중 예외 뒤에 SDK가 새 요청을 처리할 수 있도록 복원한다.

## Non-goals
완료 여부가 불명확한 action을 자동 재실행하지 않는다. 인증 거절을 우회하거나 게임의 임의 상태를 저장·복원하지 않는다. 서버 QA run의 자동 재개 정책은 변경하지 않는다.

## Context / Constraints
사용자가 보고한 RecordFrameTime, Overlay.Update, StopTransport의 null 예외에서 시작했다. PR 요청 뒤 연결 종료와 상태 유실의 복구까지 범위가 확대됐다. 원래 SDK 기반 PR #174는 #176에 통합되어 닫혔으므로 최신 #176 위에 별도 변경으로 올린다.

## Approach (Checklist)
- [x] **Step 0: Recon** runtime 소유 표시와 객체의 reload 차이, 기존 reconnect backoff, connection epoch, action worker의 종료 경로 확인.
- [x] **Step 1: Implementation** OnEnable 복원, 초기화와 Update 실패 시 5초 후 재시도, 연결 세대 변경 시 action 취소·입력 해제·timeScale 복원·stream 폐기, 각 종료 단계의 실패 격리, overlay 진행 표시 초기화.
- [x] **Step 2: Tests** null 상태·빠른 reconnect·전송 실패·종료 실패·실제 loopback 서버 재시작·실제 script reload 검증.
- [x] **Step 3: Rollout / Rollback** 사용 중 runtime package를 동기화하고 draft PR로 제출한다. 자동 merge·배포는 하지 않는다.

## Validation
- **Commands to run:** Unity 2022.3.62f3 GPU batchmode `-runTests -testPlatform PlayMode`, `EditMode`; `-executeMethod SdkLifecycleReloadProbe.Run`; `git diff --check`.
- **Expected output:** 이전 연결의 action 결과가 새 연결로 전달되지 않고, 실패 정리 후 새 요청이 처리된다.
- **Results:** 최종 PlayMode 106/106, EditMode 617/617 통과(실패·skip 0). 실제 loopback WebSocket 서버를 새 인스턴스로 재시작한 뒤 자동 reconnect, 새 echo 응답, 이전 메시지 무효화 통과. 실제 script reload 뒤 manager와 overlay 복원, 반복 StopTransport, 재활성화와 두 번째 Play/Stop까지 오류 없이 완료. `git diff --check` 통과.
- **Evidence:** 격리 프로젝트 `.work/qa-websocket-sdk-unity`의 `results-recovery-playmode-final.xml`, `results-recovery-editmode-final.xml`, `recovery-reload-final.log`.

## Risks & Rollback
- **Risks:** 연결이 바뀌면 실행 중이거나 대기 중인 action은 취소한다. 결과를 새 연결에 재전송하거나 action을 자동 재실행하지 않으므로 서버가 새 요청을 보내야 한다. 인증 복구와 운영 서버 QA E2E는 이번 로컬 검증에 포함하지 않는다.
- **Rollback steps:** 이 PR의 commit만 revert한다. #176의 기존 변경과 게임 데이터는 유지한다.

## Open Questions
- 없음.
