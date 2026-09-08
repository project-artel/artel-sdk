using System;

namespace Artel
{
    internal interface IArtelWebSocketTransport : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>지금 어디에 있는지. 오버레이가 이 값으로 상태 문구를 고른다.</summary>
        ArtelTransportPhase Phase { get; }

        void Start();
        void Stop();
        bool TryDequeueMessage(out ArtelWebSocketMessage message);
        void Send(string text);
    }
}
