using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Artel.Auth;
using Artel.Affordances.Live;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Artel.Tests {
    public sealed class ForceReadingsTests {
        private GameObject host;
        private ArtelManager manager;
        private ArtelManager displaced;
        private ReadingTransport transport;

        [SetUp]
        public void SetUp() {
            displaced = ArtelManagerSlot.Clear();
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ArtelSdkSession.Clear();
            host = new GameObject("Force readings test");
            manager = host.AddComponent<ArtelManager>();
            transport = new ReadingTransport();
            manager.SetWebSocketTransport(transport, false);
        }

        [TearDown]
        public void TearDown() {
            manager.StopReadings();
            Object.DestroyImmediate(host);
            ArtelManagerSlot.Restore(displaced);
            ArtelSdkSession.Clear();
            ArtelSecretStore.Current = null;
        }

        [UnityTest]
        public IEnumerator ForceAfterReconnectSendsWholeWithSameStreamAndPace() {
            yield return null;
            var pace = new Pulse.Pace { Interval = 10f, Delivery = 10f, MinDelivery = 0f };
            Assert.That(manager.StartReadings(pace), Is.True);
            transport.Connected = false;
            transport.ConnectionEpoch++;
            yield return null;
            transport.Connected = true;
            transport.ConnectionEpoch++;
            yield return null;
            var previous = transport.Pulses.Last();
            var before = transport.Pulses.Count;

            transport.Enqueue(101, "[{\"force\":true,\"interval\":0.2,\"delivery\":2}]");
            yield return WaitForResult(101);

            Assert.That(transport.Result(101)["results"][0]["success"].Value<bool>(), Is.True);
            Assert.That(transport.Pulses.Count, Is.EqualTo(before + 1));
            var whole = transport.Pulses.Last();
            Assert.That(whole["whole"].Value<bool>(), Is.True);
            Assert.That((string)whole["stream"], Is.EqualTo((string)previous["stream"]));
            Assert.That(whole["seq"].Value<long>(), Is.GreaterThan(previous["seq"].Value<long>()));
            var currentPace = (Pulse.Pace)typeof(ArtelManager).GetField("readingPace", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            Assert.That(currentPace.Interval, Is.EqualTo(pace.Interval));
            Assert.That(currentPace.Delivery, Is.EqualTo(pace.Delivery));
            Assert.That(currentPace.MinDelivery, Is.EqualTo(pace.MinDelivery));
        }

        [UnityTest]
        public IEnumerator DefaultAndFalseDoNotResendWhole() {
            yield return null;
            manager.StartReadings(new Pulse.Pace { Interval = 10f, Delivery = 10f, MinDelivery = 0f });
            var before = transport.Pulses.Count;
            transport.Enqueue(102, "[]");
            transport.Enqueue(103, "[{\"force\":false}]", "start_readings");
            yield return WaitForResult(103);
            Assert.That(transport.Pulses.Count, Is.EqualTo(before));
            Assert.That(transport.Result(102)["results"][0]["success"].Value<bool>(), Is.True);
            Assert.That(transport.Result(103)["results"][0]["success"].Value<bool>(), Is.True);
        }

        [UnityTest]
        public IEnumerator ColdForceProducesOnlyOneWhole() {
            yield return null;
            transport.Enqueue(104, "[{\"force\":true,\"interval\":10,\"delivery\":10}]");
            yield return WaitForResult(104);
            Assert.That(transport.Result(104)["results"][0]["success"].Value<bool>(), Is.True);
            Assert.That(transport.Pulses.Count, Is.EqualTo(1));
            Assert.That(transport.Pulses[0]["whole"].Value<bool>(), Is.True);
        }

        [UnityTest]
        public IEnumerator InvalidForceDoesNotChangeRunningStream() {
            yield return null;
            manager.StartReadings(new Pulse.Pace { Interval = 10f, Delivery = 10f, MinDelivery = 0f });
            var before = transport.Pulses.Count;
            var id = 110;
            foreach (var value in new[] { "\"true\"", "1", "null" }) {
                transport.Enqueue(id, "[{\"force\":" + value + "}]");
                yield return WaitForResult(id);
                Assert.That(transport.Result(id)["results"][0]["success"].Value<bool>(), Is.False);
                id++;
            }
            Assert.That(transport.Pulses.Count, Is.EqualTo(before));
            Assert.That(manager.Reading, Is.True);
        }

        [UnityTest]
        public IEnumerator LegacyAndCanonicalNamesShareOneSubscription() {
            yield return null;
            transport.Enqueue(120, "[{\"force\":true}]", "start_readings");
            yield return WaitForResult(120);
            Assert.That(manager.Reading, Is.True);
            transport.Enqueue(121, "[]", "stop_readings");
            yield return WaitForResult(121);
            Assert.That(manager.Reading, Is.False);
            transport.Enqueue(122, "[{\"force\":true}]");
            yield return WaitForResult(122);
            Assert.That(manager.Reading, Is.True);
            transport.Enqueue(123, "[]", "unsubscribe_pulse");
            yield return WaitForResult(123);
            Assert.That(manager.Reading, Is.False);
        }

        private IEnumerator WaitForResult(int id) {
            for (var frame = 0; frame < 120 && transport.Result(id) == null; frame++) yield return null;
            Assert.That(transport.Result(id), Is.Not.Null, "Request " + id);
        }

        private sealed class ReadingTransport : IArtelWebSocketTransport, IArtelConnectionEpochSource {
            private readonly Queue<ArtelWebSocketMessage> incoming = new Queue<ArtelWebSocketMessage>();
            private readonly List<JObject> sent = new List<JObject>();
            public bool Connected = true;
            public long ConnectionEpoch { get; set; }
            public bool IsConnected => Connected;
            public ArtelTransportPhase Phase => Connected ? ArtelTransportPhase.Connected : ArtelTransportPhase.Connecting;
            public List<JObject> Pulses => sent.Where(d => (string)d["type"] == "PULSE").ToList();
            public JObject Result(int id) => sent.FirstOrDefault(d => (string)d["type"] == "ACTION_RESULT" && (int?)d["requestId"] == id);
            public void Start() { }
            public void Stop() { Connected = false; }
            public void Dispose() { }
            public void Send(string text) => sent.Add(JObject.Parse(text));
            public bool TryDequeueMessage(out ArtelWebSocketMessage message) {
                message = incoming.Count > 0 ? incoming.Dequeue() : null;
                return message != null;
            }
            public void Enqueue(int id, string parameters, string method = "subscribe_pulse") {
                var epoch = ConnectionEpoch;
                incoming.Enqueue(new ArtelWebSocketMessage("{\"type\":\"ACTION\",\"id\":" + id +
                    ",\"actions\":[{\"id\":1,\"method\":\"" + method + "\",\"params\":" + parameters + "}]}",
                    Send, () => Connected && ConnectionEpoch == epoch));
            }
        }
    }
}
