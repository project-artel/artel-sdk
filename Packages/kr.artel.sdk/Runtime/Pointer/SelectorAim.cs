using UnityEngine;

namespace Artel.Pointer
{
    /// <summary>
    /// <c>move_mouse</c> 가 받는 세 번째 형태: <c>Artel.Affordances.Scan.ScenePath.SelectorOf</c> 가 쓴
    /// selector 문자열.
    /// </summary>
    internal sealed class SelectorAim : IPointerAim
    {
        private readonly string selector;

        public SelectorAim(string selector)
        {
            this.selector = selector;
        }

        public bool TryResolve(SceneScanner scanner, out Vector2 reportedPosition, out string error)
        {
            reportedPosition = default;

            if (!ScenePathSelectorResolver.TryResolve(selector, out var transform, out error))
            {
                return false;
            }

            if (!PointerAimTargets.TryResolveScreenPoint(transform, out reportedPosition))
            {
                error = "Target has no usable position on screen: " + selector;
                return false;
            }

            return true;
        }
    }
}
