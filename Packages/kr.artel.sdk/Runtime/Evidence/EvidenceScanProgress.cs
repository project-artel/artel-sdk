using System;
using Artel.Protocol.Dto;
using Artel.Serialization;

namespace Artel.Evidence
{
    /// <summary>근거 스캔이 지금 어디쯤인지 바깥에 알린다(ARTEL-1177).</summary>
    internal interface IEvidenceScanProgress
    {
        void Report(string stage, int done, int total, string scene);
    }

    /// <summary>
    /// 진행을 지금 붙어 있는 <c>/ws/sdk</c> 연결로 바로 보낸다.
    /// </summary>
    /// <remarks>
    /// 액션 큐를 거치지 않는다. 스캔 자신이 그 큐를 쥐고 도는 중이라, 큐 뒤에 세우면 스캔이 끝난 뒤에야 나간다.
    ///
    /// 연결이 없으면 버린다. 진행은 다음 것이 앞 것을 덮는 값이라 쌓아 두었다 보낼 까닭이 없고, 결과(<c>ACTION_RESULT</c>)는
    /// 이것과 상관없이 간다.
    /// </remarks>
    internal sealed class WebSocketEvidenceScanProgress : IEvidenceScanProgress
    {
        public const string Scanning = "SCANNING";
        public const string Uploading = "UPLOADING";

        private const string MessageType = "EVIDENCE_SCAN_PROGRESS";

        private readonly IJsonCodec jsonCodec;
        private readonly Func<IArtelWebSocketTransport> currentTransport;

        /// <param name="currentTransport">보낼 때마다 읽는다. 매니저가 연결을 갈아 끼워도 낡은 소켓에 쓰지 않는다.</param>
        public WebSocketEvidenceScanProgress(IJsonCodec jsonCodec, Func<IArtelWebSocketTransport> currentTransport)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            this.currentTransport = currentTransport ?? throw new ArgumentNullException(nameof(currentTransport));
        }

        public void Report(string stage, int done, int total, string scene)
        {
            var transport = currentTransport();
            if (transport == null || !transport.IsConnected)
            {
                return;
            }

            transport.Send(jsonCodec.Serialize(new EvidenceScanProgressMessageDto
            {
                Type = MessageType,
                Stage = stage,
                Done = done,
                Total = total,
                Scene = scene
            }));
        }
    }
}
