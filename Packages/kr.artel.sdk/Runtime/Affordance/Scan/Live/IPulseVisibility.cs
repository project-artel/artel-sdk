using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// <c>pulse</c> 의 객체 항목에 실을 <c>visible</c> 을 판정하는 자리(ARTEL-1435).
    /// </summary>
    /// <remarks>
    /// 판정은 scan 블록의 <c>visible</c>(ARTEL-1225)과 같은 규칙이어야 하고, 그 규칙은 uGUI 의 <c>Graphic</c>·<c>Mask</c>·
    /// <c>EventSystem</c> 을 직접 쓴다. 이 어셈블리는 uGUI 를 참조하지 않으므로(<see cref="UiState"/> 가 이름으로 맞추는
    /// 이유와 같다) <see cref="IPulseAttachment"/> 처럼 <c>Artel.Runtime</c> 이 이것을 구현해 <see cref="LiveState.Visibility"/>
    /// 에 꽂는다.
    /// </remarks>
    internal interface IPulseVisibility
    {
        /// <summary>판독 하나를 쓰기 전에 한 번. 카메라와 <c>EventSystem</c> 을 객체마다가 아니라 판독마다 푼다.</summary>
        void Begin();

        /// <summary>켜진 객체가 지금 보이는지. 프레임 밖, 꺼진 renderer·graphic, 투명, mask 밖, 다른 것에 덮임이면 false.</summary>
        bool IsVisible(Transform transform);
    }
}
