using UnityEngine;

namespace Artel.Pointer
{
    /// <summary>
    /// <c>move_mouse</c> 의 원래 형태: 스캔이 보고하는 것과 같은 좌표계의 좌표 하나 그대로다.
    /// </summary>
    internal sealed class ScreenPointAim : IPointerAim
    {
        private readonly Vector2 reportedPosition;

        public ScreenPointAim(Vector2 reportedPosition)
        {
            this.reportedPosition = reportedPosition;
        }

        public bool TryResolve(SceneScanner scanner, out Vector2 reportedPosition, out string error)
        {
            reportedPosition = this.reportedPosition;
            error = null;
            return true;
        }
    }
}
