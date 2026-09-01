using System;
using UnityEngine;

namespace Artel.Domain
{
    [Serializable]
    public sealed class Server
    {
        // 기본값이 실제 서버다. 그래야 씬에 매니저를 놓지 않은 게임도 붙는다 (ARTEL-703).
        //
        // 전에는 `host` 가 비어 있었고, 그래서 개발 빌드가 스스로 만드는 매니저
        // (`ArtelManager.SpawnInDevelopmentBuilds`)가 붙을 곳을 모르는 껍데기였다 — 실제로
        // 쓰려면 사람이 씬에 매니저를 놓고 인스펙터를 채워야 했다. 비어 있던 것은 판단이
        // 아니라 로컬 개발 기본값이 그대로 남은 것이다.
        //
        // 게임마다 다를 값이 아니라서 기본값으로 둘 수 있다. SDK 를 쓰는 게임은 모두 같은
        // 오케스트레이션에 붙고, 환경이 바뀌면 SDK 를 다시 낸다. 인스펙터 필드는 그대로
        // 남아서, 자기 서버를 보는 개발자가 덮어쓴다.
        [SerializeField] private bool secure = true;
        [SerializeField] private string host = "stage-orch.artel.kr";
        [SerializeField] private int port = 443;

        // 로그인 중계 페이지는 오케스트레이션 서버가 아니라 웹 콘솔에 있다. 호스트도 포트도
        // 다르므로 위 세 값에서 유도할 수 없다.
        [SerializeField] private string frontendOrigin = "https://home.stage.artel.kr";

        public Server()
        {
        }

        public Server(bool secure, string host, int port)
        {
            this.secure = secure;
            this.host = host;
            this.port = port;
        }

        public Uri HttpBaseUri
        {
            get { return BuildBaseUri(secure ? "https" : "http"); }
        }

        public Uri WebSocketBaseUri
        {
            get { return BuildBaseUri(secure ? "wss" : "ws"); }
        }

        public Uri FrontendBaseUri
        {
            get
            {
                if (string.IsNullOrWhiteSpace(frontendOrigin))
                {
                    throw new InvalidOperationException("Frontend origin is required.");
                }

                return new Uri(frontendOrigin.Trim(), UriKind.Absolute);
            }
        }

        private Uri BuildBaseUri(string scheme)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException("Server host is required.");
            }

            if (port < 1 || port > 65535)
            {
                throw new InvalidOperationException("Server port must be between 1 and 65535: " + port);
            }

            return new UriBuilder(scheme, host.Trim(), port).Uri;
        }
    }
}
