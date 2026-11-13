using UnityEngine;

namespace Artel.Pointer
{
    /// <summary>
    /// <c>move_mouse</c> 가 받는 세 형태의 겨냥 — 좌표, instance id, selector — 을 하나의 물음으로 만든다.
    /// </summary>
    /// <remarks>
    /// 답은 언제나 스캔이 보고하는 것과 같은 좌표계다: 왼쪽 위가 원점이고 y 가 아래로 자란다. <c>ExecuteMoveMouse</c>
    /// 는 이 값에 <c>Screen.height - y</c> 뒤집기를 단 한 번 적용해 Unity 의 화면 공간으로 바꾼다 — 세 형태
    /// 모두에게 같은 자리에서 같은 방식으로, 그래서 겨냥이 무엇이었는지와 무관하게 그 뒤집기는 한 곳에만 산다.
    /// </remarks>
    internal interface IPointerAim
    {
        /// <summary>
        /// 겨눈 자리를 화면 좌표로 낸다. 실패하면 이유를 <paramref name="error"/> 에 담아 false 를 돌려준다 —
        /// 던지지 않는다. id 를 못 찾은 것, selector 에 아무것도 없는 것, 대상은 있으나 화면에 겨눌 자리가 없는
        /// 것은 서로 다른 실패이고, 에이전트가 다음 수를 그 문구로 정하므로 문구를 갈라 둔다.
        /// </summary>
        bool TryResolve(SceneScanner scanner, out Vector2 reportedPosition, out string error);
    }
}
