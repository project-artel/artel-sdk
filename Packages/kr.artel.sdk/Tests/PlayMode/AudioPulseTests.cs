using System.Collections;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary><see cref="AudioPulse"/> 가 재생을 시작한 <c>AudioSource</c> 를 <c>PULSE.audio</c> 에 싣는지(ARTEL-1224).</summary>
    public sealed class AudioPulseTests
    {
        private GameObject host;
        private AudioClip clip;

        [SetUp]
        public void SetUp()
        {
            clip = AudioClip.Create("artel test tone", 44100 * 2, 1, 44100, false);
            host = new GameObject("Artel audio pulse test");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(clip);
        }

        [UnityTest]
        public IEnumerator APlayedSource_IsReportedOnceAsStarted_AndCountedWhilePlaying()
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            var pulse = new AudioPulse(() => new[] { source });

            pulse.Compose(true, out _);
            source.Play();
            yield return null;

            var started = Parse(pulse.Compose(false, out var startedNews));
            Assert.That(startedNews, Is.True);
            Assert.That((int)started["playing"], Is.EqualTo(1));
            Assert.That((string)started["started"][0]["clip"], Is.EqualTo("artel test tone"));
            Assert.That((int)started["started"][0]["frame"], Is.EqualTo(Time.frameCount));

            Assert.That(pulse.Compose(false, out var quietNews), Is.Null);
            Assert.That(quietNews, Is.False, "still playing, nothing new");

            source.Stop();
            var stopped = Parse(pulse.Compose(false, out var stoppedNews));
            Assert.That(stoppedNews, Is.True, "the playing count changed");
            Assert.That((int)stopped["playing"], Is.EqualTo(0));
            Assert.That(stopped["started"], Is.Empty);
        }

        [UnityTest]
        public IEnumerator ASourceAlreadyPlayingAtTheFirstReading_IsNotCalledStarted()
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.Play();
            yield return null;

            var first = Parse(new AudioPulse(() => new[] { source }).Compose(true, out _));

            Assert.That((int)first["playing"], Is.EqualTo(1));
            Assert.That(first["started"], Is.Empty);
        }

        [UnityTest]
        public IEnumerator PlayingAgainFromTheStart_CountsAsANewStart()
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            var pulse = new AudioPulse(() => new[] { source });
            pulse.Compose(true, out _);
            source.Play();
            yield return new WaitForSecondsRealtime(0.3f);
            pulse.Compose(false, out _);

            source.Stop();
            source.Play();
            yield return null;

            var again = Parse(pulse.Compose(false, out var news));
            Assert.That(news, Is.True);
            Assert.That(((JArray)again["started"]).Count, Is.EqualTo(1));
        }

        /// <summary>
        /// <c>PlayOneShot</c> 도 쉬던 source 에서는 <c>isPlaying</c> 을 올린다(2022.3.34f1 에서 확인). 다만 <c>clip</c> 이 비어 있어
        /// 이름을 알 수 없다. 이미 <c>isPlaying</c> 이 true 인 source 위의 <c>PlayOneShot</c> 은 이 방식으로 보이지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayOneShot_OnAnIdleSource_IsStarted_WithoutAClipName()
        {
            var source = host.AddComponent<AudioSource>();
            // clip 없이 붙인 source 는 playOnAwake 때문에 isPlaying 이 true 로 남는다(2022.3.34f1). 쉬는 source 를 만들려고 멈춘다.
            source.Stop();
            yield return null;
            var pulse = new AudioPulse(() => new[] { source });
            pulse.Compose(true, out _);

            source.PlayOneShot(clip);
            yield return null;
            yield return null;

            var started = Parse(pulse.Compose(false, out _));
            Assert.That(((JArray)started["started"]).Count, Is.EqualTo(1));
            Assert.That(started["started"][0]["clip"].Type, Is.EqualTo(JTokenType.Null));
        }

        private static JObject Parse(string fragment)
        {
            Assert.That(fragment, Is.Not.Null);
            return (JObject)JObject.Parse("{\"x\":0" + fragment + "}")["audio"];
        }
    }
}
