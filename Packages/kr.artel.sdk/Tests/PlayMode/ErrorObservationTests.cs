using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Artel.Auth;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Artel.Tests
{
    /// <summary>
    /// 게임이 던진 예외가 살아 있는 <see cref="ArtelManager"/> 를 거쳐 <c>PULSE.errors</c> 와 <c>ACTION_RESULT.errors</c> 로
    /// 나가는지 본다(ARTEL-1218).
    /// </summary>
    public sealed class ErrorObservationTests
    {
        private ArtelManager displacedInstance;
        private GameObject host;
        private GameObject thrower;
        private ScriptedTransport transport;

        [SetUp]
        public void SetUp()
        {
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            displacedInstance = ArtelManagerSlot.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (thrower != null)
            {
                Object.DestroyImmediate(thrower);
            }

            if (host != null)
            {
                host.GetComponent<ArtelManager>().StopReadings();
                Object.DestroyImmediate(host);
            }

            ArtelManagerSlot.Restore(displacedInstance);
            ArtelSecretStore.Current = null;
        }

        [UnityTest]
        public IEnumerator AnExceptionThrownInUpdate_ArrivesInPulseErrors_GroupedWithACount()
        {
            yield return StartManager();
            transport.Receive(Batch(1, "{\"id\":1,\"method\":\"start_readings\",\"params\":[]}"));
            yield return null;

            thrower = new GameObject("Artel error test thrower");
            thrower.AddComponent<ThrowingBehaviour>().Message = "pulse error test";
            yield return null;
            yield return null;
            yield return null;
            Object.DestroyImmediate(thrower);

            yield return new WaitForSecondsRealtime(1.5f);

            var items = transport.Sent
                .Where(message => Type(message) == "PULSE")
                .Select(JObject.Parse)
                .Where(pulse => pulse["errors"] != null)
                .SelectMany(pulse => pulse["errors"])
                .Where(item => ((string)item["message"]).Contains("pulse error test"))
                .ToList();

            Assert.That(items, Is.Not.Empty, string.Join("\n", transport.Sent.Where(message => Type(message) == "PULSE")));
            var last = items.Last();
            Assert.That((string)last["type"], Is.EqualTo("Exception"));
            Assert.That((int)last["count"], Is.GreaterThanOrEqualTo(2), "every frame's throw lands in one group");
            Assert.That((int)last["lastFrame"], Is.GreaterThan((int)last["firstFrame"]));
            Assert.That((string)last["stackHead"], Does.Contain("ThrowingBehaviour"));
        }

        [UnityTest]
        public IEnumerator DebugLogException_IsCollectedLikeAnUncaughtOne()
        {
            yield return StartManager();
            transport.Receive(Batch(1, "{\"id\":1,\"method\":\"start_readings\",\"params\":[]}"));
            yield return null;

            try
            {
                throw new InvalidOperationException("swallowed by the game");
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogException(exception);
            }

            yield return new WaitForSecondsRealtime(1.5f);

            var messages = transport.Sent
                .Where(message => Type(message) == "PULSE")
                .Select(JObject.Parse)
                .Where(pulse => pulse["errors"] != null)
                .SelectMany(pulse => pulse["errors"])
                .Select(item => (string)item["message"]);

            Assert.That(messages, Has.Some.Contains("swallowed by the game"));
        }

        [UnityTest]
        public IEnumerator AnExceptionDuringABatch_IsAttachedToItsActionResult()
        {
            yield return StartManager();

            thrower = new GameObject("Artel error test thrower");
            thrower.AddComponent<ThrowingBehaviour>().Message = "action error test";
            // key_click 은 지정한 초만큼 여러 frame 에 걸친다. 그 사이 매 frame 예외가 찍힌다.
            transport.Receive(Batch(7, "{\"id\":1,\"method\":\"key_click\",\"params\":[\"Space\",0.1]}"));

            var deadline = Time.realtimeSinceStartup + 3f;
            while (!transport.Sent.Any(message => Type(message) == "ACTION_RESULT") && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var result = JObject.Parse(transport.Sent.First(message => Type(message) == "ACTION_RESULT"));
            var errors = result["errors"];

            Assert.That(errors, Is.Not.Null, result.ToString());
            Assert.That(errors.Select(item => (string)item["message"]), Has.Some.Contains("action error test"));
            Assert.That(errors.All(item => (int)item["lastFrame"] <= (int)result["frame"]), Is.True);
        }

        private static string Batch(int id, string action) =>
            "{\"type\":\"ACTION\",\"id\":" + id + ",\"actions\":[" + action + "]}";

        private static string Type(string message) => (string)JObject.Parse(message)["type"];

        private IEnumerator StartManager()
        {
            // 이 테스트는 일부러 예외를 찍는다. SetUp 에서 켜면 테스트 본문에 들어올 때 다시 꺼지므로 본문 안에서 켠다.
            LogAssert.ignoreFailingMessages = true;
            host = new GameObject("Artel error observation test");
            host.AddComponent<ArtelManager>();
            transport = new ScriptedTransport();
            host.GetComponent<ArtelManager>().SetWebSocketTransport(transport, false);
            yield return null;
            yield return null;
        }

        private sealed class ThrowingBehaviour : MonoBehaviour
        {
            public string Message;

            private void Update()
            {
                throw new InvalidOperationException(Message);
            }
        }

        private sealed class ScriptedTransport : IArtelWebSocketTransport
        {
            private readonly Queue<ArtelWebSocketMessage> incoming = new Queue<ArtelWebSocketMessage>();

            public List<string> Sent { get; } = new List<string>();

            public bool IsConnected => true;

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
                Sent.Add(text);
            }

            public void Dispose()
            {
            }
        }
    }
}
