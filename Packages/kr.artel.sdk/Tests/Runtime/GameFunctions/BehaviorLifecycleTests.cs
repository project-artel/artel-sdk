using System.Collections.Generic;
using System.Linq;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using static Artel.Tests.GameFunctions.GameFunctionTestKit;

namespace Artel.Tests.GameFunctions
{
    /// <summary>
    /// 계약의 lifecycle 표를 한 줄씩. runner 를 테스트가 직접 <c>Tick</c> 해 프레임과 시간을 정한다.
    /// </summary>
    public sealed class BehaviorLifecycleTests
    {
        private const float Frame = 0.1f;

        private readonly List<GameObject> created = new List<GameObject>();
        private GameFunctionActions actions;
        private float clock;

        [SetUp]
        public void SetUp()
        {
            FixtureCheats.Reset();
            clock = 0f;
            actions = Actions(LoadWovenFixtures(), () => clock);
        }

        [TearDown]
        public void TearDown()
        {
            actions.EndRun("test teardown");
            foreach (var gameObject in created)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            created.Clear();
        }

        private FixturePlayer AddPlayer()
        {
            var gameObject = new GameObject("Player");
            created.Add(gameObject);
            return gameObject.AddComponent<FixturePlayer>();
        }

        private void Tick(float deltaTime = Frame, int times = 1)
        {
            for (var index = 0; index < times; index++)
            {
                clock += Mathf.Max(deltaTime, Frame);
                actions.Runner.Tick(deltaTime, clock);
            }
        }

        private int StartOk(string name, Dictionary<string, object> args = null, double? maxSeconds = null)
        {
            var fields = new Dictionary<string, object> { { "name", name } };
            if (args != null)
            {
                fields["args"] = args;
            }

            if (maxSeconds.HasValue)
            {
                fields["maxSeconds"] = maxSeconds.Value;
            }

            var result = Start(actions, fields);
            Assert.That(result.IsSuccess, Is.True, result.Error);
            return ((BehaviorStartedDto)result.ReturnValue).BehaviorId;
        }

        private static Dictionary<string, object> Range(double range) =>
            new Dictionary<string, object> { { "range", range } };

        private BehaviorEndedMessageDto SingleEnded()
        {
            var ended = Ended(actions);
            Assert.That(ended, Has.Count.EqualTo(1));
            return ended[0];
        }

        [Test]
        public void Start_RunsTheFirstStep_AndReturnsItsReport()
        {
            var result = Start(actions, new Dictionary<string, object> { { "name", "count" } });

            var started = (BehaviorStartedDto)result.ReturnValue;
            Assert.That(started.BehaviorId, Is.EqualTo(1));
            Assert.That(started.Report, Is.EqualTo("frames=1"));

            Tick();
            Assert.That(actions.Runner.Running[0].Context.LatestReport, Is.EqualTo("frames=2"));
        }

        [Test]
        public void Stopped_RunsFinally()
        {
            var id = StartOk("count");

            var result = actions.StopBehavior(2, Params(new Dictionary<string, object> { { "behaviorId", (long)id } }));

            Assert.That(((BehaviorsStoppedDto)result.ReturnValue).Stopped, Is.EqualTo(new[] { id }));
            var ended = SingleEnded();
            Assert.That(ended.Reason, Is.EqualTo("stopped"));
            Assert.That(ended.Target, Is.Null);
            Assert.That(ended.Detail, Is.Null);
            Assert.That(FixtureCheats.CountFinallyRuns, Is.EqualTo(1));
        }

        [Test]
        public void NotAllowed_IsTheStopReasonPassedThrough_AndAllStopsEverything()
        {
            StartOk("count");
            StartOk("count");

            var result = actions.StopBehavior(2, Params(new Dictionary<string, object>
            {
                { "all", true }, { "reason", "not_allowed" }
            }));

            Assert.That(((BehaviorsStoppedDto)result.ReturnValue).Stopped, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Ended(actions).Select(ended => ended.Reason), Is.EqualTo(new[] { "not_allowed", "not_allowed" }));

            var none = actions.StopBehavior(3, Params(new Dictionary<string, object> { { "all", true } }));
            Assert.That(((BehaviorsStoppedDto)none.ReturnValue).Stopped, Is.Empty);
        }

        [Test]
        public void StopRefusesAnUnknownIdAndAnUnknownReason()
        {
            var unknown = actions.StopBehavior(1, Params(new Dictionary<string, object> { { "behaviorId", 42L } }));
            var badReason = actions.StopBehavior(1, Params(new Dictionary<string, object>
            {
                { "behaviorId", 1L }, { "reason", "timeout" }
            }));

            Assert.That(unknown.Error, Is.EqualTo("unknown behavior id: 42"));
            Assert.That(badReason.Error, Is.EqualTo("bad argument reason: must be stopped or not_allowed"));
        }

        [Test]
        public void Completed_ByContextComplete_CarriesTheReason()
        {
            AddPlayer();
            StartOk("strike", new Dictionary<string, object> { { "frames", 2L } });

            Tick(times: 2);

            var ended = SingleEnded();
            Assert.That(ended.Reason, Is.EqualTo("completed"));
            Assert.That(ended.Detail, Is.EqualTo("struck 2 times"));
            Assert.That(ended.Target, Is.Not.Null);
        }

        [Test]
        public void Completed_ByReturning_AfterWaitForSecondsOfGameTime()
        {
            StartOk("linger", new Dictionary<string, object> { { "seconds", 1.0 } });

            Tick(0.4f);
            Assert.That(Ended(actions), Is.Empty);

            Tick(0.4f, times: 2);

            var ended = SingleEnded();
            Assert.That(ended.Reason, Is.EqualTo("completed"));
            Assert.That(ended.Detail, Is.Null);
        }

        [Test]
        public void Failed_WhenTheIteratorThrows()
        {
            AddPlayer();
            StartOk("stumble");

            Tick();

            var ended = SingleEnded();
            Assert.That(ended.Reason, Is.EqualTo("failed"));
            Assert.That(ended.Detail, Is.EqualTo("InvalidOperationException: tripped"));
        }

        [Test]
        public void Timeout_AfterMaxSecondsOfGameTime_RunsFinally()
        {
            StartOk("count", maxSeconds: 1);

            Tick(0.6f);
            Assert.That(Ended(actions), Is.Empty);
            Tick(0.6f);

            var ended = SingleEnded();
            Assert.That(ended.Reason, Is.EqualTo("timeout"));
            Assert.That(ended.Seconds, Is.EqualTo(1.2).Within(0.001));
            Assert.That(FixtureCheats.CountFinallyRuns, Is.EqualTo(1));
        }

        [Test]
        public void PausedGameTime_FreezesTheBehaviorAndItsTimeout()
        {
            StartOk("count", maxSeconds: 1);

            Tick(0f, times: 50);

            Assert.That(Ended(actions), Is.Empty);
            Assert.That(actions.Runner.Running[0].Seconds, Is.EqualTo(0));
            Assert.That(actions.Runner.Running[0].Context.LatestReport, Is.EqualTo("frames=1"),
                "a paused game does not step its behaviors");
        }

        [Test]
        public void Replaced_BySameGroup_RunsFinally_AndLeavesOtherGroupsAlone()
        {
            var player = AddPlayer();
            var attack = StartOk("auto_attack", Range(8));
            var counter = StartOk("count");

            var strike = StartOk("strike", new Dictionary<string, object> { { "frames", 10L } });

            var ended = SingleEnded();
            Assert.That(ended.BehaviorId, Is.EqualTo(attack));
            Assert.That(ended.Reason, Is.EqualTo("replaced"));
            Assert.That(ended.Detail, Is.EqualTo("replaced by strike (behaviorId " + strike + ")"));
            Assert.That(player.FinallyRuns, Is.EqualTo(1));
            Assert.That(actions.Runner.Running.Select(behavior => behavior.Id), Is.EquivalentTo(new[] { counter, strike }));
        }

        [Test]
        public void TargetGone_WhenTheInstanceIsDestroyed()
        {
            var player = AddPlayer();
            StartOk("auto_attack", Range(8));

            Object.DestroyImmediate(player.gameObject);
            Tick();

            Assert.That(SingleEnded().Reason, Is.EqualTo("target_gone"));
        }

        [Test]
        public void Unavailable_WhenItsWhenTurnsFalse_CheckedOncePerSecond()
        {
            var player = AddPlayer();
            StartOk("auto_attack", Range(8));
            player.InBattle = false;

            actions.Runner.Tick(Frame, 0.5f);
            Assert.That(Ended(actions), Is.Empty, "when is checked at most once per second");

            actions.Runner.Tick(Frame, 1.1f);

            var ended = SingleEnded();
            Assert.That(ended.Reason, Is.EqualTo("unavailable"));
            Assert.That(ended.Detail, Is.EqualTo("CanFight returned false"));
            Assert.That(player.FinallyRuns, Is.EqualTo(1));
        }

        [Test]
        public void UnavailableAtStart_IsRefused()
        {
            AddPlayer().InBattle = false;

            var result = Start(actions, new Dictionary<string, object> { { "name", "auto_attack" }, { "args", Range(8) } });

            Assert.That(result.Error, Is.EqualTo("unavailable: CanFight returned false"));
        }

        [Test]
        public void RunEnded_StopsEverything()
        {
            StartOk("count");

            actions.EndRun("reset_game");

            var ended = SingleEnded();
            Assert.That(ended.Reason, Is.EqualTo("run_ended"));
            Assert.That(ended.Detail, Is.EqualTo("reset_game"));
            Assert.That(actions.Runner.Running, Is.Empty);
        }

        [Test]
        public void AFifthBehavior_IsRefusedWithTheRunningNames()
        {
            for (var index = 0; index < 4; index++)
            {
                StartOk("count");
            }

            var result = Start(actions, new Dictionary<string, object> { { "name", "count" } });

            Assert.That(result.Error, Is.EqualTo("too many behaviors running: count, count, count, count"));
        }

        [Test]
        public void ABehaviorThatReplacesAnother_DoesNotCountAgainstTheCap()
        {
            AddPlayer();
            StartOk("count");
            StartOk("count");
            StartOk("count");
            StartOk("auto_attack", Range(8));

            var result = Start(actions, new Dictionary<string, object>
            {
                { "name", "strike" }, { "args", new Dictionary<string, object> { { "frames", 3L } } }
            });

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(actions.Runner.Running, Has.Count.EqualTo(4));
        }

        [TestCase(0.5)]
        [TestCase(301.0)]
        public void MaxSecondsOutsideOneTo300_IsRefused(double maxSeconds)
        {
            var result = Start(actions, new Dictionary<string, object> { { "name", "count" }, { "maxSeconds", maxSeconds } });

            Assert.That(result.Error, Is.EqualTo("bad argument maxSeconds: must be between 1 and 300"));
        }

        [Test]
        public void Extend_AddsSeconds_UpTo300FromStart()
        {
            var id = StartOk("count");

            var extended = actions.ExtendBehavior(2, Params(new Dictionary<string, object>
            {
                { "behaviorId", (long)id }, { "seconds", 60L }
            }));
            var tooFar = actions.ExtendBehavior(3, Params(new Dictionary<string, object>
            {
                { "behaviorId", (long)id }, { "seconds", 211L }
            }));
            var unknown = actions.ExtendBehavior(4, Params(new Dictionary<string, object>
            {
                { "behaviorId", 99L }, { "seconds", 1L }
            }));

            Assert.That(((BehaviorExtendedDto)extended.ReturnValue).MaxSeconds, Is.EqualTo(90));
            Assert.That(tooFar.IsSuccess, Is.False);
            Assert.That(tooFar.Error, Does.StartWith("bad argument seconds: "));
            Assert.That(unknown.Error, Is.EqualTo("unknown behavior id: 99"));
        }

        [Test]
        public void AnUnknownBehavior_IsRefused()
        {
            Assert.That(Start(actions, new Dictionary<string, object> { { "name", "dance" } }).Error,
                Is.EqualTo("unknown behavior: dance"));
        }
    }
}
