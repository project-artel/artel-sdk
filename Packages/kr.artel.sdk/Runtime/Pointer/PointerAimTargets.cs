using Artel.Tracking;
using UnityEngine;

namespace Artel.Pointer
{
    /// <summary>
    /// id 나 selector 가 가리키는 <see cref="Transform"/> 을, 그것을 겨눌 화면 좌표 하나로 바꾼다.
    /// </summary>
    /// <remarks>
    /// <see cref="BlockTransformReader"/> 가 이미 하는 투영을 한 번 더 만들지 않는다 — 스캔이 쓰는 것과 같은
    /// reader 로 같은 rect 를 얻는다. 겨누는 곳은 그 rect 가운데 화면에 들어온 부분의 중심이다(<see cref="AimPoint"/>).
    /// 화면 안에 다 들어온 객체라면 스캔이 보고한 rect 의 중심과 같은 픽셀이다.
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

            reportedPosition = AimPoint(blockTransform.ScreenRect, Screen.width, Screen.height);
            return true;
        }

        /// <summary>
        /// <paramref name="rect"/> 가운데 화면(0~<paramref name="screenWidth"/>, 0~<paramref name="screenHeight"/>)에 들어온
        /// 부분의 중심. 좌표는 스캔과 같이 왼쪽 위가 원점이다.
        /// </summary>
        /// <remarks>
        /// 스프라이트의 rect 는 렌더러 경계 전체라 화면 밖으로 나간 부분도 들어 있다. 그 중심을 겨누면 사람이 누를 수
        /// 없는 자리를 누르게 된다 — WordVenture 의 손패 카드는 대부분 화면 아래에 숨어 쉬어서, 카드 rect 의 중심이 화면
        /// 아래 끝보다 1.5px 아래였다. 그 자리는 카드가 hover 로 올라가면 카드 밖이 되어, 누름이 절반쯤 뒤의 영역에
        /// 떨어졌다. 화면에 보이는 부분 안에서 겨누면 사람이 겨누는 자리와 같아진다. 호출하는 쪽이 이미 화면과 겹친다고
        /// 확인한 rect 만 넘긴다.
        /// </remarks>
        internal static Vector2 AimPoint(Rect rect, float screenWidth, float screenHeight)
        {
            var visible = Rect.MinMaxRect(
                Mathf.Max(rect.xMin, 0f),
                Mathf.Max(rect.yMin, 0f),
                Mathf.Min(rect.xMax, screenWidth),
                Mathf.Min(rect.yMax, screenHeight));

            // 겹치지 않는 rect 가 들어오면 원래대로 중심을 돌려준다. 겨눌 자리를 지어내지 않는다.
            return visible.width >= 0f && visible.height >= 0f ? visible.center : rect.center;
        }
    }
}
