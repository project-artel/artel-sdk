#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    /// <summary>1초 주기 하나. catalog 재전송, <c>when</c> 평가, behaviors 보고가 모두 이 값을 쓴다.</summary>
    internal static class GameFunctionTiming
    {
        public const float PollSeconds = 1f;
    }

    /// <summary>지금 도는 behavior 하나.</summary>
    internal sealed class RunningBehavior
    {
        public int Id;
        public GameFunction Function;
        public Component Target;
        public string TargetSelector;
        public ArtelBehaviorContext Context;
        public IteratorStepper Stepper;

        /// <summary><c>Time.deltaTime</c> 을 더한 초. <c>pause_time</c> 중에는 늘지 않는다.</summary>
        public double Seconds;

        public double MaxSeconds;

        /// <summary><c>WaitForSeconds</c> 가 남긴 게임 시간 초.</summary>
        public double WaitRemaining;

        public float NextConditionCheck;
        public bool WarnedAboutYield;
    }

    /// <summary>
    /// behavior 를 프레임마다 한 걸음씩 돌리고, 끝날 때 이유를 남긴다.
    /// </summary>
    /// <remarks>
    /// <c>ArtelManager.Update</c> 가 프레임마다 <see cref="Tick"/> 을 부른다. 끝난 behavior 의 <c>BEHAVIOR_ENDED</c> 는
    /// 바로 보내지 않고 쌓아 두며, 매니저가 다음 <c>Update</c> 에서 꺼내 보낸다 — <c>start_behavior</c> 의 첫 걸음에서
    /// 끝난 behavior 의 frame 이 그 action 의 <c>ACTION_RESULT</c> 보다 먼저 나가지 않게 하려는 것이다.
    ///
    /// 시간은 <c>Time.deltaTime</c> 으로 잰다. agent 가 <c>pause_time</c> 으로 게임을 세우면 behavior 도, 그 timeout 도
    /// 함께 선다.
    /// </remarks>
    internal sealed class BehaviorRunner
    {
        public const int MaxRunning = 4;
        public const double MaxSecondsLimit = 300;
        public const double DefaultMaxSeconds = 30;

        private readonly List<RunningBehavior> running = new List<RunningBehavior>();
        private readonly Queue<BehaviorEndedMessageDto> ended = new Queue<BehaviorEndedMessageDto>();
        private int nextId = 1;

        public IReadOnlyList<RunningBehavior> Running => running;

        public bool TryDequeueEnded(out BehaviorEndedMessageDto message)
        {
            if (ended.Count == 0)
            {
                message = null;
                return false;
            }

            message = ended.Dequeue();
            return true;
        }

        /// <summary>
        /// 이 behavior 를 시작하면 상한 4 를 넘는지. 같은 group 이라 바뀔 behavior 는 세지 않는다. 넘지 않으면 null.
        /// </summary>
        public string CapRefusal(GameFunction function)
        {
            var staying = running.Where(behavior => !WillBeReplaced(behavior, function)).ToList();
            if (staying.Count < MaxRunning)
            {
                return null;
            }

            return "too many behaviors running: " + string.Join(", ", staying.Select(behavior => behavior.Function.Name));
        }

        /// <summary>
        /// 시작하고 첫 <c>MoveNext()</c> 까지 돌린다. 그 걸음에서 이미 끝났어도 behavior 는 시작한 것이고 id 를 받는다.
        /// </summary>
        public RunningBehavior Start(
            GameFunction function, Component target, string targetSelector, object[] values,
            ArtelBehaviorContext context, double maxSeconds, float unscaledNow, out string error)
        {
            IEnumerator routine;
            try
            {
                routine = function.Call(target, values) as IEnumerator;
            }
            catch (Exception exception)
            {
                error = GameFunctionTargets.Describe(exception);
                return null;
            }

            if (routine == null)
            {
                error = function.Name + " did not return an IEnumerator";
                return null;
            }

            var behavior = new RunningBehavior
            {
                Id = nextId++,
                Function = function,
                Target = target,
                TargetSelector = targetSelector,
                Context = context,
                Stepper = new IteratorStepper(routine),
                MaxSeconds = maxSeconds,
                NextConditionCheck = unscaledNow + GameFunctionTiming.PollSeconds
            };

            foreach (var replaced in running.Where(other => WillBeReplaced(other, function)).ToList())
            {
                End(replaced, BehaviorEndReason.Replaced, "replaced by " + function.Name + " (behaviorId " + behavior.Id + ")");
            }

            running.Add(behavior);
            Step(behavior);
            error = null;
            return behavior;
        }

        public bool TryFind(int behaviorId, out RunningBehavior behavior)
        {
            behavior = running.FirstOrDefault(candidate => candidate.Id == behaviorId);
            return behavior != null;
        }

        public void Stop(RunningBehavior behavior, string reason, string detail)
        {
            if (running.Contains(behavior))
            {
                End(behavior, reason, detail);
            }
        }

        /// <summary>도는 것 전부를 같은 이유로 끝내고 그 id 들을 돌려준다.</summary>
        public List<int> StopAll(string reason, string detail)
        {
            var stopped = new List<int>();
            foreach (var behavior in running.ToList())
            {
                stopped.Add(behavior.Id);
                End(behavior, reason, detail);
            }

            return stopped;
        }

        public void Tick(float deltaTime, float unscaledNow)
        {
            foreach (var behavior in running.ToList())
            {
                if (!running.Contains(behavior))
                {
                    continue;
                }

                TickOne(behavior, deltaTime, unscaledNow);
            }
        }

        private void TickOne(RunningBehavior behavior, float deltaTime, float unscaledNow)
        {
            // Unity 의 == 는 파괴된 객체를 null 로 본다. 씬이 내려가 사라진 대상도 여기서 잡힌다.
            if (!behavior.Function.IsStatic && behavior.Target == null)
            {
                End(behavior, BehaviorEndReason.TargetGone, "target was destroyed");
                return;
            }

            behavior.Seconds += deltaTime;
            if (behavior.Seconds >= behavior.MaxSeconds)
            {
                End(behavior, BehaviorEndReason.Timeout, "maxSeconds " + behavior.MaxSeconds.ToString(CultureInfo.InvariantCulture) + " passed");
                return;
            }

            if (unscaledNow >= behavior.NextConditionCheck)
            {
                behavior.NextConditionCheck = unscaledNow + GameFunctionTiming.PollSeconds;
                var reason = GameFunctionTargets.UnavailableReason(behavior.Function, behavior.Target);
                if (reason != null)
                {
                    End(behavior, BehaviorEndReason.Unavailable, reason);
                    return;
                }
            }

            // Complete 나 Fail 이 MoveNext 밖에서, 게임의 이벤트 handler 같은 곳에서 불렸을 수 있다.
            if (EndIfRequested(behavior))
            {
                return;
            }

            // pause_time 중에는 걸음도 멈춘다. agent 가 멈춘 화면을 보는 동안 behavior 가 게임을 움직이면 멈춘 뜻이 없다.
            if (deltaTime <= 0f)
            {
                return;
            }

            if (behavior.WaitRemaining > 0)
            {
                behavior.WaitRemaining -= deltaTime;
                if (behavior.WaitRemaining > 0)
                {
                    return;
                }
            }

            Step(behavior);
        }

        private void Step(RunningBehavior behavior)
        {
            bool alive;
            try
            {
                alive = behavior.Stepper.MoveNext();
            }
            catch (Exception exception)
            {
                End(behavior, BehaviorEndReason.Failed, GameFunctionTargets.Describe(exception));
                return;
            }

            if (EndIfRequested(behavior))
            {
                return;
            }

            if (!alive)
            {
                End(behavior, BehaviorEndReason.Completed, null);
                return;
            }

            var yielded = behavior.Stepper.Current;
            if (yielded == null)
            {
                return;
            }

            if (IteratorStepper.TryReadWaitSeconds(yielded, out var seconds))
            {
                behavior.WaitRemaining = seconds;
                return;
            }

            if (!behavior.WarnedAboutYield)
            {
                behavior.WarnedAboutYield = true;
                Debug.LogWarning("[Artel] Behavior " + behavior.Function.Name + " yielded " + yielded.GetType().Name +
                                 ". Behaviors understand null and WaitForSeconds; this counts as one frame.");
            }
        }

        private bool EndIfRequested(RunningBehavior behavior)
        {
            switch (behavior.Context.EndRequest)
            {
                case BehaviorEndRequest.Complete:
                    End(behavior, BehaviorEndReason.Completed, behavior.Context.EndReason);
                    return true;
                case BehaviorEndRequest.Fail:
                    End(behavior, BehaviorEndReason.Failed, behavior.Context.EndReason);
                    return true;
                default:
                    return false;
            }
        }

        private void End(RunningBehavior behavior, string reason, string detail)
        {
            // 목록에서 먼저 뺀다. finally 안의 게임 코드가 다시 이 runner 에 닿아도 같은 behavior 를 두 번 끝내지 않는다.
            running.Remove(behavior);
            behavior.Stepper.Dispose();

            ended.Enqueue(new BehaviorEndedMessageDto
            {
                BehaviorId = behavior.Id,
                Name = behavior.Function.Name,
                Target = behavior.TargetSelector,
                Reason = reason,
                Detail = detail,
                Seconds = Math.Round(behavior.Seconds, 1),
                Frame = Time.frameCount
            });
        }

        private static bool WillBeReplaced(RunningBehavior behavior, GameFunction starting) =>
            starting.Group != null && behavior.Function.Group == starting.Group;
    }
}
#endif
