#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Artel.Affordances.Live;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// <c>PULSE</c> 에 <c>functions</c> 와 <c>behaviors</c> 를 더한다.
    /// </summary>
    /// <remarks>
    /// <c>functions</c> 는 <c>(kind, name, target)</c> 키의 집합으로 차이를 낸다. 전량 <c>pulse</c> 에는 전량을, 그 밖에는
    /// 바뀐 항목과 사라진 키(<c>removed: true</c>)만 싣는다. instance 가 하나도 없는 instance 함수는 <c>target: null</c> 인
    /// 항목 하나로 서므로, instance 가 생기면 그 키가 사라진 것으로, 마지막 instance 가 가면 다시 생긴 것으로 나간다.
    ///
    /// <c>when</c> 은 게임 코드라 값이 있다. 1초에 한 번까지만 평가한다(<see cref="GameFunctionTiming.PollSeconds"/>).
    ///
    /// <c>behaviors</c> 는 behavior 가 하나라도 등록돼 있으면 모든 <c>pulse</c> 에 싣는다. 감시 대상 값이 가만히 있으면
    /// <c>pulse</c> 자체가 나가지 않으므로, behavior 가 도는 동안은 1초에 한 번 <c>news</c> 를 올려 진행 초와 report 가
    /// agent 에게 닿게 한다.
    /// </remarks>
    internal sealed class GameFunctionPulse : IPulseAttachment
    {
        private readonly GameFunctionActions actions;
        private readonly Func<float> clock;

        private Dictionary<(string kind, string name, string target), FunctionAvailabilityDto> lastSent =
            new Dictionary<(string, string, string), FunctionAvailabilityDto>();

        private float nextEvaluation;
        private string lastBehaviorsSignature = string.Empty;
        private float nextBehaviorsReport;

        public GameFunctionPulse(GameFunctionActions actions, Func<float> clock = null)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
            this.clock = clock ?? (() => Time.unscaledTime);
        }

        public string Compose(bool whole, out bool news)
        {
            var now = clock();
            var fragment = string.Empty;
            news = false;

            var functions = ComposeFunctions(whole, now);
            if (functions != null)
            {
                fragment += ",\"functions\":" + JsonConvert.SerializeObject(functions);
                news = true;
            }

            if (actions.Registry.HasBehaviors)
            {
                var running = actions.Runner.Running.Select(ToDto).ToList();
                fragment += ",\"behaviors\":" + JsonConvert.SerializeObject(running);
                news |= BehaviorsHaveNews(running, now);
            }

            return fragment.Length == 0 ? null : fragment;
        }

        private FunctionsPulseDto ComposeFunctions(bool whole, float now)
        {
            if (!whole && now < nextEvaluation)
            {
                return null;
            }

            nextEvaluation = now + GameFunctionTiming.PollSeconds;
            var current = Evaluate();
            var previous = lastSent;
            lastSent = current;

            if (whole)
            {
                return new FunctionsPulseDto { Whole = true, Entries = current.Values.Cast<object>().ToList() };
            }

            var entries = new List<object>();
            foreach (var pair in current)
            {
                if (!previous.TryGetValue(pair.Key, out var before) ||
                    before.Available != pair.Value.Available || before.Reason != pair.Value.Reason)
                {
                    entries.Add(pair.Value);
                }
            }

            foreach (var key in previous.Keys)
            {
                if (!current.ContainsKey(key))
                {
                    entries.Add(new FunctionRemovedDto { Kind = key.kind, Name = key.name, Target = key.target });
                }
            }

            return entries.Count == 0 ? null : new FunctionsPulseDto { Whole = false, Entries = entries };
        }

        private Dictionary<(string kind, string name, string target), FunctionAvailabilityDto> Evaluate()
        {
            var result = new Dictionary<(string, string, string), FunctionAvailabilityDto>();
            var liveByComponent = new Dictionary<Type, List<GameFunctionTargets.LiveInstance>>();
            var registry = actions.Registry;

            foreach (var function in registry.Cheats.Concat(registry.Behaviors).OrderBy(function => function.Name, StringComparer.Ordinal))
            {
                var kind = GameFunctionRegistry.KindWord(function.Kind);
                if (function.IsStatic)
                {
                    Add(result, kind, function.Name, null, GameFunctionTargets.UnavailableReason(function, null));
                    continue;
                }

                if (!liveByComponent.TryGetValue(function.Component, out var live))
                {
                    live = GameFunctionTargets.LiveInstances(function.Component);
                    liveByComponent[function.Component] = live;
                }

                if (live.Count == 0)
                {
                    Add(result, kind, function.Name, null, GameFunctionTargets.NoActive(function.Component.Name));
                    continue;
                }

                foreach (var instance in live)
                {
                    Add(result, kind, function.Name, instance.Selector,
                        GameFunctionTargets.UnavailableReason(function, instance.Component));
                }
            }

            return result;
        }

        private static void Add(
            Dictionary<(string, string, string), FunctionAvailabilityDto> result,
            string kind, string name, string target, string reason)
        {
            result[(kind, name, target)] = new FunctionAvailabilityDto
            {
                Kind = kind,
                Name = name,
                Target = target,
                Available = reason == null,
                Reason = reason
            };
        }

        private bool BehaviorsHaveNews(List<RunningBehaviorDto> running, float now)
        {
            var signature = string.Join("|", running.Select(behavior => behavior.BehaviorId + ":" + behavior.Report));
            var changed = signature != lastBehaviorsSignature;
            var due = running.Count > 0 && now >= nextBehaviorsReport;
            lastBehaviorsSignature = signature;
            if (!changed && !due)
            {
                return false;
            }

            nextBehaviorsReport = now + GameFunctionTiming.PollSeconds;
            return true;
        }

        private static RunningBehaviorDto ToDto(RunningBehavior behavior)
        {
            return new RunningBehaviorDto
            {
                BehaviorId = behavior.Id,
                Name = behavior.Function.Name,
                Target = behavior.TargetSelector,
                Seconds = Math.Round(behavior.Seconds, 1),
                MaxSeconds = behavior.MaxSeconds,
                Report = behavior.Context.LatestReport
            };
        }
    }
}
#endif
