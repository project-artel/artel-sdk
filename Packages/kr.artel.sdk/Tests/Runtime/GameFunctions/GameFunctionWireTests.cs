using System.Collections.Generic;
using System.Linq;
using Artel.Protocol.Dto;
using Artel.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using static Artel.Tests.GameFunctions.GameFunctionTestKit;

namespace Artel.Tests.GameFunctions
{
    /// <summary>
    /// <c>GAME_FUNCTION_CATALOG</c>, <c>BEHAVIOR_ENDED</c>, <c>PULSE.functions</c>, <c>PULSE.behaviors</c> 를 계약의 JSON 과
    /// 그대로 맞춘다. null 인 키가 빠지지 않는 것이 요점이다.
    /// </summary>
    public sealed class GameFunctionWireTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private GameFunctionRegistry registry;
        private GameFunctionActions actions;
        private float clock;

        [SetUp]
        public void SetUp()
        {
            FixtureCheats.Reset();
            clock = 0f;
            registry = LoadWovenFixtures();
            actions = Actions(registry, () => clock);
        }

        [TearDown]
        public void TearDown()
        {
            actions.EndRun("test teardown");
            foreach (var gameObject in created.Where(gameObject => gameObject != null))
            {
                Object.DestroyImmediate(gameObject);
            }

            created.Clear();
        }

        private static JObject Serialize(object value) => JObject.Parse(new NewtonsoftJsonCodec().Serialize(value));

        private static void AssertJson(JToken actual, string expected)
        {
            var expectedToken = JToken.Parse(expected);
            Assert.That(JToken.DeepEquals(actual, expectedToken), Is.True,
                "expected " + expectedToken.ToString(Newtonsoft.Json.Formatting.None) +
                "\nactual   " + actual.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static JToken Entry(JObject catalog, string list, string name) =>
            catalog[list].First(entry => (string)entry["name"] == name);

        [Test]
        public void Catalog_StaticCheat_MatchesTheContractExample()
        {
            var catalog = Serialize(actions.BuildCatalog());

            Assert.That((string)catalog["type"], Is.EqualTo("GAME_FUNCTION_CATALOG"));
            Assert.That((long)catalog["revision"], Is.EqualTo(registry.Revision));
            AssertJson(Entry(catalog, "cheats", "give_gold"), @"{
                ""name"": ""give_gold"", ""description"": ""Adds gold to the wallet."",
                ""params"": [ { ""name"": ""amount"", ""type"": ""int"", ""min"": 1, ""max"": 1000000, ""default"": null, ""values"": null } ],
                ""target"": null, ""group"": null }");
        }

        [Test]
        public void Catalog_InstanceBehavior_CarriesTargetGroupAndHidesTheContext()
        {
            var catalog = Serialize(actions.BuildCatalog());

            AssertJson(Entry(catalog, "behaviors", "auto_attack"), @"{
                ""name"": ""auto_attack"", ""description"": ""Attacks every frame while in battle."",
                ""params"": [ { ""name"": ""range"", ""type"": ""float"", ""min"": 1, ""max"": 20, ""default"": null, ""values"": null } ],
                ""target"": { ""component"": ""FixturePlayer"" }, ""group"": ""combat"" }");
        }

        [Test]
        public void Catalog_EnumDefault_IsTheMemberName()
        {
            var catalog = Serialize(actions.BuildCatalog());

            AssertJson(Entry(catalog, "cheats", "travel")["params"][0],
                @"{ ""name"": ""to"", ""type"": ""enum"", ""min"": null, ""max"": null, ""default"": ""Boss"", ""values"": [""Start"", ""Boss""] }");
            AssertJson(Entry(catalog, "cheats", "heal")["params"][0],
                @"{ ""name"": ""amount"", ""type"": ""int"", ""min"": null, ""max"": null, ""default"": 5, ""values"": null }");
        }

        [Test]
        public void BehaviorEnded_KeepsNullTargetAndDetail()
        {
            Start(actions, new Dictionary<string, object> { { "name", "count" } });
            actions.StopBehavior(2, Params(new Dictionary<string, object> { { "behaviorId", 1L } }));
            actions.TryDequeueEnded(out var ended);

            var json = Serialize(ended);

            AssertJson(json, @"{ ""type"": ""BEHAVIOR_ENDED"", ""behaviorId"": 1, ""name"": ""count"", ""target"": null,
                ""reason"": ""stopped"", ""detail"": null, ""seconds"": 0, ""frame"": " + ended.Frame + " }");
        }

        [Test]
        public void Pulse_WholeListsEveryEntry_IncludingTheNoInstancePlaceholder()
        {
            var pulse = new GameFunctionPulse(actions, () => clock);

            var fragment = pulse.Compose(true, out var news);
            var document = JObject.Parse("{" + fragment.Substring(1) + "}");

            Assert.That(news, Is.True);
            Assert.That((bool)document["functions"]["whole"], Is.True);
            var entries = document["functions"]["entries"];
            AssertJson(entries.First(entry => (string)entry["name"] == "give_gold"),
                @"{ ""kind"": ""cheat"", ""name"": ""give_gold"", ""target"": null, ""available"": true, ""reason"": null }");
            AssertJson(entries.First(entry => (string)entry["name"] == "auto_attack"),
                @"{ ""kind"": ""behavior"", ""name"": ""auto_attack"", ""target"": null, ""available"": false, ""reason"": ""no active FixturePlayer"" }");
            AssertJson(document["behaviors"], "[]");
        }

        [Test]
        public void Pulse_Delta_RemovesThePlaceholder_WhenAnInstanceAppears_AtMostOncePerSecond()
        {
            var pulse = new GameFunctionPulse(actions, () => clock);
            pulse.Compose(true, out _);

            var gameObject = new GameObject("Player");
            created.Add(gameObject);
            var player = gameObject.AddComponent<FixturePlayer>();
            player.InBattle = false;
            var selector = GameFunctionTargets.SelectorOf(player);

            clock = 0.5f;
            Assert.That(pulse.Compose(false, out var earlyNews), Does.Not.Contain("functions"));
            Assert.That(earlyNews, Is.False);

            clock = 1.0f;
            var fragment = pulse.Compose(false, out var news);
            var functions = JObject.Parse("{" + fragment.Substring(1) + "}")["functions"];

            Assert.That(news, Is.True);
            Assert.That((bool)functions["whole"], Is.False);
            var entries = functions["entries"].ToList();
            Assert.That(entries.Count(entry => (string)entry["name"] == "give_gold"), Is.Zero, "unchanged entries are not repeated");
            AssertJson(entries.First(entry => (string)entry["name"] == "auto_attack" && entry["removed"] != null),
                @"{ ""kind"": ""behavior"", ""name"": ""auto_attack"", ""target"": null, ""removed"": true }");
            AssertJson(entries.First(entry => (string)entry["name"] == "auto_attack" && entry["removed"] == null),
                @"{ ""kind"": ""behavior"", ""name"": ""auto_attack"", ""target"": """ + selector +
                @""", ""available"": false, ""reason"": ""CanFight returned false"" }");
        }

        [Test]
        public void Pulse_Behaviors_ListsWhatRuns_AndRaisesNewsOncePerSecond()
        {
            var pulse = new GameFunctionPulse(actions, () => clock);
            pulse.Compose(true, out _);
            Start(actions, new Dictionary<string, object> { { "name", "count" }, { "maxSeconds", 60L } });

            clock = 0.2f;
            var fragment = pulse.Compose(false, out var news);
            var behaviors = JObject.Parse("{" + fragment.Substring(1) + "}")["behaviors"];

            Assert.That(news, Is.True, "a new behavior is news");
            AssertJson(behaviors, @"[ { ""behaviorId"": 1, ""name"": ""count"", ""target"": null, ""seconds"": 0,
                ""maxSeconds"": 60, ""report"": ""frames=1"" } ]");

            clock = 0.4f;
            pulse.Compose(false, out var quiet);
            Assert.That(quiet, Is.False, "nothing changed within the second");

            clock = 1.3f;
            pulse.Compose(false, out var due);
            Assert.That(due, Is.True, "a running behavior is reported once a second");
        }

        [Test]
        public void Pulse_WithoutAnyBehaviorRegistered_SendsNoBehaviorsField()
        {
            var empty = Actions(new GameFunctionRegistry(), () => clock);
            var pulse = new GameFunctionPulse(empty, () => clock);

            var fragment = pulse.Compose(true, out _);

            Assert.That(fragment, Does.Not.Contain("behaviors"));
        }
    }
}
