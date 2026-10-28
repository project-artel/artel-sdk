using System;

namespace Artel.Affordances
{
    /// <summary>
    /// 게임이 도는 동안 읽어 달라고 게임 코드가 직접 표시하는 필드나 프로퍼티.
    /// </summary>
    /// <remarks>
    /// 빌드 분석은 조건과 효과가 이름 댄 멤버를 watch list 에 담고, `watch` action 은 런이 이름을 대고 청한다. 이것은
    /// 셋째 길이다 — 어느 조건도 읽지 않고 아무도 청하지 않았지만 테스트가 묻는 값을, 그 값을 가진 쪽이 미리 말해 두는
    /// 것. 프로퍼티에 붙이면 getter 가 판독마다 돈다. 그것을 허락하는 것은 붙인 사람이다.
    ///
    /// 컴포넌트가 아닌 타입에 붙여도 된다. 그 인스턴스는 static 필드와 씬 위 컴포넌트에서 참조를 따라가 찾는다.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ArtelObserveAttribute : Attribute
    {
    }
}
