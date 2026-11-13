using System.Collections.Generic;
using Artel.Affordances.Live;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Artel.Tests {
    public sealed class PaceReaderTests {
        [TestCase(true)]
        [TestCase(false)]
        public void ForceIsReadFromWireAndDictionary(bool value) {
            foreach (var options in new object[] {
                JObject.FromObject(new { force = value, interval = 0.2f, delivery = 2f, minDelivery = 0.3f }),
                new Dictionary<string, object> { ["force"] = value, ["interval"] = 0.2f, ["delivery"] = 2f, ["minDelivery"] = 0.3f }
            }) {
                Assert.That(PaceReader.TryRead(new List<object> { options }, out var pace, out var force, out var error), Is.True, error);
                Assert.That(force, Is.EqualTo(value));
                Assert.That(pace.Interval, Is.EqualTo(0.2f));
                Assert.That(pace.Delivery, Is.EqualTo(2f));
                Assert.That(pace.MinDelivery, Is.EqualTo(0.3f));
            }
        }

        [TestCase("true")]
        [TestCase(1)]
        [TestCase(null)]
        public void InvalidForceIsRejected(object value) {
            foreach (var options in new object[] {
                JObject.FromObject(new { force = value }),
                new Dictionary<string, object> { ["force"] = value }
            }) {
                Assert.That(PaceReader.TryRead(new List<object> { options }, out _, out _, out var error), Is.False);
                Assert.That(error, Is.EqualTo("subscribe_pulse force must be a boolean."));
            }
        }

        [Test]
        public void ExistingCallsDefaultToFalse() {
            foreach (var parameters in new[] { null, new List<object>(), new List<object> { new JObject() } }) {
                Assert.That(PaceReader.TryRead(parameters, out var pace, out var force, out var error), Is.True, error);
                Assert.That(force, Is.False);
                Assert.That(pace.Interval, Is.EqualTo(Pulse.Pace.Default.Interval));
            }
        }
    }
}
