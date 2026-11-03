using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Artel.Tests
{
    public sealed class ActionDeliveryTests
    {
        private GameObject host;
        private GameObject buttonObject;
        private ArtelManager manager;
        private ArtelManager displacedInstance;
        private DeliveryTransport transport;
        private int clicks;
        private float originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            clicks = 0;
            displacedInstance = ArtelManagerSlot.Clear();
            originalTimeScale = Time.timeScale;
            host = new GameObject("Artel delivery test");
            manager = host.AddComponent<ArtelManager>();
            transport = new DeliveryTransport();
            manager.SetWebSocketTransport(transport, false);
            buttonObject = new GameObject("delivery click", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            buttonObject.GetComponent<Button>().onClick.AddListener(() => clicks++);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(buttonObject);
            Object.DestroyImmediate(host);
            Time.timeScale = originalTimeScale;
            ArtelManagerSlot.Restore(displacedInstance);
        }

        [UnityTest]
        public IEnumerator 실행_중_중복은_ACK만_보내고_게임_효과는_한_번만_실행한다()
        {
            yield return null;
            var wire = ClickRequest(41);
            transport.Enqueue(wire);
            transport.Enqueue(wire);
            yield return WaitForResult(41);
            Assert.That(transport.Acknowledgements(41), Is.EqualTo(2));
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That((string)JObject.Parse(transport.Result(41))["processId"],
                Is.EqualTo(transport.ActionDelivery.ProcessId));
        }

        [UnityTest]
        public IEnumerator 결과가_유실돼도_재전송은_같은_결과만_다시_보낸다()
        {
            yield return null;
            var wire = ClickRequest(42);
            transport.DropNextResult = true;
            transport.Enqueue(wire);
            for (var frame = 0; frame < 6; frame++) yield return null;
            Assert.That(transport.Result(42), Is.Null);
            Assert.That(transport.DroppedResult, Is.Not.Null);
            transport.Enqueue(wire);
            yield return WaitForResult(42);
            Assert.That(transport.Result(42), Is.EqualTo(transport.DroppedResult));
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ACK가_유실돼도_동일_요청의_게임_효과는_한_번만_실행한다()
        {
            yield return null;
            var wire = ClickRequest(43);
            transport.DropNextAcknowledgement = true;
            transport.Enqueue(wire);
            transport.Enqueue(wire);
            yield return WaitForResult(43);
            Assert.That(transport.Acknowledgements(43), Is.EqualTo(1));
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator 재접속은_완료한_요청을_다시_실행하지_않는다()
        {
            yield return null;
            var wire = ClickRequest(44);
            transport.Enqueue(wire);
            yield return WaitForResult(44);
            var first = transport.Result(44);
            transport.Results.Clear();
            transport.ConnectionEpoch++;
            yield return null;
            transport.Enqueue(wire);
            yield return WaitForResult(44);
            Assert.That(transport.Result(44), Is.EqualTo(first));
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator 연결이_끊긴_실행과_대기_요청은_재실행하지_않는다()
        {
            yield return null;
            var running = Request(45, "key_click", new List<object> { "Mouse0", 60 });
            var queued = Request(46, "pause_time", new List<object>());
            transport.Enqueue(running);
            transport.Enqueue(queued);
            yield return null;
            Assert.That(transport.Acknowledgements(45), Is.EqualTo(1));
            transport.Connected = false;
            transport.ConnectionEpoch++;
            yield return null;
            transport.Connected = true;
            transport.ConnectionEpoch++;
            yield return null;
            transport.Enqueue(running);
            transport.Enqueue(queued);
            yield return WaitForResult(46);
            Assert.That(transport.Result(45), Does.Contain("execution outcome unknown"));
            Assert.That(transport.Result(46), Does.Contain("execution outcome unknown"));
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));
            Assert.That(ArtelInput.IsMouseButtonHeld(0), Is.False);
        }

        [UnityTest]
        public IEnumerator 같은_ID의_다른_본문과_만료한_요청은_실행하지_않는다()
        {
            yield return null;
            var wire = ClickRequest(47);
            transport.Enqueue(wire);
            yield return WaitForResult(47);
            transport.Results.Clear();
            transport.Enqueue(Request(47, "pause_time", new List<object>()));
            yield return WaitForResult(47);
            Assert.That(transport.Result(47), Does.Contain("different request body"));
            transport.Enqueue(ClickRequest(48, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1));
            yield return WaitForResult(48);
            Assert.That(transport.Result(48), Does.Contain("expired"));
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));
        }

        [UnityTest]
        public IEnumerator 큐에서_기다리다_만료한_요청은_게임_효과를_실행하지_않는다()
        {
            yield return null;
            var handle = typeof(ArtelManager).GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            var blocking = Request(49, "scan_scene", new List<object>());
            var queued = ClickRequest(50);
            handle.Invoke(manager, new object[] { new ArtelWebSocketMessage(blocking, transport.Send) });
            handle.Invoke(manager, new object[] { new ArtelWebSocketMessage(queued, transport.Send) });
            var entry = transport.ActionDelivery.Register(JsonConvert.DeserializeObject<ArtelRequestDto>(queued),
                queued, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out var added, out _);
            Assert.That(added, Is.False);
            entry.Request.Delivery.ExpiresAtUnixMs = 0;
            yield return WaitForResult(50);
            Assert.That(transport.Result(50), Does.Contain("expired"));
            Assert.That(clicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator 결과_전송_예외가_나도_완료한_효과는_재실행하지_않는다()
        {
            yield return null;
            var wire = ClickRequest(51);
            transport.ThrowNextResult = true;
            LogAssert.Expect(LogType.Warning, new Regex("Action execution failed: injected result send failure"));
            transport.Enqueue(wire);
            for (var frame = 0; frame < 6; frame++) yield return null;
            transport.Enqueue(wire);
            yield return WaitForResult(51);
            Assert.That((bool)JObject.Parse(transport.Result(51))["results"][0]["success"], Is.True);
            Assert.That(clicks, Is.EqualTo(1));
        }

        private string ClickRequest(long id, long? expiry = null) =>
            Request(id, "button_click", new List<object> { ObjectIds.Of(buttonObject) }, expiry);

        private string Request(long id, string method, List<object> parameters, long? expiry = null)
        {
            return JsonConvert.SerializeObject(new ArtelRequestDto
            {
                Type = "ACTION", Id = id,
                Delivery = new ActionDeliveryDto
                {
                    ProcessId = transport.ActionDelivery.ProcessId,
                    ExpiresAtUnixMs = expiry ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 25000
                },
                Actions = new List<ActionRequestDto>
                {
                    new ActionRequestDto { Id = 1, Method = method, Parameters = parameters }
                }
            });
        }

        private IEnumerator WaitForResult(long id)
        {
            for (var frame = 0; frame < 30 && transport.Result(id) == null; frame++) yield return null;
            Assert.That(transport.Result(id), Is.Not.Null, "requestId=" + id);
        }

        private sealed class DeliveryTransport : IArtelWebSocketTransport, IArtelConnectionEpochSource,
            IArtelActionDeliverySource
        {
            private readonly Queue<ArtelWebSocketMessage> messages = new Queue<ArtelWebSocketMessage>();
            internal readonly List<string> Results = new List<string>();
            internal readonly List<string> Acknowledged = new List<string>();
            internal bool Connected = true;
            internal bool DropNextResult;
            internal bool ThrowNextResult;
            internal bool DropNextAcknowledgement;
            internal string DroppedResult;
            public ActionDeliveryJournal ActionDelivery { get; } = new ActionDeliveryJournal();
            public bool IsConnected => Connected;
            public long ConnectionEpoch { get; set; } = 1;
            public ArtelTransportPhase Phase => Connected ? ArtelTransportPhase.Connected : ArtelTransportPhase.Connecting;
            public void Start() { }
            public void Stop() { Connected = false; }
            public void Dispose() { }
            public bool TryDequeueMessage(out ArtelWebSocketMessage message)
            {
                message = messages.Count == 0 ? null : messages.Dequeue();
                return message != null;
            }
            public void Send(string text)
            {
                var type = (string)JObject.Parse(text)["type"];
                if (type == "ACTION_ACCEPTED")
                {
                    if (DropNextAcknowledgement) DropNextAcknowledgement = false;
                    else Acknowledged.Add(text);
                }
                if (type != "ACTION_RESULT") return;
                if (ThrowNextResult)
                {
                    ThrowNextResult = false;
                    throw new InvalidOperationException("injected result send failure");
                }
                if (DropNextResult)
                {
                    DropNextResult = false;
                    DroppedResult = text;
                }
                else Results.Add(text);
            }
            internal void Enqueue(string text)
            {
                var epoch = ConnectionEpoch;
                messages.Enqueue(new ArtelWebSocketMessage(text, Send,
                    () => Connected && epoch == ConnectionEpoch));
            }
            internal string Result(long id) => Results.LastOrDefault(text => (long)JObject.Parse(text)["requestId"] == id);
            internal int Acknowledgements(long id) =>
                Acknowledged.Count(text => (long)JObject.Parse(text)["requestId"] == id);
        }
    }
}
