using System;

namespace Artel
{
    /// <summary>
    /// QA agent 가 <c>start_behavior</c> 로 시작하고 <c>stop_behavior</c> 로 멈추는 behavior 로 메서드를 등록한다.
    /// </summary>
    /// <remarks>
    /// 메서드는 <c>IEnumerator</c> 를 돌려주고 프레임마다 한 걸음씩 돈다. 마지막 parameter 로
    /// <see cref="ArtelBehaviorContext"/> 를 받을 수 있고, 그것은 agent 에게 보이지 않는다.
    ///
    /// SDK 가 iterator 를 직접 <c>MoveNext()</c> 하고 끝날 때 <c>Dispose()</c> 하므로, 어떻게 끝나든 메서드 안의
    /// <c>finally</c> 가 돈다. 정리 코드는 <c>finally</c> 에 둔다.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ArtelBehaviorAttribute : Attribute
    {
        public string Description { get; }

        /// <summary>
        /// 같은 group 의 behavior 는 동시에 하나만 돈다. 새것이 시작하면 돌던 것은 <c>replaced</c> 로 끝난다.
        /// </summary>
        public string Group { get; }

        /// <summary>catalog 이름. null 이면 메서드 이름의 snake_case.</summary>
        public string Name { get; }

        /// <summary>지금 쓸 수 있는지 답하는 메서드 이름. 도는 동안 false 가 되면 <c>unavailable</c> 로 끝난다.</summary>
        public string When { get; }

        public ArtelBehaviorAttribute(string description, string group = null, string name = null, string when = null)
        {
            Description = description;
            Group = group;
            Name = name;
            When = when;
        }
    }
}
