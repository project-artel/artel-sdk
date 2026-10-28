using System.Collections.Generic;
using NUnit.Framework;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>버튼 click 에 잘못 묶이는 기록을 걸러내는지 본다(ARTEL-1229).</summary>
    [TestFixture]
    internal sealed class ButtonJoinTests
    {
        private const string Start = "System.Void Combat.UI.CombineZone::Start()";
        private const string Handler = "System.Void Combat.UI.CombineZone::OnButtonClick()";

        private static Variant Handed(string handedTo, string handedMethod)
        {
            var variant = new Variant { Method = handedMethod, Entry = Start, HandedIn = 0, HandedTo = handedTo };
            variant.CallPath.Add(Start);
            variant.CallPath.Add(handedMethod);
            return variant;
        }

        [Test]
        public void AListenerThatIsItsOwnEntryIsNotRecordedAgainFromWhereItWasHandedOver()
        {
            var variants = new List<Variant>
            {
                Handed("UnityEngine.Events.UnityEvent::AddListener", Handler),
                Handed("UnityEngine.Events.UnityEvent::AddListener", "System.Void Combat.UI.CombineZone::<Start>b__1_0()"),
                Handed("Combat.Allies.AllySlime::add_Died", Handler)
            };

            var dropped = ButtonJoin.DropHandedRoots(variants, new HashSet<string> { Start, Handler });

            Assert.That(dropped, Is.EqualTo(1));
            Assert.That(variants, Has.Count.EqualTo(2), "lambda 와 UnityEvent 가 아닌 event 로 넘긴 기록은 남아야 한다.");
        }

        [Test]
        public void AValueTypeConstructorThatOnlyWritesItsFieldsIsDropped()
        {
            var prs = new Variant
            {
                Method = "System.Void Cards.Prs::.ctor(UnityEngine.Vector3,UnityEngine.Quaternion,UnityEngine.Vector3)"
            };
            prs.Outcomes.Add(new Outcome { Kind = "write", Target = "Prs.pos" });

            var spawning = new Variant { Method = "System.Void Cards.Deck::.ctor()" };
            spawning.Outcomes.Add(new Outcome { Kind = "spawn", Target = "Deck.card" });

            var behaviour = new Variant { Method = "System.Void Cards.Card::.ctor()" };
            behaviour.Outcomes.Add(new Outcome { Kind = "write", Target = "Card.count" });

            var variants = new List<Variant> { prs, spawning, behaviour };
            var dropped = ButtonJoin.DropValueConstructors(variants, type => type == "Cards.Card");

            Assert.That(dropped, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { spawning, behaviour }, variants);
        }

        [Test]
        public void AButtonHandlersRecordIsNotFoldedUnderAnotherEntry()
        {
            var fromDrop = new Variant
            {
                Method = "System.Void Combat.UI.CombineZone::SetActivateButtonVisible(System.Boolean)",
                MethodId = "id", Entry = "System.Void Cards.CardManager::CardMouseUp()",
                EntryId = "drop", TriggerKind = "unity-event"
            };
            var fromButton = new Variant
            {
                Method = fromDrop.Method, MethodId = "id", Entry = Handler, EntryId = "button", TriggerKind = "unity-event"
            };
            var fromUpdate = new Variant
            {
                Method = fromDrop.Method, MethodId = "id", Entry = "System.Void Combat.UI.CombineZone::Update()",
                EntryId = "update", TriggerKind = "lifecycle"
            };
            var fromDisable = new Variant
            {
                Method = fromDrop.Method, MethodId = "id", Entry = "System.Void Combat.UI.CombineZone::OnDisable()",
                EntryId = "disable", TriggerKind = "lifecycle"
            };

            var variants = new List<Variant> { fromDrop, fromButton, fromUpdate, fromDisable };
            var folded = DuplicateVariants.Fold(variants);

            Assert.That(folded, Is.EqualTo(1), "lifecycle 진입점 둘만 접혀야 한다.");
            CollectionAssert.AreEqual(new[] { fromDrop, fromButton, fromUpdate }, variants);
            Assert.That(fromUpdate.AlsoReachedBy, Has.Count.EqualTo(1));
        }
    }
}
