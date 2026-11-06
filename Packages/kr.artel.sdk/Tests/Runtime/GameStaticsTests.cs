using System;
using System.Collections.Generic;
using System.Linq;
using GameStaticsFixtures;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// reset_game 이 씬을 다시 열 때 게임 코드의 static 은 게임을 막 켠 값이어야 한다. 그렇지 않으면 static 에 남은 옛 씬의 참조가
    /// 새 씬에서 예외를 낸다 — 씬을 다시 여는 일이 없는 게임에서는 플레이어가 만날 수 없는 상태다.
    /// </summary>
    public sealed class GameStaticsTests
    {
        [SetUp]
        public void SetUp()
        {
            ExplosionSwitch.On = false;
        }

        /// <summary>리스너 사전 · 카운터 · 이벤트 · auto-property 가 모두 선언한 초기값으로 돌아온다.</summary>
        [Test]
        public void ResetGivesBackTheValuesTheGameStartedWith()
        {
            var before = ListenerState.Listeners;
            ListenerState.Listeners[1] = () => { };
            ListenerState.Counter = 9;
            ListenerState.Changed += () => { };
            ListenerState.Name = "changed";

            var outcome = GameStatics.Reset(new[] { typeof(ListenerState) });

            Assert.That(ListenerState.Listeners, Is.Empty);
            Assert.That(ListenerState.Listeners, Is.Not.SameAs(before));
            Assert.That(ListenerState.Counter, Is.EqualTo(3));
            Assert.That(ListenerState.HasChangedListeners, Is.False);
            Assert.That(ListenerState.Name, Is.EqualTo("start"));
            Assert.That(outcome.Types, Is.EqualTo(1));
            Assert.That(outcome.Failed, Is.Empty);
        }

        /// <summary>
        /// 제네릭 타입은 닫힌 형태마다 static 을 따로 갖는데 어셈블리에는 열린 정의만 있다. 게임 코드가 쓰는 형태를 찾아 되돌린다.
        /// </summary>
        [Test]
        public void ClosedGenericsTheGameUsesAreFoundAndReset()
        {
            GenericUser.Use();

            var types = GameStatics.TypesOf(new[] { typeof(ListenerState).Assembly });
            GameStatics.Reset(types.Where(t => t == typeof(GenericBus<int>)));

            Assert.That(types, Has.Member(typeof(GenericBus<int>)));
            Assert.That(types, Has.No.Member(typeof(GenericBus<>)));
            Assert.That(GenericBus<int>.Items, Is.Empty);
        }

        /// <summary>SDK 가 게임 어셈블리에 심는 <c>Artel.*</c> 타입은 되돌리지 않는다. 다시 돌리면 SDK 의 등록부에 같은 것이 두 번 든다.</summary>
        [Test]
        public void TypesInTheArtelNamespaceAreLeftAlone()
        {
            var types = GameStatics.TypesOf(new[] { typeof(ListenerState).Assembly });

            Assert.That(types.Where(t => t.Namespace == "Artel" || (t.Namespace ?? "").StartsWith("Artel.")), Is.Empty);
            Assert.That(types, Has.Member(typeof(ListenerState)));
        }

        /// <summary>초기화가 던지는 타입이 있어도 나머지는 되돌리고, 던진 타입은 이름과 이유를 남긴다.</summary>
        [Test]
        public void ATypeWhoseInitializerThrowsDoesNotStopTheOthers()
        {
            ListenerState.Counter = 9;
            Assert.That(ExplodingState.Value, Is.EqualTo(1));
            ExplosionSwitch.On = true;

            var outcome = GameStatics.Reset(new[] { typeof(ExplodingState), typeof(ListenerState) });

            Assert.That(ListenerState.Counter, Is.EqualTo(3));
            Assert.That(outcome.Failed.Single(), Does.Contain(nameof(ExplodingState)).And.Contain("boom"));
        }

        /// <summary>게임이 켤 때 Unity 가 부르는 초기화 메서드를 그 시점 순서대로 다시 부른다.</summary>
        [Test]
        public void InitializersRunAgainInLaunchOrder()
        {
            Boot.Calls.Clear();

            GameStatics.RunInitializers(new[] { typeof(Boot) },
                new[] { RuntimeInitializeLoadType.SubsystemRegistration, RuntimeInitializeLoadType.BeforeSceneLoad });

            Assert.That(Boot.Calls, Is.EqualTo(new[] { "subsystem", "before" }));
        }

        /// <summary>옛 서버는 params 없이 부른다. 그때도 static 을 되돌린다 — 씬만 여는 리셋은 새로 켠 게임과 다르다.</summary>
        [Test]
        public void StaticsResetByDefaultAndTheFlagTurnsItOff()
        {
            Assert.That(ResetRequestReader.TryRead(new List<object>(), out var none, out _), Is.True);
            Assert.That(none.ResetStatics, Is.True);

            var off = new Dictionary<string, object> { ["resetStatics"] = false };
            Assert.That(ResetRequestReader.TryRead(new List<object> { off }, out var kept, out _), Is.True);
            Assert.That(kept.ResetStatics, Is.False);

            var text = new Dictionary<string, object> { ["resetStatics"] = "false" };
            Assert.That(ResetRequestReader.TryRead(new List<object> { text }, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("resetStatics"));
        }
    }
}

// 게임 코드처럼 굴 타입. Artel.* 네임스페이스는 SDK 의 것으로 보고 건너뛰므로 밖에 둔다.
namespace GameStaticsFixtures
{
    internal static class ListenerState
    {
        public static Dictionary<int, Action> Listeners = new Dictionary<int, Action>();
        public static int Counter = 3;
        public static event Action Changed;
        public static string Name { get; set; } = "start";

        public static bool HasChangedListeners => Changed != null;
    }

    /// <summary><see cref="ExplodingState"/> 의 초기화를 던지게 하는 스위치. 같은 타입에 두면 되돌리기가 스위치도 끈다.</summary>
    internal static class ExplosionSwitch
    {
        public static bool On;
    }

    internal static class ExplodingState
    {
        public static int Value = ExplosionSwitch.On ? throw new InvalidOperationException("boom") : 1;
    }

    internal static class GenericBus<T>
    {
        public static List<T> Items = new List<T>();
    }

    internal static class GenericUser
    {
        public static void Use()
        {
            GenericBus<int>.Items.Add(1);
        }
    }

    internal static class Boot
    {
        public static readonly List<string> Calls = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Before() => Calls.Add("before");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Subsystem() => Calls.Add("subsystem");
    }
}
