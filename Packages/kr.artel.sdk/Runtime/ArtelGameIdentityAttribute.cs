using System;

namespace Artel
{
    /// <summary>field 또는 property가 가리키는 게임 값의 의미를 선언한다. 값이나 getter는 읽지 않는다.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class ArtelGameIdentityAttribute : Attribute
    {
        public string Kind { get; set; }
        /// <summary>ordinal이 세기 시작하는 수(0 또는 1). 생략하면 catalog의 base는 null이다.</summary>
        public int Base { get; set; }
        public string Identifies { get; set; }
        public string Role { get; set; }
        public string Meaning { get; set; }
        public string ChangesWhen { get; set; }
    }
}
