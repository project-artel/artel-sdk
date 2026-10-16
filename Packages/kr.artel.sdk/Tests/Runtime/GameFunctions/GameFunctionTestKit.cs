using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Artel.Protocol.Dto;
using Artel.Tests.GameFunctions;
using NUnit.Framework;

namespace Artel.Tests.GameFunctions
{
    /// <summary>game function 테스트가 같이 쓰는 준비물.</summary>
    internal static class GameFunctionTestKit
    {
        /// <summary>
        /// Unity 의 ILPP 가 fixture 어셈블리에 만든 등록 코드를 새 registry 에 대고 돌린다.
        /// </summary>
        /// <remarks>
        /// 그 코드는 <c>RuntimeInitializeOnLoadMethod</c> 라 에디터 모드 테스트에서는 저절로 돌지 않는다. 그래서 여기서 부르고,
        /// 공유 registry 를 건드리지 않도록 잠시 바꿔 끼운다.
        /// </remarks>
        public static GameFunctionRegistry LoadWovenFixtures()
        {
            var registry = new GameFunctionRegistry();
            var registration = typeof(FixtureCheats).Assembly.GetType("Artel.Generated.GameFunctionRegistration");
            Assert.That(registration, Is.Not.Null, "GameFunctionWeaver did not weave the fixture assembly.");

            using (GameFunctionRegistry.Replace(registry))
            {
                registration.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }

            return registry;
        }

        public static GameFunctionActions Actions(GameFunctionRegistry registry, System.Func<float> clock = null) =>
            new GameFunctionActions(() => registry, clock ?? (() => 0f));

        /// <summary>action 의 params 한 칸. 값은 JSON 이 Newtonsoft 를 거쳐 오는 모양(정수는 long)으로 넣는다.</summary>
        public static List<object> Params(Dictionary<string, object> fields) => new List<object> { fields };

        public static ActionResultDto Invoke(GameFunctionActions actions, Dictionary<string, object> fields)
        {
            ActionResultDto result = null;
            Drain(actions.InvokeCheat(1, Params(fields), completed => result = completed));
            Assert.That(result, Is.Not.Null, "invoke_cheat never completed.");
            return result;
        }

        public static ActionResultDto Start(GameFunctionActions actions, Dictionary<string, object> fields) =>
            actions.StartBehavior(1, Params(fields));

        public static List<BehaviorEndedMessageDto> Ended(GameFunctionActions actions)
        {
            var ended = new List<BehaviorEndedMessageDto>();
            while (actions.TryDequeueEnded(out var message))
            {
                ended.Add(message);
            }

            return ended;
        }

        /// <summary>coroutine 을 Unity 없이 끝까지 민다. 안쪽 <c>IEnumerator</c> 는 따라 들어가고, 그 밖의 yield 는 한 걸음으로 친다.</summary>
        public static void Drain(IEnumerator routine)
        {
            var frames = new Stack<IEnumerator>();
            frames.Push(routine);
            var guard = 0;
            while (frames.Count > 0)
            {
                Assert.That(++guard, Is.LessThan(10000), "coroutine did not finish.");
                var top = frames.Peek();
                if (!top.MoveNext())
                {
                    frames.Pop();
                    continue;
                }

                if (top.Current is IEnumerator nested)
                {
                    frames.Push(nested);
                }
            }
        }
    }
}
