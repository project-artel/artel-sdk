using System;
using UnityEngine;

namespace Artel.Domain
{
    [Serializable]
    public sealed class Server
    {
        public const string DefaultUrl = "https://stage-orch.artel.kr/";
        [SerializeField, Tooltip("Artel 서버의 HTTP(S) 주소")]
        private string serverUrl = "";

        // 이전 씬의 주소를 보존한다. URL을 입력하면 새 값이 우선한다.
        [SerializeField, HideInInspector] private bool secure = true;
        [SerializeField, HideInInspector] private string host = "stage-orch.artel.kr";
        [SerializeField, HideInInspector] private int port = 443;

        public Server() { }

        public Server(string url)
        {
            serverUrl = NormalizeUrl(url).AbsoluteUri;
        }

        public Server(bool secure, string host, int port)
        {
            this.secure = secure;
            this.host = host;
            this.port = port;
        }

        public Uri HttpBaseUri
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(serverUrl)) return NormalizeUrl(serverUrl);
                if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535)
                    throw new InvalidOperationException("서버 주소와 port를 확인해 주세요.");
                return NormalizeUrl(new UriBuilder(secure ? "https" : "http", host.Trim(), port).Uri.AbsoluteUri);
            }
        }

        public Uri WebSocketBaseUri
        {
            get
            {
                var address = HttpBaseUri;
                return new UriBuilder(address) { Scheme = address.Scheme == "https" ? "wss" : "ws", Port = address.Port }.Uri;
            }
        }

        internal static Uri NormalizeUrl(string url)
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var address) ||
                (address.Scheme != "http" && address.Scheme != "https") ||
                string.IsNullOrEmpty(address.Host) || !string.IsNullOrEmpty(address.UserInfo) ||
                address.AbsolutePath != "/" || address.Query.Length != 0 || address.Fragment.Length != 0)
                throw new ArgumentException("서버 주소는 경로 없이 http:// 또는 https://로 입력해 주세요.");
            return new Uri(address.GetLeftPart(UriPartial.Authority) + "/");
        }

        internal void OverrideEndpoints(bool? overriddenSecure, string overriddenHost, int? overriddenPort)
        {
            if (!overriddenSecure.HasValue && overriddenHost == null && !overriddenPort.HasValue) return;
            var current = HttpBaseUri;
            serverUrl = new UriBuilder(
                (overriddenSecure ?? (current.Scheme == "https")) ? "https" : "http",
                overriddenHost ?? current.Host, overriddenPort ?? current.Port).Uri.AbsoluteUri;
        }
    }
}
