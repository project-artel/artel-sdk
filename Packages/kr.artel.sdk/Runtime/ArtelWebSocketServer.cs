using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Artel
{
    internal sealed class ArtelWebSocketMessage
    {
        public ArtelWebSocketMessage(string text, Action<string> reply, Func<bool> isCurrent = null)
        {
            Text = text;
            Reply = reply;
            IsCurrent = isCurrent ?? (() => true);
        }

        public string Text { get; private set; }
        public Action<string> Reply { get; private set; }
        internal Func<bool> IsCurrent { get; private set; }
    }

    internal sealed class ArtelWebSocketServer : IArtelWebSocketTransport, IArtelMediaWebSocketTransport
    {
        private readonly string bindAddress;
        private readonly int port;
        private readonly ConcurrentQueue<ArtelWebSocketMessage> incomingMessages =
            new ConcurrentQueue<ArtelWebSocketMessage>();
        private readonly Dictionary<string, Action<string>> sendByConnectionId =
            new Dictionary<string, Action<string>>();
        private WebSocketServer server;
        private long connectionEpoch;
        public long ConnectionEpoch => System.Threading.Interlocked.Read(ref connectionEpoch);
        private int mediaSending;
        private int mediaGeneration;

        public ArtelWebSocketServer(string bindAddress, int port)
        {
            this.bindAddress = bindAddress;
            this.port = port;
        }

        public bool IsConnected
        {
            get { return server != null; }
        }

        /// <summary>
        /// 이쪽은 받는 자리다. 걸다 마는 중간 상태가 없어 서 있거나 아니거나 둘 중 하나다.
        /// </summary>
        public ArtelTransportPhase Phase
        {
            get
            {
                return server == null
                    ? ArtelTransportPhase.Idle
                    : ArtelTransportPhase.Connected;
            }
        }

        public void Start()
        {
            if (server != null)
            {
                return;
            }

            server = new WebSocketServer("ws://" + bindAddress + ":" + port);
            server.AddWebSocketService("/ws", CreateBehavior);
            server.Start();
        }

        public bool TryDequeueMessage(out ArtelWebSocketMessage message)
        {
            return incomingMessages.TryDequeue(out message);
        }

        public void Send(string text)
        {
            lock (sendByConnectionId)
            {
                foreach (var send in sendByConnectionId.Values)
                {
                    send(text);
                }
            }
        }

        public void Stop()
        {
            Interlocked.Increment(ref mediaGeneration);
            Interlocked.Exchange(ref mediaSending, 0);
            server?.Stop();
            server = null;

            lock (sendByConnectionId)
            {
                sendByConnectionId.Clear();
            }
        }

        public bool TrySendMedia(string text, Action<bool> completed)
        {
            if (text == null || System.Text.Encoding.UTF8.GetByteCount(text) > 184 * 1024) return false;
            var current = server;
            var generation = mediaGeneration;
            if (current == null || Interlocked.CompareExchange(ref mediaSending, 1, 0) != 0) return false;
            try
            {
                current.WebSocketServices.BroadcastAsync(text, () =>
                {
                    if (generation == mediaGeneration) Interlocked.Exchange(ref mediaSending, 0);
                    completed(generation == mediaGeneration);
                });
                return true;
            }
            catch (Exception)
            {
                if (generation == mediaGeneration) Interlocked.Exchange(ref mediaSending, 0);
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private ArtelWebSocketBehavior CreateBehavior()
        {
            var behavior = new ArtelWebSocketBehavior();
            string currentConnectionId = null;
            behavior.Configure(
                (connectionId, send) =>
                {
                    lock (sendByConnectionId)
                    {
                        sendByConnectionId[connectionId] = send;
                        currentConnectionId = connectionId;
                        System.Threading.Interlocked.Increment(ref connectionEpoch);
                    }
                },
                connectionId =>
                {
                    lock (sendByConnectionId)
                    {
                        sendByConnectionId.Remove(connectionId);
                        System.Threading.Interlocked.Increment(ref connectionEpoch);
                    }
                },
                (text, reply) =>
                {
                    var messageEpoch = ConnectionEpoch;
                    incomingMessages.Enqueue(new ArtelWebSocketMessage(text, reply, () =>
                    {
                        lock (sendByConnectionId)
                            return currentConnectionId != null && sendByConnectionId.ContainsKey(currentConnectionId) &&
                                   messageEpoch == ConnectionEpoch;
                    }));
                });
            return behavior;
        }
    }

    internal sealed class ArtelWebSocketBehavior : WebSocketBehavior
    {
        private Action<string, Action<string>> onOpen;
        private Action<string> onClose;
        private Action<string, Action<string>> onMessage;

        public void Configure(
            Action<string, Action<string>> onOpen,
            Action<string> onClose,
            Action<string, Action<string>> onMessage)
        {
            this.onOpen = onOpen;
            this.onClose = onClose;
            this.onMessage = onMessage;
        }

        protected override void OnOpen()
        {
            onOpen?.Invoke(ID, Send);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            onClose?.Invoke(ID);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
            {
                onMessage?.Invoke(e.Data, Send);
            }
        }
    }
}
