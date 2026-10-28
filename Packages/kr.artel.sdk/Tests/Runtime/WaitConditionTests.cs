using Artel.Affordances.Live;
using Artel.Affordances.Scan;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Tests
{
    /// <summary><c>wait_until</c> 의 조건이 읽히고 평가되는지(ARTEL-1284). 선언형 소수만 받는다.</summary>
    public sealed class WaitConditionTests
    {
        private GameObject _probe;

        [SetUp]
        public void Up()
        {
            Lookup.Forget();
            WatchList.Forget();
            _probe = new GameObject("Wait probe");
        }

        [TearDown]
        public void Down()
        {
            Object.DestroyImmediate(_probe);
            Lookup.Forget();
            WatchList.Forget();
        }

        private static bool Parses(string text, out WaitCondition condition, out string error)
        {
            return WaitCondition.TryParse(text, out condition, out error);
        }

        private string Selector()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var index = System.Array.IndexOf(roots, _probe);
            return ScenePath.SelectorOf(_probe.transform, index);
        }

        [Test]
        public void static_멤버_비교를_읽고_평가한다()
        {
            Assert.That(Parses("LookupRoots::Plain == 5", out var condition, out var error), Is.True, error);
            Assert.That(condition.Holds(out error), Is.True, error);

            Assert.That(Parses("LookupRoots::Plain > 7", out condition, out _), Is.True);
            Assert.That(condition.Holds(out _), Is.False);

            Assert.That(Parses("LookupProgress::Title == \"intro\"", out condition, out _), Is.True);
            Assert.That(condition.Holds(out error), Is.True, error);
        }

        [Test]
        public void exists_와_active_는_selector_로_씬_객체를_본다()
        {
            Assert.That(Parses("exists(" + Selector() + ")", out var exists, out var error), Is.True, error);
            Assert.That(exists.Holds(out _), Is.True);

            Assert.That(Parses("active(" + Selector() + ")", out var active, out _), Is.True);
            Assert.That(active.Holds(out _), Is.True);

            _probe.SetActive(false);
            Assert.That(active.Holds(out _), Is.False);
            Assert.That(exists.Holds(out _), Is.True, "꺼진 객체도 있는 객체다.");

            Assert.That(Parses("exists(Nope[99])", out var missing, out _), Is.True);
            Assert.That(missing.Holds(out error), Is.False);
            Assert.That(error, Is.Null, "없는 것은 거짓이지 오류가 아니다.");
        }

        [Test]
        public void and_가_or_보다_먼저_묶이고_괄호가_그것을_바꾼다()
        {
            // false or (true and true) → true
            Assert.That(Parses("LookupRoots::Plain == 1 or LookupRoots::Plain == 5 and exists(" + Selector() + ")", out var a, out var error), Is.True, error);
            Assert.That(a.Holds(out _), Is.True);

            // (false or true) and false → false
            Assert.That(Parses("(LookupRoots::Plain == 1 or LookupRoots::Plain == 5) and exists(Nope[99])", out var b, out _), Is.True);
            Assert.That(b.Holds(out _), Is.False);
        }

        [Test]
        public void 식이나_모르는_모양은_거절한다()
        {
            Assert.That(Parses("LookupRoots::Plain + 1 == 6", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("wait_until condition"));
            Assert.That(Parses("Plain == 5", out _, out error), Is.False, "Type::member 가 아니다.");
            Assert.That(Parses("exists(", out _, out _), Is.False);
            Assert.That(Parses("", out _, out _), Is.False);
            Assert.That(Parses("LookupRoots::Plain == 5 and", out _, out _), Is.False);
        }

        [Test]
        public void 못_푸는_이름은_평가에서_오류로_말한다()
        {
            Assert.That(Parses("Nope::x == 1", out var condition, out _), Is.True, "문법은 맞다. 이름은 평가 때 푼다.");
            Assert.That(condition.Holds(out var error), Is.False);
            Assert.That(error, Does.Contain("no such type"));
        }

        [Test]
        public void 비교는_수_bool_문자열_null_을_각각_다룬다()
        {
            Assert.That(WaitCondition.Compare(3, "<=", 3.0), Is.True);
            Assert.That(WaitCondition.Compare(2.5f, ">", 2.0), Is.True);
            Assert.That(WaitCondition.Compare(true, "==", true), Is.True);
            Assert.That(WaitCondition.Compare("Battle", "==", "Battle"), Is.True);
            Assert.That(WaitCondition.Compare(KeyCode.Space, "==", "Space"), Is.True, "enum 은 이름으로.");
            Assert.That(WaitCondition.Compare(null, "==", null), Is.True);
            Assert.That(WaitCondition.Compare(null, "!=", 1.0), Is.True);
            Assert.That(WaitCondition.Compare(new System.Collections.Generic.List<int> { 1, 2 }, "==", 2.0), Is.True, "컬렉션은 개수로.");
        }
    }
}
