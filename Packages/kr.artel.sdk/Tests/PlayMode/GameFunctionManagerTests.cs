using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Artel.Auth;
using Artel.Tests.GameFunctions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// 살아 있는 <see cref="ArtelManager"/> 위에서 game function frame 이 언제 나가는지 본다. 등록은 fixture 어셈블리에
    /// Unity ILPP 가 만든 <c>RuntimeInitializeOnLoadMethod</c> 가 플레이 모드 진입 때 한 것이다.
    /// </summary>
    public sealed class GameFunctionManagerTests
    {
        private ArtelManager displacedInstance;
        private GameObject host;
        private ArtelManager manager;
        private ScriptedTransport transport;
        private float timeScale;

        [SetUp]
        public void SetUp()
        {
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            displacedInstance = ArtelManagerSlot.Clear();
            FixtureCheats.Reset();
            timeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = timeScale;
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            ArtelManagerSlot.Restore(displacedInstance);
            ArtelSecretStore.Current = null;
        }

        [Test]
        public void TheWovenRegistration_RanWhenPlayModeStarted()
        {
            Assert.That(GameFunctionRegistry.Shared.TryGet(GameFunctionKind.Cheat, "give_gold", out _), Is.True);
            Assert.That(GameFunctionRegistry.Shared.TryGet(GameFunctionKind.Behavior, "auto_attack", out _), Is.True);
        }

        [UnityTest]
        public IEnumerator TheCatalog_FollowsDeviceContext_OnConnect()
        {
            yield return StartManager();

            var types = transport.Sent.Select(Type).ToList();
            var deviceContext = types.IndexOf("DEVICE_CONTEXT");
            Assert.That(deviceContext, Is.GreaterThanOrEqualTo(0));
            Assert.That(types[deviceContext + 1], Is.EqualTo("GAME_FUNCTION_CATALOG"));

            var catalog = JObject.Parse(transport.Sent[deviceContext + 1]);
            Assert.That(catalog["cheats"].Select(entry => (string)entry["name"]), Does.Contain("give_gold"));
        }

        [UnityTest]
        public IEnumerator ACatalogChange_IsSentAgain()
        {
            yield return StartManager();
            var before = transport.Sent.Count(message => Type(message) == "GAME_FUNCTION_CATALOG");

            ArtelCheats.Register("manager_test_cheat", "Registered at runtime.", null, args => null);
            try
            {
                yield return new WaitForSecondsRealtime(1.2f);

                Assert.That(transport.Sent.Count(message => Type(message) == "GAME_FUNCTION_CATALOG"), Is.EqualTo(before + 1));
            }
            finally
            {
                ArtelCheats.Unregister("manager_test_cheat");
            }
        }

        [UnityTest]
        public IEnumerator RunFinished_EndsRunningBehaviors_WithRunEnded()
        {
            yield return StartManager();
            transport.Receive(StartCount());
            yield return null;
            yield return null;
            Assert.That(transport.Sent.Any(message => Type(message) == "ACTION_RESULT" && message.Contains("\"success\":true")),
                Is.True, string.Join("\n", transport.Sent));

            transport.Receive("{\"type\":\"RUN_STATUS\",\"state\":\"FINISHED\",\"qaRunId\":1,\"qaTryId\":1}");
            yield return null;
            yield return null;

            var ended = transport.Sent.Where(message => Type(message) == "BEHAVIOR_ENDED").Select(JObject.Parse).Single();
            Assert.That((string)ended["reason"], Is.EqualTo("run_ended"));
            Assert.That((string)ended["detail"], Is.EqualTo("run finished"));
            Assert.That(FixtureCheats.CountFinallyRuns, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ALostConnection_EndsRunningBehaviors()
        {
            yield return StartManager();
            transport.Receive(StartCount());
            yield return null;
            yield return null;

            transport.Connected = false;
            yield return null;
            yield return null;

            Assert.That(FixtureCheats.CountFinallyRuns, Is.EqualTo(1));
            Assert.That(transport.Sent.Any(message => Type(message) == "BEHAVIOR_ENDED"), Is.False,
                "a frame for a closed socket is dropped, not sent");
        }

        [UnityTest]
        public IEnumerator PauseTime_FreezesARunningBehavior()
        {
            yield return StartManager();
            transport.Receive(StartCount());
            yield return null;
            yield return null;

            Time.timeScale = 0f;
            yield return null;
            var frozenReport = RunningReport();
            yield return null;
            yield return null;

            Assert.That(RunningReport(), Is.EqualTo(frozenReport));

            Time.timeScale = 1f;
            yield return null;
            yield return null;
            Assert.That(RunningReport(), Is.Not.EqualTo(frozenReport));
        }

        private string RunningReport()
        {
            var actions = (GameFunctionActions)typeof(ArtelManager)
                .GetField("gameFunctions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(manager);
            return actions.Runner.Running.Single().Context.LatestReport;
        }

        private static string StartCount() =>
            "{\"type\":\"ACTION\",\"id\":9,\"actions\":[{\"id\":1,\"method\":\"start_behavior\",\"params\":[{\"name\":\"count\"}]}]}";

        private static string Type(string message) => (string)JObject.Parse(message)["type"];

        private IEnumerator StartManager()
        {
            host = new GameObject("Artel game function test");
            manager = host.AddComponent<ArtelManager>();
            transport = new ScriptedTransport();
            manager.SetWebSocketTransport(transport, false);
            yield return null;
            yield return null;
        }

        /// <summary>테스트가 들어오는 메시지와 연결 상태를 정하는 전송.</summary>
        private sealed class ScriptedTransport : IArtelWebSocketTransport
        {
            private readonly Queue<ArtelWebSocketMessage> incoming = new Queue<ArtelWebSocketMessage>();

            public List<string> Sent { get; } = new List<string>();

            public bool Connected { get; set; } = true;

            public bool IsConnected => Connected;

            public ArtelTransportPhase Phase => ArtelTransportPhase.Connected;

            public void Receive(string text)
            {
                incoming.Enqueue(new ArtelWebSocketMessage(text, Sent.Add));
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public bool TryDequeueMessage(out ArtelWebSocketMessage message)
            {
                if (incoming.Count == 0)
                {
                    message = null;
                    return false;
                }

                message = incoming.Dequeue();
                return true;
            }

            public void Send(string text)
            {
                if (Connected)
                {
                    Sent.Add(text);
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
