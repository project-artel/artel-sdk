#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Artel.Affordances.Live;
using Newtonsoft.Json;

namespace Artel
{
    /// <summary>
    /// <c>PULSE</c> 에 <c>errors</c> 를 더한다(ARTEL-1218).
    /// </summary>
    /// <remarks>
    /// 직전 <c>pulse</c> 이후 새로 생기거나 count 가 늘어난 묶음만 싣는다. 실을 것이 있으면 <c>news</c> 를 올려, 감시 대상
    /// 값이 하나도 움직이지 않은 frame 에도 예외가 agent 에게 닿게 한다.
    ///
    /// 전량 <c>pulse</c> 에도 전에 보낸 묶음을 다시 싣지 않는다. 예외는 상태가 아니라 일어난 일이라, 다시 실으면 같은 일이
    /// 두 번 일어난 것처럼 읽힌다. 그래서 전달에 실패한 <c>pulse</c> 에 실렸던 묶음은 그 묶음이 다시 찍히기 전까지 다시
    /// 나가지 않는다.
    /// </remarks>
    internal sealed class ErrorPulse : IPulseAttachment
    {
        internal const int MaxItemsPerPulse = 20;

        private readonly LoggedErrors errors;

        public ErrorPulse(LoggedErrors errors)
        {
            this.errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public string Compose(bool whole, out bool news)
        {
            var unsent = errors.TakeUnsent(MaxItemsPerPulse);
            news = unsent.Count > 0;
            return news ? ",\"errors\":" + JsonConvert.SerializeObject(unsent) : null;
        }
    }
}
#endif
