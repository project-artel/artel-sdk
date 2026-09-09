using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Artel.Pointer
{
    /// <summary>
    /// <c>move_mouse</c> 의 <c>params</c> 를 세 형태 중 하나로 가른다: <c>[x, y]</c>, <c>[instanceId]</c>,
    /// <c>[selector]</c>. 어느 것과도 안 맞으면 그렇게 말한다.
    /// </summary>
    /// <remarks>
    /// 개수가 먼저 가른다 — 숫자 둘이면 좌표다. 하나짜리를 다시 가르는 것은 타입이고, 숫자면 id, 문자열이면
    /// selector 다. 숫자로도 읽히는 문자열은 id 가 아니라 selector 로 남는다: JSON 이 이미 숫자와 문자열을
    /// 구분해 보내므로, 여기서 그 구분을 무너뜨리면 <c>"12345"</c> 라는 이름의 오브젝트를 겨눌 방법이 없어진다.
    /// </remarks>
    internal static class PointerAimParser
    {
        internal static bool TryParse(List<object> parameters, out IPointerAim aim, out string error)
        {
            aim = null;

            if (parameters != null && parameters.Count == 2 &&
                TryReadNumber(parameters[0], out var x) && TryReadNumber(parameters[1], out var y))
            {
                aim = new ScreenPointAim(new Vector2(x, y));
                error = null;
                return true;
            }

            if (parameters != null && parameters.Count == 1)
            {
                if (TryReadInstanceId(parameters[0], out var instanceId))
                {
                    aim = new InstanceIdAim(instanceId);
                    error = null;
                    return true;
                }

                if (parameters[0] is string selector && !string.IsNullOrEmpty(selector))
                {
                    aim = new SelectorAim(selector);
                    error = null;
                    return true;
                }
            }

            error = "move_mouse requires params [x, y], [instanceId], or [selector].";
            return false;
        }

        private static bool TryReadInstanceId(object value, out int instanceId)
        {
            instanceId = 0;
            switch (value)
            {
                case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                    instanceId = (int)longValue;
                    return true;

                case int intValue:
                    instanceId = intValue;
                    return true;

                // 테스트와 일부 클라이언트가 정수 값을 double 로 실어 보내는 경우를 받아 준다. 소수부가
                // 남아 있는 값은 id 도 좌표도 아니므로 여기서 걸러 selector 쪽으로도 넘어가지 않는다.
                case double doubleValue when doubleValue == Math.Floor(doubleValue) &&
                    !double.IsInfinity(doubleValue) &&
                    doubleValue >= int.MinValue && doubleValue <= int.MaxValue:
                    instanceId = (int)doubleValue;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryReadNumber(object value, out float number)
        {
            number = 0f;
            if (value == null ||
                !float.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return false;
            }

            return !float.IsInfinity(number) && !float.IsNaN(number);
        }
    }
}
