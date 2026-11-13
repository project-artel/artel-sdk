using System;
using NUnit.Framework;

namespace Artel.GameFunctions.Editor.Tests
{
    public class ArtelDefinesTests
    {
        private static readonly string[] Supported = { "ARTEL_GAME_FUNCTIONS" };

        [Test]
        public void AddsSupportedDefineAfterGameDefines()
        {
            var result = ArtelDefines.Reconcile(new[] { "DOTWEEN" }, Supported);

            Assert.That(result, Is.EqualTo(new[] { "DOTWEEN", "ARTEL_GAME_FUNCTIONS" }));
        }

        [Test]
        public void RemovesArtelDefineThisVersionDoesNotSupport()
        {
            var result = ArtelDefines.Reconcile(new[] { "ARTEL_FOO", "DOTWEEN", "ARTEL_GAME_FUNCTIONS" }, Supported);

            Assert.That(result, Is.EqualTo(new[] { "DOTWEEN", "ARTEL_GAME_FUNCTIONS" }));
        }

        [Test]
        public void LeavesDefinesWithoutArtelPrefixAndTheirOrder()
        {
            var result = ArtelDefines.Reconcile(new[] { "MY_GAME_FLAG", "ARTEL_GAME_FUNCTIONS", "DOTWEEN", "ARTELX" }, Supported);

            Assert.That(result, Is.EqualTo(new[] { "MY_GAME_FLAG", "ARTEL_GAME_FUNCTIONS", "DOTWEEN", "ARTELX" }));
        }

        [Test]
        public void ReturnsSameListWhenNothingChanges()
        {
            var current = new[] { "DOTWEEN", "ARTEL_GAME_FUNCTIONS" };

            Assert.That(ArtelDefines.Reconcile(current, Supported), Is.EqualTo(current));
        }

        [Test]
        public void RemovesEveryArtelDefineWhenNothingIsSupported()
        {
            var result = ArtelDefines.Reconcile(new[] { "ARTEL_GAME_FUNCTIONS", "DOTWEEN", "ARTEL_FOO" }, Array.Empty<string>());

            Assert.That(result, Is.EqualTo(new[] { "DOTWEEN" }));
        }
    }
}
