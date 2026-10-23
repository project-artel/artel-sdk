using System.Linq;
using Artel.Affordances.Live;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Protocol
{
    /// <summary><see cref="LoggedErrors"/> 가 무엇을 모으고 어떻게 묶어 <c>pulse</c> 와 <c>ACTION_RESULT</c> 에 내주는지.</summary>
    public sealed class LoggedErrorsTests
    {
        private int frame;
        private LoggedErrors errors;

        [SetUp]
        public void SetUp()
        {
            frame = 10;
            errors = new LoggedErrors(() => frame);
        }

        [Test]
        public void OnlyExceptionErrorAndAssert_AreCollected()
        {
            errors.Record("warning", "", LogType.Warning);
            errors.Record("log", "", LogType.Log);
            errors.Record("boom", "A.B ()\n", LogType.Exception);
            errors.Record("bad", "", LogType.Error);
            errors.Record("assert", "", LogType.Assert);

            var types = errors.TakeUnsent(20).Select(item => item.Type).ToList();

            Assert.That(types, Is.EqualTo(new[] { "Exception", "Error", "Assert" }));
        }

        [Test]
        public void TheSameTypeMessageAndStackHead_CountAsOneGroup()
        {
            errors.Record("NullReferenceException: x", "Story.Advance ()\nStory.Update ()\n", LogType.Exception);
            frame = 12;
            errors.Record("NullReferenceException: x", "Story.Advance ()\nOther.Caller ()\n", LogType.Exception);
            errors.Record("NullReferenceException: x", "Camera.Awake ()\n", LogType.Exception);

            var items = errors.TakeUnsent(20);

            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(items[0].StackHead, Is.EqualTo("Story.Advance ()"));
            Assert.That(items[0].Count, Is.EqualTo(2));
            Assert.That(items[0].FirstFrame, Is.EqualTo(10));
            Assert.That(items[0].LastFrame, Is.EqualTo(12));
        }

        [Test]
        public void AGroupIsTakenAgain_OnlyAfterItsCountGrows()
        {
            errors.Record("boom", "A ()", LogType.Exception);
            Assert.That(errors.TakeUnsent(20).Count, Is.EqualTo(1));
            Assert.That(errors.TakeUnsent(20), Is.Empty, "nothing new since the last pulse");

            errors.Record("boom", "A ()", LogType.Exception);
            var again = errors.TakeUnsent(20);

            Assert.That(again.Single().Count, Is.EqualTo(2));
        }

        [Test]
        public void ALongMessage_IsCutAt200_AndMarkedTruncated()
        {
            errors.Record(new string('x', 500), "", LogType.Error);
            errors.Record("short", "", LogType.Error);

            var items = errors.TakeUnsent(20);
            var json = JArray.FromObject(items);

            Assert.That(items[0].Message.Length, Is.EqualTo(LoggedErrors.MaxMessageLength));
            Assert.That((bool)json[0]["truncated"], Is.True);
            Assert.That(json[1]["truncated"], Is.Null, "the marker is written only when the message was cut");
        }

        [Test]
        public void MoreThanTheLimit_WaitsForTheNextTake()
        {
            for (var index = 0; index < 25; index++)
            {
                errors.Record("error " + index, "", LogType.Error);
            }

            var first = errors.TakeUnsent(20);
            var second = errors.TakeUnsent(20);

            Assert.That(first.Count, Is.EqualTo(20));
            Assert.That(second.Select(item => item.Message), Is.EqualTo(Enumerable.Range(20, 5).Select(index => "error " + index)));
        }

        [Test]
        public void GroupsStayBounded_ByDroppingTheOldestSentOne()
        {
            for (var index = 0; index < LoggedErrors.MaxGroups; index++)
            {
                frame = index;
                errors.Record("error " + index, "", LogType.Error);
            }

            errors.TakeUnsent(LoggedErrors.MaxGroups);
            frame = 1000;
            errors.Record("one more", "", LogType.Error);

            Assert.That(errors.GroupCount, Is.EqualTo(LoggedErrors.MaxGroups));
            Assert.That(errors.SeenSince(0, 200).Any(item => item.Message == "error 0"), Is.False);
            Assert.That(errors.TakeUnsent(20).Single().Message, Is.EqualTo("one more"));
        }

        [Test]
        public void SeenSince_ReturnsGroupsLoggedAtOrAfterTheFrame_WithoutMarkingThemSent()
        {
            errors.Record("before", "", LogType.Error);
            frame = 20;
            errors.Record("after", "", LogType.Error);

            var seen = errors.SeenSince(20, 20);

            Assert.That(seen.Select(item => item.Message), Is.EqualTo(new[] { "after" }));
            Assert.That(errors.TakeUnsent(20).Count, Is.EqualTo(2), "an ACTION_RESULT does not consume what the pulse owes");
        }

        [Test]
        public void ErrorPulse_WritesErrors_AndRaisesNewsOnlyWhenThereAreSome()
        {
            var pulse = new ErrorPulse(errors);

            Assert.That(pulse.Compose(false, out var quietNews), Is.Null);
            Assert.That(quietNews, Is.False);

            errors.Record("boom", "A ()", LogType.Exception);
            var fragment = pulse.Compose(false, out var news);
            var document = JObject.Parse("{\"x\":0" + fragment + "}");

            Assert.That(news, Is.True);
            Assert.That((string)document["errors"][0]["type"], Is.EqualTo("Exception"));
            Assert.That((int)document["errors"][0]["count"], Is.EqualTo(1));
            Assert.That((string)document["errors"][0]["stackHead"], Is.EqualTo("A ()"));
        }

        [Test]
        public void AttachmentSet_JoinsFragments_AndSkipsOneThatThrows()
        {
            var set = new PulseAttachmentSet(new Fixed(",\"a\":1", false), new Throwing(), new Fixed(",\"b\":2", true));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("pulse attachment"));
            var fragment = set.Compose(false, out var news);

            Assert.That(fragment, Is.EqualTo(",\"a\":1,\"b\":2"));
            Assert.That(news, Is.True);
        }

        private sealed class Fixed : IPulseAttachment
        {
            private readonly string fragment;
            private readonly bool news;

            public Fixed(string fragment, bool news)
            {
                this.fragment = fragment;
                this.news = news;
            }

            public string Compose(bool whole, out bool hasNews)
            {
                hasNews = news;
                return fragment;
            }
        }

        private sealed class Throwing : IPulseAttachment
        {
            public string Compose(bool whole, out bool news)
            {
                throw new System.InvalidOperationException("broken");
            }
        }
    }
}
