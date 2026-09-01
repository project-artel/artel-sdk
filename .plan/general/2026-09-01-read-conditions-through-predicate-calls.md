# 2026-09-01 — 근거 조건이 bool 을 돌려주는 호출 안까지 읽는다

- Date: 2026-09-01
- Jira: ARTEL-700
- Status: Implemented

## Goal

`bool` 을 돌려주는 게임 자신의 메서드로 분기할 때, 그 메서드가 그 답을 돌려주는 조건을 읽어 호출자의
조건 자리에 넣는다.

지금:

```
if (StoryController.chatWindowController.IsStreaming != 0)
```

목표:

```
if (StoryController.chatWindowController.streamingCoroutine != null)
```

앞의 것은 호출 이름이라 테스터가 무엇을 준비해야 하는지 말하지 않고, 되읽을 자리도 없다. 뒤의 것은
규칙이고, 감시할 필드를 함께 나른다.

## Non-goals

- 엔진과 서드파티 어셈블리. `CallGraph.CalleeAt` 이 같은 모듈만 돌려주므로 `CompareTag` 는 계속
  `!= 0` 으로 남는다.
- override 가 여럿일 수 있는 virtual 호출.
- 여러 단계 중첩. 술어 안의 술어는 따라가지 않는다.
- 수신자도 인자도 없는 static 술어. `Binding` 이 비어 `ReadFrom` 이 거절한다. 지금 모양으로 되돌아간다.
- `switch` 나 정수를 돌려주는 메서드.
- 조건 렌더링 형식 바꾸기.

## Context / Constraints

### 지금 어디서 멈추는가

`VariantBuilder.ReadCondition` (`VariantBuilder.cs:879`) 이 `brtrue` / `brfalse` 를 만나면 그 값을 만든
명령어를 본다.

1. 비교면 `ComparisonOperator` (`VariantBuilder.cs:1244`) 가 연산자를, `Operands` 가 좌우 항을 읽는다.
   이것이 `if (a > v)` 가 읽히는 경로다.
2. 비교가 아니면 좌변을 `IlReading.Describe` 로 이름만 붙이고 우변에 `"0"` 을 둔다
   (`VariantBuilder.cs:931`). 호출은 `IlReading.CallName` (`IlReading.cs:441`) 이 `Owner.Method(args)`
   문자열로 만들고 끝난다.

callee 의 반환값을 읽는 코드는 없다. `Code.Ret` 를 언급하는 파일은 `SimpleSetter.cs` 하나뿐이고, 그것도
`ldfld` 와 `stfld` 만 허용해 분기나 비교가 하나라도 있으면 포기한다. 게다가 outcome 경로
(`OutcomeReader.cs:333`, `OutcomeReader.cs:573`) 에만 연결돼 있어 조건 경로에서는 부르지도 않는다.

같은 메서드 안이면 한 단계 간접은 이미 읽힌다. `Incoming` (`VariantBuilder.cs:379`) 이 들어오는 블록들이
지역 변수에 무엇을 밀어 넣었는지 앞으로 읽는다. 메서드 경계만 못 넘는다.

### 이미 있어서 쓸 수 있는 것

- `CallGraph.CalleeAt` (`CallGraph.cs:413`) — 같은 모듈에서 body 가 있는 메서드만 돌려준다. 스코프 판정이
  여기 이미 있다.
- `ControlFlowGraph.Build` 와 `ControlDependence.Compute` — 어떤 메서드에도 쓸 수 있다.
- `VariantBuilder.Reach` — "이 블록에 닿으려면 무엇이 참이어야 했나".
- `Condition.Either` — 참을 돌려주는 경로가 여럿일 때 그대로 받는다.
- `Binding.Of` (`Binding.cs:36`) 와 `Condition.ReadFrom` (`Condition.cs:235`) — callee 의 말을 호출자의
  말로 갈아 끼운다. 옮기지 못하면 통째로 null 을 돌려준다.

### 반환값의 두 가지 모양

`ret` 하나만 보는 것으로는 부족하다. 술어는 두 모양으로 컴파일된다.

**값 모양** — `return streamingCoroutine != null` 은 분기를 만들지 않는다. 비교 명령어의 결과가 그대로
반환된다. `IsStreaming` 이 이 모양이고, `return a > v` 도 이 모양이다. 블록이 하나뿐이라 control
dependence 는 `Always` 를 돌려준다. 조건은 제어 흐름이 아니라 **반환되는 값 안에** 있다.

**분기 모양** — `if (x) return true; return false;` 는 상수 `0` 과 `1` 을 서로 다른 블록에서 내놓는다.
여기서는 조건이 제어 흐름에 있다. 참을 돌려주는 블록들의 `Reach` 를 `Either` 로 묶은 것이 답이다.

디버그 빌드는 분기 모양의 반환을 지역 변수 하나로 모아 `ret` 을 하나로 만든다. 그 지역 변수는 여러 자리에서
저장되므로 `IlReading.StoredOnce` 가 거절한다. `Incoming` 이 결정 블록에 대해 하는 일 — 들어오는 블록에게
무엇을 밀어 넣었는지 묻기 — 을 `ret` 블록에 대해 한 번 더 한다.

### 부정을 만들지 않는다

`if (!IsStreaming)` 도 읽어야 한다. `Condition` 에는 부정이 없고, 트리를 De Morgan 으로 뒤집는 코드를
새로 만들 이유도 없다.

- 값 모양은 `ComparisonOperator(producer, holds, ...)` 에 `holds` 를 그대로 넘긴다. 연산자가 뒤집혀 나온다.
- 분기 모양은 상수 `1` 대신 상수 `0` 을 내놓는 블록들을 모은다.

양쪽 다 정확하고, 새 부정 연산이 필요 없다.

### `this` 에 대고 부른 호출은 갈아 끼우지 않는다

`Collect` (`VariantBuilder.cs:107`) 이 이미 그렇게 한다: `sameObject` 면 `binding` 을 쓰지 않고 callee 의
조건을 그대로 둔다. callee 의 `this` 가 호출자의 `this` 이므로 그 용어가 이미 맞기 때문이다.

새 코드도 같은 규칙을 쓴다. 수신자가 `this` 면 조건을 그대로, 아니면 `ReadFrom(binding)` 을 거쳐서.
`Swapped` 가 `StoryController.field` 를 `this.field` 로 만드는 것을 막는 것이 이 규칙이다.

판정은 `CallSiteConditions.OnThis` (`CallSiteConditions.cs:286`) 를 그대로 쓴다. `ReceiverWhere` 를 보고
`"this"` 인지 묻는 것으로는 안 된다. 그 물음에는 `this.combineZone.AddCard()` 도 `"this"` 라고 답하는데
— `this` 의 필드는 `this` 에 대한 것이므로 — 그것은 다른 객체다. `OnThis` 는 수신자가 `this` 에 속하는지가
아니라 `this` **인지**를 묻는다. 지금 `private` 이라 `internal` 로 연다.

### 옮겨진 조건의 identity 는 callee 의 offset 을 쓴다

`Condition.Key` 는 검사를 `문장 @ offset` 으로 가른다 (`Condition.cs:445` 부근). 옮겨진 조건은 callee 의
offset 을 그대로 나르므로, 한 술어를 두 자리에서 부르면 두 조건은 같은 offset 을 갖는다. 그때 `Key` 를
가르는 것은 옮겨진 문장뿐이다.

이것이 맞다. 문장이 같다는 것은 수신자까지 같은 이름으로 옮겨졌다는 뜻이고, 같은 객체에 대한 같은 검사
둘은 하나다. 문장이 다르면 `Key` 도 다르다.

### `ReadFrom` 이 `Watch` 를 버린다

`Condition.ReadFrom` 의 `Test` 분기 (`Condition.cs:265`) 는 새 `Precondition` 을 만들면서
`Left`, `Operator`, `Right`, `Context`, `SubjectLost`, `Offset` 만 넘기고 `Watch` 를 빠뜨린다.

`WatchTarget` 은 선언 타입과 멤버 이름 (`WatchTarget.cs:31` 부근) 이라 수신자 식을 바꿔도 그대로다.
`arg:` 치환에서도 같은 필드다. 그래서 그냥 나르면 된다.

이것은 지금도 있는 결함이다. 호출 경로를 따라 옮겨진 조건은 전부 되읽을 자리를 잃고 있다. 이 작업이
그 위에 얹히므로 먼저 고친다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 완료. 위 `## Context / Constraints` 가 그 결과다.

- [ ] **Step 1: `ReadFrom` 이 `Watch` 를 나른다**
  - `Condition.cs:265` 근처, `Test` 분기가 만드는 `Precondition` 에 `Watch = Test.Watch` 를 더한다.
  - 한 줄이다. 이것만으로 기존 동작이 나아진다.

- [ ] **Step 2: helper 넷을 `internal` 로 연다**
  - `VariantBuilder.Producer`, `VariantBuilder.ComparisonOperator`, `VariantBuilder.Operands`,
    `CallSiteConditions.OnThis`.
  - `Reach` 는 이미 `ReachOf` (`VariantBuilder.cs:216`) 로 열려 있다. `CallSiteConditions` 가 쓰고 있다.
  - 대안은 `VariantBuilder.cs` (지금 1372 줄) 에 200 줄을 더 넣는 것이다. 코딩 스타일이 거대한 클래스를
    피하라고 하므로 파일을 나누고 helper 를 어셈블리 안에서 연다.

- [ ] **Step 3: `PredicateConditions.cs` 를 새로 만든다**
  - 자리: `Packages/kr.artel.sdk/Editor/Affordance/CodeGen/PredicateConditions.cs`
  - 이름은 `CallSiteConditions` 와 짝을 이룬다. 그쪽은 "호출 지점에서 무엇이 참이어야 했나", 이쪽은
    "이 술어가 그 답을 돌려주려면 무엇이 참이어야 했나".
  - `internal static Condition For(MethodDefinition callee, bool wantTrue)`
  - 거절 조건 — 하나라도 걸리면 `null`:
    - 반환 타입이 `MetadataType.Boolean` 이 아니다
    - body 가 없거나 `AnalysisScope.IsTooLarge`
    - `IsVirtual` 이면서 `IsFinal` 도 아니고 선언 타입이 `sealed` 도 아니다
    - 이미 술어 하나를 읽는 중이다 (재진입 금지, 한 단계만)
  - 값 모양: `ret` 이 하나이고 그 producer 가 `ComparisonOperator` 로 읽히면
    `Condition.FromTest(...)` 하나를 돌려준다.
  - 분기 모양: 반환되는 값이 전부 상수 `0` 이나 `1` 이면, 원하는 상수를 내놓는 블록들의 `Reach` 를
    `Either` 로 묶어 돌려준다. 지역 변수로 모인 모양은 `ret` 블록의 predecessor 에게 물어 푼다.
  - 결과에 `HasUnknown` 이 있으면 `null`. 읽지 못한 조각이 든 문장은 지금의 `f() != 0` 보다 나쁘다.
  - 캐시: `SimpleSetter` 와 같은 방식. static 사전에 `(method, wantTrue)` 로 쥐고,
    `AffordanceILPostProcessor.cs:123` 의 `SimpleSetter.Forget()` 옆에서 `Forget()` 한다.

- [ ] **Step 4: `VariantBuilder.Literal` 에 hook 을 건다**
  - 자리: `Incoming` 이 `null` 을 돌려준 뒤, `ReadCondition` 을 부르기 전 (`VariantBuilder.cs:671`).
  - 분기가 `brtrue` / `brfalse` 이고 producer 가 `call` / `callvirt` 일 때만.
  - `CallGraph.CalleeAt` 으로 callee 를 풀고 `PredicateConditions.For(callee, holds)`.
  - 수신자가 `this` 면 그대로, 아니면 `IlReading.Receiver` / `ReceiverWhere` / `Arguments` 로
    `Binding.Of` 를 만들고 `ReadFrom`.
  - 무엇이든 `null` 이면 그대로 떨어져 `ReadCondition` 이 지금 모양을 만든다.

- [ ] **Step 5: 테스트 어셈블리를 만든다**
  - `Packages/kr.artel.sdk/Tests/Editor/Affordance/` 아래 `Artel.Affordances.CodeGen.Tests.asmdef`
  - `Unity.Artel.Affordances.CodeGen` 을 참조하고 `overrideReferences` 로 `Mono.Cecil.dll` 를 끌어온다.
    `optionalUnityReferences: ["TestAssemblies"]`.
  - CodeGen 어셈블리에 `[assembly: InternalsVisibleTo("Artel.Affordances.CodeGen.Tests")]` 를 더한다.
  - 테스트는 제 어셈블리를 Cecil 로 다시 읽는다:
    `AssemblyDefinition.ReadAssembly(typeof(PredicateFixtures).Assembly.Location)`.
    fixture 를 C# 으로 쓰고 그 IL 을 그대로 분석한다.
  - 이 분석기에 대한 첫 테스트다. 지금은 골든 파일이 유일한 검증이고 그것은 다른 레포에 있다.
  - **이 단계가 이 계획에서 가장 불확실하다.** `Unity.Artel.Affordances.CodeGen` 은 ILPostProcessor
    어셈블리라 `autoReferenced: false`, `noEngineReferences: true`, `overrideReferences: true` 다. 평범한
    EditMode 테스트 어셈블리가 이것을 참조할 수 있는지는 돌려 봐야 안다. Step 3 과 Step 4 를 먼저 끝내고
    이것을 붙인다. 참조가 안 되면 테스트를 접는 대신 fixture 를 별도 어셈블리로 빼고 분석기 진입점만
    얇게 여는 쪽을 시도한다. 그것도 안 되면 검증을 WordVenture 스캔 하나로만 하고 그 사실을 PR 에 적는다 —
    조용히 넘어가지 않는다.

- [ ] **Step 6: 테스트 케이스**
  - `return field != null` — 값 모양, 참
  - `return count > 0` — 값 모양, 참
  - `if (!x) return false; return true;` — 분기 모양
  - `if (a) return true; if (b) return true; return false;` — `Either` 로 나온다
  - `if (!IsReady())` — 부정된 쪽이 뒤집힌 연산자로 나온다
  - 다른 객체에 대고 부른 술어 — 수신자가 치환된다
  - 인자를 비교하는 술어 — 인자가 치환된다
  - 이름 붙일 수 없는 수신자 — 지금 모양 (`f() != 0`) 으로 되돌아간다
  - 읽을 수 없는 조각이 든 술어 — 지금 모양으로 되돌아간다
  - `virtual` 술어에 override 가 있는 경우 — 지금 모양으로 되돌아간다
  - 술어 안의 술어 — 한 단계만 풀고 나머지는 지금 모양
  - 옮겨진 조건이 `Watch` 를 나른다

- [ ] **커밋 순서**
  - 값 모양이 먼저 독립된 커밋으로 들어간다. `IsStreaming` 을 고치는 것이 그것이고, 여든 줄쯤이며,
    분기 모양 없이도 혼자 성립한다.
  - 분기 모양이 그다음 커밋이다. 이 계획에서 위험과 복잡도가 가장 큰 부분이라 따로 되돌릴 수 있게 둔다.

- [ ] **Step 7: 샘플 게임으로 확인**
  - WordVenture 를 다시 스캔해 `IsStreaming` 조건이 바뀌는 것을 본다.
  - `ScopeReport` 의 읽지 못한 조건 개수가 늘지 않는지 확인한다.

## Validation

- **Commands to run:**

  ```bash
  .github/scripts/setup-unity-test-project.sh /mnt/c/temp/artel-unity-test

  "/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1/Editor/Unity.exe" \
    -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath 'C:\temp\artel-unity-test' \
    -testResults 'C:\temp\artel-unity-test\results-editmode.xml' \
    -logFile 'C:\temp\artel-unity-test\unity-editmode.log'

  python3 .github/scripts/summarize-test-results.py \
    /mnt/c/temp/artel-unity-test/results-editmode.xml EditMode
  ```

- **Expected output:** merge-base 기준선 위에서 EditMode 가 늘어난 새 테스트만큼 늘고 실패가 없다.
  2026-08-27 기준 develop 은 EditMode 319, PlayMode 22 가 전부 통과였다. 먼저 기준선을 잰다.

- 종료 코드 2 는 테스트가 돌고 일부가 실패했다는 뜻이다. `results-editmode.xml` 을 읽는다.

## Risks & Rollback

- **틀린 조건을 확신에 차서 적을 위험.** 이 코드베이스가 이미 실측한 실패 방식이다 — 추측이 완벽하게
  읽히면서 엉뚱한 객체에 대한 문장을 만든다. 막는 것은 세 가지다: `ReadFrom` 이 반만 옮기면 통째로
  거절하는 것, `HasUnknown` 이면 버리는 것, virtual 이면 손대지 않는 것.
- **virtual 판정이 좁다.** `IsVirtual` 이면서 `IsFinal` 이 아닌 것을 전부 거절하므로, override 가 하나도
  없는 virtual 술어도 읽지 않는다. 틀리는 쪽보다 덜 읽는 쪽을 고른다.
- **분석 시간.** callee 마다 `ControlFlowGraph` 를 하나 더 만든다. 캐시가 메서드당 한 번으로 묶고,
  `AnalysisScope.IsTooLarge` (명령어 4000) 가 큰 것을 자른다. 재진입을 막으므로 깊이는 1 이다.
- **`Watch` 를 나르는 것이 감시 목록을 늘린다.** 늘어나는 것은 지금 조건이 이미 가리키고 있으나 옮겨지며
  잃은 필드들이다. `WatchListJson` 이 중복을 `Key` 로 접는다.
- **Rollback:** 전부 되돌릴 수 있다. Step 1 은 한 줄, Step 4 의 hook 은 몇 줄이라 그것만 빼면 지금
  동작으로 돌아간다. `git revert` 로 충분하다.

## What Changed While Implementing

계획을 쓰고 나서 넷이 달라졌다. 셋은 계획이 틀렸던 것이고, 하나는 실측이 새로 알려 준 것이다.

1. **`this` 판정.** `ReceiverWhere == "this"` 로 하려던 것을 `CallSiteConditions.OnThis` 로 바꿨다.
   앞엣것은 `this.zone.Method()` 에도 참이라 다른 객체를 같은 객체로 읽는다.

2. **디버그 빌드의 반환 funnel.** 계획은 값 모양이 곧 비교 명령어라고 보았는데, 블록 body 는 디버그
   빌드에서 답을 지역 변수에 넣고 무조건 점프를 건너 되읽는다. 그 모양을 못 읽으면 에디터 스캔과 개발
   빌드가 같은 소스에 대해 다른 말을 한다. `PredicateConditions.Computing` 이 그것을 건넌다.

3. **`this` 에 대고 부른 술어의 매개변수.** 객체가 같아도 매개변수는 같지 않다. `Above(int mark)` 안의
   `mark > 0` 은 호출자가 이름 댈 수 없는 것에 대한 문장이다. `AboutSelfOnly` 로 막는다.

4. **`null` 비교가 크기 비교로 읽히던 것.** 구현 뒤 실제 출력을 재 보니 `handle != null` 이
   `handle > null` 로 나왔다. 컴파일러가 참조의 null 비교를 `cgt.un` 으로 쓰는데 그것을 크기로 읽고
   있었다. 참조에 크기 순서는 없으므로 그 문장은 아무도 마련할 수도 확인할 수도 없고, 이 작업의 동기인
   `IsStreaming` 이 정확히 그 모양이다. 렌더링을 건드리지 않겠다던 non-goal 을 접고 범위에 넣었다.
   같은 명령어가 float 의 `<=` 에도 쓰이므로 피연산자 한쪽이 리터럴 `null` 일 때만 `!=` 로 읽는다.

## Open Questions

- 수신자도 인자도 없는 static 술어를 나중에 열 것인가. `Condition` 이 static 항만 든 것을 가릴 수 있어야
  하는데 (`AboutSelfOnly` 은 `this` 와 `static` 을 같이 참으로 본다) 지금은 그 구분이 없다. 이번에는
  non-goal 로 두고, 샘플 게임에서 몇 건인지 세어 본 다음 따로 다룬다.
- 테스트 어셈블리는 Unity 에디터가 디버그로 컴파일하므로 분기 모양이 지역 변수로 모인 형태만 검증된다.
  최적화된 모양은 WordVenture 스캔으로만 확인된다. 값 모양은 양쪽에서 같다.
