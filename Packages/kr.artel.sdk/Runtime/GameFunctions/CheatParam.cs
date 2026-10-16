using System;
using System.Collections.Generic;

namespace Artel
{
    /// <summary>
    /// <see cref="ArtelCheats.Register"/> 로 등록하는 cheat 의 parameter 하나.
    /// </summary>
    /// <remarks>
    /// <c>[ArtelCheat]</c> 메서드는 이것을 쓰지 않는다. 그쪽은 weaver 가 메서드 signature 에서 같은 정보를 읽는다.
    /// </remarks>
    public sealed class CheatParam
    {
        public string Name { get; }
        public CheatParamType Type { get; }
        public double? Min { get; }
        public double? Max { get; }

        /// <summary>parameter 를 생략할 수 있는지. 생략하면 <see cref="Default"/> 가 쓰인다.</summary>
        public bool IsOptional { get; }

        public object Default { get; }

        /// <summary><see cref="CheatParamType.Enum"/> 일 때 받는 이름들. 그 밖에는 null.</summary>
        public IReadOnlyList<string> Values { get; }

        private CheatParam(
            string name, CheatParamType type, double? min, double? max,
            bool isOptional, object defaultValue, IReadOnlyList<string> values)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A cheat parameter needs a name.", nameof(name));
            }

            Name = name;
            Type = type;
            Min = min;
            Max = max;
            IsOptional = isOptional;
            Default = defaultValue;
            Values = values;
        }

        public static CheatParam Int(string name, int? min = null, int? max = null, int? defaultValue = null)
        {
            return new CheatParam(
                name, CheatParamType.Int, min, max, defaultValue.HasValue, defaultValue, null);
        }

        public static CheatParam Float(string name, float? min = null, float? max = null, float? defaultValue = null)
        {
            return new CheatParam(
                name, CheatParamType.Float, min, max, defaultValue.HasValue, defaultValue, null);
        }

        public static CheatParam Bool(string name, bool? defaultValue = null)
        {
            return new CheatParam(
                name, CheatParamType.Bool, null, null, defaultValue.HasValue, defaultValue, null);
        }

        public static CheatParam String(string name, string defaultValue = null)
        {
            return new CheatParam(
                name, CheatParamType.String, null, null, defaultValue != null, defaultValue, null);
        }

        public static CheatParam Enum(string name, IReadOnlyList<string> values, string defaultValue = null)
        {
            if (values == null || values.Count == 0)
            {
                throw new ArgumentException("An enum cheat parameter needs at least one value.", nameof(values));
            }

            return new CheatParam(
                name, CheatParamType.Enum, null, null, defaultValue != null, defaultValue, values);
        }
    }
}
