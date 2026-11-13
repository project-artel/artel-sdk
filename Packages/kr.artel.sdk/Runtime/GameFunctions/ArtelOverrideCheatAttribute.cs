using System;

namespace Artel
{
    /// <summary>Overrides an SDK built-in cheat in Editor and development builds.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ArtelOverrideCheatAttribute : Attribute
    {
        public ArtelOverrideCheatAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }
    }
}
