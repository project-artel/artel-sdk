using System;
using System.ComponentModel;
using System.Globalization;
using UnityEngine;

namespace Artel
{
    /// <summary>weaver 가 만든 호출 stub 의 모양. 인자는 이미 메서드가 받는 타입으로 박싱돼 있다.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public delegate object GameFunctionCall(object target, object[] arguments);

    /// <summary>weaver 가 만든 <c>when</c> stub 의 모양. <c>bool</c> 이나 <c>string</c> 을 돌려준다.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public delegate object GameFunctionCondition(object target);

    /// <summary>weaver 가 만든 등록 코드가 한 함수를 채우는 동안 쥐는 것.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class GameFunctionDraft
    {
        internal readonly GameFunction Function;

        internal GameFunctionDraft(GameFunction function)
        {
            Function = function;
        }
    }

    /// <summary>
    /// weaver 가 만든 <c>Artel.Generated.GameFunctionRegistration.Register</c> 가 부르는 입구. 게임 코드가 쓰는 API 가 아니다.
    /// </summary>
    /// <remarks>
    /// 게임 어셈블리에서 불리므로 public 이어야 한다. 배열을 IL 로 만드는 대신 함수 하나를 <see cref="Begin"/>,
    /// parameter 마다 <see cref="Param"/>, <see cref="End"/> 세 번의 호출로 나눈 것은, 그 편이 weaver 가 내는 IL 이
    /// 짧고 읽기 쉬워서다.
    ///
    /// 기본값은 weaver 가 invariant culture 문자열로 넘기고 여기서 타입으로 되돌린다. 박싱을 IL 로 타입마다 쓰는
    /// 것보다 단순하다. enum 기본값은 멤버 이름이다.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class GameFunctionTable
    {
        public static GameFunctionDraft Begin(
            GameFunctionKind kind, string name, string description, string group, Type component,
            GameFunctionCall call, GameFunctionCondition condition, string conditionName, bool takesContext)
        {
            return new GameFunctionDraft(new GameFunction
            {
                Kind = kind,
                Name = name,
                Description = description,
                Group = group,
                Component = component,
                Call = call,
                Condition = condition,
                ConditionName = conditionName,
                TakesContext = takesContext
            });
        }

        public static void Param(
            GameFunctionDraft draft, string name, CheatParamType type, Type enumType,
            bool hasRange, double min, double max, bool isOptional, string defaultText)
        {
            draft.Function.Params.Add(new GameFunctionParam
            {
                Name = name,
                Type = type,
                EnumType = enumType,
                EnumValues = enumType == null ? null : Enum.GetNames(enumType),
                Min = hasRange ? min : (double?)null,
                Max = hasRange ? max : (double?)null,
                IsOptional = isOptional,
                Default = isOptional ? ParseDefault(type, enumType, defaultText) : null
            });
        }

        public static void End(GameFunctionDraft draft)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!GameFunctionRegistry.Shared.TryAdd(draft.Function, out var error))
            {
                Debug.LogWarning("[Artel] " + error);
            }
#endif
        }

        private static object ParseDefault(CheatParamType type, Type enumType, string text)
        {
            if (text == null)
            {
                return null;
            }

            switch (type)
            {
                case CheatParamType.Int:
                    return int.Parse(text, CultureInfo.InvariantCulture);
                case CheatParamType.Float:
                    return float.Parse(text, CultureInfo.InvariantCulture);
                case CheatParamType.Bool:
                    return bool.Parse(text);
                case CheatParamType.Enum:
                    return enumType == null ? text : Enum.Parse(enumType, text);
                default:
                    return text;
            }
        }
    }
}
