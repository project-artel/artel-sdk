# Architecture Decision Records

- 이 저장소의 모양을 정한 결정 여섯 개입니다.
- 나머지 결정은 아래 「그 밖의 결정」 이 가리키는 `.plan/general/` 문서에 있습니다.

| ADR | 제목 | 상태 |
| --- | --- | --- |
| [0001](0001-input-hooking.md) | Input 후킹을 ILPP build-time rewriting 으로 한다 | 확정 |
| [0002](0002-action-vocabulary.md) | agent 에게 여는 조작을 범용 UI 조작으로만 정규화한다 | 확정 |
| [0003](0003-state-without-vision.md) | 상태 관측을 text scraping 과 attribute 로 하고 SDK 안에 vision 판정을 두지 않는다 | 확정 |
| [0004](0004-evidence-payload.md) | evidence 를 assembly 안 deflate blob 으로 싣고 attribute 에는 anchor 만 남긴다 | 확정 |
| [0005](0005-affordance-analyser-gate.md) | affordance analyser 를 여는 define gate 를 없애고 3겹 안전장치로 대신한다 | 뒤집힘 (두 번) |
| [0006](0006-websocket-reconnect.md) | 끊긴 WebSocket 을 무제한 재시도와 handshake 마감으로 되살린다 | 뒤집힘 |

## 그 밖의 결정

- `.plan/general/` 에 39 개, `.plan/issues/` 에 5 개의 plan 문서가 있습니다.
- ADR 로 옮기지 않은 결정의 이유는 전부 거기 있습니다. 아래는 그것을 주제별로 묶은 것입니다.

### 좌표와 겨냥

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-07-28-add-world-and-screen-coordinates-to-scene-state.md`](../../.plan/general/2026-07-28-add-world-and-screen-coordinates-to-scene-state.md) | `world` 와 `rect` 를 둘 다 싣는 이유, 원점과 방향 |
| [`2026-07-15-use-game-object-instance-id-for-scene-blocks.md`](../../.plan/general/2026-07-15-use-game-object-instance-id-for-scene-blocks.md) | 블록 id 를 Unity instance id 로 잡은 이유 |
| [`2026-07-28-add-virtual-mouse-and-drag-actions.md`](../../.plan/general/2026-07-28-add-virtual-mouse-and-drag-actions.md) | 가상 마우스, 드래그를 큐 조합으로 만드는 이유 |
| [`2026-07-16-add-virtual-cursor.md`](../../.plan/general/2026-07-16-add-virtual-cursor.md) | 가상 커서를 화면에 그리는 이유 |
| [`2026-08-27-bridge-mouse-keycodes-to-mouse-events.md`](../../.plan/general/2026-08-27-bridge-mouse-keycodes-to-mouse-events.md) | `KeyCode.Mouse0` 이 마우스 이벤트를 내야 하는 이유 |

### 씬 순회와 그 부작용

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-08-26-clean-scene-walk-persistent-objects.md`](../../.plan/general/2026-08-26-clean-scene-walk-persistent-objects.md) | 순회가 게임 위에 남기는 `DontDestroyOnLoad` 객체를 치우는 법 |
| [`2026-07-26-scan-all-scenes-full-serialized-fields.md`](../../.plan/general/2026-07-26-scan-all-scenes-full-serialized-fields.md) | `scan_all_scenes` 의 `full` 모드 |
| [`2026-07-15-add-scene-scan-polling.md`](../../.plan/general/2026-07-15-add-scene-scan-polling.md) | 폴링 주기와 변화 감지 |
| [`2026-08-13-port-affordance-runtime-scan-and-report.md`](../../.plan/general/2026-08-13-port-affordance-runtime-scan-and-report.md) | 런타임 스캔과 리포트 이식 |
| [`2026-08-21-sdk-receives-remote-scan-command.md`](../../.plan/general/2026-08-21-sdk-receives-remote-scan-command.md) | `scan_evidence` 를 원격 명령으로 받는 이유 |

### `reset_game`

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-08-26-reset-game-clears-player-prefs.md`](../../.plan/general/2026-08-26-reset-game-clears-player-prefs.md) | `clearPlayerPrefs` 가 선택인 이유, SDK 자신의 `Artel.*` 키를 살리는 법 |

### screen capture 와 스트리밍

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-07-28-capture-screen-action.md`](../../.plan/general/2026-07-28-capture-screen-action.md) | 전체 화면과 요소 잘라내기, 형식과 크기 |
| [`2026-08-26-capture-scene-thumbnails.md`](../../.plan/general/2026-08-26-capture-scene-thumbnails.md) | evidence walk 중에 씬 섬네일을 찍는 이유 |
| [`2026-09-09-hide-keyboard-status-during-capture.md`](../../.plan/general/2026-09-09-hide-keyboard-status-during-capture.md) | screen capture 프레임에만 keyboard status 패널을 끄는 이유 |
| [`2026-08-13-keep-streaming-while-window-unfocused.md`](../../.plan/general/2026-08-13-keep-streaming-while-window-unfocused.md) | 창이 포커스를 잃어도 스트리밍이 유지되어야 하는 이유 |
| [`2026-08-27-test-page-webrtc-capture-isolation.md`](../../.plan/general/2026-08-27-test-page-webrtc-capture-isolation.md) | 테스트 페이지의 WebRTC 와 screen capture 를 격리하는 이유 |

### 오버레이와 인증

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-07-31-sdk-loopback-login-jwt-auth.md`](../../.plan/general/2026-07-31-sdk-loopback-login-jwt-auth.md) | loopback 로그인 + PKCE, instance key 를 `sdkUuid` 로 바꾼 이유 |
| [`2026-07-30-redesign-sdk-overlay-ui.md`](../../.plan/general/2026-07-30-redesign-sdk-overlay-ui.md) | 오버레이 재설계와 첫 실행 게이트 |
| [`2026-07-30-apply-artel-brand-to-sdk.md`](../../.plan/general/2026-07-30-apply-artel-brand-to-sdk.md) | 브랜드 적용 범위 |
| [`2026-07-28-fix-registration-screen-flicker.md`](../../.plan/general/2026-07-28-fix-registration-screen-flicker.md) | 등록 중 화면이 깜박이던 원인 |
| [`2026-07-16-add-keyboard-status-visualization.md`](../../.plan/general/2026-07-16-add-keyboard-status-visualization.md) | keyboard status 패널이 있는 이유 |

### 성능 지표

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-08-13-frame-time-metrics-collection.md`](../../.plan/general/2026-08-13-frame-time-metrics-collection.md) | FPS 분포와 hitch 카운트를 재는 법 |
| [`2026-08-19-collected-metric-groups-in-device-context.md`](../../.plan/general/2026-08-19-collected-metric-groups-in-device-context.md) | 지표군 목록을 `DEVICE_CONTEXT` 에 싣는 이유 |
| [`2026-08-13-profiler-markers-on-sdk-hot-paths.md`](../../.plan/general/2026-08-13-profiler-markers-on-sdk-hot-paths.md) | SDK 자신의 hot path 를 계측하는 이유 |

### evidence 분석의 깊이

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-09-01-read-conditions-through-predicate-calls.md`](../../.plan/general/2026-09-01-read-conditions-through-predicate-calls.md) | 조건을 `bool` 을 돌려주는 호출 안까지 읽는 이유 |
| [`2026-08-18-name-the-prefab-and-record-the-cut.md`](../../.plan/general/2026-08-18-name-the-prefab-and-record-the-cut.md) | `createdBy` 가 프리팹을 지목하고 `cut` 이 끊긴 자리를 적는 이유 |
| [`2026-08-21-sdk-uploads-evidence-document.md`](../../.plan/general/2026-08-21-sdk-uploads-evidence-document.md) | SDK 가 evidence 문서를 스스로 올리는 경로 |

### 입력 차단과 접근성

| 문서 | 무엇에 답하나 |
| --- | --- |
| [`2026-07-26-block-actions-on-non-interactable-ui.md`](../../.plan/general/2026-07-26-block-actions-on-non-interactable-ui.md) | 누를 수 없는 버튼에 대한 action 을 거절하는 이유 |
| [`2026-08-11-add-set-axis-and-set-button-actions.md`](../../.plan/general/2026-08-11-add-set-axis-and-set-button-actions.md) | 축을 이름으로 직접 모는 action 이 따로 필요한 이유 |
| [`2026-07-16-add-agent-key-input.md`](../../.plan/general/2026-07-16-add-agent-key-input.md) | 가상 키 입력의 첫 설계 |
