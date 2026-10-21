using System;

namespace Artel
{
    /// <summary>
    /// QA agent 가 <c>invoke_cheat</c> 로 부를 수 있는 cheat 로 메서드를 등록한다.
    /// </summary>
    /// <remarks>
    /// 등록은 빌드 때 일어난다. <c>Editor/CodeGen/GameFunctionWeaver</c> 가 이 attribute 가 붙은 메서드를 찾아
    /// 그 메서드를 직접 부르는 등록 코드를 만들고, 런타임에는 reflection 으로 어셈블리를 훑지 않는다. 그 weaver 는
    /// 에디터와 <c>DEVELOPMENT_BUILD</c> 에서만 돌므로 출시 빌드에서 이 attribute 는 아무 일도 하지 않는다.
    ///
    /// 받을 수 있는 parameter 는 <c>int</c>, <c>float</c>, <c>bool</c>, <c>string</c>, enum 이고, 반환은 <c>void</c>,
    /// <c>string</c>(결과 <c>message</c> 가 된다), <c>IEnumerator</c>(씬 로드처럼 기다리는 cheat) 중 하나다. 그 밖의 모양은
    /// weaver 가 컴파일 오류로 막는다.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ArtelCheatAttribute : Attribute
    {
        /// <summary>agent 가 catalog 에서 읽는 한 줄 설명.</summary>
        public string Description { get; }

        /// <summary>catalog 이름. null 이면 메서드 이름의 snake_case(<c>GiveGold</c> → <c>give_gold</c>).</summary>
        public string Name { get; }

        /// <summary>
        /// 지금 쓸 수 있는지 답하는, 같은 타입의 parameter 없는 메서드 이름. <c>bool</c> 이나 <c>string</c> 을 돌려준다.
        /// </summary>
        public string When { get; }

        public ArtelCheatAttribute(string description, string name = null, string when = null)
        {
            Description = description;
            Name = name;
            When = when;
        }
    }
}
