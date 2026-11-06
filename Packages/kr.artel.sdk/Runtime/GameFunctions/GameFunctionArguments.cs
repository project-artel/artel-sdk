#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using Artel.Protocol;

namespace Artel
{
    /// <summary>
    /// action 의 <c>args</c> 오브젝트를 메서드가 받는 타입의 <c>object[]</c> 로 바꾼다. 실패는
    /// <c>bad argument &lt;param&gt;: &lt;why&gt;</c> 다.
    /// </summary>
    /// <remarks>
    /// 게임 코드가 불리기 전에 모든 것을 검사한다. 범위를 벗어난 값을 게임이 받으면 무엇을 할지 SDK 가 알 수 없고, 그때
    /// 나는 예외는 agent 에게 인자 문제가 아니라 게임 문제로 보인다.
    ///
    /// 변환 규칙은 관대하지 않다. <c>bool</c> 자리에 숫자 1 이나 문자열 <c>"true"</c> 를 받지 않는다 — 모델이 보낸 모양이
    /// 틀렸다는 것을 그 자리에서 말해야 다음 호출이 고쳐진다. 예외는 하나, <c>int</c> 자리의 <c>5.0</c> 이다. JSON 은
    /// 정수와 소수를 구분하지 않는 경로를 지나올 수 있다.
    /// </remarks>
    internal static class GameFunctionArguments
    {
        public static bool TryRead(
            GameFunction function, object args, ArtelBehaviorContext context,
            out object[] values, out string error)
        {
            values = new object[function.Params.Count + (function.TakesContext ? 1 : 0)];
            error = null;

            IReadOnlyDictionary<string, object> fields = new Dictionary<string, object>();
            if (args != null && !ActionParamsObject.TryRead(args, out fields))
            {
                error = BadArgument("args", "must be an object");
                return false;
            }

            foreach (var key in fields.Keys)
            {
                if (!function.Params.Exists(parameter => parameter.Name == key))
                {
                    error = BadArgument(key, "no such parameter");
                    return false;
                }
            }

            for (var index = 0; index < function.Params.Count; index++)
            {
                var parameter = function.Params[index];
                if (!fields.TryGetValue(parameter.Name, out var raw) || raw == null)
                {
                    if (!parameter.IsOptional)
                    {
                        error = BadArgument(parameter.Name, "required");
                        return false;
                    }

                    values[index] = parameter.Default;
                    continue;
                }

                if (!TryConvert(parameter, raw, out values[index], out var why))
                {
                    error = BadArgument(parameter.Name, why);
                    return false;
                }
            }

            if (function.TakesContext)
            {
                values[values.Length - 1] = context;
            }

            return true;
        }

        public static string BadArgument(string name, string why) => "bad argument " + name + ": " + why;

        private static bool TryConvert(GameFunctionParam parameter, object raw, out object value, out string why)
        {
            value = null;
            why = null;
            switch (parameter.Type)
            {
                case CheatParamType.Int:
                    if (!TryReadInteger(raw, out var integer))
                    {
                        why = "expected an integer";
                        return false;
                    }

                    if (!InRange(parameter, integer, out why))
                    {
                        return false;
                    }

                    value = integer;
                    return true;

                case CheatParamType.Float:
                    if (!TryReadNumber(raw, out var number) || double.IsNaN(number) || double.IsInfinity(number))
                    {
                        why = "expected a number";
                        return false;
                    }

                    if (!InRange(parameter, number, out why))
                    {
                        return false;
                    }

                    value = (float)number;
                    return true;

                case CheatParamType.Bool:
                    if (!(raw is bool flag))
                    {
                        why = "expected true or false";
                        return false;
                    }

                    value = flag;
                    return true;

                case CheatParamType.String:
                    if (!(raw is string text))
                    {
                        why = "expected a string";
                        return false;
                    }

                    value = text;
                    return true;

                default:
                    return TryReadEnum(parameter, raw, out value, out why);
            }
        }

        private static bool TryReadEnum(GameFunctionParam parameter, object raw, out object value, out string why)
        {
            value = null;
            var member = raw as string;
            var names = parameter.EnumValues ?? Array.Empty<string>();
            foreach (var name in names)
            {
                if (name != member)
                {
                    continue;
                }

                value = parameter.EnumType == null ? (object)name : Enum.Parse(parameter.EnumType, name);
                why = null;
                return true;
            }

            why = "expected one of: " + string.Join(", ", names);
            return false;
        }

        private static bool TryReadInteger(object raw, out int integer)
        {
            integer = 0;
            switch (raw)
            {
                case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                    integer = (int)longValue;
                    return true;
                case int intValue:
                    integer = intValue;
                    return true;
                case double doubleValue when doubleValue == Math.Floor(doubleValue) &&
                                             doubleValue >= int.MinValue && doubleValue <= int.MaxValue:
                    integer = (int)doubleValue;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadNumber(object raw, out double number)
        {
            switch (raw)
            {
                case long longValue:
                    number = longValue;
                    return true;
                case int intValue:
                    number = intValue;
                    return true;
                case double doubleValue:
                    number = doubleValue;
                    return true;
                case float floatValue:
                    number = floatValue;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        private static bool InRange(GameFunctionParam parameter, double number, out string why)
        {
            if ((parameter.Min.HasValue && number < parameter.Min.Value) ||
                (parameter.Max.HasValue && number > parameter.Max.Value))
            {
                why = "must be between " + Format(parameter.Min) + " and " + Format(parameter.Max);
                return false;
            }

            why = null;
            return true;
        }

        private static string Format(double? bound) =>
            bound.HasValue ? bound.Value.ToString(CultureInfo.InvariantCulture) : "any";
    }
}
#endif
