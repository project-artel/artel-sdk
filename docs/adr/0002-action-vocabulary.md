# ADR 0002 — agent 에게 여는 조작을 범용 UI 조작으로만 정규화한다

- 상태: 확정
- 근거: Notion `3960bce5-474c-808b-a907-c0524b995d03`, `.plan/general/2026-07-28-add-virtual-mouse-and-drag-actions.md`, `Packages/kr.artel.sdk/Runtime/ActionExecutor.cs`

## 결정

- agent 가 보낼 수 있는 action 은 범용 UI 조작과 겨냥뿐입니다 — 클릭, 드래그, 키, 축, 시간 제어,
  screen capture, 스캔.
- 게임의 뜻을 담은 action 은 하나도 두지 않습니다.
- `ActionExecutor` 가 받는 action 은 17 개이고, 그 위에 `ArtelManager` 가 배치 안에서 직접 처리하는
  `scan_scene` 과 `scan_all_scenes` 가 있습니다. 전체 목록과 params 모양은
  [`docs/protocol.md`](../protocol.md) 에 있습니다.

겨냥은 화면 좌표로만 하지 않습니다. 세 모양 다 `PointerAimParser` 한 자리에서 갈립니다.

| `move_mouse` 의 params | 무엇 |
| --- | --- |
| `[x, y]` | 화면 좌표 |
| `[instanceId]` | Unity instance id |
| `[selector]` | `ScenePath.SelectorOf` 가 쓰는 `selector` |

- `button_click` 은 예외입니다. `GameObject.Find()` 로 찾은 `Button` 의 `onClick.Invoke()` 를 직접
  부르고 화면 좌표를 쓰지 않습니다.

## 왜

- 어떤 SDK 도 모든 게임의 내부 로직을 이해할 수 없습니다.
  - "공격" 이 무엇인지는 게임마다 다르고, SDK 가 그것을 알려면 게임마다 어댑터를 써야 합니다.
  - 그 순간 "패키지만 넣으면 붙는다" 가 깨집니다 ([ADR 0001](0001-input-hooking.md)).
- 대신 SDK 는 사람이 할 수 있는 조작만 엽니다. 무엇이 공격인지는 agent 가 evidence 와 scene 상태를 읽고
  판단합니다 — SDK 는 값을 보고하고 agent 가 뜻을 정합니다.
- `ScreenRectDto` 가 블록의 화면 좌표를 싣기 때문에 agent 는 좌표를 계산해 `move_mouse` 에 넣을 수
  있습니다.
  - 그래서 `move_mouse` 는 화면 좌표 하나의 모양으로 두고, Unity 의 좌하단 원점으로 뒤집는 일은
    `ActionExecutor` 안 한 줄에 가둡니다.
  - 부르는 쪽은 스캔이 보고한 `rect` 숫자를 그대로 넣으면 됩니다.

## 거절한 대안

| 대안 | 왜 거절했나 |
| --- | --- |
| SDK 에 "공격"·"방어" 같은 게임 의미 action 을 정의 | 게임마다 다른 것을 SDK 의 계약에 넣으면 계약이 게임 수만큼 늘어남 |
| 드래그를 한 번에 하는 `drag_and_drop(a, b)` | ACTION 큐가 이미 직렬이라 같은 일을 두 번째 방법으로 적는 것 |
| 키 상태를 뒤집는 toggle action | SDK 와 agent 가 서로 다른 상태를 믿을 때 복구할 길이 없음 |
| `button_click` 을 pointer event 경로로 교체 | 같은 문서에서 비목표로 뒀고 그대로 남음 |

- `drag_and_drop` 을 거절한 자리는 `.plan/general/2026-07-28-add-virtual-mouse-and-drag-actions.md`
  입니다.
  - `ArtelManager.EnqueueAction` 이 `ProcessActions` coroutine 하나로 `Dequeue` 하고, 배치 안의
    `ExecuteActionRequest` 도 action 마다 `yield return` 합니다.
  - 그래서 `[mouse_down, move_mouse, move_mouse, mouse_up]` 의 순서와 프레임 간격이 이미 보장됩니다.
  - 편의 action 은 필요해지면 그때 더합니다.
- toggle action 은 같은 문서에서 거절했습니다. `key_down`·`key_up` 쌍은 어느 쪽이 무엇을 믿든 `key_up`
  하나로 같은 상태에 도달합니다.
- `button_click` 은 지금 deprecated 입니다.
  - `EventSystem` 을 거치지 않고 `onClick` 을 직접 부르므로 다른 것에 가려진 버튼도 눌립니다.
  - 대신 쓸 것은 `move_mouse` 뒤의 `mouse_down`·`mouse_up` 이고, 그 경로는 `PointerEventDispatcher` 와
    `EventSystem` 을 실제로 거칩니다.
  - 지우지 않고 남겨 둔 것은 이 저장소 바깥의 여러 자리가 지금도 그 action 을 보내고 있기 때문입니다.

## 대가

| 대가 | 무엇이 일어나나 |
| --- | --- |
| 게임의 뜻을 아는 부담이 agent 로 옮겨 감 | agent 가 evidence 와 scene 상태로 스스로 알아내야 함 |
| `mouse_down` 이 한 프레임을 기다림 | 누른 뒤 `reached` 와 `pointerHeldByPerson` 을 `returnValue` 에 실음 (ARTEL-769) |
| `set_axis`·`set_button` 이 별도 action 으로 존재 | legacy Input Manager 가 축과 키의 연결을 런타임 API 로 내주지 않아 가상 키 입력이 `GetAxis` 에 닿지 못함 |

- `mouse_down` 이 기다리지 않으면 겨냥이 빗나간 것, 게임이 입력을 막고 있던 것, 사람이 포인터를 도로
  가져간 것 셋이 전부 `ok` 로 읽힙니다.
