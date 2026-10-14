using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Tests.GameFunctions;
using NUnit.Framework;
using static Artel.Tests.GameFunctions.GameFunctionTestKit;

namespace Artel.Tests.GameFunctions
{
    /// <summary><c>invoke_cheat</c> 의 인자 검사, 실패 문장, coroutine cheat, 런타임 등록.</summary>
    public sealed class CheatInvocationTests
    {
        private GameFunctionRegistry registry;
        private GameFunctionActions actions;

        [SetUp]
        public void SetUp()
        {
            FixtureCheats.Reset();
            registry = LoadWovenFixtures();
            actions = Actions(registry);
        }

        private ActionResultDto GiveGold(object amount) => Invoke(actions, new Dictionary<string, object>
        {
            { "name", "give_gold" },
            { "args", new Dictionary<string, object> { { "amount", amount } } }
        });

        [Test]
        public void ValidArguments_RunTheCheat_AndReturnItsMessage()
        {
            var result = GiveGold(500L);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(result.Action, Is.EqualTo("invoke_cheat"));
            Assert.That(((CheatInvokedDto)result.ReturnValue).Message, Is.EqualTo("gold=500"));
            Assert.That(FixtureCheats.Gold, Is.EqualTo(500));
        }

        [Test]
        public void AnIntegralDouble_IsAcceptedForAnInt()
        {
            Assert.That(GiveGold(7.0).IsSuccess, Is.True);
            Assert.That(FixtureCheats.Gold, Is.EqualTo(7));
        }

        [TestCase("x", "bad argument amount: expected an integer")]
        [TestCase(5.5, "bad argument amount: expected an integer")]
        [TestCase(0L, "bad argument amount: must be between 1 and 1000000")]
        [TestCase(1000001L, "bad argument amount: must be between 1 and 1000000")]
        [TestCase(10000000000L, "bad argument amount: expected an integer")]
        public void BadValues_FailBeforeTheGameRuns(object amount, string expected)
        {
            var result = GiveGold(amount);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.EqualTo(expected));
            Assert.That(FixtureCheats.Gold, Is.EqualTo(0), "the cheat ran with a bad argument");
        }

        [Test]
        public void AMissingRequiredArgument_AndAnUnknownKey_AreNamed()
        {
            var missing = Invoke(actions, new Dictionary<string, object> { { "name", "give_gold" } });
            var unknown = Invoke(actions, new Dictionary<string, object>
            {
                { "name", "give_gold" },
                { "args", new Dictionary<string, object> { { "amount", 1L }, { "bogus", 1L } } }
            });

            Assert.That(missing.Error, Is.EqualTo("bad argument amount: required"));
            Assert.That(unknown.Error, Is.EqualTo("bad argument bogus: no such parameter"));
        }

        [Test]
        public void EveryParameterType_ArrivesAsItsCSharpType()
        {
            var result = Invoke(actions, new Dictionary<string, object>
            {
                { "name", "every_type" },
                {
                    "args", new Dictionary<string, object>
                    {
                        { "count", 3L }, { "speed", 1.5 }, { "flag", true }, { "label", "hi" }, { "spawn", "Boss" }
                    }
                }
            });

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(((CheatInvokedDto)result.ReturnValue).Message, Is.EqualTo("3|1.5|True|hi|Boss"));
        }

        [TestCase("flag", 1L, "bad argument flag: expected true or false")]
        [TestCase("label", 3L, "bad argument label: expected a string")]
        [TestCase("spawn", "boss", "bad argument spawn: expected one of: Start, Boss")]
        [TestCase("speed", "fast", "bad argument speed: expected a number")]
        public void EachTypeRefusesTheWrongShape(string key, object value, string expected)
        {
            var args = new Dictionary<string, object>
            {
                { "count", 3L }, { "speed", 1.5 }, { "flag", true }, { "label", "hi" }, { "spawn", "Boss" }
            };
            args[key] = value;

            var result = Invoke(actions, new Dictionary<string, object> { { "name", "every_type" }, { "args", args } });

            Assert.That(result.Error, Is.EqualTo(expected));
        }

        [Test]
        public void ArgsThatAreNotAnObject_AreRefused()
        {
            var result = Invoke(actions, new Dictionary<string, object> { { "name", "give_gold" }, { "args", "500" } });

            Assert.That(result.Error, Is.EqualTo("bad argument args: must be an object"));
        }

        [Test]
        public void AnUnknownName_IsRefused()
        {
            var result = Invoke(actions, new Dictionary<string, object> { { "name", "nope" } });

            Assert.That(result.Error, Is.EqualTo("unknown cheat: nope"));
        }

        [Test]
        public void AStaticCheat_RefusesATarget()
        {
            var result = Invoke(actions, new Dictionary<string, object>
            {
                { "name", "give_gold" },
                { "target", "Root[0]" },
                { "args", new Dictionary<string, object> { { "amount", 1L } } }
            });

            Assert.That(result.Error, Is.EqualTo("give_gold is static and takes no target"));
        }

        [Test]
        public void AHandlerThatThrows_FailsWithTheExceptionTypeAndMessage()
        {
            var result = Invoke(actions, new Dictionary<string, object> { { "name", "explode" } });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.EqualTo("InvalidOperationException: boom"));
        }

        [Test]
        public void WhenReturningAReason_MakesTheCheatUnavailable()
        {
            FixtureCheats.WalletMissingReason = "no wallet loaded";

            var result = GiveGold(10L);

            Assert.That(result.Error, Is.EqualTo("unavailable: no wallet loaded"));
            Assert.That(FixtureCheats.Gold, Is.EqualTo(0));
        }

        [Test]
        public void ACoroutineCheat_RunsToTheEnd_AndReportsTheLastStringItYielded()
        {
            var result = Invoke(actions, new Dictionary<string, object> { { "name", "travel" } });

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(((CheatInvokedDto)result.ReturnValue).Message, Is.EqualTo("arrived at Boss"),
                "the omitted enum argument takes its C# default");
        }

        [Test]
        public void AReleaseBuild_RefusesAllFourActions()
        {
            Assert.That(GameFunctionActions.NotInThisBuild(1, "invoke_cheat").Error,
                Is.EqualTo("cheats are not available in this build"));
            foreach (var method in new[] { "start_behavior", "stop_behavior", "extend_behavior" })
            {
                var result = GameFunctionActions.NotInThisBuild(1, method);
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Error, Is.EqualTo("behaviors are not available in this build"));
            }
        }

        [Test]
        public void ARuntimeRegisteredCheat_TakesTheSamePath()
        {
            var runtime = new GameFunctionRegistry();
            using (GameFunctionRegistry.Replace(runtime))
            {
                var registered = ArtelCheats.Register(
                    "unlock_stage",
                    "Unlocks a stage.",
                    new[] { CheatParam.Int("stage", 1, 10), CheatParam.Enum("mode", new[] { "easy", "hard" }, "easy") },
                    args => "stage " + args.Int("stage") + " " + args.Enum("mode"));
                var duplicate = ArtelCheats.Register("unlock_stage", "again", null, args => null);

                Assert.That(registered, Is.True);
                Assert.That(duplicate, Is.False);
            }

            var runtimeActions = Actions(runtime);
            var result = Invoke(runtimeActions, new Dictionary<string, object>
            {
                { "name", "unlock_stage" },
                { "args", new Dictionary<string, object> { { "stage", 3L } } }
            });
            var outOfRange = Invoke(runtimeActions, new Dictionary<string, object>
            {
                { "name", "unlock_stage" },
                { "args", new Dictionary<string, object> { { "stage", 11L } } }
            });

            Assert.That(((CheatInvokedDto)result.ReturnValue).Message, Is.EqualTo("stage 3 easy"));
            Assert.That(outOfRange.Error, Is.EqualTo("bad argument stage: must be between 1 and 10"));
        }

        [Test]
        public void Revision_StartsAtOne_AndMovesOnlyOnARealChange()
        {
            var empty = new GameFunctionRegistry();
            Assert.That(empty.Revision, Is.EqualTo(1));

            using (GameFunctionRegistry.Replace(empty))
            {
                ArtelCheats.Register("a_cheat", "a", null, args => null);
                ArtelCheats.Register("a_cheat", "again", null, args => null);
                ArtelCheats.Unregister("never_registered");
                Assert.That(empty.Revision, Is.EqualTo(2));

                ArtelCheats.Unregister("a_cheat");
                Assert.That(empty.Revision, Is.EqualTo(3));
            }
        }
    }
}
