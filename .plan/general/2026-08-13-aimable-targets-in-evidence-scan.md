# 2026-08-13 — [SDK] evidence scan에 조작 어휘를 싣고 선별 정책을 이원화한다

- Date: 2026-08-13
- Jira: [ARTEL-397](https://artel-asm.atlassian.net/browse/ARTEL-397)
- Status: Implemented

## 실행 결과 (2026-08-13)

```
누락 0            화면에 있는 것을 하나도 잃지 않았다 (7개 씬 전부)
의미 타입 정확     TitleScene: button ×3 · sprite ×2 · text ×1
리포트 불변       records 318 · types 21 · objects 27 · atoms 820 · gesture 25
                 evidence 지문 d4b31e4da9504b7d 동일 · types 내용 동일 · unplaced 동일
```

씬별 조준 채널 대 기대값:

```
TitleScene       6 / 6      GameClearScene   2 / 2
StoryScene       6 / 5      GameOverScene    1 / 1
Map_scene       16 / 15     EndingScene      5 / 4
TurnBattleScene 16 / 9
```

제품이 기대값보다 많은 것은 정상이다 — 하니스는 `Graphic·Renderer·Selectable` 을 가진
**활성** 오브젝트만 기대값으로 삼고, 제품은 거기에 `Collider` 와 비활성까지 본다.

**하니스가 제품 판정을 베끼지 않도록 독립적으로 세웠다.** 같은 코드를 쓰면 둘 다 틀려도
통과한다. `Artel.Affordances.Editor` 가 `Artel.Runtime` 을 참조하지 않아 생긴 제약이
결과적으로 더 나은 검증이 됐다.

### 계획이 틀린 곳 둘

**`internal` 로 시작했다가 컴파일이 깨졌다.** 어셈블리 경계를 못 넘는다. 그런데 ARTEL-398
에서 `Artel.Runtime` 도 이 사전을 읽어야 하므로 **처음부터 공개 API 였어야 했다.** 읽기는
`public`, 쓰기(`Keep`)는 `internal` 로 두었다 — 채우는 것은 순회하는 쪽의 일이다.

**rect 는 배치 모드로 검증할 수 없다.** 52개 중 14개가 음수 좌표로 나왔다
(`[-1445,631 2400x1284]`). `-nographics` 에는 화면이 없어 `Screen.height` 와 카메라 투영이
실제 화면을 반영하지 않는다. **코드가 틀렸는지 환경이 없어서인지 구분되지 않는다.**
검증을 ARTEL-398 로 넘기고 수용 기준을 그쪽에 적었다 — 실제로 눌러 보는 수밖에 없다.

## Goal

evidence scan이 **조준할 수 있는 것**을 알아보고, 그것에 대해 `id`·`rect`·`interactable`·
의미 타입을 낼 수 있게 한다. 아직 아무도 쓰지 않는다 — 쓰는 것은 ARTEL-398이다.

**리포트는 한 글자도 바뀌지 않는다.** 그것이 이 이슈의 안전선이다.

## 측정으로 정한 것

WordVenture 씬 파일 7개:

```
전체 GameObject        77
조준 가능              31   ← Graphic · SpriteRenderer · Selectable · Collider
리포트(근거·배선)       27
```

**두 집합은 포함 관계가 아니다.**

```
리포트에만   TitleSceneController · SaveLoadController   근거의 출처, 화면에 없음
조준에만     Title · BackGround · Background 5           화면에 있음, 근거 없음
```

한쪽을 넓혀 다른 쪽을 만들 수 없다. 필터가 둘이어야 하는 이유다.

**조작 채널은 `SceneScanner`보다 작다** (31 vs 77). 그쪽은 활성 GameObject 전부를 블록으로
내보내는데, 그중 46개는 `Canvas`·`EventSystem`·`Main Camera`·`BackgroundMusic`처럼 화면에도
없고 입력도 못 받는다. 대체가 아니라 정제다.

## 순회는 이미 완전하다

```csharp
foreach (var transform in root.GetComponentsInChildren<Transform>(true))
    if (Describe(text, transform.gameObject, ...))
```

`SceneEvidenceScan`은 **비활성 포함 모든 transform을 이미 열거한다.** 선별은 쓸지 말지만
정한다. 그러므로 조작 채널을 더해도 **순회 비용이 늘지 않는다.**

## Context / Constraints

### `Artel.Runtime`을 참조하면 안 된다

`BlockTransformReader`(rect 판독)는 SDK의 `Artel.Runtime`에 있다. 그것을 참조하고 싶지만
**ARTEL-398에서 `Artel.Runtime`이 거꾸로 affordance를 참조해야 한다** (연결 시
`WatchLiveState` 호출, `ActionExecutor`가 새 사전 사용). 지금 참조를 걸면 그때 순환이 된다.

따라서 rect 판독을 **affordance 어셈블리 안에 다시 만든다.** 규칙은 같다:

```
RectTransform     GetWorldCorners → 스크린 사각형
Renderer 있음     bounds 를 스크린으로 투영   ← SpriteRenderer 가 RectTransform 이 아니라서
그 외             위치 한 점 (넓이 0)
```

### 사전은 id가 아니라 살아 있는 참조를 담는다

`ActionExecutor`는 `target.Click()`·`target.EnterText()`·`target.RectTransform`을 쓴다.
JSON에 id를 싣는 것만으로는 부족하고, **id → GameObject** 사전이 SDK 안에 있어야 한다.

이 이슈는 사전을 **채우기만** 한다. 누가 읽을지는 398이 정한다.

### 좌표계

`move_mouse`는 **좌상단 원점 픽셀**을 받는다 (SDK README). Unity 스크린 좌표는 좌하단
원점이므로 뒤집어야 한다. `BlockTransformReader`가 이미 그렇게 하고 있으므로 같은 규약을
따른다.

## Approach (Checklist)

- [ ] **Step 1: `Aimable.cs` 신설** — `Runtime/Affordance/Scan/`
  - [ ] `Is(GameObject)` — Graphic · SpriteRenderer/Renderer · Selectable · Collider 중 하나
  - [ ] `KindOf(GameObject)` — `button`·`editText`·`text`·`image`·`sprite`·`block`
  - [ ] `Interactable(GameObject)` — `Selectable.interactable` + `CanvasGroup` 차단 + `enabled`
  - [ ] 각 판정의 근거를 주석으로 남긴다

- [ ] **Step 2: `ScreenRect.cs` 신설** — rect 판독. `Camera.main`을 스캔당 한 번만 잡는다

- [ ] **Step 3: 순회에 붙인다**
  - [ ] `SceneEvidenceScan`의 기존 열거 안에서 `Aimable.Is` 를 물어 registry 에 담는다
  - [ ] **리포트 쓰기 경로를 건드리지 않는다** — 조건도 순서도 그대로

- [ ] **Step 4: 접근자**
  - [ ] `id → GameObject` 사전과 조준 대상 목록을 읽을 수 있게 연다. 398이 쓴다

- [ ] **Step 5: 검증 하니스** — 커밋하지 않는다
  - [ ] 에디터 스크립트로 `SceneScanner`와 새 registry 를 같은 씬에서 돌려 **이름 집합** 대조
  - [ ] play mode 없이 `Artel / Scan Loaded Scenes` 경로로 돈다

## Validation

| # | 확인 | 기대 |
|---|---|---|
| 1 | 리포트 수치 | records 318 · types 21 · objects 27 · atoms 820 · gesture 25 — **기준선과 동일** |
| 2 | evidence 지문 | `d4b31e4da9504b7d` 동일 |
| 3 | 조작 채널 ⊇ | `SceneScanner`가 `button`·`editText`·`text`·`image`·`sprite`를 달아 낸 블록이 **전부** 있다 |
| 4 | 맨 블록 | `Canvas`·`EventSystem`·`Camera` 는 빠져도 된다 |
| 5 | rect | 화면 안 오브젝트의 rect 가 0 넓이가 아니다 |
| 6 | EditMode | 262 / 0 |

**3번이 이 이슈의 판정이다.** 숫자 비교가 아니라 이름 집합 비교다 — 수는 줄어드는 것이
정상이므로 숫자로 보면 회귀처럼 읽힌다.

## Risks

- **새 코드다.** 지금까지는 "옮기고 안 바뀌었음"을 증명했지만, 여기서는 새 판정이 옳음을
  논증해야 한다. 리포트 불변은 안전선일 뿐 조작 채널의 옳음을 말해주지 않는다
- **rect 를 다시 만든다.** `BlockTransformReader`와 같은 규칙이어야 하는데 코드를 공유할 수
  없다. 두 구현이 어긋나면 조준이 빗나간다 — 398에서 실제로 눌러 봐야 확인된다
- **런타임 생성 오브젝트.** 씬 파일 기준 31개는 정적인 것만이다. `Instantiate` 로 생기는
  것은 씬 로드 시점 스캔에 안 잡힌다. 사전 갱신 주기 문제이며 398의 몫이다
- `Selectable` 은 `UnityEngine.UI` 참조를 요구한다. `Artel.Affordances.Scan` asmdef 에
  참조가 없으면 추가해야 하고, 그러면 uGUI 없는 프로젝트에서 컴파일이 깨진다 —
  **착수 시 확인하고, 필요하면 타입 이름 기반 판정으로 우회한다**

## Rollback

`git revert` 한 번. 새 파일 추가와 순회 안의 호출 몇 줄이 전부이며 리포트 경로를 건드리지
않으므로 되돌려도 기준선이 그대로다.
