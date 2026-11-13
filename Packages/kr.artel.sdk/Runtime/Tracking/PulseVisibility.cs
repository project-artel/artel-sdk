using Artel.Affordances.Live;
using UnityEngine;

namespace Artel.Tracking
{
    /// <summary>
    /// <c>pulse</c> 의 <c>visible</c> 을 scan 블록의 <c>visible</c> 과 같은 reader 로 판정한다(ARTEL-1435).
    /// </summary>
    /// <remarks>
    /// 판정을 새로 만들지 않는다. 같은 객체가 scan 에서는 보이고 <c>pulse</c> 에서는 안 보인다고 말하면 읽는 쪽이 어느 것도
    /// 믿을 수 없다.
    /// </remarks>
    internal sealed class PulseVisibility : IPulseVisibility
    {
        private readonly BlockTransformReader reader = new BlockTransformReader();

        public void Begin()
        {
            reader.BeginScan();
        }

        public bool IsVisible(Transform transform)
        {
            return reader.Read(transform).Visible;
        }
    }
}
