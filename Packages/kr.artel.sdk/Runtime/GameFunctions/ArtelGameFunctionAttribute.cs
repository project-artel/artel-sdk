using System;

namespace Artel
{
    public enum ArtelGameFunctionMode
    {
        ReadOnly,
        Setup,
        Behavior
    }

    /// <summary>QA에서 호출할 읽기 전용 조회, 사전조건 준비 또는 지속 동작을 등록한다.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ArtelGameFunctionAttribute : Attribute
    {
        public string Description { get; }
        public ArtelGameFunctionMode Mode { get; }
        public string Name { get; }
        public string When { get; }
        public string Group { get; }

        public ArtelGameFunctionAttribute(
            string description, ArtelGameFunctionMode mode, string name = null,
            string when = null, string group = null)
        {
            Description = description;
            Mode = mode;
            Name = name;
            When = when;
            Group = group;
        }
    }
}
