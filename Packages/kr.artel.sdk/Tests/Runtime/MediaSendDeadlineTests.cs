using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Artel.Domain;
using NUnit.Framework;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Artel.Tests.Streaming
{
    public sealed class MediaSendDeadlineTests
    {
        private sealed class Receiver : WebSocketBehavior { public Receiver() { } }

        [Test]
        public void callback이_끝나지_않으면_이전_소켓을_닫은_뒤_재연결해_전송한다()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var server = new WebSocketServer("ws://127.0.0.1:" + port);
            server.AddWebSocketService<Receiver>("/ws/sdk"); server.Start();
            WebSocket firstSocket = null;
            Action<bool> lateCallback = null;
            Action<bool> secondCallback = null;
            Action<bool> thirdCallback = null;
            var sends = 0;
            var completed = new ManualResetEventSlim(false);
            var transport = new ArtelWebSocketClient(new Server(false, "127.0.0.1", port), () => "test", () => "1",
                TimeSpan.FromMilliseconds(150), (socket, text, callback) =>
                {
                    var count = Interlocked.Increment(ref sends);
                    if (count == 1) { firstSocket = socket; lateCallback = callback; }
                    else if (count == 2) { secondCallback = callback; callback(true); }
                    else if (count == 3) thirdCallback = callback;
                    else callback(true);
                });
            try
            {
                transport.Start();
                Assert.That(SpinWait.SpinUntil(() => transport.IsConnected, 5000), Is.True);
                Assert.That(transport.TrySendMedia("frame one", _ => { }), Is.True);
                Assert.That(transport.TrySendMedia("excess frame", _ => { }), Is.False);
                Assert.That(SpinWait.SpinUntil(() => firstSocket.ReadyState == WebSocketState.Closed, 5000), Is.True);
                Assert.That(SpinWait.SpinUntil(() => transport.IsConnected, 5000), Is.True);
                Assert.That(transport.TrySendMedia("frame two", value => { if (value) completed.Set(); }), Is.True);
                Assert.That(completed.Wait(1000), Is.True);
                lateCallback(false);
                Assert.That(transport.TrySendMedia("frame three", _ => { }), Is.True);
                secondCallback(true);
                Assert.That(transport.TrySendMedia("excess after stale callback", _ => { }), Is.False);
                thirdCallback(true);
                Assert.That(transport.TrySendMedia("frame four", _ => { }), Is.True);
                Assert.That(sends, Is.EqualTo(4));
            }
            finally { transport.Dispose(); server.Stop(); completed.Dispose(); }
        }
    }
}
