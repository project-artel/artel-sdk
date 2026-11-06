namespace Artel
{
    /// <summary>
    /// 전송이 지금 어디에 있는지. 오버레이가 이 값을 그린다.
    /// </summary>
    /// <remarks>
    /// <see cref="IArtelWebSocketTransport.IsConnected"/> 만으로는 화면에 적을 말을 고를 수 없다.
    /// 붙어 있지 않은 것은 같아도, 다시 거는 중이라면 사람이 할 일이 없고, 자격증명이 거절됐다면
    /// 연결 버튼으로 다시 등록하는 것 말고는 길이 없다. 그 둘을 한 문구로 적으면 화면이 거짓말을
    /// 한다 — ARTEL-842 에서 사람이 본 것이 그 거짓말이다.
    /// </remarks>
    internal enum ArtelTransportPhase
    {
        /// <summary>연 적이 없거나 <see cref="IArtelWebSocketTransport.Stop"/> 로 멈췄다.</summary>
        Idle,

        /// <summary>거는 중이거나, 끊긴 뒤 다시 걸기를 기다리는 중.</summary>
        Connecting,

        Connected,

        /// <summary>
        /// 서버가 자격증명을 거절했거나(닫힘 코드 4001), 걸 자격증명 자체가 없다. 같은 값으로 다시
        /// 걸면 같은 대답이 오므로 스스로 재시도하지 않는다. 여기서 나가는 길은 재등록뿐이다.
        /// </summary>
        Refused
    }
}
