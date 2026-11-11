using UnityEngine;

namespace Artel
{
    // FixedUpdate 가 멈추거나 ArtelManager 가 파괴되어도 놓음 뒤 게임이 처리할 frame 을 보장한다. NewInputMouseLifetime 과 같다.
    internal sealed class NewInputKeyboardLifetime : MonoBehaviour
    {
        internal InputSystemKeyboardBackend Backend;

        private void LateUpdate() => Backend?.FinishRelease();
        private void OnDestroy() => Backend?.LifetimeDestroyed(this);
    }
}
