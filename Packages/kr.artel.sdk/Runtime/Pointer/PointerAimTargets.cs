using Artel.Tracking;
using UnityEngine;

namespace Artel.Pointer
{
    /// <summary>
    /// id 나 selector 가 가리키는 <see cref="Transform"/> 을, 그것을 겨눌 화면 좌표 하나로 바꾼다.
    /// </summary>
    /// <remarks>
    /// <see cref="BlockTransformReader"/> 가 이미 하는 투영을 한 번 더 만들지 않는다 — 스캔이 쓰는 것과 같은
    /// reader 로 같은 rect 를 얻고, 그 중심을 쓴다. 그래야 id 로 겨눈 자리가 그 객체를 스캔이 보고했을 좌표와
    /// 정확히 같은 픽셀이 된다.
    /// <para>
    /// 매 호출마다 <see cref="BlockTransformReader"/> 를 새로 만들고 <see cref="BlockTransformReader.BeginScan"/>
    /// 을 부른다. 겨냥은 매 스캔마다 아니라 액션 하나당 한 번이라 <c>Camera.main</c> 을 다시 찾는 비용은 무시할
    /// 만하고, 부르지 않으면 UI 가 아닌 오브젝트가 null 카메라에 대고 투영되어 값이 깨진다.
    /// </para>
    /// </remarks>
    internal static class PointerAimTargets
    {
        internal static bool TryResolveScreenPoint(Transform transform, out Vector2 reportedPosition)
        {
            var reader = new BlockTransformReader();
            reader.BeginScan();
            var blockTransform = reader.Read(transform);

            if (!blockTransform.OnScreen)
            {
                reportedPosition = default;
                return false;
            }

            reportedPosition = blockTransform.ScreenRect.center;
            return true;
        }
    }
}
