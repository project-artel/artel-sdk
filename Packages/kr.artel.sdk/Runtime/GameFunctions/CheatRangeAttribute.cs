using System;

namespace Artel
{
    /// <summary>
    /// cheat 와 behavior parameter 가 받는 값의 범위. catalog 의 <c>min</c> / <c>max</c> 가 되고, 범위를 벗어난 값은
    /// 게임 코드가 불리기 전에 <c>bad argument</c> 로 거절된다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class CheatRangeAttribute : Attribute
    {
        public double Min { get; }
        public double Max { get; }

        public CheatRangeAttribute(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }
}
