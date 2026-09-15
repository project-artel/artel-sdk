# ADR 0003 — 상태 관측을 text scraping 과 attribute 로 하고 SDK 안에 vision 판정을 두지 않는다

- 상태: 확정
- 근거: Notion `3960bce5-474c-808b-a907-c0524b995d03`, Notion `3b50bce5-474c-81de-be32-f98008c0494b`, `Packages/kr.artel.sdk/Runtime/SceneScanner.cs`, `Packages/kr.artel.sdk/Runtime/Tracking/SceneStatePoller.cs`

## 결정

SDK 가 게임 상태를 읽는 통로는 둘입니다.

| 통로 | 무엇을 읽나 | 게임이 할 일 |
| --- | --- | --- |
| text scraping | `Text`·`TMP_Text`·`InputField`·`TMP_InputField` 가 실제로 보여 주는 문자열, `Button` 이 `onClick` 에 직렬화해 둔 호출 | 없음 |
| attribute | `[ArtelState]` 와 `[ArtelAction]` 이 붙은 멤버 | 선택. 붙이지 않아도 게임이 쓴 `MonoBehaviour` 의 직렬화 필드는 읽힘 |

- action 전후를 가르는 것은 snapshot diffing 입니다. `SceneStatePoller` 가 스냅샷을 해시해 달라진
  때만 `GAME_STATE` 를 보냅니다.
- SDK 안에는 화면을 보고 판정하는 코드가 없습니다. screen capture 는 찍어서 올리기만 하고, 그 그림으로
  무엇이 참인지 정하는 일은 SDK 밖입니다.

Notion 원문과 실제로 나간 것이 세 자리에서 다릅니다.

| Notion 원문 | 이 저장소 | 확인한 것 |
| --- | --- | --- |
| 세 번째 통로로 log hooking | 없음 | `Application.logMessageReceived` 를 거는 자리가 하나도 없고, `ArtelLoggerBehaviour` 는 시작할 때 `Debug.Log` 한 줄을 쓸 뿐 |
| `[QAAction]` | `[ArtelAction]` | 원문 이름으로 저장소를 검색하면 아무것도 안 나옴 |
| `[QAWatch]` | `[ArtelState]` | 같음 |

## 왜

- DB 접근도 외부 저장소 접근도 없이 white-box QA 를 하려면 SDK 가 게임에 파고들지 않아야 합니다.
  - 게임의 세이브 파일 형식을 알아야 하거나 서버 API 를 알아야 하는 SDK 는 게임마다 다시 만드는
    SDK 입니다.
- 게임이 이미 화면에 쓰고 있는 값은 게임이 스스로 참이라고 말한 값입니다. 그것을 읽는 데는 게임의
  동의도 게임의 코드 수정도 필요 없습니다.
- 값을 재는 쪽과 그림을 판정하는 쪽을 가른 것이 이 결정의 요점입니다. SDK 는 숫자와 문자열을 보고하고,
  판정은 그것을 읽는 쪽이 합니다.

### agent-server 의 현재 판정 방식

- 이 ADR 은 뒤집히지 않았지만, agent-server 는 지금 screen capture 를 보고 판정합니다.
- 그래도 이 ADR 이 그은 선은 그대로입니다 — SDK 는 값을 보고하고 agent 가 그림을 판정합니다.
- 관측 통로가 SDK 안에서 어디서 오는지에 대한 결정이지, 시스템 전체가 vision 을 쓰지 않는다는 결정이
  아닙니다.

## 거절한 대안

| 대안 | 왜 거절했나 |
| --- | --- |
| screen capture 를 vision AI 에 넘겨 SDK 가 판정 | 화면에 안 보이는 값을 답할 수 없고, 같은 화면을 두 번 보내도 같은 답이 온다는 보장이 없음 |
| 게임의 DB 나 외부 저장소를 읽기 | 게임마다 다르고, QA 대상 빌드가 그 접근 권한을 갖고 있으리라는 보장이 없음 |

- 체력 수치가 UI 에 없으면 그림에도 없습니다. 답이 재현되지 않으면 회귀 판정에 쓸 수 없습니다.
- 판정을 SDK 안에 두면 판정 규칙을 고칠 때마다 게임을 다시 빌드해야 합니다.

## 대가

| 대가 | 무엇이 일어나나 |
| --- | --- |
| 소리는 관측할 수 없음 | `AudioSource` 가 스캔에 나타나지 않음. 소리를 보는 테스트 케이스 넷(id 9, 10, 81, 82)을 지움 |
| 엔진 컴포넌트가 통째로 안 보임 | `Image`·`TMP_Text`·`EventTrigger` 의 직렬화 필드는 읽지 않음 |
| `onScreen=true` 는 보인다는 뜻이 아님 | `RectMask2D` 에 잘린 것, 완전히 가려진 것, `CanvasGroup.alpha=0` 인 것이 전부 `true` |
| 좌표를 그대로 싣지 않음 | `rect` 는 정수 픽셀, `world` 는 소수점 4 자리 |
| `GAME_STATE` 자체가 기본으로 꺼져 있음 | `ArtelManager.SendsGameState` 가 `false` |

- `SceneScanner.IsGameBehaviour` 는 컴포넌트가 `MonoBehaviour` 이면서 그 어셈블리가
  `Unity`·`System`·`mscorlib` 로 시작하지 않고 `Artel.Runtime` 도 아닐 때만 읽습니다.
  - `AudioSource` 는 Unity 내장 `Behaviour` 라 그 필터에 걸립니다
    (Notion `3b50bce5-474c-81de-be32-f98008c0494b`).
- 엔진 컴포넌트를 거르는 대가는 소리만이 아닙니다. 전부 읽으면 게임 자신의 데이터가 수백 개의 레이아웃
  필드 밑에 묻힙니다. 어셈블리 이름이 그 선이고, 선을 그은 이상 저쪽에 있는 것은 안 보입니다.
- `onScreen` 을 정확히 답하려면 블록마다 mask walk 나 raycast 가 붙는데, 이 코드는 폴링 경로에서 돕니다.
- 좌표를 자르지 않으면 숨 쉬는 idle 애니메이션 하나가 매 폴링 `GAME_STATE` 를 다시 내보냅니다. 자세한
  것은 [`docs/protocol.md`](../protocol.md) 에 있습니다.
- `GAME_STATE` 스위치는 `pulse` 가 그 자리를 대신할 수 있는지 재는 중이라 남아 있고, ARTEL-400 이 씬
  순회를 지우면 함께 사라집니다.
