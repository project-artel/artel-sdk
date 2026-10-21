#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// cheat coroutine 과 behavior 를 Unity scheduler 없이 한 걸음씩 민다.
    /// </summary>
    /// <remarks>
    /// <c>StartCoroutine</c> 에 맡기지 않는 이유는 둘이다. <c>StopCoroutine</c> 은 iterator 의 <c>finally</c> 를 돌리지
    /// 않지만 <c>Dispose()</c> 는 돌린다. 그리고 Unity 가 돌리는 coroutine 안의 예외는 그 coroutine 을 멈추는데, cheat 는
    /// <c>ArtelManager.ProcessActions</c> 의 coroutine 안에서 돌므로 그 예외가 action 큐 전체를 멈춘다.
    ///
    /// <c>yield return</c> 이 다른 <c>IEnumerator</c> 를 내면 그것을 안쪽 frame 으로 쌓아 같은 걸음 안에서 바로 민다.
    /// Unity 의 중첩 coroutine 과 같은 순서다. <c>CustomYieldInstruction</c> 도 <c>IEnumerator</c> 이므로 같은 길로 간다.
    /// </remarks>
    internal sealed class IteratorStepper : IDisposable
    {
        private static readonly FieldInfo WaitForSecondsField =
            typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Stack<IEnumerator> frames = new Stack<IEnumerator>();

        public IteratorStepper(IEnumerator root)
        {
            frames.Push(root ?? throw new ArgumentNullException(nameof(root)));
        }

        /// <summary>안쪽 frame 이 아닌 값을 낸 마지막 <c>yield return</c>.</summary>
        public object Current { get; private set; }

        /// <summary>
        /// 다음 <c>yield return</c> 까지 민다. 바깥 iterator 가 끝났으면 false. 게임 코드의 예외는 그대로 던진다.
        /// </summary>
        public bool MoveNext()
        {
            while (frames.Count > 0)
            {
                var top = frames.Peek();
                if (!top.MoveNext())
                {
                    DisposeFrame(frames.Pop());
                    continue;
                }

                if (top.Current is IEnumerator nested)
                {
                    frames.Push(nested);
                    continue;
                }

                Current = top.Current;
                return true;
            }

            Current = null;
            return false;
        }

        /// <summary>안쪽부터 모든 frame 을 Dispose 한다. 각 iterator 의 <c>finally</c> 가 여기서 돈다.</summary>
        public void Dispose()
        {
            while (frames.Count > 0)
            {
                DisposeFrame(frames.Pop());
            }
        }

        /// <summary>
        /// <c>WaitForSeconds</c> 가 기다리라는 초. Unity 가 그 값을 공개하지 않아 private field 를 읽는다.
        /// </summary>
        /// <remarks>
        /// field 를 찾지 못하는 Unity 버전이면 false 이고, 부르는 쪽은 한 프레임으로 친다.
        /// </remarks>
        public static bool TryReadWaitSeconds(object yielded, out float seconds)
        {
            seconds = 0f;
            if (!(yielded is WaitForSeconds) || WaitForSecondsField == null)
            {
                return false;
            }

            seconds = (float)WaitForSecondsField.GetValue(yielded);
            return true;
        }

        // finally 안의 예외는 끝나는 이유를 바꾸지 않는다. 이미 정해진 이유를 덮으면 agent 는 왜 끝났는지를 잃는다.
        private static void DisposeFrame(IEnumerator frame)
        {
            if (!(frame is IDisposable disposable))
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Artel] A game function's finally block threw: " +
                                 GameFunctionTargets.Describe(exception));
            }
        }
    }
}
#endif
