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
    /// <c>watch</c> 로 청한 멤버가 살아 있는 <see cref="ArtelManager"/> 를 거쳐 <c>PULSE</c> 에 실리는지, 그리고 값이 움직인 뒤
    /// 다음 <c>PULSE</c> 가 1초 배치를 기다리지 않고 나가는지 본다(ARTEL-1275).
    /// </summary>
    public sealed class WatchActionTests
    {
        private const string Score = "Artel.Tests.WatchFixtureBehaviour::Score";

        private ArtelManager displacedInstance;
        private GameObject host;
        private GameObject fixture;
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
            if (fixture != null)
            {
                Object.DestroyImmediate(fixture);
            }

            if (host != null)
            {
                host.GetComponent<ArtelManager>().StopReadings();
                Object.DestroyImmediate(host);
            }

            Affordances.Live.WatchList.Forget();
            Affordances.Live.Lookup.Forget();
            Affordances.Live.Worth.Forget();
            ArtelManagerSlot.Restore(displacedInstance);
            ArtelSecretStore.Current = null;
        }

        [UnityTest]
        public IEnumerator Watch_뒤_청한_멤버가_PULSE_에_실리고_변화는_다음_tick_에_나간다()
        {
            yield return StartManager();
            fixture = new GameObject("Artel watch fixture");
            var behaviour = fixture.AddComponent<WatchFixtureBehaviour>();
            behaviour.Score = 1;

            transport.Receive(Batch(1,
                "{\"id\":1,\"method\":\"watch\",\"params\":[[\"" + Score + "\"]]}," +
                "{\"id\":2,\"method\":\"start_readings\",\"params\":[]}"));

            yield return Until(() => transport.Sent.Any(message => Type(message) == "ACTION_RESULT"), 3f);

            var result = JObject.Parse(transport.Sent.First(message => Type(message) == "ACTION_RESULT"));
            var watched = result["results"][0];

            Assert.That((bool)watched["success"], Is.True, result.ToString());
            Assert.That((string)watched["returnValue"]["resolved"][0]["kind"], Is.EqualTo("component"));
            Assert.That((int)watched["returnValue"]["resolved"][0]["holders"], Is.EqualTo(1));
            Assert.That(watched["returnValue"]["unresolvable"], Is.Empty);

            yield return Until(() => Pulses().Any(pulse => pulse.Contains("WatchFixtureBehaviour")), 3f);

            var before = Pulses().Count;
            behaviour.Score = 42;
            var changed = Time.realtimeSinceStartup;

            yield return Until(() => Pulses().Skip(before).Any(pulse => pulse.Contains("\"value\":42")), 3f);

            var waited = Time.realtimeSinceStartup - changed;

            Assert.That(Pulses().Skip(before).Any(pulse => pulse.Contains("\"value\":42")), Is.True,
                string.Join("\n", Pulses()));
            // 1초 배치였으면 평균 0.5초다. 판독 간격 0.1초에 바닥 0.1초, 테스트 러너의 frame 을 감안해도 그 아래다.
            Assert.That(waited, Is.LessThan(0.45f), "value changed → next PULSE took " + waited + "s");
        }

        [UnityTest]
        public IEnumerator read_member_는_지금_값을_한_번_돌려준다()
        {
            yield return StartManager();
            fixture = new GameObject("Artel watch fixture");
            fixture.AddComponent<WatchFixtureBehaviour>().Score = 9;

            transport.Receive(Batch(2, "{\"id\":1,\"method\":\"read_member\",\"params\":[\"" + Score + "\"]}"));

            yield return Until(() => transport.Sent.Any(message => Type(message) == "ACTION_RESULT"), 3f);

            var result = JObject.Parse(transport.Sent.First(message => Type(message) == "ACTION_RESULT"));
            var read = result["results"][0];

            Assert.That((bool)read["success"], Is.True, result.ToString());
            Assert.That((int)read["returnValue"]["values"][0]["value"], Is.EqualTo(9));
            Assert.That((string)read["returnValue"]["values"][0]["path"], Does.Contain("Artel watch fixture"));
            Assert.That(Affordances.Live.WatchList.Requested, Is.Empty, "read_member 는 목록에 더하지 않는다.");
        }

        [UnityTest]
        public IEnumerator start_readings_는_틀린_속도를_거절한다()
        {
            yield return StartManager();

            transport.Receive(Batch(3, "{\"id\":1,\"method\":\"start_readings\",\"params\":[{\"interval\":0}]}"));

            yield return Until(() => transport.Sent.Any(message => Type(message) == "ACTION_RESULT"), 3f);

            var result = JObject.Parse(transport.Sent.First(message => Type(message) == "ACTION_RESULT"));

            Assert.That((bool)result["results"][0]["success"], Is.False);
            Assert.That((string)result["results"][0]["error"], Does.StartWith("start_readings"));
            Assert.That(host.GetComponent<ArtelManager>().Reading, Is.False);
        }

        [UnityTest]
        public IEnumerator flush_는_돌고_있을_때만_지금_읽어_바로_보낸다()
        {
            yield return StartManager();
            fixture = new GameObject("Artel watch fixture");
            var behaviour = fixture.AddComponent<WatchFixtureBehaviour>();

            transport.Receive(Batch(4, "{\"id\":1,\"method\":\"flush\",\"params\":[]}"));
            yield return Until(() => transport.Sent.Any(message => Type(message) == "ACTION_RESULT"), 3f);

            var refused = JObject.Parse(transport.Sent.First(message => Type(message) == "ACTION_RESULT"));
            Assert.That((bool)refused["results"][0]["success"], Is.False);
            Assert.That((string)refused["results"][0]["error"], Does.StartWith("flush:"));

            transport.Receive(Batch(5,
                "{\"id\":1,\"method\":\"watch\",\"params\":[[\"" + Score + "\"]]}," +
                "{\"id\":2,\"method\":\"start_readings\",\"params\":[{\"minDelivery\":5,\"delivery\":5}]}"));
            yield return Until(() => Pulses().Count > 0, 3f);

            // 바닥을 5초로 두어 박자는 아무것도 보내지 못한다. 그 사이에 바뀐 값은 flush 만이 내보낸다.
            var before = Pulses().Count;
            behaviour.Score = 77;
            yield return null;
            yield return null;
            Assert.That(Pulses().Count, Is.EqualTo(before));

            transport.Receive(Batch(6, "{\"id\":1,\"method\":\"flush\",\"params\":[]}"));
            yield return Until(() => Pulses().Skip(before).Any(pulse => pulse.Contains("\"value\":77")), 3f);

            Assert.That(Pulses().Skip(before).Any(pulse => pulse.Contains("\"value\":77")), Is.True,
                string.Join("\n", Pulses().Skip(before)));
        }

        [UnityTest]
        public IEnumerator wait_until_은_값이_바뀌는_프레임에_잡고_시간_초과는_값으로_답한다()
        {
            yield return StartManager();
            fixture = new GameObject("Artel watch fixture");
            var behaviour = fixture.AddComponent<WatchFixtureBehaviour>();

            transport.Receive(Batch(7, "{\"id\":1,\"method\":\"wait_until\",\"params\":[\"" + Score + " == 5\", 3]}"));
            yield return null;
            yield return null;
            yield return null;
            behaviour.Score = 5;

            yield return Until(() => transport.Sent.Any(message => Type(message) == "ACTION_RESULT"), 4f);

            var result = JObject.Parse(transport.Sent.First(message => Type(message) == "ACTION_RESULT"))["results"][0];

            Assert.That((bool)result["success"], Is.True, result.ToString());
            Assert.That((bool)result["returnValue"]["held"], Is.True);
            Assert.That((bool)result["returnValue"]["timedOut"], Is.False);
            Assert.That((int)result["returnValue"]["waitedFrames"], Is.InRange(1, 10));

            transport.Sent.Clear();
            transport.Receive(Batch(8, "{\"id\":1,\"method\":\"wait_until\",\"params\":[\"" + Score + " == 99\", 0.2]}"));
            yield return Until(() => transport.Sent.Any(message => Type(message) == "ACTION_RESULT"), 4f);

            var late = JObject.Parse(transport.Sent.First(message => Type(message) == "ACTION_RESULT"))["results"][0];

            Assert.That((bool)late["success"], Is.True, "시간 초과는 실패가 아니다.");
            Assert.That((bool)late["returnValue"]["timedOut"], Is.True);
            Assert.That((bool)late["returnValue"]["held"], Is.False);
        }

        private List<string> Pulses()
        {
            return transport.Sent.Where(message => Type(message) == "PULSE").ToList();
        }

        private static IEnumerator Until(System.Func<bool> condition, float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;

            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static string Batch(int id, string actions) =>
            "{\"type\":\"ACTION\",\"id\":" + id + ",\"actions\":[" + actions + "]}";

        private static string Type(string message) => (string)JObject.Parse(message)["type"];

        private IEnumerator StartManager()
        {
            host = new GameObject("Artel watch action test");
            host.AddComponent<ArtelManager>();
            transport = new ScriptedTransport();
            host.GetComponent<ArtelManager>().SetWebSocketTransport(transport, false);
            yield return null;
            yield return null;
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

    internal sealed class WatchFixtureBehaviour : MonoBehaviour
    {
        public int Score;
    }
}
