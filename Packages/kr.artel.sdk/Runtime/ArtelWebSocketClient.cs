using System;
using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Threading;
using Artel.Domain;
using WebSocketSharp;

namespace Artel
{
    /// <summary>
    /// 연결이 끊겼을 때 이 게임이 언제까지 다시 붙어 보는가 (ARTEL-797).
    /// </summary>
    /// <remarks>
    /// 정책을 가르는 사실은 세션의 출처다. 실행 인자가 세션을 넣은 실행에는 오버레이를 누를
    /// 사람이 없으므로, 포기하는 순간 그 게임과 그 위의 QA run 이 함께 끝난다.
    ///
    /// 이 값을 <see cref="ArtelWebSocketClient"/> 가 생성자로 받는 것이 요점이다. 소켓 쪽이
    /// <c>ArtelLaunchArguments</c> 를 직접 읽으면 전송이 실행 인자와 세션 저장소까지 알게 되고,
    /// 그러면 테스트가 소켓 하나를 재려고 프로세스의 명령행을 흉내 내야 한다. 실행 인자를 이미
    /// 읽고 있는 <see cref="ArtelManager"/> 가 그 사실을 이 두 값 중 하나로 옮긴다.
    /// </remarks>
    internal enum ArtelReconnectPolicy
    {
        /// <summary>사람이 보고 있는 실행. 여덟 번 뒤 멈추고 오버레이가 다시 붙는 길을 준다.</summary>
        Attended,

        /// <summary>실행 인자가 세션을 넣은 실행. 최대 간격으로 계속 시도한다.</summary>
        Unattended
    }

    internal sealed class ArtelWebSocketClient : IArtelWebSocketTransport
    {
        private const string SdkWebSocketPath = "/ws/sdk";

        // 서버가 자격증명이나 인스턴스 접근을 거절할 때 붙이는 코드. 같은 URL 로 다시 걸면 같은
        // 대답이 오므로 이 코드만은 재시도하지 않는다.
        private const ushort CredentialsRefusedCloseCode = 4001;

        private const int MaxReconnectAttempts = 8;
        private const double FirstReconnectDelaySeconds = 1;
        private const double MaxReconnectDelaySeconds = 30;

        // 이만큼 열려 있었으면 건강한 세션으로 보고 시도 횟수를 되돌린다. 아래 HandleClose 참고.
        private const double HealthyConnectionSeconds = 60;

        // 간격이 상한에 닿은 뒤로는 이만큼마다 한 번만 적는다. 간격이 30초이므로 10분에 한 번이다.
        // 서버가 여덟 시간 내려가 있으면 재시도는 960번인데, 그중 48번 것만 로그에 남는다.
        private const int QuietReconnectLogEvery = 20;

        // 간격이 처음으로 상한에 닿는 시도 번호. 1초에서 2배씩 늘어 30초를 넘는 지점이므로
        // ceil(log2(30)) = 5 다. 상수 셋 중 하나가 바뀌어도 따라오도록 계산해 둔다.
        private static readonly int FirstCeilingAttempt = (int)Math.Ceiling(
            Math.Log(MaxReconnectDelaySeconds / FirstReconnectDelaySeconds, 2));

        // 유휴 연결을 살아 있는 것으로 보이게 하는 주기. WebSocket.WaitTime 기본값 5초보다 충분히
        // 길어야 ping 이 다음 ping 을 밀지 않는다.
        private const double KeepAliveIntervalSeconds = 30;

        private readonly string url;
        private readonly ArtelReconnectPolicy reconnectPolicy;
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
        private int reconnectAttempt;
        private bool stopped;

        // 이번 재시도 주기의 로그를 적을 것인가. 무인 실행이 상한 간격으로 계속 시도하는 동안
        // 연결 시작·오류·닫힘·다음 시도까지 네 줄이 주기마다 쌓이므로, 넷을 함께 막는다.
        // HandleError 는 websocket-sharp 수신 스레드에서 읽으므로 volatile 이다.
        private volatile bool announcesRetryLogs = true;

        public ArtelWebSocketClient(
            Server server, string token, string instanceId, ArtelReconnectPolicy reconnectPolicy)
        {
            url = BuildEndpoint(server, token, instanceId).AbsoluteUri;
            this.reconnectPolicy = reconnectPolicy;
        }

        /// <summary>
        /// 연결을 연다. 이미 열려 있거나 여는 중이면 아무것도 하지 않는다.
        /// </summary>
        /// <remarks>
        /// 살아 있는 소켓이 있을 때만 물러선다. "client 가 null 이 아니면 물러선다"로 두면 끊긴
        /// 소켓이 그 자리를 영원히 차지한다 — client 를 비우는 곳은 Stop 뿐이라, 한 번 끊긴 뒤에는
        /// 오버레이의 연결 버튼이 여기까지 닿아도 아무 일도 일어나지 않으면서 시작 로그만 남는다.
        /// </remarks>
        public void Start()
        {
            lock (gate)
            {
                stopped = false;
                reconnectAttempt = 0;
                announcesRetryLogs = true;
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
        /// 4001 은 토큰이나 인스턴스 접근이 거절됐다는 뜻이다. 재연결은 같은 URL 을 다시 쓰므로
        /// 대답도 같고, 재시도는 끝나지 않는 고리가 된다. 4002(이미 붙어 있는 인스턴스)는 다르다 —
        /// 앞 연결이 서버에서 정리되면 풀리므로 backoff 를 두고 다시 시도할 값이 있다. 이 판정은
        /// 정책과 무관하다: 무인 실행이라고 4001 을 다시 걸어 봐야 같은 4001 을 받는다.
        ///
        /// 정책이 가르는 것은 횟수 상한 하나뿐이다 (ARTEL-797). 간격 수열은 두 정책에서 같고,
        /// <see cref="ArtelReconnectPolicy.Unattended"/> 는 상한에 닿은 뒤 30초 간격을 계속 쓴다.
        ///
        /// 시계를 읽지 않는다. 정책이 순수해야 닫힘 코드별 동작을 시간에 기대지 않고 시험할 수 있다.
        /// </remarks>
        internal static bool TryReconnectDelay(
            ushort closeCode, int attempt, ArtelReconnectPolicy policy, out TimeSpan delay)
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

            if (policy == ArtelReconnectPolicy.Attended && attempt >= MaxReconnectAttempts)
            {
                return false;
            }

            var seconds = Math.Min(
                FirstReconnectDelaySeconds * Math.Pow(2, attempt),
                MaxReconnectDelaySeconds);
            delay = TimeSpan.FromSeconds(seconds);
            return true;
        }

        /// <summary>
        /// 이번 시도의 로그를 적을 것인가 (ARTEL-797).
        /// </summary>
        /// <remarks>
        /// 무인 실행은 서버가 돌아올 때까지 30초마다 계속 시도하므로, 같은 네 줄이 하루에 만 줄
        /// 넘게 쌓인다. 그 로그가 무인 실행의 유일한 단서인데 정작 읽을 수 없게 된다.
        ///
        /// 간격이 아직 자라는 동안에는 매번 적는다. 그때는 줄 수가 다섯을 넘지 않고, 처음 몇 초가
        /// 무슨 일이 일어났는지 말해 주는 구간이다. 상한에 처음 닿은 시도도 적는다 — 그 줄이
        /// "이제부터 30초마다 계속 시도한다" 를 말하는 자리다.
        ///
        /// 사람이 보고 있는 실행에서는 아무것도 줄이지 않는다. 여덟 번이 전부라 넘칠 것이 없고,
        /// 줄이면 지금 보이던 줄이 이유 없이 사라진다.
        /// </remarks>
        internal static bool AnnouncesReconnect(ArtelReconnectPolicy policy, int attempt, TimeSpan delay)
        {
            if (policy == ArtelReconnectPolicy.Attended)
            {
                return true;
            }

            if (delay.TotalSeconds < MaxReconnectDelaySeconds)
            {
                return true;
            }

            return (attempt - FirstCeilingAttempt) % QuietReconnectLogEvery == 0;
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
            int attempts;
            lock (gate)
            {
                if (!ReferenceEquals(socket, client))
                {
                    return;
                }

                attempts = reconnectAttempt;
                announcesRetryLogs = true;
                connectionUptime.Restart();
                StartKeepAlive();
            }

            // 되돌아온 줄은 언제나 적는다. 무인 실행에서 "언제 다시 붙었는가" 를 아는 근거가
            // 이 줄 하나뿐이고, 그 줄까지 QuietReconnectLogEvery 로 줄이면 재시도를 줄인 이유가
            // 없어진다. 시도 횟수를 함께 적으면 앞선 실패 줄이 조용히 빠진 구간도 읽힌다.
            UnityEngine.Debug.Log(attempts == 0
                ? "[Artel] WebSocket connected."
                : "[Artel] WebSocket reconnected after " + attempts + " attempts.");
        }

        private void HandleError(ErrorEventArgs e)
        {
            // 열려 있는 연결의 오류는 언제나 남는다. HandleOpen 이 이 값을 참으로 되돌리므로,
            // 여기서 빠지는 것은 조용한 재시도 주기에서 붙기도 전에 실패한 오류뿐이다.
            if (!announcesRetryLogs)
            {
                return;
            }

            UnityEngine.Debug.LogError("[Artel] WebSocket error: " + e.Message);
        }

        private void HandleClose(WebSocket socket, CloseEventArgs e)
        {
            // 앞 주기가 정한 값으로 적는다. 이 줄이 이번 주기의 판정보다 먼저 오므로, 조용한
            // 구간에서는 닫힘도 함께 빠지고 다시 적는 주기에는 네 줄이 한 묶음으로 남는다.
            if (announcesRetryLogs)
            {
                UnityEngine.Debug.LogWarning(
                    "[Artel] WebSocket closed: code=" + e.Code + " reason=" + e.Reason);
            }

            lock (gate)
            {
                // 버려 둔 소켓의 늦은 닫힘이 살아 있는 연결의 재시도를 예약하면 안 된다.
                if (!ReferenceEquals(socket, client))
                {
                    return;
                }

                DisposeTimer(ref keepAliveTimer);

                if (stopped)
                {
                    return;
                }

                // 오래 버틴 연결이 끊긴 것은 새 사고다. 앞선 실패들이 쌓아 둔 수열에 얹으면 정상
                // 세션이 몇 시간에 한 번 끊길 때마다 남은 시도가 줄어들어, 결국 재연결을 포기한다.
                //
                // 그렇다고 HandleOpen 에서 되돌리면 반대쪽이 깨진다. 서버는 중복 인스턴스를 핸드셰이크가
                // 끝난 뒤 4002 로 끊으므로 그 경우에도 열림이 먼저 오고, 시도 횟수가 영영
                // 0에 머물러 재시도가 멈추지 않는다. 그래서 기준은 "열렸는가"가 아니라 "버텼는가"다.
                if (connectionUptime.Elapsed.TotalSeconds >= HealthyConnectionSeconds)
                {
                    reconnectAttempt = 0;
                }

                connectionUptime.Reset();

                TimeSpan delay;
                if (!TryReconnectDelay(e.Code, reconnectAttempt, reconnectPolicy, out delay))
                {
                    UnityEngine.Debug.LogError(
                        "[Artel] WebSocket will not reconnect: close code=" + e.Code +
                        ", attempts=" + reconnectAttempt +
                        ". Reconnect from the Artel overlay to try again.");
                    return;
                }

                announcesRetryLogs = AnnouncesReconnect(reconnectPolicy, reconnectAttempt, delay);
                reconnectAttempt++;

                if (announcesRetryLogs)
                {
                    UnityEngine.Debug.LogWarning(
                        "[Artel] Reconnecting WebSocket in " + delay.TotalSeconds + "s (attempt " +
                        reconnectAttempt +
                        (reconnectPolicy == ArtelReconnectPolicy.Unattended
                            ? "; this run signed in from launch arguments, so it keeps retrying)."
                            : " of " + MaxReconnectAttempts + ")."));
                }

                DisposeTimer(ref reconnectTimer);
                reconnectTimer = new Timer(
                    _ => Reconnect(), null, delay, Timeout.InfiniteTimeSpan);
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
            lock (gate)
            {
                // 재시도를 막는 것은 이 표시다. 닫힘 핸들러는 그대로 달려 있고 로그도 그대로 남는다 —
                // client 를 비웠으므로 그 핸들러는 재시도까지 가지 않고 로그만 남기고 돌아선다.
                stopped = true;
                reconnectAttempt = 0;
                announcesRetryLogs = true;
                DisposeTimer(ref reconnectTimer);
                DisposeTimer(ref keepAliveTimer);
                connectionUptime.Reset();

                if (client == null)
                {
                    return;
                }

                var closing = client;
                client = null;
                closing.CloseAsync();
            }
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

            var socket = new WebSocket(url);
            EnableModernTls(socket);
            socket.OnMessage += (sender, e) => HandleMessage(socket, e);
            socket.OnOpen += (sender, e) => HandleOpen(socket);
            socket.OnError += (sender, e) => HandleError(e);
            socket.OnClose += (sender, e) => HandleClose(socket, e);
            client = socket;

            if (announcesRetryLogs)
            {
                UnityEngine.Debug.Log("[Artel] Connecting WebSocket to " + url);
            }

            socket.ConnectAsync();
        }

        private void Reconnect()
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
