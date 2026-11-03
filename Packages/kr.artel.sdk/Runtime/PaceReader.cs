using System;
using System.Collections.Generic;
using System.Globalization;
using Artel.Affordances.Live;
using Artel.Protocol;

namespace Artel
{
    /// <summary><c>start_readings</c> 의 params. <c>[]</c> 이면 기본 속도, <c>[{ interval?, delivery?, minDelivery? }]</c> 이면 그 값.</summary>
    internal static class PaceReader
    {
        public static bool TryRead(List<object> parameters, out Pulse.Pace pace, out string error)
        {
            pace = Pulse.Pace.Default;
            error = null;

            // params 없는 호출은 속도를 받기 전의 서버가 보내는 모양 그대로다.
            if (parameters == null || parameters.Count == 0 || parameters[0] == null)
            {
                return true;
            }

            if (parameters.Count > 1)
            {
                error = "start_readings params are [] or [options].";
                return false;
            }

            if (!ActionParamsObject.TryRead(parameters[0], out var options))
            {
                error = "start_readings options must be an object.";
                return false;
            }

            if (!Seconds(options, "interval", ref pace.Interval, ref error) ||
                !Seconds(options, "delivery", ref pace.Delivery, ref error) ||
                !Seconds(options, "minDelivery", ref pace.MinDelivery, ref error))
            {
                return false;
            }

            if (!pace.Valid)
            {
                error = "start_readings: interval > 0, minDelivery >= 0, delivery >= interval.";
                return false;
            }

            // 모르는 필드는 무시한다. 서버가 필드를 먼저 늘려도 옛 SDK 가 액션 전체를 거절하지 않는다.
            return true;
        }

        private static bool Seconds(
            IReadOnlyDictionary<string, object> options, string key, ref float into, ref string error)
        {
            if (!options.TryGetValue(key, out var value) || value == null)
            {
                return true;
            }

            try
            {
                into = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                error = "start_readings " + key + " must be a number of seconds.";
                return false;
            }
        }
    }
}
