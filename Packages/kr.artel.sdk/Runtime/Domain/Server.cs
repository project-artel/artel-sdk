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

        /// <summary>
        /// 받은 값만 덮어쓰고 나머지는 그대로 둔다. <c>ArtelLaunchArguments</c> 가 실행 인자를
        /// 여기로 넘긴다 (ARTEL-787).
        /// </summary>
        /// <remarks>
        /// 생성자로는 이 자리를 채울 수 없다. <c>Server(bool, string, int)</c> 는 셋을 한꺼번에
        /// 받으므로 <c>-artel-frontend</c> 하나만 준 실행에서도 host 와 port 를 호출 쪽이 다시
        /// 적어야 하는데, 기본값을 읽을 방법이 없다 — 세 필드 모두 getter 가 없고 밖에서 보이는
        /// 것은 <see cref="HttpBaseUri"/> 뿐이다. frontendOrigin 은 생성자가 아예 받지 못한다.
        ///
        /// <c>internal</c> 인 것은 게임 코드가 인스펙터 대신 이 길로 서버를 바꾸는 것을 막기
        /// 위해서다. 씬에 매니저를 놓은 게임은 인스펙터가 유일한 입구로 남는다.
        /// </remarks>
        internal void OverrideEndpoints(
            bool? overriddenSecure,
            string overriddenHost,
            int? overriddenPort,
            string overriddenFrontendOrigin)
        {
            if (overriddenSecure.HasValue)
            {
                secure = overriddenSecure.Value;
            }

            if (!string.IsNullOrWhiteSpace(overriddenHost))
            {
                host = overriddenHost.Trim();
            }

            if (overriddenPort.HasValue)
            {
                port = overriddenPort.Value;
            }

            if (!string.IsNullOrWhiteSpace(overriddenFrontendOrigin))
            {
                frontendOrigin = overriddenFrontendOrigin.Trim();
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
