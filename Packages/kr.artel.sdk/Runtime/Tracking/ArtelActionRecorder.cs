using System;
using System.ComponentModel;
using System.Threading;

namespace Artel.Tracking
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ArtelActionRecorder
    {
        public static ActionInvocationBuffer GetOrCreate(ref ActionInvocationBuffer buffer)
        {
            var current = buffer;
            if (current != null)
            {
                return current;
            }

            var created = new ActionInvocationBuffer();
            return Interlocked.CompareExchange(ref buffer, created, null) ?? created;
        }

        public static void RecordSuccess(IArtelActionSource source, string tag, string methodName, object returnValue)
        {
            source.ArtelActionBuffer.Record(tag, methodName, true, returnValue, null, null);
        }

        public static void RecordFailure(IArtelActionSource source, string tag, string methodName, Exception exception)
        {
            source.ArtelActionBuffer.Record(
                tag, methodName, false, null, exception.GetType().FullName, exception.Message);
        }

        // 인자를 받는 둘(ARTEL-1283). 위의 둘은 옛 SDK 로 위빙된 어셈블리가 부르는 모양이라 남겨 둔다.

        public static void RecordSuccess(
            IArtelActionSource source, string tag, string methodName, object[] arguments, object returnValue)
        {
            source.ArtelActionBuffer.Record(tag, methodName, true, returnValue, null, null, Lowered(arguments));
        }

        public static void RecordFailure(
            IArtelActionSource source, string tag, string methodName, object[] arguments, Exception exception)
        {
            source.ArtelActionBuffer.Record(
                tag, methodName, false, null, exception.GetType().FullName, exception.Message, Lowered(arguments));
        }

        /// <summary>JSON 으로 쓸 수 있는 모양으로. 직렬화 못 하는 인자는 타입 이름만.</summary>
        private static object[] Lowered(object[] arguments)
        {
            if (arguments == null)
            {
                return null;
            }

            var lowered = new object[arguments.Length];

            for (var at = 0; at < arguments.Length; at++)
            {
                lowered[at] = EngineEvents.Lower(arguments[at]);
            }

            return lowered;
        }
    }
}
