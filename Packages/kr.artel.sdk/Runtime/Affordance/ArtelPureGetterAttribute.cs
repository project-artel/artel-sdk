using System;
using System.ComponentModel;

namespace Artel.Affordances
{
    /// <summary>
    /// 빌드 때 SDK 가 IL 을 읽어 부작용이 없다고 증명한 getter 에 붙이는 표시. 손으로 쓰지 않는다.
    /// </summary>
    /// <remarks>
    /// 계산 프로퍼티(<c>IsLocked =&gt; locked || IsPaused</c>)는 값이 어디에도 저장돼 있지 않아 getter 를 돌려야 읽힌다.
    /// <c>Lookup</c> 은 무엇을 하는지 모르는 getter 를 돌리지 않으므로 그동안은 <see cref="ArtelObserveAttribute"/> 가 있어야
    /// 했다. <c>PureGetterWeaver</c> 가 getter 의 IL 이 읽기·산술·분기와 허용 목록의 호출만 한다는 것을 확인하면 이 표시를
    /// 남기고, <c>Lookup</c> 은 이것을 <see cref="ArtelObserveAttribute"/> 와 같이 받는다(ARTEL-1294).
    ///
    /// <see cref="ArtelObserveAttribute"/> 와 다른 점: 그것은 게임이 "이 값을 매 판독 실어 달라" 고 말하는 것이고
    /// (<c>Declared</c> 가 전부 감시한다), 이것은 "이름으로 청하면 돌려도 안전하다" 는 SDK 의 판정일 뿐이다. 감시 목록에 더하지
    /// 않는다.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class ArtelPureGetterAttribute : Attribute
    {
    }
}
