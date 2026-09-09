using System;
using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Threading;
using Artel.Domain;
using WebSocketSharp;

namespace Artel
{
    internal sealed class ArtelWebSocketClient : IArtelWebSocketTransport
    {
        private const string SdkWebSocketPath = "/ws/sdk";

        // 서버가 자격증명이나 인스턴스 접근을 거절할 때 붙이는 코드. 토큰과 instanceId 가 그대로면
        // 다시 걸어도 같은 대답이 오므로 이 코드만은 재시도하지 않는다.
        private const ushort CredentialsRefusedCloseCode = 4001;

        // 닫힘 코드 없이 우리 쪽에서 연결을 버릴 때 쓰는 코드. 재시도 정책에는 프록시가 끊은
        // 것과 같은 뜻이다.
        private const ushort AbnormalCloseCode = 1006;

        private const double FirstReconnectDelaySeconds = 1;
        private const double MaxReconnectDelaySeconds = 30;

        // 이만큼 열려 있었으면 건강한 세션으로 보고 시도 횟수를 되돌린다. 아래 ScheduleReconnect 참고.
        private const double HealthyConnectionSeconds = 60;

        // 유휴 연결을 살아 있는 것으로 보이게 하는 주기. WebSocket.WaitTime 기본값 5초보다 충분히
        // 길어야 ping 이 다음 ping 을 밀지 않는다.
        private const double KeepAliveIntervalSeconds = 30;

        // handshake 에 두는 마감. 아래 AbandonStalledHandshake 참고. Windows 가 대답 없는 SYN 을
        // 포기하는 데 약 21초가 걸리므로, 그보다 넉넉해야 TCP 가 스스로 낼 닫힘을 앞지르지 않는다.
        private const double HandshakeDeadlineSeconds = 30;

        private readonly Server server;

        // 토큰과 instanceId 를 값이 아니라 읽는 방법으로 받는다. dial 마다 다시 읽어야, 토큰을
        // refresh 하거나 인스턴스를 다시 등록한 뒤의 재연결이 새 값으로 간다. 값으로 받아 URL 을
        // 생성자에서 굳히면 4001 로 끊긴 연결은 프로세스가 사는 동안 회복할 길이 없다(ARTEL-842).
        private readonly Func<string> tokenSource;
        private readonly Func<string> instanceIdSource;

        private readonly ConcurrentQueue<ArtelWebSocketMessage> incomingMessages =
            new ConcurrentQueue<ArtelWebSocketMessage>();

        // 소켓 교체와 타이머 예약은 세 스레드가 함께 닿는다: Start/Stop 을 부르는 Unity 메인
        // 스레드, 닫힘과 열림을 올리는 websocket-sharp 수신 스레드, 재시도를 깨우는 타이머
        // 스레드. 상태 전이는 전부 이 자물쇠 아래에서만 한다.
        private readonly object gate = new object();
        private readonly System.Diagnostics.Stopwatch connectionUptime =
            new System.Diagnostics.Stopwatch();

        private WebSocket client;
        private Timer reconnectTimer;
        private Timer keepAliveTimer;
        private Timer handshakeTimer;
        private int reconnectAttempt;
        private bool stopped;

        // 쓰기는 gate 아래에서만 하고 읽기는 잠금 없이 한다. 오버레이가 프레임마다 읽으므로,
        // 그 읽기가 dial 이나 닫힘 뒤에서 기다리면 안 된다.
        private volatile ArtelTransportPhase phase = ArtelTransportPhase.Idle;

        public ArtelWebSocketClient(Server server, Func<string> token, Func<string> instanceId)
        {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            tokenSource = token ?? throw new ArgumentNullException(nameof(token));
            instanceIdSource = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        }

        /// <summary>
        /// 연결을 연다. 이미 열려 있거나 여는 중이면 아무것도 하지 않는다.
        /// </summary>
        /// <remarks>
        /// 살아 있는 소켓이 있을 때만 물러선다. "client 가 null 이 아니면 물러선다"로 두면 끊긴
        /// 소켓이 그 자리를 영원히 차지한다 — client 를 비우는 곳은 Stop 뿐이라, 한 번 끊긴 뒤에는
        /// 오버레이의 연결 버튼이 여기까지 닿아도 아무 일도 일어나지 않으면서 시작 로그만 남는다.
        ///
        /// 여는 중인 소켓 앞에서도 물러서는 것은 그 상태가 <see cref="HandshakeDeadlineSeconds"/>
        /// 안에 반드시 끝나기 때문이다. 마감이 없던 때에는 여기가 다른 함정이었다: 열리지도 닫히지도
        /// 않는 소켓 앞에서 이 버튼이 영영 아무 일도 하지 않았다.
        /// </remarks>
        public void Start()
        {
            lock (gate)
            {
                stopped = false;
                reconnectAttempt = 0;
                DisposeTimer(ref reconnectTimer);

                if (IsLive(client))
                {
                    return;
                }

                Connect();
            }
        }

        // websocket-sharp opens its own TcpClient and negotiates TLS through Mono's SslStream,
        // so the protocol list comes from this library and not from the native stack behind
        // UnityWebRequest. Its default is SslProtocols.Default, which is Ssl3 | TLS 1.0. A proxy
        // serving TLS 1.2 and up answers that ClientHello with a protocol_version alert, and the
        // socket closes with code 1015 before the HTTP upgrade is ever sent. REST calls to the
        // same host keep working, so the failure reads as a WebSocket outage rather than a TLS one.
        //
        // TLS 1.3 is left out deliberately: Unity's Mono TLS provider does not implement it, and
        // requesting it fails the handshake instead of falling back to 1.2.
        internal static void EnableModernTls(WebSocket socket)
        {
            if (!socket.IsSecure)
            {
                return;
            }

            socket.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
        }

        /// <summary>
        /// 닫힘 코드와 지금까지의 시도 횟수로 다음 재시도까지 기다릴 시간을 정한다.
        /// </summary>
        /// <remarks>
        /// 4001 은 토큰이나 인스턴스 접근이 거절됐다는 뜻이다. 그 값을 바꾸는 것은 재등록뿐이고
        /// 재등록은 사람이 오버레이에서 하므로, 여기서는 멈춘다. 4002(이미 붙어 있는 인스턴스)는
        /// 다르다 — 앞 연결이 서버에서 정리되면 풀리므로 기다렸다 다시 걸 값이 있다.
        ///
        /// 시도 횟수에는 상한을 두지 않는다. 상한이 있던 때에는 재시도 창이 통틀어 약 121초였고,
        /// 그보다 오래 걸리는 배포나 네트워크 장애 하나가 QA 런을 통째로 끝냈다. 사람이 창을 보고
        /// 있지 않은 실행에서는 스스로 돌아오는 것 말고 다른 회복 수단이 없다(ARTEL-842).
        /// 두드리는 빈도는 30초 상한이 인스턴스당 분당 두 번으로 묶는다.
        ///
        /// 시계를 읽지 않는다. 정책이 순수해야 닫힘 코드별 동작을 시간에 기대지 않고 시험할 수 있다.
        /// </remarks>
        internal static bool TryReconnectDelay(ushort closeCode, int attempt, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;

            if (closeCode == CredentialsRefusedCloseCode)
            {
                return false;
            }

            if (attempt < 0)
            {
                return false;
            }

            var seconds = Math.Min(
                FirstReconnectDelaySeconds * Math.Pow(2, attempt),
                MaxReconnectDelaySeconds);
            delay = TimeSpan.FromSeconds(seconds);
            return true;
        }

        /// <summary>연결을 열었거나 여는 중인 소켓인지.</summary>
        internal static bool IsLive(WebSocket socket)
        {
            if (socket == null)
            {
                return false;
            }

            return socket.ReadyState == WebSocketState.Connecting ||
                   socket.ReadyState == WebSocketState.Open;
        }

        // ConnectAsync reports nothing to the caller, so without these the socket can fail to
        // open and every layer above still reads as connected. The close code matters most:
        // the server sends 4001 when the token or the instance is refused, and 4002 when that
        // instance already holds a connection.
        private void HandleOpen(WebSocket socket)
        {
            lock (gate)
            {
                if (!ReferenceEquals(socket, client))
                {
                    return;
                }

                DisposeTimer(ref handshakeTimer);
                connectionUptime.Restart();
                phase = ArtelTransportPhase.Connected;
                StartKeepAlive();
            }

            UnityEngine.Debug.Log("[Artel] WebSocket connected.");
        }

        private void HandleError(ErrorEventArgs e)
        {
            UnityEngine.Debug.LogError("[Artel] WebSocket error: " + e.Message);
        }

        private void HandleClose(WebSocket socket, CloseEventArgs e)
        {
            UnityEngine.Debug.LogWarning(
                "[Artel] WebSocket closed: code=" + e.Code + " reason=" + e.Reason);

            lock (gate)
            {
                // 버려 둔 소켓의 늦은 닫힘이 살아 있는 연결의 재시도를 예약하면 안 된다.
                if (!ReferenceEquals(socket, client))
                {
                    return;
                }

                DisposeTimer(ref keepAliveTimer);
                DisposeTimer(ref handshakeTimer);

                if (stopped)
                {
                    phase = ArtelTransportPhase.Idle;
                    return;
                }

                ScheduleReconnect(e.Code);
            }
        }

        public bool IsConnected
        {
            get
            {
                var socket = client;
                return socket != null && socket.ReadyState == WebSocketState.Open;
            }
        }

        public ArtelTransportPhase Phase
        {
            get { return phase; }
        }

        public bool TryDequeueMessage(out ArtelWebSocketMessage message)
        {
            return incomingMessages.TryDequeue(out message);
        }

        public void Send(string text)
        {
            var socket = client;
            if (socket == null || socket.ReadyState != WebSocketState.Open)
            {
                throw new InvalidOperationException("Artel WebSocket client is not connected.");
            }

            socket.Send(text);
        }

        public void Stop()
        {
            WebSocket closing;
            lock (gate)
            {
                // 재시도를 막는 것은 이 표시다. 닫힘 핸들러는 그대로 달려 있고 로그도 그대로 남는다 —
                // client 를 비웠으므로 그 핸들러는 재시도까지 가지 않고 로그만 남기고 돌아선다.
                stopped = true;
                reconnectAttempt = 0;
                DisposeTimer(ref reconnectTimer);
                DisposeTimer(ref keepAliveTimer);
                DisposeTimer(ref handshakeTimer);
                connectionUptime.Reset();
                phase = ArtelTransportPhase.Idle;

                if (client == null)
                {
                    return;
                }

                closing = client;
                client = null;
            }

            // CloseAsync 는 닫힘 handshake 를 기다리며 막힐 수 있다. gate 를 놓고 부른다.
            closing.CloseAsync();
        }

        public void Dispose()
        {
            Stop();
        }

        // ponytail: 토큰이 쿼리에 실린다. WebSocketSharp의 커스텀 헤더로 옮기려면 서버
        // 핸드셰이크도 같이 바꿔야 하므로, 그때 양쪽을 함께 옮긴다.
        internal static Uri BuildEndpoint(Server server, string token, string instanceId)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("SDK token is required.", nameof(token));
            }

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("Instance id is required.", nameof(instanceId));
            }

            var endpoint = new Uri(server.WebSocketBaseUri, SdkWebSocketPath);
            return new UriBuilder(endpoint)
            {
                Query = "token=" + Uri.EscapeDataString(token) +
                        "&instanceId=" + Uri.EscapeDataString(instanceId)
            }.Uri;
        }

        private void HandleMessage(WebSocket socket, MessageEventArgs eventArgs)
        {
            if (!eventArgs.IsText)
            {
                return;
            }

            if (!ReferenceEquals(socket, client))
            {
                return;
            }

            incomingMessages.Enqueue(new ArtelWebSocketMessage(eventArgs.Data, Send));
        }

        /// <summary>
        /// 새 소켓을 열고, 그 전에 있던 소켓을 놓아준다.
        /// </summary>
        /// <remarks>
        /// gate 를 쥔 채 부른다.
        ///
        /// 핸들러가 이벤트의 sender 대신 자기가 달린 소켓을 캡처해서 받는다. websocket-sharp 이
        /// sender 로 무엇을 넘기는지는 이 어셈블리 밖에서 정해지는 값이고, 자동 재연결 전체가 그
        /// 판정 위에 서 있기 때문이다. 캡처한 참조는 이 파일이 스스로 아는 값이다.
        ///
        /// 예전 소켓을 놓기 전에 client 를 먼저 비운다. Discard 안의 Dispose 가 같은 스레드에서
        /// 닫힘을 올릴 수 있는데, 그때 client 가 아직 그 소켓을 가리키고 있으면 방금 버린 연결이
        /// 재시도를 예약한다.
        /// </remarks>
        private void Connect()
        {
            var previous = client;
            client = null;
            Discard(previous);
            DisposeTimer(ref handshakeTimer);

            Uri endpoint;
            try
            {
                endpoint = BuildEndpoint(server, tokenSource(), instanceIdSource());
            }
            catch (Exception exception)
            {
                // 걸 자격증명이 없다. 다시 걸어 본다고 생기는 값이 아니므로 재시도를 예약하지 않고,
                // 오버레이의 연결 버튼이 등록을 다시 해 채우도록 둔다.
                phase = ArtelTransportPhase.Refused;
                UnityEngine.Debug.LogError(
                    "[Artel] WebSocket has no credentials to dial with: " + exception.Message +
                    " Reconnect from the Artel overlay to register again.");
                return;
            }

            var socket = new WebSocket(endpoint.AbsoluteUri);
            EnableModernTls(socket);
            socket.OnMessage += (sender, e) => HandleMessage(socket, e);
            socket.OnOpen += (sender, e) => HandleOpen(socket);
            socket.OnError += (sender, e) => HandleError(e);
            socket.OnClose += (sender, e) => HandleClose(socket, e);
            client = socket;
            phase = ArtelTransportPhase.Connecting;

            var deadline = TimeSpan.FromSeconds(HandshakeDeadlineSeconds);
            handshakeTimer = new Timer(
                _ => AbandonStalledHandshake(socket), null, deadline, Timeout.InfiniteTimeSpan);

            // 쿼리는 적지 않는다. 토큰이 그 안에 있고, 이 줄은 고객 빌드의 플레이어 로그에 남는다.
            UnityEngine.Debug.Log(
                "[Artel] Connecting WebSocket to " + endpoint.GetLeftPart(UriPartial.Path));
            socket.ConnectAsync();
        }

        /// <summary>
        /// 열리지도 닫히지도 않는 handshake 를 버리고 다시 건다.
        /// </summary>
        /// <remarks>
        /// websocket-sharp 은 handshake 에 마감을 두지 않는다. TCP 는 열렸는데 101 이 돌아오지
        /// 않으면 — 프록시가 연결만 받아 두고 뒤로 보내지 못할 때가 그 모습이다 — 소켓은
        /// Connecting 에 머물고 OnClose 도 오지 않는다. 그러면 재시도가 예약되지 않고,
        /// <see cref="Start"/> 는 그 소켓을 살아 있는 것으로 보고 물러서므로 오버레이의 연결
        /// 버튼도 아무 일도 하지 못한다. 그 상태에서 나가는 길은 시간뿐이라 여기서 잰다.
        ///
        /// 마감이 왔을 때 열려 있지 않은 소켓은 전부 버린다. Connecting 만 버리고 나머지를 그대로
        /// 두면 또 다른 구멍이 남는다 — ConnectAsync 가 잘못된 상태에서 불렸을 때 websocket-sharp
        /// 은 OnError 만 올리고 OnClose 는 올리지 않으므로, 닫힌 소켓 위에서 재시도를 예약할 곳이
        /// 여기 말고는 없다.
        /// </remarks>
        private void AbandonStalledHandshake(WebSocket socket)
        {
            lock (gate)
            {
                if (stopped || !ReferenceEquals(socket, client))
                {
                    return;
                }

                if (socket.ReadyState == WebSocketState.Open)
                {
                    return;
                }

                // 이 소켓에서 손을 뗀다. 뒤늦게 올라오는 닫힘이 재시도를 한 번 더 예약하지 않는다.
                client = null;
            }

            UnityEngine.Debug.LogWarning(
                "[Artel] WebSocket did not open within " + HandshakeDeadlineSeconds +
                "s. Abandoning the socket and dialing again.");

            // CloseAsync 는 막힐 수 있다. gate 를 놓고 부른다.
            socket.CloseAsync();

            lock (gate)
            {
                if (stopped || IsLive(client))
                {
                    return;
                }

                ScheduleReconnect(AbnormalCloseCode);
            }
        }

        /// <summary>
        /// 다음 재시도를 예약한다. gate 를 쥔 채 부른다.
        /// </summary>
        /// <remarks>
        /// 오래 버틴 연결이 끊긴 것은 새 사고다. 앞선 실패들이 쌓아 둔 수열에 얹으면 정상 세션이
        /// 몇 시간에 한 번 끊길 때마다 다음 재시도가 30초 뒤로 밀린다.
        ///
        /// 그렇다고 HandleOpen 에서 되돌리면 반대쪽이 깨진다. 서버는 중복 인스턴스를 핸드셰이크가
        /// 끝난 뒤 4002 로 끊으므로 그 경우에도 열림이 먼저 오고, 시도 횟수가 영영 0에 머물러
        /// 1초 간격으로 두드린다. 그래서 기준은 "열렸는가"가 아니라 "버텼는가"다.
        /// </remarks>
        private void ScheduleReconnect(ushort closeCode)
        {
            if (connectionUptime.Elapsed.TotalSeconds >= HealthyConnectionSeconds)
            {
                reconnectAttempt = 0;
            }

            connectionUptime.Reset();

            TimeSpan delay;
            if (!TryReconnectDelay(closeCode, reconnectAttempt, out delay))
            {
                phase = ArtelTransportPhase.Refused;
                UnityEngine.Debug.LogError(
                    "[Artel] WebSocket will not reconnect: close code=" + closeCode +
                    ". Reconnect from the Artel overlay to register again.");
                return;
            }

            reconnectAttempt++;
            phase = ArtelTransportPhase.Connecting;
            UnityEngine.Debug.LogWarning(
                "[Artel] Reconnecting WebSocket in " + delay.TotalSeconds +
                "s (attempt " + reconnectAttempt + ").");

            DisposeTimer(ref reconnectTimer);
            reconnectTimer = new Timer(
                _ => Reconnect(), null, delay, Timeout.InfiniteTimeSpan);
        }

        private void Reconnect()
        {
            try
            {
                lock (gate)
                {
                    if (stopped || IsLive(client))
                    {
                        return;
                    }

                    Connect();
                }
            }
            catch (Exception exception)
            {
                // 타이머 콜백에서 예외가 새면 이 인스턴스는 다시 걸 일이 없다. 그 자리에서 죽는
                // 것보다 다음 시도를 예약하는 편이 낫다 — backoff 가 두드리는 속도를 묶는다.
                UnityEngine.Debug.LogError("[Artel] WebSocket reconnect failed: " + exception);

                lock (gate)
                {
                    if (!stopped)
                    {
                        ScheduleReconnect(AbnormalCloseCode);
                    }
                }
            }
        }

        /// <summary>
        /// 갈아치운 소켓의 자원을 놓는다.
        /// </summary>
        /// <remarks>
        /// 닫힌 소켓에만 Dispose 를 부른다. 닫히는 중인 소켓은 닫힘 핸드셰이크를 WaitTime 만큼
        /// 기다리는데, 이 자리는 Start 를 거쳐 온 Unity 메인 스레드일 수 있다. 그런 소켓은 스스로
        /// 닫히도록 두고, 이 인스턴스는 client 를 비운 것으로 이미 손을 뗐다.
        /// </remarks>
        private static void Discard(WebSocket socket)
        {
            if (socket == null || socket.ReadyState != WebSocketState.Closed)
            {
                return;
            }

            ((IDisposable)socket).Dispose();
        }

        /// <summary>gate 를 쥔 채 부른다.</summary>
        private void StartKeepAlive()
        {
            DisposeTimer(ref keepAliveTimer);

            var interval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds);
            keepAliveTimer = new Timer(_ => SendKeepAlivePing(), null, interval, interval);
        }

        /// <summary>
        /// 조용한 연결도 살아 있음을 중간 프록시에 보인다.
        /// </summary>
        /// <remarks>
        /// 답 없는 ping 하나로 끊지 않는다. pong 이 한 번 늦는 것과 연결이 죽은 것은 다르고,
        /// 후자라면 어차피 닫힘이 온다. 여기서 앞질러 끊으면 멀쩡한 연결을 우리 손으로 버린다.
        /// </remarks>
        private void SendKeepAlivePing()
        {
            WebSocket socket;
            lock (gate)
            {
                socket = client;
                if (stopped || socket == null || socket.ReadyState != WebSocketState.Open)
                {
                    return;
                }
            }

            // Ping 은 pong 을 WaitTime 만큼 기다리며 막힌다. gate 를 놓고 부른다.
            if (!socket.Ping())
            {
                UnityEngine.Debug.LogWarning("[Artel] WebSocket keep-alive ping went unanswered.");
            }
        }

        private static void DisposeTimer(ref Timer timer)
        {
            if (timer == null)
            {
                return;
            }

            timer.Dispose();
            timer = null;
        }
    }
}
