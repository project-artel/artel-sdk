# ADR 0005 — affordance analyser 를 여는 define gate 를 없애고 3겹 안전장치로 대신한다

- 상태: 뒤집힘 (두 번)
- 근거: `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/AffordanceILPostProcessor.cs`, `.plan/general/2026-08-13-port-affordance-attribute-assembly-and-manifest.md`, `.plan/general/2026-08-13-port-affordance-il-analyser-locked.md`, `.plan/general/2026-08-13-unlock-affordance-analyser-and-fix-ilpp-order.md`

## 결정

- `ARTEL_AFFORDANCE` scripting define gate 를 없앱니다.
- 지금 `AffordanceILPostProcessor.WillProcess` 는 어셈블리 이름만 봅니다.

```csharp
public override bool WillProcess(ICompiledAssembly compiledAssembly)
{
    return !IsSkipped(compiledAssembly.Name);
}
```

gate 를 대신하는 것은 세 겹입니다.

| 겹 | 무엇 |
| --- | --- |
| 모든 루프가 유계 | 분석이 어차피 끝남 |
| 어셈블리 하나에 10 초 (`BudgetMilliseconds = 10000`) | 그 뒤에는 닿은 만큼을 보고하고 나머지를 남김 |
| 어떤 `throw` 든 `Process` 에 떨어짐 | 컴파일러 자신의 어셈블리를 그대로 돌려줌 |

- 실패하거나, 거절하거나, 아무것도 찾지 못한 모든 경로가 `null` 을 들고 같은 자리에 도착하므로, 전체가
  다 되지 않는 한 원본이 섭니다.
- 분석 자체는 `UNITY_EDITOR` 나 `DEVELOPMENT_BUILD` 가 있는 어셈블리에만 돕니다(`IsDiscoveryBuild`).
  그것은 gate 가 아니라 출시 빌드에는 읽을 evidence 가 없다는 사실의 반영입니다.

## 왜

- 코드에는 gate 가 없습니다.
  - `defineConstraints` 는 모든 asmdef 에서 비어 있습니다 — 선택적 의존 패키지의 존재를 보는
    `Artel.Affordances.Addressables` 의 `ARTEL_ADDRESSABLES` 만 남았습니다.
  - `IsEnabledFor`·`EnableDefine`·`Unlocked` 는 저장소에 하나도 없습니다.
- 그 결정과 이유는 `AffordanceILPostProcessor` 의 클래스 주석에만 적혀 있습니다.

> define 은 그 값을 하기를 그만두었다. 도구가 존재하려면 프로젝트가 옵트인돼 있어야 하므로 무언가가
> 그들을 대신해 옵트인해 주어야 했고, 그러면 그것이 막아 주던 상태 — 설치됐는데 꺼짐 — 는 손으로만
> 도달하는 곳이 됐다.

- gate 는 사람이 켠다는 전제 위에 서 있었는데, 실제로는 설치 과정이 사람을 대신해 켜 주게 됐습니다.
- 그 순간 gate 가 지키던 상태는 아무도 도달하지 않는 상태가 되고, 그 아래 코드 전부는 "분석이 존재하기는
  하는지" 를 먼저 묻고 시작해야 했습니다.

## plan 문서 셋과 코드의 차이

- plan 문서 셋이 이 결정을 다루는데, 셋 다 "gate 를 유지한다" 에서 끝나고 셋 중 어느 것도 실제로 나간
  결론을 적지 않습니다.

| 문서 | 이슈 | 그 문서의 입장 |
| --- | --- | --- |
| `2026-08-13-port-affordance-attribute-assembly-and-manifest.md` | ARTEL-391 | "최종 결정: 게이트를 그대로 유지한다" — 한 문서 안에서 뒤집힌 결과 |
| `2026-08-13-port-affordance-il-analyser-locked.md` | ARTEL-392 | 9,472 줄을 잠근 채 머지. gate 유지를 명시 |
| `2026-08-13-unlock-affordance-analyser-and-fix-ilpp-order.md` | ARTEL-393 | gate 제거는 비목표, "유지하기로 확정됐다" 고 적음 |

- ARTEL-391 은 한 문서 안에서 결정이 뒤집힙니다.
  - Open Questions 의 Q1 은 처음에 "gate 를 들여오지 않는다" 로 적혔고, 같은 날 같은 문서 안에서
    취소됐습니다.
  - 제거를 주장하며 든 근거 셋이 왜 틀렸는지가 그 아래에 남아 있습니다 — "gate 가 아끼는 건 에디터
    컴파일 시간뿐"(두 번째 gate 는 게임 어셈블리 무접촉을 보장하고 그것이 먹통 방지입니다), "침묵하는
    실패가 최악"(열리지도 않는 에디터 앞에서는 침묵이 안전한 쪽입니다), "kill switch 는 opt-out
    이어야"(먹통이 된 에디터에서는 스위치를 끌 수 없습니다).
- ARTEL-392 는 **9,472 줄**짜리 analyser 를 옮기되 **잠근 채로** 머지했습니다.
  - "옮기기" 와 "동작시키기" 를 두 이슈로 나눈 이유는 한 이슈로 묶으면 회귀가 났을 때 이동 탓인지
    활성화 탓인지 가릴 수 없기 때문입니다.
  - 잠금은 `private static readonly bool Unlocked = false;` 하나였습니다.
  - `const` 가 아닌 것이 일부러입니다 — `const` 는 컴파일 시점에 접혀 죽은 코드로 읽히고, 다음 이슈의
    리뷰어에게는 "지우면 되는 스위치" 로 보여야 했습니다.
- ARTEL-393 은 `Unlocked` 를 지우고 ILPP 순서 의존성을 없앴습니다.

## 거절한 대안

| 대안 | 왜 거절했나 |
| --- | --- |
| gate 를 유지한다 | 근거는 지금도 유효하지만 위의 이유로 버림. 세 겹이 gate 가 하던 일을 실제로 대신하는지가 이 결정의 전부 |
| `ILPostProcessor` 실행 순서를 고정한다 | Unity 는 ILPP 실행 순서를 보장하지 않고 지정하는 지원되는 수단도 없음 |

- gate 유지의 근거는 이 분석이 세 차례 Unity Editor 를 먹통으로 만들었고 그때마다 왜 그런지 알아보려고
  그것을 열 수조차 없었다는 것입니다. 빠져나오는 길은 Unity 를 죽이고 manifest 를 손으로 고치는
  것이었습니다.
- 순서를 고정하는 대신 순서에 무관하게 만들었습니다.
  - gesture 를 읽는 자리가 `UnityEngine.Input` 과 `Artel.ArtelInput` 을 **둘 다** 받습니다.
  - proxy 의 멤버 이름이 원본과 정확히 같아서 `switch (called.Name)` 은 한 글자도 바뀌지 않았고, 선언
    타입 비교만 넓혔습니다.
- 착수 전 A/B 실측에서 SDK 패키지 유무와 무관하게 gesture 가 25 로 같았습니다.
  - 그것이 "지금 순서가 맞다" 는 증거로 읽혔는데, 실은 affordance 가 먼저 도는 우연이었습니다.
  - 순서가 바뀌면 실패가 아니라 **축소**로 나타납니다 — 컴파일도 테스트도 통과하는데 gesture 만 0 이
    됩니다.

## 대가

| 대가 | 무엇이 일어나나 |
| --- | --- |
| 실패 경로를 읽어서 확인할 수 없음 | 예외를 일부러 주입해서 확인했음 |
| 사고의 사정거리가 넓어짐 | affordance ILPP 가 SDK 를 설치한 모든 프로젝트의 모든 게임 어셈블리에서 돎 |
| 10 초 예산은 분석을 끝나게 하는 장치가 아님 | 모든 루프가 유계이므로 분석은 어차피 끝남. 이것은 분석을 *곧* 끝나게 함 |
| `WillProcess` 의 접두어 목록에 `Artel` 이 있음 | 제 어셈블리를 그렇게 이름 지은 게임은 조용히 지나쳐짐 |

- gate 가 애초에 있었던 이유가 먹통 이력이고, 그 마지막 방어선을 형식적으로 읽고 넘어갈 수는
  없었습니다.
- 아무도 예상 못 한 모양의 어셈블리는 완벽히 유한하면서도 느릴 수 있고, 몇 분씩 멈춰 있는 컴파일은
  그것을 기다리는 사람에게 고장 난 빌드입니다.
- `Artel` 을 넓게 잡은 것은 이 vendor 의 형제 SDK 도 함께 걸러야 했기 때문입니다 — 샘플 프로젝트에서
  재 보니 그 SDK 자신의 컴포넌트 둘이 2.5MB 짜리 리포트 중 2MB 를 차지했습니다.
