using System.Collections.Generic;
using Artel.Affordances.Live;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.SceneManagement;

namespace Artel.Tests.GameFunctions
{
    /// <summary><c>LiveState.Compose</c> 가 붙은 조각을 문서에 넣고, 조각의 <c>news</c> 로 <c>pulse</c> 를 내보내는지.</summary>
    public sealed class PulseAttachmentTests
    {
        private IPulseAttachment previous;

        [SetUp]
        public void SetUp()
        {
            previous = LiveState.Attachment;
        }

        [TearDown]
        public void TearDown()
        {
            LiveState.Attachment = previous;
        }

        [Test]
        public void TheFragment_LandsInTheDocument_AndNewsKeepsAQuietReadingFromBeingHeld()
        {
            var attachment = new FixedAttachment();
            LiveState.Attachment = attachment;
            var since = new Dictionary<string, string>();
            var restless = new Restless();
            var pixels = new Restless(1f);
            var scene = SceneManager.GetActiveScene();

            LiveState.Compose(1, scene, restless, pixels, since, false, out _);

            attachment.News = false;
            LiveState.Compose(2, scene, restless, pixels, since, false, out var quietSettled);

            attachment.News = true;
            var document = LiveState.Compose(3, scene, restless, pixels, since, false, out var newsSettled);

            Assert.That(quietSettled, Is.True, "nothing moved and the fragment had no news");
            Assert.That(newsSettled, Is.False, "the fragment's news must send the reading");
            Assert.That((int)JObject.Parse(document)["extra"], Is.EqualTo(1));
            Assert.That(attachment.LastWhole, Is.False);
        }

        private sealed class FixedAttachment : IPulseAttachment
        {
            public bool News = true;
            public bool LastWhole;

            public string Compose(bool whole, out bool news)
            {
                LastWhole = whole;
                news = News;
                return ",\"extra\":1";
            }
        }
    }
}
