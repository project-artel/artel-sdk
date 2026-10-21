using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Artel.Protocol;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// <c>invoke_cheat</c>, <c>start_behavior</c>, <c>stop_behavior</c>, <c>extend_behavior</c> 네 action 과, 그것들이 쓰는
    /// registry 와 behavior runner 를 쥔다.
    /// </summary>
    /// <remarks>
    /// <c>ActionExecutor</c> 는 출시 빌드에도 들어가므로 이 타입도 모든 빌드에 있다. registry 와 runner 는
    /// <c>UNITY_EDITOR || DEVELOPMENT_BUILD</c> 에만 있고, 출시 빌드의 네 action 은 <see cref="NotInThisBuild"/> 로 거절한다
    /// (ADR 0007).
    ///
    /// 실패 문장의 앞부분은 <c>.plan/general/2026-10-14-game-functions-wire-contract.md</c> 3 절이 정한 그대로다. agent 와
    /// orchestration 이 그 앞부분으로 실패의 종류를 가린다.
    /// </remarks>
    internal sealed class GameFunctionActions
    {
        public const string InvokeCheatMethod = "invoke_cheat";
        public const string StartBehaviorMethod = "start_behavior";
        public const string StopBehaviorMethod = "stop_behavior";
        public const string ExtendBehaviorMethod = "extend_behavior";

        /// <summary>
        /// cheat coroutine 이 이보다 오래 돌면 끝낸다. cheat 는 action 큐 안에서 돌므로, 끝나지 않는 cheat 는 그 뒤의 모든
        /// action 을 막는다.
        /// </summary>
        public const float CheatCoroutineLimitSeconds = 60f;

        /// <summary>
        /// 출시 빌드가 네 action 에 돌려주는 실패. 에디터에서도 부를 수 있게 <c>#if</c> 밖에 두어 문장을 테스트한다.
        /// </summary>
        public static ActionResultDto NotInThisBuild(int actionId, string method)
        {
            return ActionResultDto.Failure(
                actionId,
                method,
                method == InvokeCheatMethod
                    ? "cheats are not available in this build"
                    : "behaviors are not available in this build");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly Func<GameFunctionRegistry> registry;

        /// <summary><c>when</c> 을 다시 물을 때를 재는 실제 시간. 테스트가 바꿔 끼운다.</summary>
        private readonly Func<float> clock;

        public GameFunctionActions(Func<GameFunctionRegistry> registry = null, Func<float> clock = null)
        {
            this.registry = registry ?? (() => GameFunctionRegistry.Shared);
            this.clock = clock ?? (() => Time.unscaledTime);
        }

        public BehaviorRunner Runner { get; } = new BehaviorRunner();

        public GameFunctionRegistry Registry => registry();
#endif

        /// <summary>프레임마다 behavior 를 한 걸음씩 민다.</summary>
        public void Tick()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Runner.Tick(Time.deltaTime, clock());
#endif
        }

        /// <summary><c>reset_game</c>, run 종료, 연결 끊김. 도는 behavior 를 모두 <c>run_ended</c> 로 끝낸다.</summary>
        public void EndRun(string detail)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Runner.StopAll(BehaviorEndReason.RunEnded, detail);
#endif
        }

        public bool TryDequeueEnded(out BehaviorEndedMessageDto message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return Runner.TryDequeueEnded(out message);
#else
            message = null;
            return false;
#endif
        }

        public IEnumerator InvokeCheat(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!TryPrepare(GameFunctionKind.Cheat, parameters, null, out var call, out var error))
            {
                completed(Fail(actionId, InvokeCheatMethod, error));
                yield break;
            }

            object result;
            try
            {
                result = call.Function.Call(call.Target, call.Values);
            }
            catch (Exception exception)
            {
                completed(Fail(actionId, InvokeCheatMethod, GameFunctionTargets.Describe(exception)));
                yield break;
            }

            if (!(result is IEnumerator routine))
            {
                completed(Succeed(actionId, InvokeCheatMethod, new CheatInvokedDto { Message = result?.ToString() }));
                yield break;
            }

            // 결과는 상자에 담아 돌려받는다. iterator 안에서는 out 도 ref 도 쓸 수 없다.
            var outcome = new CheatOutcome();
            yield return DriveCheat(call.Function.Name, routine, outcome);
            completed(outcome.Error == null
                ? Succeed(actionId, InvokeCheatMethod, new CheatInvokedDto { Message = outcome.Message })
                : Fail(actionId, InvokeCheatMethod, outcome.Error));
#else
            completed(NotInThisBuild(actionId, InvokeCheatMethod));
            yield break;
#endif
        }

        public ActionResultDto StartBehavior(int actionId, List<object> parameters)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var context = new ArtelBehaviorContext();
            if (!TryPrepare(GameFunctionKind.Behavior, parameters, context, out var call, out var error))
            {
                return Fail(actionId, StartBehaviorMethod, error);
            }

            if (!TryReadMaxSeconds(call.Fields, out var maxSeconds, out error))
            {
                return Fail(actionId, StartBehaviorMethod, error);
            }

            var reason = GameFunctionTargets.UnavailableReason(call.Function, call.Target);
            if (reason != null)
            {
                return Fail(actionId, StartBehaviorMethod, "unavailable: " + reason);
            }

            var refusal = Runner.CapRefusal(call.Function);
            if (refusal != null)
            {
                return Fail(actionId, StartBehaviorMethod, refusal);
            }

            var behavior = Runner.Start(
                call.Function, call.Target, call.Selector, call.Values, context, maxSeconds, clock(), out error);
            if (behavior == null)
            {
                return Fail(actionId, StartBehaviorMethod, error);
            }

            return Succeed(actionId, StartBehaviorMethod, new BehaviorStartedDto
            {
                BehaviorId = behavior.Id,
                Report = context.LatestReport
            });
#else
            return NotInThisBuild(actionId, StartBehaviorMethod);
#endif
        }

        public ActionResultDto StopBehavior(int actionId, List<object> parameters)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!TryReadFields(parameters, out var fields, out var error))
            {
                return Fail(actionId, StopBehaviorMethod, error);
            }

            var reason = BehaviorEndReason.Stopped;
            if (fields.TryGetValue("reason", out var reasonValue) && reasonValue != null)
            {
                reason = reasonValue as string;
                if (reason != BehaviorEndReason.Stopped && reason != BehaviorEndReason.NotAllowed)
                {
                    return Fail(actionId, StopBehaviorMethod,
                        GameFunctionArguments.BadArgument("reason", "must be stopped or not_allowed"));
                }
            }

            if (fields.TryGetValue("all", out var allValue) && allValue is bool all && all)
            {
                return Succeed(actionId, StopBehaviorMethod, new BehaviorsStoppedDto { Stopped = Runner.StopAll(reason, null) });
            }

            if (!TryReadBehaviorId(fields, out var behaviorId, out error))
            {
                return Fail(actionId, StopBehaviorMethod, error);
            }

            if (!Runner.TryFind(behaviorId, out var behavior))
            {
                return Fail(actionId, StopBehaviorMethod, UnknownBehaviorId(behaviorId));
            }

            Runner.Stop(behavior, reason, null);
            return Succeed(actionId, StopBehaviorMethod, new BehaviorsStoppedDto { Stopped = new List<int> { behaviorId } });
#else
            return NotInThisBuild(actionId, StopBehaviorMethod);
#endif
        }

        public ActionResultDto ExtendBehavior(int actionId, List<object> parameters)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!TryReadFields(parameters, out var fields, out var error) ||
                !TryReadBehaviorId(fields, out var behaviorId, out error))
            {
                return Fail(actionId, ExtendBehaviorMethod, error);
            }

            if (!fields.TryGetValue("seconds", out var secondsValue) ||
                !TryReadDouble(secondsValue, out var seconds) || seconds <= 0)
            {
                return Fail(actionId, ExtendBehaviorMethod,
                    GameFunctionArguments.BadArgument("seconds", "must be a positive number"));
            }

            if (!Runner.TryFind(behaviorId, out var behavior))
            {
                return Fail(actionId, ExtendBehaviorMethod, UnknownBehaviorId(behaviorId));
            }

            var extended = behavior.MaxSeconds + seconds;
            if (extended > BehaviorRunner.MaxSecondsLimit)
            {
                return Fail(actionId, ExtendBehaviorMethod, GameFunctionArguments.BadArgument(
                    "seconds",
                    "maxSeconds would be " + extended.ToString(CultureInfo.InvariantCulture) +
                    ", past the 300 second limit"));
            }

            behavior.MaxSeconds = extended;
            return Succeed(actionId, ExtendBehaviorMethod, new BehaviorExtendedDto { MaxSeconds = extended });
#else
            return NotInThisBuild(actionId, ExtendBehaviorMethod);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary><c>GAME_FUNCTION_CATALOG</c> 한 장. 이름 순이다.</summary>
        public GameFunctionCatalogMessageDto BuildCatalog()
        {
            var current = Registry;
            return new GameFunctionCatalogMessageDto
            {
                Revision = current.Revision,
                Cheats = current.Cheats.OrderBy(function => function.Name, StringComparer.Ordinal).Select(ToEntry).ToList(),
                Behaviors = current.Behaviors.OrderBy(function => function.Name, StringComparer.Ordinal).Select(ToEntry).ToList()
            };
        }

        private static GameFunctionEntryDto ToEntry(GameFunction function)
        {
            return new GameFunctionEntryDto
            {
                Name = function.Name,
                Description = function.Description,
                Params = function.Params.Select(ToParam).ToList(),
                Target = function.IsStatic ? null : new GameFunctionTargetDto { Component = function.Component.Name },
                Group = function.Group
            };
        }

        private static GameFunctionParamDto ToParam(GameFunctionParam parameter)
        {
            return new GameFunctionParamDto
            {
                Name = parameter.Name,
                Type = parameter.Type.ToString().ToLowerInvariant(),
                Min = parameter.Min,
                Max = parameter.Max,
                // enum 은 박싱된 enum 값으로 들고 있다. 그대로 쓰면 Newtonsoft 가 숫자로 쓴다.
                Default = parameter.Default is Enum member ? member.ToString() : parameter.Default,
                Values = parameter.EnumValues?.ToList()
            };
        }

        /// <summary>두 action 이 공유하는 앞부분. 이름, 대상, 인자 순으로 검사한다.</summary>
        private bool TryPrepare(
            GameFunctionKind kind, List<object> parameters, ArtelBehaviorContext context,
            out PreparedCall call, out string error)
        {
            call = null;
            if (!TryReadFields(parameters, out var fields, out error))
            {
                return false;
            }

            fields.TryGetValue("name", out var nameValue);
            var name = nameValue as string;
            if (!Registry.TryGet(kind, name, out var function))
            {
                error = "unknown " + GameFunctionRegistry.KindWord(kind) + ": " + name;
                return false;
            }

            fields.TryGetValue("target", out var targetValue);
            if (!GameFunctionTargets.TryResolve(function, targetValue, out var target, out var selector, out error))
            {
                return false;
            }

            fields.TryGetValue("args", out var args);
            if (!GameFunctionArguments.TryRead(function, args, context, out var values, out error))
            {
                return false;
            }

            if (kind == GameFunctionKind.Cheat)
            {
                var reason = GameFunctionTargets.UnavailableReason(function, target);
                if (reason != null)
                {
                    error = "unavailable: " + reason;
                    return false;
                }
            }

            call = new PreparedCall
            {
                Fields = fields,
                Function = function,
                Target = target,
                Selector = selector,
                Values = values
            };
            return true;
        }

        private static IEnumerator DriveCheat(string name, IEnumerator routine, CheatOutcome outcome)
        {
            var deadline = Time.realtimeSinceStartup + CheatCoroutineLimitSeconds;
            using (var stepper = new IteratorStepper(routine))
            {
                while (true)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        outcome.Error = name + " did not finish within " +
                                        CheatCoroutineLimitSeconds.ToString(CultureInfo.InvariantCulture) + " seconds";
                        yield break;
                    }

                    bool alive;
                    try
                    {
                        alive = stepper.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        outcome.Error = GameFunctionTargets.Describe(exception);
                        yield break;
                    }

                    if (!alive)
                    {
                        yield break;
                    }

                    // 마지막으로 낸 문자열이 결과 message 다. Unity 는 문자열을 기다릴 줄 모르므로 한 프레임으로 넘긴다.
                    if (stepper.Current is string message)
                    {
                        outcome.Message = message;
                        yield return null;
                        continue;
                    }

                    // AsyncOperation, WaitForSeconds 같은 Unity 의 yield instruction 은 그대로 Unity 에 맡긴다.
                    yield return stepper.Current;
                }
            }
        }

        private static bool TryReadFields(
            List<object> parameters, out IReadOnlyDictionary<string, object> fields, out string error)
        {
            fields = null;
            error = null;
            if (parameters == null || parameters.Count != 1 || !ActionParamsObject.TryRead(parameters[0], out fields))
            {
                error = GameFunctionArguments.BadArgument("params", "must be [ { ... } ]");
                return false;
            }

            return true;
        }

        private static bool TryReadMaxSeconds(
            IReadOnlyDictionary<string, object> fields, out double maxSeconds, out string error)
        {
            maxSeconds = BehaviorRunner.DefaultMaxSeconds;
            error = null;
            if (!fields.TryGetValue("maxSeconds", out var value) || value == null)
            {
                return true;
            }

            if (TryReadDouble(value, out maxSeconds) && maxSeconds >= 1 && maxSeconds <= BehaviorRunner.MaxSecondsLimit)
            {
                return true;
            }

            error = GameFunctionArguments.BadArgument("maxSeconds", "must be between 1 and 300");
            return false;
        }

        private static bool TryReadBehaviorId(
            IReadOnlyDictionary<string, object> fields, out int behaviorId, out string error)
        {
            behaviorId = 0;
            error = null;
            fields.TryGetValue("behaviorId", out var value);
            if (TryReadDouble(value, out var number) && number == Math.Floor(number) &&
                number >= int.MinValue && number <= int.MaxValue)
            {
                behaviorId = (int)number;
                return true;
            }

            error = GameFunctionArguments.BadArgument("behaviorId", "must be an integer");
            return false;
        }

        private static bool TryReadDouble(object value, out double number)
        {
            switch (value)
            {
                case long longValue:
                    number = longValue;
                    return true;
                case int intValue:
                    number = intValue;
                    return true;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                    number = doubleValue;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        private static string UnknownBehaviorId(int behaviorId) =>
            "unknown behavior id: " + behaviorId.ToString(CultureInfo.InvariantCulture);

        private static ActionResultDto Succeed(int actionId, string method, object returnValue) =>
            ActionResultDto.Success(actionId, method, returnValue);

        private static ActionResultDto Fail(int actionId, string method, string error) =>
            ActionResultDto.Failure(actionId, method, error);

        private sealed class PreparedCall
        {
            public IReadOnlyDictionary<string, object> Fields;
            public GameFunction Function;
            public Component Target;
            public string Selector;
            public object[] Values;
        }

        private sealed class CheatOutcome
        {
            public string Message;
            public string Error;
        }
#endif
    }
}
