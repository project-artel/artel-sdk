# orch 경유 WebSocket 영상 전송

SDK는 기존 인증된 `/ws/sdk`에 JPEG frame을 보낸다. orch JVM이 viewer에 frame을 전달하고 H.264 MP4 변환·S3 저장까지 처리한다. SDK와 브라우저의 직접 연결, 별도 미디어 worker, TURN, 추가 미디어 포트는 사용하지 않는다.

## 전송 계약

서버의 `STREAM_START`는 `transport: "JPEG_WS"`와 `video` 제한, `streamId`, `leaseSeconds`를 전달한다. SDK는 `STREAM_STATE`의 `transport: "JPEG_WS"`로 지원 여부를 알린다. 다른 transport 요청에는 FAILED를 반환한다.

`video`의 기본 제한은 `maxWidth:1280`, `maxHeight:720`, `maxFramerate:10`, `jpegQuality:65`, `maxFrameBytes:131072`다. SDK는 더 큰 값이 들어와도 자체 상한을 넘기지 않는다. 실제 화면 비율을 유지하고 같은 texture를 재사용한다. overlay를 포함한 화면 back buffer를 읽으며 GPU UV가 위에서 시작하는 환경은 JPEG도 상하 보정한다. JPEG bytes가 상한을 넘으면 quality 35로 다시 시도하고, 계속 초과하면 capture surface를 절반으로 줄인다. 축소한 surface는 다음 frame에도 재사용한다. 최대 7회의 축소·인코딩 시도 후에도 제한을 맞추지 못하면 조용히 누락하지 않고 FAILED를 보고한다.

SDK→orch frame은 `STREAM_FRAME {streamId, frameId, jpeg}`다. `frameId`는 stream 안에서 증가하며 `jpeg`는 base64 JPEG다. JSON은 UTF-8 184KiB 이하다. 서버→SDK는 `STREAM_FRAME_ACK {streamId, frameId, accepted}`다. ACK는 전송 flow-control이며 인코딩·S3 저장 성공을 뜻하지 않는다. accepted=false도 다음 frame 전송을 허용한다.

SDK는 비동기 송신과 ACK를 각각 하나만 기다린다. 두 작업이 끝나기 전에는 다음 capture도 생략한다. ACK 또는 송신 callback이 10초 동안 끝나지 않으면 capture를 종료하고 FAILED를 보고한다. transport의 송신 callback도 별도 10초 deadline으로 제한한다. deadline이 지나면 해당 소켓을 close frame·ACK 대기 없이 실제로 해제한 뒤 기존 backoff로 재연결한다. 해제가 확인되기 전에 새 송신을 만들지 않으며 이전 callback은 새 소켓의 자리를 풀지 않는다. STOP·lease 만료·소켓 교체 시 네트워크 callback을 기다리지 않고 texture와 capture loop를 정리한다.

## 변경 범위와 배포

서버·SDK·home을 같은 계약으로 배포해야 한다. 구 SDK의 WebRTC offer는 새 서버에서 SDK 업데이트 필요 오류로 처리한다. 로컬 `ArtelTestPage`도 JPEG frame과 ACK를 사용한다. `com.unity.webrtc` package와 runtime assembly 참조는 제거했다.

JPEG 전송은 WebRTC보다 네트워크 사용량이 클 수 있다. 해상도·fps·frame 크기·미완료 송신 수를 제한하고, 실제 Windows Unity→WSL orch→브라우저에서 영상 갱신·QA action 지연·S3 MP4를 확인한다. `ScreenVideoSource.LastJpegEncodeMilliseconds`와 `LastJpegBytes`는 로컬 측정에 사용할 수 있다. 자동 테스트 결과를 실제 게임 검증으로 대신하지 않는다.

SDK 변경은 ARTEL-1286, 서버는 ARTEL-1269, home은 ARTEL-1271에 연결된다. rollback은 세 구성 요소를 함께 이전 버전으로 복원하거나 영상 기능을 비활성화한다. 저장된 영상·판정·평가 이력은 보존한다.

## SDK 자동 검증

2026-10-28 Unity 2022.3.62f3의 격리된 테스트 프로젝트에서 최종 코드의 GPU EditMode 578개와 PlayMode 92개가 통과했다. ACK·송신 각각 1개 상한, 늦은 ACK·callback, 거절 ACK 후 진행, 10초 timeout, frame byte 상한과 transport 불일치를 검증한다. 일반 SDK 자동 테스트를 실제 게임의 S3 저장 증거로 사용하지 않는다.

전체 EditMode에는 streaming 28개와 기존 Transport 회귀 70개가 포함된다. callback을 반환하지 않는 송신에서 이전 실제 소켓 Closed와 새 소켓의 후속 송신을 확인했고, 같은 소켓의 늦은 callback이 다음 frame 송신 자리를 풀지 않는 것도 검증했다. 고주파 noise 화면은 JPEG 32KiB 상한에 맞게 축소되며 축소 surface가 재사용된다. 게임 장면의 실제 방향과 녹화는 통합 검증에서 별도로 확인한다.

## 실제 Unity 통합 검증

2026-10-28 실제 Windows Unity의 화면이 WSL orch의 기존 WebSocket을 통해 전달되는 것을 확인했다. 보정 후 1280×720 JPEG 40개가 약 9.37fps로 수신됐고 평균 63,367 bytes·최대 63,711 bytes였다. 실제 QA 6의 S3 H.264 MP4는 1,350,436 bytes·472 frames·약 54.57초였으며 다운로드해 정상 방향과 읽을 수 있는 글자를 확인했다. 합성 SDK 결과가 아니다.

orch Docker 이미지에서도 단일 JVM PID 1에서 JPEG decode·H.264 encode/decode와 상하 패턴 보존을 확인했으며 자식 프로세스는 없었다. 운영 장시간 부하와 main-thread JPEG encode p95는 별도 측정이 남아 있다.
