# 2026-10-23 — SDK 시연 제어권과 입력 만료 처리 추가

- Date: 2026-10-23
- Jira: ARTEL-1260
- Status: Implemented, independent review passed

## Goal

SDK protocol v1으로 사람이 시연하는 동안 단일 session/generation의 입력만 적용하고 만료·연결 변경 시 입력을 해제한다.

## Non-goals

시연 저장, 브라우저 UI, 지식 추출과 QA context는 각 서비스가 구현한다.

## Context / Constraints

공유 계획과 계약은 같은 디렉터리의 tc-demonstration 문서를 따른다. 기존 ACTION_RESULT/requestId와 시연이 없는 QA action 동작을 유지한다. Unity main thread에서만 입력 상태를 변경한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 기존 action queue, 입력 해제, WebSocket 재연결 경로 확인
- [x] **Step 1: Implementation** DTO, monotonic lease, generation 검사, queue 밖 제어 ACK, 중첩 coroutine 중단 추가
- [x] **Step 2: Tests** lease 단위 테스트와 PlayMode 입력 취소·재연결·기존 QA 거절 테스트
- [x] **Step 3: Rollout / Rollback** capabilities를 보고 서버가 시연을 허용하며 서버 feature flag로 신규 시작 중단

## Validation
- **Commands to run:** Unity 2022.3.34f1 -batchmode -nographics -runTests -testPlatform EditMode/PlayMode; throwaway project는 .github/unity-test-project와 동일 구성
- **Expected output:** XML test 결과에 실패 0개. 기존 QA 계약 테스트 유지.

## Risks & Rollback
- **Risks:** 실제 게임 입력 지연은 Unity fixture만으로 보장하지 않는다. 연결 유실 중 이미 적용된 입력을 재시도하지 않는다.
- **Rollback steps:** 서버에서 신규 시연을 끄고 lease 종료를 기다린 뒤 SDK 변경을 되돌린다.

## Open Questions

실제 게임 streaming과 orchestration의 통합 검증은 상위 작업에서 수행한다.

## 검증 결과

Unity 2022.3.34f1 EditMode 562/562, PlayMode 76/76 통과. 시연 만료·연결 변경·실행 중 및 대기 입력 차단·기존 QA 입력·readings 보존을 포함한다. sandbox 내부 실행은 LicensingClient IPC timeout(code 199)으로 컴파일 전에 중단됐으며, 같은 명령을 sandbox 밖에서 실행해 통과했다. 결과 XML은 로컬 .tmp/unity-tests/EditMode-normalized.xml과 PlayMode-normalized.xml에 보관한다. git diff --check도 통과했다.

ACQUIRE는 StartReadings 성공 이후 승인한다. 시연이 시작한 readings만 종료 시 정지하며, 시작 전에 활성화된 readings는 보존한다. lease 상한은 30000ms이며 계약 기본값 8000ms를 지원한다.


## Streaming 해상도 차이 보정

시연 move_mouse는 좌상단 원점의 normalized [x,y] (0..1)를 받으며 실행 시점 Screen.width/height로 변환한다. 기존 QA move_mouse는 픽셀 좌표를 유지한다. 최종 Unity EditMode 562/562, PlayMode 76/76 통과. .tmp 아래 버튼 카운터 Development build는 실제 streaming 검증용이며 저장소 변경에 포함하지 않는다.

## 실제 브라우저 시연 검증

격리 로컬 backend와 실제 Unity Development build(version 1.0.1258, gameInstance 1/build 1)를 연결했다. 브라우저 streaming 화면에서 Increment를 한 번 클릭하여 Count: 0에서 Count: 1로 바뀌는 것을 확인했다. ACQUIRE 뒤 readings가 활성화되고 현재 pulse에 Counter text와 SDK count가 반영되는 것도 확인했다. 이후 승인 절차를 사용한 실제 ORDERED QA 한 번에서도 reset 후 Count: 0 → 단일 click → Count: 1을 관찰해 1/1 PASS로 완료했다.

이 결과는 버튼 카운터 fixture와 로컬 연결의 통합 검증이다. 여러 게임의 입력 시스템, 장시간 네트워크 장애, 실제 서비스 환경의 latency까지 검증한 결과는 아니다. 8초 만료, 연결 epoch 변경, 실행·대기 입력 중단, 기존 QA 호환은 위 Unity 자동 테스트로 별도 확인했다. 독립 SDK diff 검토는 PASS이며 추가 수정 사항은 없었다.

backend의 시연 기능을 반영하기 전에 이 SDK 변경을 먼저 merge하고 게임 build에 포함해야 한다. capability가 없는 SDK에는 backend가 시연 시작을 허용하지 않는다.
