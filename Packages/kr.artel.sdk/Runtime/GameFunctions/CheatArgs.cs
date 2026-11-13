using System;
using System.Collections.Generic;

namespace Artel
{
    /// <summary>
    /// <see cref="ArtelCheats.Register"/> handler 가 받는 인자. SDK 가 이미 타입과 범위를 검사한 값이다.
    /// </summary>
    public sealed class CheatArgs
    {
        private readonly IReadOnlyList<CheatParam> parameters;
        private readonly object[] values;

        internal CheatArgs(IReadOnlyList<CheatParam> parameters, object[] values)
        {
            this.parameters = parameters;
            this.values = values;
        }

        public int Int(string name) => (int)Value(name);

        public float Float(string name) => (float)Value(name);

        public bool Bool(string name) => (bool)Value(name);

        public string String(string name) => (string)Value(name);

        /// <summary>enum parameter 의 값. 등록할 때 준 이름 중 하나다.</summary>
        public string Enum(string name) => (string)Value(name);

        private object Value(string name)
        {
            for (var index = 0; index < parameters.Count; index++)
            {
                if (parameters[index].Name == name)
                {
                    return values[index];
                }
            }

            throw new ArgumentException("This cheat has no parameter named " + name + ".", nameof(name));
        }
    }
}
