using System.Collections.Generic;
using Artel.Affordances.Scan;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// collider 위의 <c>OnMouse*</c> handler 가 그 객체를 누르는 <c>calls</c> 항목으로 적히는지 본다(ARTEL-1227).
    /// </summary>
    public sealed class MouseMessageReaderTests
    {
        private readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var subject in made)
            {
                Object.DestroyImmediate(subject);
            }

            made.Clear();
        }

        private GameObject Make(string name)
        {
            var subject = new GameObject(name);
            made.Add(subject);
            return subject;
        }

        [Test]
        public void A3dColliderTurnsPressAndDragHandlersIntoCalls()
        {
            var card = Make("Card");
            card.AddComponent<BoxCollider>();
            var handler = card.AddComponent<DraggedCardFixture>();

            var calls = new List<PersistentCall>();
            MouseMessageReader.Read(handler, calls);

            CollectionAssert.AreEquivalent(new[] { "OnMouseDown", "OnMouseUp", "OnMouseDrag" }, Events(calls));

            foreach (var call in calls)
            {
                Assert.AreEqual(typeof(DraggedCardFixture).FullName, call.TargetType);
                Assert.AreEqual(call.Event, call.Method);
                Assert.AreEqual("Card", call.TargetPath);
            }
        }

        [Test]
        public void A2dColliderCountsAndInheritedHandlersAreFound()
        {
            var enemy = Make("Enemy");
            enemy.AddComponent<BoxCollider2D>();
            var handler = enemy.AddComponent<DerivedTargetFixture>();

            var calls = new List<PersistentCall>();
            MouseMessageReader.Read(handler, calls);

            CollectionAssert.AreEqual(new[] { "OnMouseDown" }, Events(calls));
            Assert.AreEqual(typeof(DerivedTargetFixture).FullName, calls[0].TargetType);
        }

        [Test]
        public void WithoutAColliderNothingIsWritten()
        {
            var handler = Make("Floating").AddComponent<DraggedCardFixture>();

            var calls = new List<PersistentCall>();
            MouseMessageReader.Read(handler, calls);

            Assert.IsEmpty(calls);
        }

        [Test]
        public void HoverOnlyHandlersAreNotPresses()
        {
            var subject = Make("Hover");
            subject.AddComponent<BoxCollider>();
            var handler = subject.AddComponent<HoverOnlyFixture>();

            var calls = new List<PersistentCall>();
            MouseMessageReader.Read(handler, calls);

            Assert.IsEmpty(calls);
        }

        private static List<string> Events(List<PersistentCall> calls)
        {
            var events = new List<string>();

            foreach (var call in calls)
            {
                events.Add(call.Event);
            }

            return events;
        }
    }

    public class DraggedCardFixture : MonoBehaviour
    {
        private void OnMouseDown() { }
        private void OnMouseUp() { }
        private void OnMouseDrag() { }
        private void OnMouseEnter() { }
    }

    public class TargetFixture : MonoBehaviour
    {
        protected void OnMouseDown() { }
    }

    public class DerivedTargetFixture : TargetFixture
    {
    }

    public class HoverOnlyFixture : MonoBehaviour
    {
        private void OnMouseEnter() { }
        private void OnMouseOver() { }
    }
}
