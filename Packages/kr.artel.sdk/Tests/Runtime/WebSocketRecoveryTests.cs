using System.Collections;
using System.Net;
using System.Net.Sockets;
using Artel.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Artel.Tests.Transport
{
    public sealed class WebSocketRecoveryTests
    {
        [UnityTest]
        public IEnumerator 서버를_재시작하면_자동_재연결하고_새_메시지를_교환한다()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var server = new WebSocketServer(IPAddress.Loopback, port);
            server.AddWebSocketService<RecoveryEchoPeer>("/ws/sdk");
            var client = new ArtelWebSocketClient(new Server(false, "127.0.0.1", port),
                () => "local-test-token", () => "local-test-instance");
            try
            {
                server.Start();
                client.Start();
                yield return WaitForConnection(client, true);
                var firstEpoch = client.ConnectionEpoch;
                client.Send("before");
                ArtelWebSocketMessage previousMessage = null;
                var deadline = Time.realtimeSinceStartup + 5f;
                while (previousMessage == null && Time.realtimeSinceStartup < deadline)
                {
                    client.TryDequeueMessage(out previousMessage);
                    yield return null;
                }
                Assert.That(previousMessage?.Text, Is.EqualTo("echo:before"));
                Assert.That(previousMessage.IsCurrent(), Is.True);

                server.Stop();
                yield return WaitForConnection(client, false);
                Assert.That(previousMessage.IsCurrent(), Is.False);
                server = new WebSocketServer(IPAddress.Loopback, port);
                server.AddWebSocketService<RecoveryEchoPeer>("/ws/sdk");
                server.Start();
                yield return WaitForConnection(client, true);
                Assert.That(client.ConnectionEpoch, Is.GreaterThan(firstEpoch));
                client.Send("after");
                ArtelWebSocketMessage currentMessage = null;
                deadline = Time.realtimeSinceStartup + 5f;
                while (currentMessage == null && Time.realtimeSinceStartup < deadline)
                {
                    client.TryDequeueMessage(out currentMessage);
                    yield return null;
                }
                Assert.That(currentMessage?.Text, Is.EqualTo("echo:after"));
                Assert.That(currentMessage.IsCurrent(), Is.True);
                Assert.That(previousMessage.IsCurrent(), Is.False);
            }
            finally
            {
                client.Stop();
                client.Dispose();
                server.Stop();
            }
        }

        private static IEnumerator WaitForConnection(ArtelWebSocketClient client, bool expected)
        {
            var deadline = Time.realtimeSinceStartup + 8f;
            while (client.IsConnected != expected && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(client.IsConnected, Is.EqualTo(expected));
        }

        public sealed class RecoveryEchoPeer : WebSocketBehavior
        {
            protected override void OnMessage(MessageEventArgs message)
            {
                Send("echo:" + message.Data);
            }
        }
    }
}
