namespace Artel.Affordances.Live
{
    /// <summary>
    /// <c>PULSE</c> 문서 끝에 다른 어셈블리가 필드를 더하는 자리.
    /// </summary>
    /// <remarks>
    /// <c>Artel.Runtime</c> 이 이 어셈블리를 참조하고 그 반대는 아니어서, 런타임 쪽 상태(cheat 와 behavior)를 여기서 직접
    /// 읽을 수 없다. 그래서 런타임이 이것을 구현해 <see cref="LiveState.Attachment"/> 에 꽂는다.
    /// </remarks>
    internal interface IPulseAttachment
    {
        /// <summary>
        /// 더할 JSON 조각. <c>,"key":value</c> 처럼 쉼표로 시작한다. 더할 것이 없으면 null.
        /// </summary>
        /// <param name="whole">이번 <c>pulse</c> 가 전량인지. 그러면 조각도 전량이어야 한다.</param>
        /// <param name="news">
        /// 조각이 바뀐 것을 실었는지. true 면 감시 대상 값이 하나도 움직이지 않았어도 이번 <c>pulse</c> 를 보낸다.
        /// </param>
        string Compose(bool whole, out bool news);
    }
}
