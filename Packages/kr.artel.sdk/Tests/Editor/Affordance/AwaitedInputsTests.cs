using System.Collections.Generic;
using NUnit.Framework;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>
    /// coroutine 이 마우스 handler 가 쓰는 필드를 기다릴 때, 기다린 뒤의 기록에 그 handler 가 gap 으로 붙는지 본다(ARTEL-1227).
    /// </summary>
    [TestFixture]
    internal sealed class AwaitedInputsTests
    {
        private const string Body = "System.Boolean Game.Zone/<CastSpell>d__1::MoveNext()";
        private const string Gap = "waits-for-input:Game.Enemy::OnMouseDown";

        private static Variant Writer()
        {
            var writer = new Variant
            {
                Method = "System.Void Game.Zone::SetTarget(Game.Enemy)",
                Entry = "System.Void Game.Enemy::OnMouseDown()"
            };
            writer.Outcomes.Add(new Outcome { Kind = "write", Category = "state", Target = "Zone.target", Offset = 5 });
            return writer;
        }

        private static Variant InBody(int offset, string method = Body)
        {
            var variant = new Variant { Method = method, Entry = "System.Void Game.Zone::OnButtonClick()" };
            variant.Outcomes.Add(new Outcome { Kind = "audio", Category = "observable", Target = "Zone.sound", Offset = offset });
            return variant;
        }

        private static Variant Waiting(string method = Body)
        {
            return new Variant
            {
                Method = method,
                Entry = "System.Void Game.Zone::OnButtonClick()",
                When = Condition.FromTest(new Precondition
                {
                    Left = "Zone.target", Operator = "!=", Right = "null", Context = "this", Offset = 40
                })
            };
        }

        [Test]
        public void RecordsAfterTheWaitNameTheHandlerThatEndsIt()
        {
            var after = InBody(60);
            var before = InBody(10);
            var variants = new List<Variant> { Writer(), Waiting(), after, before };

            Assert.That(AwaitedInputs.Mark(variants), Is.EqualTo(1));
            Assert.That(after.Gaps, Does.Contain(Gap));
            Assert.That(before.Gaps, Does.Not.Contain(Gap));
        }

        [Test]
        public void ATestOutsideACoroutineIsNotAWait()
        {
            const string update = "System.Void Game.Zone::Update()";
            var after = InBody(60, update);
            var variants = new List<Variant> { Writer(), Waiting(update), after };

            Assert.That(AwaitedInputs.Mark(variants), Is.EqualTo(0));
            Assert.That(after.Gaps, Is.Empty);
        }

        [Test]
        public void AFieldNoMouseHandlerWritesIsNotAWait()
        {
            var writer = Writer();
            writer.Entry = "System.Void Game.Zone::Update()";
            var after = InBody(60);
            var variants = new List<Variant> { writer, Waiting(), after };

            Assert.That(AwaitedInputs.Mark(variants), Is.EqualTo(0));
        }

        [Test]
        public void TheHandlerNameDropsReturnTypeAndParameters()
        {
            var found = new List<string>();
            AwaitedInputs.AddIfMouse("System.Void Combat.Enemies.SelectableObject::OnMouseDown()", found);
            AwaitedInputs.AddIfMouse("System.Void Combat.UI.CombineZone::OnButtonClick()", found);

            CollectionAssert.AreEqual(new[] { "Combat.Enemies.SelectableObject::OnMouseDown" }, found);
        }
    }
}
