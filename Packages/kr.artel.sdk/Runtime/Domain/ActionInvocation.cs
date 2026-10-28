using System;

namespace Artel.Domain
{
    public sealed class ActionInvocation
    {
        public long Sequence { get; }
        public string Tag { get; }
        public string Name { get; }
        public bool Success { get; }
        public object ReturnValue { get; }
        public string ErrorType { get; }
        public string ErrorMessage { get; }
        public DateTimeOffset Timestamp { get; }

        /// <summary>호출이 받은 인자들, 직렬화할 수 있는 모양으로. 인자를 기록하지 않은 옛 위빙은 null.</summary>
        public object[] Arguments { get; }

        public ActionInvocation(
            long sequence,
            string tag,
            string name,
            bool success,
            object returnValue,
            string errorType,
            string errorMessage,
            DateTimeOffset timestamp,
            object[] arguments = null)
        {
            Arguments = arguments;
            Sequence = sequence;
            Tag = tag ?? string.Empty;
            Name = name ?? string.Empty;
            Success = success;
            ReturnValue = returnValue;
            ErrorType = errorType;
            ErrorMessage = errorMessage;
            Timestamp = timestamp;
        }
    }
}
