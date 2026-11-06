using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    [AddComponentMenu("")]
    internal sealed class ArtelOverlayRaycaster : GraphicRaycaster
    {
        // 화면 표시는 OnGUI가 맡고, EventSystem에서는 SDK 영역이 게임 버튼보다 먼저 잡혀야 한다.
        public override int sortOrderPriority => short.MaxValue;
        public override int renderOrderPriority => int.MaxValue;
    }
}
