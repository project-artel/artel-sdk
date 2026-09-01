using System.Collections.Generic;
using System.Text;
using Mono.Cecil;
using NUnit.Framework;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>
    /// 조건이 술어 메서드 안까지 읽히는지.
    /// </summary>
    /// <remarks>
    /// 이 어셈블리를 Cecil 로 다시 읽어 제 IL 을 분석기에 먹인다. 게임 하나를 스캔해 리포트를 뒤지는 대신
    /// 이렇게 하는 것은, 실패했을 때 어느 모양이 안 읽혔는지가 곧바로 나오기 때문이다.
    /// </remarks>
    [TestFixture]
    internal sealed class PredicateConditionsTests
    {
        private AssemblyDefinition _assembly;
        private TypeDefinition _fixtures;

        [SetUp]
        public void ReadOwnAssembly()
        {
            _assembly = AssemblyDefinition.ReadAssembly(typeof(PredicateFixtures).Assembly.Location);
            _fixtures = _assembly.MainModule.GetType(typeof(PredicateFixtures).FullName);

            PredicateConditions.Forget();
        }

        [TearDown]
        public void Close()
        {
            PredicateConditions.Forget();
            _assembly?.Dispose();
        }

        [Test]
        public void ExpressionBodiedPredicateReadsAsItsComparison()
        {
            Assert.That(Predicate("get_Alive", true), Is.EqualTo("PredicateFixtures.hp > 0"));
        }

        [Test]
        public void AskingForFalseFlipsTheOperator()
        {
            Assert.That(Predicate("get_Alive", false), Is.EqualTo("PredicateFixtures.hp <= 0"));
        }

        /// <remarks>
        /// 블록 body 는 디버그 빌드에서 답을 지역 변수에 넣고 무조건 점프를 건너 되읽는다. 그 모양을 못 읽으면
        /// 에디터 스캔과 개발 빌드가 같은 소스에 대해 다른 말을 하게 된다.
        /// </remarks>
        [Test]
        public void BlockBodiedPredicateReadsThroughTheLocalItWasStoredIn()
        {
            Assert.That(Predicate("get_Busy", true), Is.EqualTo("PredicateFixtures.handle != null"));
        }

        /// <remarks>
        /// 컴파일러는 참조를 <c>null</c> 과 견주는 일을 부호 없는 크기 비교로 쓴다. 그것을 크기로 읽으면
        /// <c>handle &gt; null</c> 이 나오는데, 참조에 크기 순서는 없으므로 아무도 마련할 수 없는 규칙이다.
        /// </remarks>
        [Test]
        public void NullComparisonReadsAsEqualityNotAsSize()
        {
            Assert.That(Predicate("get_Busy", false), Is.EqualTo("PredicateFixtures.handle == null"));
        }

        /// <remarks>
        /// 같은 명령어가 float 의 <c>&lt;=</c> 에도 쓰인다. null 을 알아보는 규칙이 그것까지 집어삼키면 크기
        /// 비교가 조용히 <c>==</c> 가 된다.
        /// </remarks>
        [Test]
        public void UnsignedComparisonWithoutNullStaysASizeComparison()
        {
            Assert.That(Predicate("get_WithinRatio", true), Is.EqualTo("PredicateFixtures.ratio <= 1"));
        }

        /// <remarks>
        /// 답을 제어 흐름으로 고르는 술어는 그 답을 내놓는 블록에 닿는 조건이 곧 답이다.
        /// </remarks>
        [Test]
        public void BranchingPredicateReadsAsTheConditionOnItsTrueArm()
        {
            Assert.That(
                Predicate("Ready", true),
                Is.EqualTo("PredicateFixtures.hp > PredicateFixtures.limit"));
        }

        [Test]
        public void AskingABranchingPredicateForFalseReadsTheOtherArm()
        {
            Assert.That(
                Predicate("Ready", false),
                Is.EqualTo("PredicateFixtures.hp <= PredicateFixtures.limit"));
        }

        /// <remarks>
        /// 참을 돌려주는 자리가 둘이면 둘 중 아무 곳에나 닿아도 된다. 목록으로 납작해지면 동시에 성립할 수 없는
        /// 것들을 함께 요구하게 된다.
        ///
        /// 둘째 자리는 첫 검사를 통과하지 못했어야 닿는다. 그 <c>hp &lt;= 0</c> 은 군더더기가 아니라 그 갈래를
        /// 타는 조건의 절반이고, 빼고 적으면 리포트가 게임보다 느슨한 말을 하게 된다.
        /// </remarks>
        [Test]
        public void TwoWaysToTrueArriveAsAChoice()
        {
            Assert.That(
                Predicate("EitherWay", true),
                Is.EqualTo(
                    "PredicateFixtures.hp > 0 or " +
                    "(PredicateFixtures.limit > 0 and PredicateFixtures.hp <= 0)"));
        }

        /// <remarks>
        /// 절반은 상수로, 절반은 값으로 답을 고르는 술어. 읽은 절반만 내놓으면 조건의 반쪽 진술이 된다.
        /// </remarks>
        [Test]
        public void PredicateThatMixesShapesIsNotRead()
        {
            Assert.That(Predicate("Mixed", true), Is.Null);
        }

        [Test]
        public void ConditionOnOwnObjectArrivesInTheCallersReport()
        {
            Assert.That(When("GuardedByOwn"), Is.EqualTo("PredicateFixtures.hp > 0"));
        }

        [Test]
        public void NegatedCallArrivesWithTheOperatorFlipped()
        {
            Assert.That(When("GuardedByNegatedOwn"), Is.EqualTo("PredicateFixtures.hp <= 0"));
        }

        /// <remarks>
        /// 피호출자는 제 필드를 <c>PredicateFixtures.handle</c> 이라 부른다. 호출자가 선 자리에서 그것은
        /// <c>other</c> 가 쥔 것이다.
        /// </remarks>
        [Test]
        public void ConditionOnAnotherObjectIsSaidInTheCallersTerms()
        {
            Assert.That(
                When("GuardedByOther"),
                Is.EqualTo("PredicateFixtures.other.handle != null"));
        }

        [Test]
        public void ArgumentIsSwappedForWhatTheCallerPassed()
        {
            Assert.That(
                When("GuardedByOtherWithArgument"),
                Is.EqualTo("PredicateFixtures.limit > 0"));
        }

        /// <remarks>
        /// 객체가 같아도 매개변수는 같지 않다. <c>mark</c> 는 호출자 어디에도 없는 이름이므로, 그것을 그대로
        /// 내놓으면 리포트가 마련할 수 없는 것을 마련하라고 청한다.
        /// </remarks>
        [Test]
        public void PredicateAboutItsOwnArgumentIsNotCarriedOntoTheCaller()
        {
            Assert.That(
                When("GuardedByOwnArgument"),
                Is.EqualTo("this.Above(PredicateFixtures.limit) != 0"));
        }

        [Test]
        public void BranchingPredicateArrivesInTheCallersReport()
        {
            Assert.That(
                When("GuardedByBranchingPredicate"),
                Is.EqualTo("PredicateFixtures.hp > PredicateFixtures.limit"));
        }

        [Test]
        public void ChoiceOfWaysArrivesInTheCallersReport()
        {
            Assert.That(
                When("GuardedByEitherWay"),
                Is.EqualTo(
                    "PredicateFixtures.hp > 0 or " +
                    "(PredicateFixtures.limit > 0 and PredicateFixtures.hp <= 0)"));
        }

        [Test]
        public void UnreadPredicateLeavesTheCallByItsName()
        {
            Assert.That(When("GuardedByMixedPredicate"), Is.EqualTo("this.Mixed() != 0"));
        }

        /// <remarks>
        /// 조건은 테스터가 마련해야 하는 것이고, 마련하는 일은 지금 무엇이 있는지 보는 데서 시작한다. 갈아 끼우기가
        /// 그 자리를 떨어뜨리면 문장만 좋아지고 확인은 여전히 못 한다.
        /// </remarks>
        [Test]
        public void SwappedConditionStillNamesWhereToReadItBack()
        {
            var watch = Watch(Guarded("GuardedByOther"));

            Assert.That(watch, Is.Not.Null);
            Assert.That(watch.Member, Is.EqualTo("handle"));
        }

        /// <summary>술어 하나가 그 답을 돌려주는 조건. 피호출자 자신의 용어로.</summary>
        private string Predicate(string name, bool wantTrue)
        {
            var read = PredicateConditions.For(Method(name), wantTrue);

            return read == null ? null : Wording(read);
        }

        /// <summary>호출자의 기록이 말하는 조건.</summary>
        private string When(string name)
        {
            return Wording(Guarded(name).When);
        }

        /// <summary>
        /// <c>Mark</c> 를 부르는 블록의 기록. 그것이 술어가 지키는 블록이다.
        /// </summary>
        /// <remarks>
        /// 술어를 부르는 블록도 호출을 하나 담으므로 기록이 둘 나온다. 그중 앞엣것은 아무것에도 지켜지지 않은
        /// 채 술어를 부른 자리이고, 여기서 물어보는 것이 아니다.
        /// </remarks>
        private Variant Guarded(string name)
        {
            var method = Method(name);
            var graph = ControlFlowGraph.Build(method.Body);
            var dependence = ControlDependence.Compute(graph);
            var variants = new List<Variant>();

            VariantBuilder.Collect(
                method, method, new[] { method }, false, graph, dependence, variants,
                "test", Condition.Always, true, null);

            foreach (var variant in variants)
            {
                foreach (var call in variant.Calls)
                {
                    if (call.Target != null && call.Target.EndsWith("::Mark()"))
                    {
                        return variant;
                    }
                }
            }

            Assert.Fail("no record for the block guarded by the predicate in " + name);
            return null;
        }

        private static WatchTarget Watch(Variant variant)
        {
            return variant.When.Kind == ConditionKind.Test ? variant.When.Test.Watch : null;
        }

        private MethodDefinition Method(string name)
        {
            foreach (var method in _fixtures.Methods)
            {
                if (method.Name == name)
                {
                    return method;
                }
            }

            Assert.Fail("no method named " + name + " on the fixtures");
            return null;
        }

        private static string Wording(Condition condition)
        {
            var text = new StringBuilder();
            var budget = 40;

            condition.Write(text, ref budget);
            return text.ToString();
        }
    }
}
