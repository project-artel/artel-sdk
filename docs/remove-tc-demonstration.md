# TC 시연 기능 제거

2026-10-28 사용자가 TC 시연 기능과 저장된 시연 데이터 삭제를 요청했다. 사람이 게임을 조작해 TC 수행 방법을 기록하고 agent에 승인된 절차로 전달하던 기능을 제거한다.

## SDK 변경

- WebSocket 연결 시 `DEMONSTRATION_CAPABILITIES`를 보내지 않는다.
- `DEMONSTRATION_CONTROL` 처리와 lease, generation, 만료 시연 입력 queue, 정규화 pointer 변환을 제거한다. 구형 제어 메시지는 기존 미지원 메시지 응답으로 처리하며 게임 입력이나 읽기를 시작하지 않는다.
- 일반 QA `ACTION`과 `ACTION_RESULT`, 입력 backend, 사용자 pointer 회수, `STREAM_FRAME`·ACK 전송은 유지한다.
- 일반 QA action queue에 시연 전용 lease 검사를 적용하지 않는다.

## 배포 및 데이터 삭제

서버에서 시연 진입점과 agent 참고 연결을 먼저 제거한 뒤 SDK를 배포한다. 시연 기록·전용 저장 객체 삭제는 orchestration migration과 정리 작업이 담당한다. SDK는 서버의 QA 녹화나 저장된 판정 검토 영상을 삭제하지 않는다.

SDK 코드의 revert로 제거된 기능을 복구할 수 있지만 서버에서 삭제한 시연 데이터는 복원되지 않는다. 통합 문서에 보관된 원본 PR 설명은 과거 변경 기록이며 현재 기능 명세가 아니다.

## 검증

격리된 Unity 테스트 프로젝트에서 일반 QA action batch, 입력과 streaming 회귀를 검증한다. 구형 제어 메시지가 입력을 점유하지 않고 다음 QA action이 실행되는 검사를 추가했다. 실제 사용자 게임이나 실행 중인 QA는 재시작하지 않는다.

2026-10-28 Unity 2022.3.62f3 GPU batchmode 최종 검증:

| 입력 구성 | EditMode | PlayMode |
| --- | --- | --- |
| Old Input + Input System package 설치 | 614 통과 | 95 통과 |
| New Input System 전용 | 미실행 | 105 통과 |

모든 최종 실행의 실패·skip은 0개다. PlayMode에는 구형 시연 제어 메시지 후 일반 QA action 처리, pointer·key 입력, 입력 backend·transport 회귀가 포함된다. EditMode에는 streaming·ACK·frame 크기·방향 검사가 포함된다. Both와 Input System package 미설치 구성의 추가 실행, 실제 게임 통합 검증은 이번 제거 작업에서 실행하지 않았다.

로컬 결과 XML은 `.work/qa-bundle-sdk-unity/results-remove-demo-edit-final.xml`, `results-remove-demo-play-final.xml`, `results-remove-demo-input1-play.xml`에 남겼다. 이 프로젝트는 사용자 Unity 게임과 분리돼 있다.
