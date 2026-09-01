using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 술어 하나가 그 답을 돌려주려면 무엇이 참이어야 했는가.
    /// </summary>
    /// <remarks>
    /// <see cref="CallSiteConditions"/> 와 짝이다. 그쪽은 호출자가 호출에 닿기 위해 무엇이 참이어야 했는지에
    /// 답하고, 이쪽은 그 호출이 <c>true</c> 를 돌려주려면 무엇이 참이어야 했는지에 답한다. 조건이 호출 앞에서
    /// 멈추던 것이 이 둘 중 뒤엣것이 없었기 때문이다.
    ///
    /// 게임이 조건을 술어 메서드로 빼는 것은 흔한 스타일이고, 그럴 때마다 리포트가 말할 수 있는 것이 줄었다.
    /// 샘플 게임의 <c>IsStreaming</c> 이 그렇다: 이야기 화면이 넘어가는 것을 지키는 검사인데 리포트는
    /// <c>ChatWindowController.IsStreaming != 0</c> 이라고 말했고, 그것은 규칙이 아니라 호출의 이름이다.
    /// 그 프로퍼티가 실제로 하는 비교 — <c>streamingCoroutine != null</c> — 는 리포트 어디에도 없었다.
    ///
    /// 여기서 나오는 것은 피호출자 자신의 용어로 쓰였다. 호출자의 말로 옮기는 일은 호출 지점에 있는
    /// <see cref="Binding"/> 의 몫이고, 옮기지 못하면 아무것도 내놓지 않는 것도 그쪽의 몫이다.
    /// </remarks>
    internal static class PredicateConditions
    {
        /// <summary>한 블록이 내놓는 답.</summary>
        private readonly struct Answer
        {
            internal readonly BasicBlock At;
            internal readonly bool Value;

            internal Answer(BasicBlock at, bool value)
            {
                At = at;
                Value = value;
            }
        }

        /// <summary>
        /// 이미 읽은 답. 읽어 보고 실패한 것은 null 로 남으므로 다시 열지 않는다.
        /// </summary>
        /// <remarks>
        /// 술어는 게임 안에서 여러 자리에서 불린다. 부르는 자리마다 제어 흐름 그래프를 새로 만드는 것은 같은 답을
        /// 같은 값을 치르고 다시 얻는 일이다.
        /// </remarks>
        private static readonly Dictionary<MethodDefinition, Condition> WhenTrue =
            new Dictionary<MethodDefinition, Condition>();

        private static readonly Dictionary<MethodDefinition, Condition> WhenFalse =
            new Dictionary<MethodDefinition, Condition>();

        /// <summary>
        /// 지금 열려 있는 술어. 한 단계만 들어간다는 것이 이 필드가 뜻하는 전부다.
        /// </summary>
        /// <remarks>
        /// 술어가 술어를 부르면 그 안쪽은 호출로 남는다. 깊이를 여는 대신 닫아 두는 것은, 각 단계가 제 수신자를
        /// 갈아 끼워야 하고 그 사슬이 한 걸음이라도 어긋나면 완전한 확신을 가지고 엉뚱한 객체의 이름을 대기
        /// 때문이다. 재귀하는 술어도 이것이 막는다.
        /// </remarks>
        private static MethodDefinition _reading;

        /// <summary>
        /// <paramref name="callee"/> 가 <paramref name="wantTrue"/> 를 돌려주는 조건. 읽지 못하면 null.
        /// </summary>
        /// <remarks>
        /// 부정을 만들지 않는다. <c>if (!IsStreaming)</c> 도 읽어야 하지만, 조건 트리를 뒤집는 연산을 새로 들이는
        /// 대신 원하는 답을 처음부터 넣고 읽는다 — 비교는 연산자가 뒤집혀 나오고, 그것이 이미 <c>brtrue</c> 와
        /// <c>brfalse</c> 를 한 코드로 읽는 방식이다.
        /// </remarks>
        internal static Condition For(MethodDefinition callee, bool wantTrue)
        {
            if (!CanRead(callee))
            {
                return null;
            }

            var known = wantTrue ? WhenTrue : WhenFalse;

            if (known.TryGetValue(callee, out var already))
            {
                return already;
            }

            _reading = callee;

            Condition read;

            try
            {
                read = Read(callee, wantTrue);
            }
            finally
            {
                _reading = null;
            }

            known[callee] = read;
            return read;
        }

        /// <summary>어셈블리 사이에서 비운다: <see cref="MethodDefinition"/> 은 그것을 읽은 모듈의 것이다.</summary>
        internal static void Forget()
        {
            WhenTrue.Clear();
            WhenFalse.Clear();
            _reading = null;
        }

        /// <summary>
        /// 이 메서드를 열어 볼 값이 있고, 열어도 안전한지.
        /// </summary>
        /// <remarks>
        /// virtual 판정을 일부러 좁게 둔다. <c>Resolve</c> 가 주는 것은 선언된 메서드 하나이고, 실제로 도는 것이
        /// 자식 클래스의 구현이면 그 조건은 게임이 검사한 적 없는 것에 대한 문장이 된다. override 가 하나도 없는
        /// virtual 술어까지 함께 거절하게 되지만, 덜 읽는 쪽과 틀리게 읽는 쪽 중에서는 덜 읽는 쪽이다.
        /// </remarks>
        private static bool CanRead(MethodDefinition callee)
        {
            if (callee == null || _reading != null)
            {
                return false;
            }

            if (callee.ReturnType?.MetadataType != MetadataType.Boolean)
            {
                return false;
            }

            if (!callee.HasBody || AnalysisScope.IsTooLarge(callee))
            {
                return false;
            }

            return !callee.IsVirtual || callee.IsFinal || callee.DeclaringType.IsSealed;
        }

        private static Condition Read(MethodDefinition callee, bool wantTrue)
        {
            var graph = ControlFlowGraph.Build(callee.Body);

            if (graph == null || graph.Abandoned)
            {
                return null;
            }

            return Returned(callee, graph, wantTrue) ?? Chosen(graph, wantTrue);
        }

        /// <summary>
        /// 술어가 답을 제어 흐름으로 고를 때, 그 답에 닿는 조건.
        /// </summary>
        /// <remarks>
        /// <c>if (hp > limit) return true; return false;</c> 는 비교를 돌려주지 않는다. 상수 둘을 서로 다른
        /// 자리에서 내놓고, 어느 자리에 닿았는지가 곧 답이다. 그러니 물음은 이미 이 분석이 답할 줄 아는 것이 된다 —
        /// 그 블록에 닿으려면 무엇이 참이어야 했는가.
        ///
        /// 원하는 답을 내놓는 자리가 여럿이면 그중 아무 곳에나 닿아도 되므로 <see cref="Condition.Either"/> 다.
        /// 조건을 트리로 둔 이유가 이것이고, 목록으로 납작해지면 동시에 성립할 수 없는 것들을 함께 요구하게 된다.
        ///
        /// 돌려주는 자리 하나라도 상수가 아니면 통째로 포기한다. 그런 술어는 답의 일부를 값으로, 일부를 제어 흐름으로
        /// 고르는 것이고, 그중 읽은 절반만 내놓으면 그것은 조건의 반쪽 진술 — 그냥 틀린 선행 조건의 모양이다.
        /// </remarks>
        private static Condition Chosen(ControlFlowGraph graph, bool wantTrue)
        {
            var answers = Answers(graph);

            if (answers == null)
            {
                return null;
            }

            var dependence = ControlDependence.Compute(graph);
            var reached = new Condition[graph.Blocks.Count];
            var state = new byte[graph.Blocks.Count];
            var ways = new List<Condition>();

            foreach (var answer in answers)
            {
                if (answer.Value != wantTrue)
                {
                    continue;
                }

                ways.Add(VariantBuilder.ReachOf(graph, dependence, answer.At.Index, reached, state));
            }

            // 이 답을 내놓는 자리가 하나도 없다. 늘 반대만 돌려주는 술어이고, 그것을 조건 없음으로 적으면 게임이
            // 결코 하지 않는 말을 하게 된다.
            if (ways.Count == 0)
            {
                return null;
            }

            var when = Condition.Either(ways);

            // 읽지 못한 조각이 든 문장은 호출의 이름을 그대로 두는 것보다 나쁘다. 그쪽은 덜 말할 뿐이고, 이쪽은
            // 무엇을 마련해야 하는지 말하는 척하면서 말하지 않는다.
            return when.HasUnknown(new HashSet<Condition>()) ? null : when;
        }

        /// <summary>
        /// 어느 블록이 어느 답을 내놓는가. 전부 상수일 때만.
        /// </summary>
        /// <remarks>
        /// 최적화된 빌드는 <c>ldc.i4.1; ret</c> 로 자리마다 돌려주고, 디버그 빌드는 상수를 지역 변수에 넣고 한
        /// 자리로 모아 돌려준다. 뒤엣것은 모으는 블록에게 묻는 대신 그리로 들어오는 갈래들에게 각각 무엇을 넣었는지
        /// 묻는다 — 값을 추측하려고 블록 경계를 넘는 것이 없고, 각 블록에게 그 블록 자신의 용어로 묻는다.
        /// </remarks>
        private static List<Answer> Answers(ControlFlowGraph graph)
        {
            var funnel = SingleReturn(graph);

            return funnel != null ? Stored(funnel) : Direct(graph);
        }

        /// <summary>돌아가는 자리마다 상수를 스택에 남긴 모양.</summary>
        private static List<Answer> Direct(ControlFlowGraph graph)
        {
            var answers = new List<Answer>();

            foreach (var block in graph.Blocks)
            {
                if (block.IsExit || block.Last?.OpCode.Code != Code.Ret)
                {
                    continue;
                }

                if (!IsBoolean(VariantBuilder.Producer(block.Last, block), out var answer))
                {
                    return null;
                }

                answers.Add(new Answer(block, answer));
            }

            return answers.Count > 1 ? answers : null;
        }

        /// <summary>상수들이 지역 변수 하나로 모여 한 자리에서 돌아가는 모양.</summary>
        private static List<Answer> Stored(BasicBlock returning)
        {
            var load = returning.First;

            if (!VariantBuilder.IsLoadLocal(load, out var slot) ||
                IlReading.Preceding(returning.Last, returning.First) != load ||
                returning.Predecessors.Count < 2)
            {
                return null;
            }

            var answers = new List<Answer>();

            foreach (var from in returning.Predecessors)
            {
                var store = Consuming(from);

                if (!VariantBuilder.IsStoreLocal(store, out var stored) || stored != slot ||
                    !IsBoolean(VariantBuilder.Producer(store, from), out var answer))
                {
                    return null;
                }

                answers.Add(new Answer(from, answer));
            }

            return answers;
        }

        /// <summary>이 명령어가 밀어 넣는 것이 <c>true</c> 이거나 <c>false</c> 일 때.</summary>
        private static bool IsBoolean(Instruction pushed, out bool answer)
        {
            answer = false;

            if (!IlReading.TryConstant(pushed, out var value) || (value != 0 && value != 1))
            {
                return false;
            }

            answer = value == 1;
            return true;
        }

        /// <summary>
        /// 반환되는 값이 그 자체로 비교일 때, 그 비교.
        /// </summary>
        /// <remarks>
        /// <c>return streamingCoroutine != null</c> 은 분기를 하나도 만들지 않는다. 비교 명령어의 결과가 그대로
        /// 반환되므로 블록이 하나이고, 제어 흐름에게 물으면 참이어야 했던 것이 없다고 답한다. 조건은 제어 흐름이
        /// 아니라 돌려주는 값 안에 있다.
        ///
        /// 그래서 분기가 검사하는 값을 읽는 것과 같은 읽기다 — <see cref="VariantBuilder.ComparisonOperator"/> 에
        /// 원하는 답을 넣고 물으면 연산자가, <see cref="VariantBuilder.Operands"/> 가 양쪽 항과 그것이 누구의
        /// 것인지를 준다.
        /// </remarks>
        private static Condition Returned(MethodDefinition callee, ControlFlowGraph graph, bool wantTrue)
        {
            var returning = SingleReturn(graph);

            if (returning == null)
            {
                return null;
            }

            var block = Computing(returning) ?? returning;
            var comparison = VariantBuilder.ComparisonOperator(
                VariantBuilder.Producer(Consuming(block), block), wantTrue, block, out var operands);

            if (comparison == null)
            {
                return null;
            }

            VariantBuilder.Operands(
                operands, block, graph.HasThis, callee,
                out var left, out var right, out var context, out _, out _, out var watch);

            if (left == null || right == null)
            {
                return null;
            }

            // 주어를 잃은 조건은 옮기지 않는다. 누구의 것인지 모르는 항을 호출자의 문장 안에 놓으면, 호출자가
            // 그것을 제 것으로 읽을 자리에 아무도 그렇지 않다고 말해 주는 것이 없다. 여기서 멈추면 호출은 지금까지
            // 그랬듯 이름 그대로 남고, 그것은 적어도 틀리지 않다.
            if (context == null)
            {
                return null;
            }

            return Condition.FromTest(new Precondition
            {
                Left = left,
                Operator = comparison,
                Right = right,
                Context = context,
                Watch = watch,
                Offset = returning.Last.Offset
            });
        }

        /// <summary>
        /// 답을 실제로 계산한 블록. 디버그 빌드가 그것을 지역 변수로 옮겨 놓았을 때.
        /// </summary>
        /// <remarks>
        /// 최적화하는 컴파일러는 비교의 결과를 스택에 남긴 채 <c>ret</c> 한다. 디버깅용 컴파일러는 지역 변수에
        /// 넣고 무조건 점프를 건너 되읽으므로, 돌아가는 블록은 <c>ldloc; ret</c> 두 줄이고 그 안에는 읽을 것이
        /// 없다. 같은 소스가 에디터 스캔에서는 읽히고 개발 빌드에서는 읽히지 않게 되는 자리이고, 이 분석은 그
        /// 어긋남을 이미 한 번 겪었다 — 두 빌드가 같은 말을 하는 것처럼 읽히고 있었다.
        ///
        /// 들어오는 갈래가 하나이고 그것이 이 적재가 읽는 바로 그 슬롯에 저장했을 때만 건넌다. 갈래가 둘이면
        /// 답을 고른 것은 제어 흐름이고, 그것은 여기서 읽는 모양이 아니다.
        /// </remarks>
        private static BasicBlock Computing(BasicBlock returning)
        {
            var load = returning.First;

            // 블록이 딱 그 두 줄이어야 한다. 적재 앞에 다른 것이 있으면 이것은 답을 실어 나르기만 하는 블록이
            // 아니고, 여기서 읽는 모양도 아니다.
            if (!VariantBuilder.IsLoadLocal(load, out var slot) ||
                IlReading.Preceding(returning.Last, returning.First) != load ||
                returning.Predecessors.Count != 1)
            {
                return null;
            }

            var from = returning.Predecessors[0];

            if (from.Last?.OpCode.FlowControl != FlowControl.Branch)
            {
                return null;
            }

            return VariantBuilder.IsStoreLocal(Consuming(from), out var stored) && stored == slot
                ? from
                : null;
        }

        /// <summary>블록이 만든 값을 가져가는 명령어. 돌아가는 것이거나, 그것을 어딘가에 넣는 것.</summary>
        private static Instruction Consuming(BasicBlock block)
        {
            var last = block.Last;

            // 무조건 점프는 제 것을 만들지 않고 그 앞이 만든 것을 나른다.
            return last?.OpCode.FlowControl == FlowControl.Branch
                ? IlReading.Preceding(last, block.First)
                : last;
        }

        /// <summary>메서드가 돌아가는 자리가 하나일 때 그 블록. 아니면 null.</summary>
        /// <remarks>
        /// 돌아가는 자리가 여럿이라는 것은 술어가 제 답을 제어 흐름으로 고른다는 뜻이고, 그것은 여기서 읽는 모양이
        /// 아니다. 그 모양을 읽으려면 어느 자리가 어느 답을 내놓는지를 함께 물어야 한다.
        /// </remarks>
        private static BasicBlock SingleReturn(ControlFlowGraph graph)
        {
            BasicBlock found = null;

            foreach (var block in graph.Blocks)
            {
                if (block.IsExit || block.Last?.OpCode.Code != Code.Ret)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = block;
            }

            return found;
        }
    }
}
