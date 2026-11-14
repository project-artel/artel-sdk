using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Artel.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WebSocketSharp.Server;

namespace Artel.Tests.Transport
{
    /// <summary>
    /// 열린 연결이 있는 채로 자격증명이 바뀌면 Start 가 다시 거는지(ARTEL-1455).
    /// </summary>
    /// <remarks>
    /// 오버레이에서 프로젝트를 바꾸면 등록이 새 인스턴스를 받고 전송을 다시 시작한다. 전에는 Start 가 열린 소켓을
    /// 보고 돌아가서, 게임은 옛 인스턴스로 남고 새 인스턴스는 서버에서 끝내 연결되지 않았다.
    /// </remarks>
    public sealed class WebSocketCredentialSwitchTests
    {
        private static readonly ConcurrentQueue<string> Dialed = new ConcurrentQueue<string>();

        [UnityTest]
        public IEnumerator 연결된_채로_인스턴스가_바뀌면_옛_연결을_닫고_새_인스턴스로_다시_건다()
        {
            while (Dialed.TryDequeue(out _)) { }
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var server = new WebSocketServer(IPAddress.Loopback, port);
            server.AddWebSocketService<DialRecorder>("/ws/sdk");
            var instance = "instance-a";
            var client = new ArtelWebSocketClient(new Server(false, "127.0.0.1", port),
                () => "local-test-token", () => instance);
            try
            {
                server.Start();
                client.Start();
                yield return WaitFor(() => client.IsConnected);
                var firstEpoch = client.ConnectionEpoch;

                // 같은 자격증명이면 열린 연결을 그대로 둔다.
                client.Start();
                yield return null;
                Assert.That(Dialed.Count, Is.EqualTo(1));

                instance = "instance-b";
                client.Start();
                yield return WaitFor(() => Dialed.Count == 2 && client.IsConnected);

                Assert.That(Dialed.ToArray(), Is.EqualTo(new[] { "instance-a", "instance-b" }));
                Assert.That(client.ConnectionEpoch, Is.GreaterThan(firstEpoch));
                Assert.That(server.WebSocketServices["/ws/sdk"].Sessions.Count, Is.EqualTo(1));
            }
            finally
            {
                client.Stop();
                client.Dispose();
                server.Stop();
            }
        }

        private static IEnumerator WaitFor(System.Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 8f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True);
        }

        public sealed class DialRecorder : WebSocketBehavior
        {
            protected override void OnOpen()
            {
                Dialed.Enqueue(Context.QueryString["instanceId"]);
            }
        }
    }
}
