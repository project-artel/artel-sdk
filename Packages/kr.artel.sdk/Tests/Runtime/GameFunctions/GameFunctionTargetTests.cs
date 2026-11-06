using System.Collections.Generic;
using System.Linq;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using static Artel.Tests.GameFunctions.GameFunctionTestKit;

namespace Artel.Tests.GameFunctions
{
    /// <summary>계약의 대상 규칙 표를 한 줄씩 확인한다. instance cheat <c>heal</c> 이 <c>FixturePlayer</c> 위에서 돈다.</summary>
    public sealed class GameFunctionTargetTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private GameFunctionActions actions;

        [SetUp]
        public void SetUp()
        {
            actions = Actions(LoadWovenFixtures());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in created)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            created.Clear();
        }

        private FixturePlayer AddPlayer(string name)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            return gameObject.AddComponent<FixturePlayer>();
        }

        private ActionResultDto Heal(string target)
        {
            var fields = new Dictionary<string, object> { { "name", "heal" } };
            if (target != null)
            {
                fields["target"] = target;
            }

            return Invoke(actions, fields);
        }

        [Test]
        public void NoTarget_NoInstance_FailsWithNoActive()
        {
            Assert.That(Heal(null).Error, Is.EqualTo("no active FixturePlayer"));
        }

        [Test]
        public void NoTarget_OneInstance_RunsOnIt()
        {
            var player = AddPlayer("Player");

            var result = Heal(null);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(player.Health, Is.EqualTo(15), "the omitted amount takes its C# default of 5");
        }

        [Test]
        public void NoTarget_SeveralInstances_ListsAtMostFiveSelectors()
        {
            var players = Enumerable.Range(0, 6).Select(index => AddPlayer("Player" + index)).ToList();
            var selectors = players.Select(GameFunctionTargets.SelectorOf).OrderBy(selector => selector, System.StringComparer.Ordinal).ToList();

            var result = Heal(null);

            Assert.That(result.Error, Is.EqualTo(
                "6 FixturePlayer objects; pass target as one of: " + string.Join(", ", selectors.Take(5))));
        }

        [Test]
        public void AGivenTarget_RunsOnThatInstance()
        {
            AddPlayer("First");
            var second = AddPlayer("Second");

            var result = Heal(GameFunctionTargets.SelectorOf(second));

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(second.Health, Is.EqualTo(15));
        }

        [Test]
        public void ASelectorThatResolvesToNothing_SaysSo()
        {
            Assert.That(Heal("Nowhere[99]").Error, Is.EqualTo("Nothing at selector: Nowhere[99]"));
        }

        [Test]
        public void ASelectorOnAnObjectWithoutTheComponent_SaysWhichComponentIsMissing()
        {
            var bare = new GameObject("Bare");
            created.Add(bare);
            var selector = GameFunctionTargets.SelectorOf(bare.transform);

            Assert.That(Heal(selector).Error, Is.EqualTo("FixturePlayer is not on " + selector));
        }

        [Test]
        public void ATargetThatIsNotAString_IsABadArgument()
        {
            var result = Invoke(actions, new Dictionary<string, object> { { "name", "heal" }, { "target", 7L } });

            Assert.That(result.Error, Is.EqualTo("bad argument target: must be a selector string"));
        }
    }
}
