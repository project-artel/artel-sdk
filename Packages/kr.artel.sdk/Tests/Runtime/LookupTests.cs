using System.Collections.Generic;
using Artel.Affordances;
using Artel.Affordances.Live;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// 이름으로 청한 멤버를 <see cref="Lookup"/> 이 어디서 찾는지 본다(ARTEL-1275).
    ///
    /// 네 자리다: static, 씬 위 컴포넌트, ScriptableObject, 그리고 static 에서 참조를 따라가야 닿는 일반 객체.
    /// 걷기가 유한하다는 것도 여기서 본다 — 못 찾은 것과 예산이 먼저 떨어진 것은 다른 답이다.
    /// </summary>
    public sealed class LookupTests
    {
        private readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void Up()
        {
            Lookup.Forget();
            WatchList.Forget();
        }

        [TearDown]
        public void Down()
        {
            foreach (var made in _made)
            {
                if (made != null)
                {
                    Object.DestroyImmediate(made);
                }
            }

            _made.Clear();
            Lookup.Forget();
            WatchList.Forget();
            Worth.Forget();
        }

        private static Lookup.Answer One(string name, int budget = Lookup.DefaultBudgetMilliseconds)
        {
            return Lookup.Resolve(new[] { name }, new Lookup.Walk(), budget)[0];
        }

        private static string Peek(Lookup.Answer answer)
        {
            return LiveState.Peek(answer.Member, answer.Member.Holders);
        }

        [Test]
        public void static_필드는_바로_푼다()
        {
            var answer = One("Artel.Tests.LookupRoots::Plain");

            Assert.That(answer.Resolved, Is.True, answer.Reason);
            Assert.That(answer.Kind, Is.EqualTo("static"));
            Assert.That(Peek(answer), Does.Contain("\"value\":5"));
        }

        [Test]
        public void 네임스페이스_없이_댄_타입도_게임_타입_가운데서_푼다()
        {
            var answer = One("LookupRoots::Plain");

            Assert.That(answer.Resolved, Is.True, answer.Reason);
            Assert.That(answer.Member.Declaring, Is.EqualTo("Artel.Tests.LookupRoots"));
        }

        [Test]
        public void 컴포넌트가_쥔_멤버는_씬_걷기에_맡기고_몇이나_있는지만_센다()
        {
            var carrier = new GameObject("Lookup carrier");
            _made.Add(carrier);
            carrier.AddComponent<LookupBehaviour>().Score = 7;

            var answer = One("LookupBehaviour::Score");

            Assert.That(answer.Resolved, Is.True, answer.Reason);
            Assert.That(answer.Kind, Is.EqualTo("component"));
            Assert.That(answer.Holders, Is.EqualTo(1));
            Assert.That(answer.Member.Holders, Is.Null, "씬 걷기가 매 판독 만난다. 찾아 두지 않는다.");
        }

        [Test]
        public void ScriptableObject_는_걷지_않고_Unity_에_묻는다()
        {
            var settings = ScriptableObject.CreateInstance<LookupSettings>();
            _made.Add(settings);
            settings.Hard = true;

            var walk = new Lookup.Walk();
            var answer = Lookup.Resolve(new[] { "LookupSettings::Hard" }, walk)[0];

            Assert.That(answer.Resolved, Is.True, answer.Reason);
            Assert.That(answer.Kind, Is.EqualTo("object"));
            Assert.That(answer.Holders, Is.GreaterThanOrEqualTo(1));
            Assert.That(walk.Visited, Is.EqualTo(0), "애셋은 걷지 않는다.");
            Assert.That(Peek(answer), Does.Contain("\"value\":true"));
        }

        [Test]
        public void static_에서_참조를_따라가_일반_객체에_닿는다()
        {
            var walk = new Lookup.Walk();
            var answer = Lookup.Resolve(new[] { "LookupProgress::stage" }, walk)[0];

            Assert.That(answer.Resolved, Is.True, answer.Reason);
            Assert.That(answer.Kind, Is.EqualTo("object"));
            Assert.That(answer.Holders, Is.EqualTo(1));
            Assert.That(walk.Visited, Is.GreaterThan(0));
            Assert.That(walk.Exhausted, Is.False);
            Assert.That(Peek(answer), Does.Contain("\"value\":3"));
        }

        [Test]
        public void 컬렉션_안의_객체도_닿는다()
        {
            var answer = One("LookupItem::Count");

            Assert.That(answer.Resolved, Is.True, answer.Reason);
            // 하나 찾으면 멈춘다. 둘째는 다음 씬 로드의 다시 찾기 몫이다.
            Assert.That(answer.Holders, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void 코드를_도는_프로퍼티는_ArtelObserve_나_빌드가_증명한_순수_getter_일_때만_받는다()
        {
            // 필드도 backing field 도 없는 getter 는 게임 코드다. 이름을 댄 것만으로는 안 돈다 — 필드를 쓰는 getter 는
            // 빌드가 순수하다고 증명하지 못해 표시가 없다(ARTEL-1294).
            var refused = One("LookupProgress::Bumped");

            Assert.That(refused.Resolved, Is.False);
            Assert.That(refused.Reason, Is.EqualTo("property runs code: mark it [ArtelObserve] or name the field"));

            // 필드를 읽기만 하는 계산 getter 는 이 테스트 어셈블리가 위빙될 때 표시를 받았다.
            var proven = One("LookupProgress::Stage");

            Assert.That(proven.Resolved, Is.True, proven.Reason);
            Assert.That(proven.Member.Getter, Is.Not.Null);
            Assert.That(Peek(proven), Does.Contain("\"value\":3"));

            var allowed = One("LookupProgress::Marked");

            Assert.That(allowed.Resolved, Is.True, allowed.Reason);
            Assert.That(allowed.Member.Getter, Is.Not.Null);
            Assert.That(allowed.Member.Field, Is.Null);
            Assert.That(Peek(allowed), Does.Contain("\"value\":4"));

            // 필드를 대면 그대로 읽힌다 — 거절 문장이 가리키는 길.
            Assert.That(One("LookupProgress::stage").Resolved, Is.True);
        }

        [Test]
        public void 다른_getter_와_엔진_frame_을_읽는_static_계산_프로퍼티도_이름_그대로_읽힌다()
        {
            // WordVenture 의 `Core.InteractionLock` 과 같은 모양(ARTEL-1294). 런 155 에서 거절됐던 이름이다.
            var locked = One("LookupLock::IsLocked");
            var paused = One("LookupLock::IsPaused");

            Assert.That(locked.Resolved, Is.True, locked.Reason);
            Assert.That(paused.Resolved, Is.True, paused.Reason);
            Assert.That(locked.Member.Static, Is.True);
            Assert.That(Peek(locked), Does.Contain("\"value\":false"));

            // 같은 타입이라도 디스크를 읽는 getter 는 허용 목록 밖이라 그대로 거절된다.
            Assert.That(One("LookupLock::HasSave").Resolved, Is.False);
        }

        [Test]
        public void 자동_프로퍼티는_backing_field_를_읽고_이름은_프로퍼티_것이다()
        {
            var answer = One("LookupProgress::Title");

            Assert.That(answer.Resolved, Is.True, answer.Reason);
            Assert.That(answer.Member.Field, Is.Not.Null);
            Assert.That(answer.Member.Field.Name, Is.EqualTo("<Title>k__BackingField"));
            Assert.That(answer.Member.Property, Is.EqualTo("Title"));
            Assert.That(Peek(answer), Does.Contain("\"value\":\"intro\""));
        }

        [Test]
        public void 경로는_필드가_쥔_것에서_걸어갈_수_있을_때만_받는다()
        {
            var counted = One("LookupHub::Items.Count");

            Assert.That(counted.Resolved, Is.True, counted.Reason);
            Assert.That(counted.Member.Via, Is.EqualTo("Count"));
            Assert.That(Peek(counted), Does.Contain("\"value\":2"));

            Assert.That(One("LookupHub::Items.Nope").Reason, Is.EqualTo("no such path: Nope"));
        }

        [Test]
        public void 못_푼_이름은_이유를_댄다()
        {
            Assert.That(One("Nope::x").Reason, Is.EqualTo("no such type"));
            Assert.That(One("LookupHub::nope").Reason, Is.EqualTo("no such member"));
            Assert.That(One("LookupHub").Reason, Is.EqualTo("bad name: use Type::member"));
            Assert.That(One("::x").Reason, Is.EqualTo("bad name: use Type::member"));
        }

        [Test]
        public void 어디서도_참조되지_않는_타입은_not_reachable()
        {
            var walk = new Lookup.Walk();
            var answer = Lookup.Resolve(new[] { "LookupOrphan::Never" }, walk)[0];

            Assert.That(answer.Reason, Is.EqualTo("not reachable"));
            Assert.That(walk.Exhausted, Is.False, "뿌리를 다 보고 나서 없다고 말한다.");
        }

        [Test]
        public void 예산이_먼저_떨어지면_그렇다고_말한다()
        {
            var walk = new Lookup.Walk();
            var answer = Lookup.Resolve(new[] { "LookupProgress::stage" }, walk, 0)[0];

            Assert.That(answer.Reason, Is.EqualTo("not reachable within budget"));
            Assert.That(walk.Exhausted, Is.True);
        }

        [Test]
        public void watch_는_목록에_더하고_같은_이름을_두_번_더하지_않는다()
        {
            var before = WatchList.All().Count;

            var first = Lookup.Watch(new[] { "LookupProgress::stage", "LookupRoots::Plain" }, new Lookup.Walk());
            Lookup.Watch(new[] { "LookupProgress::stage" }, new Lookup.Walk());

            Assert.That(first, Has.All.Property("Resolved").True);
            Assert.That(WatchList.All().Count, Is.EqualTo(before + 2));
            Assert.That(WatchList.Requested, Has.Count.EqualTo(2));
        }

        [Test]
        public void 판독은_찾아_둔_인스턴스의_값을_statics_자리에_쓴다()
        {
            Lookup.Watch(new[] { "LookupProgress::stage" }, new Lookup.Walk());

            var since = new Dictionary<string, string>();
            var document = LiveState.Compose(
                1, default, new Restless(), new Restless(1f), since, false, out _);

            Assert.That(document, Does.Contain("\"declaring\":\"Artel.Tests.LookupProgress\",\"member\":\"stage\""));
            Assert.That(document, Does.Contain("\"value\":3"));
        }
    }

    internal static class LookupRoots
    {
        public static int Plain = 5;
        public static LookupHub Hub = new LookupHub();
    }

    internal sealed class LookupHub
    {
        public LookupProgress Progress = new LookupProgress();

        public List<LookupItem> Items = new List<LookupItem> { new LookupItem(), new LookupItem() };

        public Dictionary<string, LookupItem> Named = new Dictionary<string, LookupItem>
        {
            ["sword"] = new LookupItem()
        };
    }

    internal sealed class LookupProgress
    {
        private int stage = 3;

        public int Stage => stage;

        // 읽을 때마다 필드를 바꾼다 — 빌드가 표시하지 않는다.
        public int Bumped => ++stage;

        [ArtelObserve] public int Marked => stage + 1;

        public string Title { get; set; } = "intro";
    }

    internal static class LookupLock
    {
        private static bool locked;

        private static bool paused;

        private static int resumedFrame = -1;

        public static bool IsPaused => paused || Time.frameCount == resumedFrame;

        public static bool IsLocked => locked || IsPaused;

        public static bool HasSave => PlayerPrefs.HasKey("lookup-lock-save");

        // 경고 없이 컴파일되도록 쓰는 곳을 둔다. 테스트는 부르지 않는다.
        internal static void Set(bool isLocked, bool isPaused, int frame)
        {
            locked = isLocked;
            paused = isPaused;
            resumedFrame = frame;
        }
    }

    internal sealed class LookupItem
    {
        public int Count = 1;
    }

    internal sealed class LookupOrphan
    {
        public int Never;
    }

    internal sealed class LookupBehaviour : MonoBehaviour
    {
        public int Score;
    }

    internal sealed class LookupSettings : ScriptableObject
    {
        public bool Hard;
    }
}
