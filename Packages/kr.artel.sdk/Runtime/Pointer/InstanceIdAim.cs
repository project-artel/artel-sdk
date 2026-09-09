using UnityEngine;

namespace Artel.Pointer
{
    /// <summary>
    /// <c>move_mouse</c> 가 받는 두 번째 형태: <c>button_click</c> 이 받는 것과 같은 종류의 Unity instance id.
    /// </summary>
    internal sealed class InstanceIdAim : IPointerAim
    {
        private readonly int instanceId;

        public InstanceIdAim(int instanceId)
        {
            this.instanceId = instanceId;
        }

        public bool TryResolve(SceneScanner scanner, out Vector2 reportedPosition, out string error)
        {
            reportedPosition = default;

            if (!scanner.TryGetTransform(instanceId, out var transform))
            {
                error = "Unknown target id: " + instanceId;
                return false;
            }

            if (!PointerAimTargets.TryResolveScreenPoint(transform, out reportedPosition))
            {
                error = "Target has no usable position on screen: " + instanceId;
                return false;
            }

            error = null;
            return true;
        }
    }
}
