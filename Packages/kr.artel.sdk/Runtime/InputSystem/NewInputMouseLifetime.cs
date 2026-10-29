using UnityEngine;

namespace Artel
{
    // FixedUpdate가 멈추거나 ArtelManager가 파괴되어도 release 뒤 UI가 처리할 frame을 보장한다.
    internal sealed class NewInputMouseLifetime : MonoBehaviour
    {
        internal InputSystemMouseBackend Backend;

        private void LateUpdate() => Backend?.FinishRelease();
        private void OnDestroy() => Backend?.LifetimeDestroyed(this);
    }
}
