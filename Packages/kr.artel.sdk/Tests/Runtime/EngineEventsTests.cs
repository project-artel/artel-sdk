using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>프록시가 사건을 적고 <see cref="EngineEventPulse"/> 가 그것을 <c>events</c> 로 싣는지(ARTEL-1283).</summary>
    public sealed class EngineEventsTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [SetUp]
        public void Up()
        {
            EngineEvents.Forget();
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
            EngineEvents.Forget();
            PlayerPrefs.DeleteKey("artel-events-test");
        }

        private GameObject Object_(string name)
        {
            var made = new GameObject(name);
            _made.Add(made);
            return made;
        }

        [Test]
        public void 프록시는_원래_호출을_하고_사건을_적는다()
        {
            var speaker = Object_("Speaker").AddComponent<AudioSource>();

            ArtelEngine.Play(speaker);
            ArtelEngine.SetInt("artel-events-test", 7);

            var events = EngineEvents.Drain(out var dropped);

            Assert.That(dropped, Is.EqualTo(0));
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].Kind, Is.EqualTo("audio.play"));
            Assert.That(events[0].Target, Is.EqualTo("Speaker"));
            Assert.That(events[0].Count, Is.EqualTo(1));
            Assert.That(events[1].Kind, Is.EqualTo("prefs.set"));
            Assert.That(events[1].Detail, Is.EqualTo("artel-events-test"));
            Assert.That(events[1].Value, Is.EqualTo(7));
            Assert.That(PlayerPrefs.GetInt("artel-events-test"), Is.EqualTo(7), "원래 호출이 그대로 일어난다.");
        }

        [Test]
        public void 같은_사건은_한_판독_안에서_count_로_접히고_값이_다르면_따로다()
        {
            var puppet = Object_("Puppet").AddComponent<Animator>();

            for (var at = 0; at < 3; at++)
            {
                ArtelEngine.SetFloat(puppet, "Speed", 0.5f);
            }

            ArtelEngine.SetFloat(puppet, "Speed", 0.7f);

            var events = EngineEvents.Drain(out _);

            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].Count, Is.EqualTo(3));
            Assert.That(events[0].Value, Is.EqualTo(0.5f));
            Assert.That(events[1].Count, Is.EqualTo(1));
        }

        [Test]
        public void 상한을_넘긴_사건은_버리고_그_수를_말한다()
        {
            for (var at = 0; at < EngineEvents.MaxPerReading + 5; at++)
            {
                ArtelEngine.SetInt("artel-events-test", at);
            }

            EngineEvents.Drain(out var dropped);

            Assert.That(dropped, Is.EqualTo(5));
        }

        [Test]
        public void pulse_는_사건이_있을_때만_events_를_싣고_비우며_news_로_답한다()
        {
            var pulse = new EngineEventPulse();

            Assert.That(pulse.Compose(false, out var quiet), Is.Null);
            Assert.That(quiet, Is.False);

            // 씬을 실제로 바꾸지 않고 프록시가 적는 것과 같은 모양으로 적는다.
            EngineEvents.Record("scene.load", null, "Map_scene", null);

            var fragment = pulse.Compose(false, out var news);

            Assert.That(news, Is.True);
            Assert.That(fragment, Does.StartWith(",\"events\":[{"));
            Assert.That(fragment, Does.Contain("\"kind\":\"scene.load\"").And.Contain("\"detail\":\"Map_scene\""));
            Assert.That(pulse.Compose(false, out var drained), Is.Null, "한 번 실은 것은 다시 싣지 않는다.");
            Assert.That(drained, Is.False);
        }

        [Test]
        public void 인자는_JSON_으로_쓸_수_있는_모양으로_낮춘다()
        {
            var carrier = Object_("Carrier");

            Assert.That(EngineEvents.Lower(3), Is.EqualTo(3));
            Assert.That(EngineEvents.Lower("x"), Is.EqualTo("x"));
            Assert.That(EngineEvents.Lower(KeyCode.Space), Is.EqualTo("Space"));
            Assert.That(EngineEvents.Lower(carrier), Is.EqualTo("Carrier"));
            Assert.That(EngineEvents.Lower(new Vector3(1, 2, 3)), Is.EqualTo(new Vector3(1, 2, 3).ToString()));
            Assert.That(EngineEvents.Lower(new List<int>()), Is.EqualTo(typeof(List<int>).FullName));
            Assert.That(EngineEvents.Lower(null), Is.Null);
        }
    }
}
