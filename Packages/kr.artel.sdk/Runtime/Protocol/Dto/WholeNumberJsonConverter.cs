using System;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 소수 부분이 없는 <c>double</c> 을 <c>90.0</c> 이 아니라 <c>90</c> 으로 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>maxSeconds</c> 와 <c>[CheatRange(1, 20)]</c> 의 <c>min</c> / <c>max</c> 는 사람이 정수로 적은 값이다. 계약의 예시도
    /// 정수로 적혀 있고, 받는 쪽이 정수 필드로 읽어도 깨지지 않게 그대로 돌려준다.
    /// </remarks>
    internal sealed class WholeNumberJsonConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) =>
            objectType == typeof(double) || objectType == typeof(double?);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var number = (double)value;
            if (number == Math.Floor(number) && Math.Abs(number) < long.MaxValue)
            {
                writer.WriteValue((long)number);
                return;
            }

            writer.WriteValue(number);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }
    }
}
