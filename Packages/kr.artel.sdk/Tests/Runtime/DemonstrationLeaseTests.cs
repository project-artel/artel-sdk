using Artel.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Collections.Generic;

namespace Artel.Tests
{
    public sealed class DemonstrationLeaseTests
    {
        private double now;
        private DemonstrationLease lease;

        [SetUp] public void SetUp() { now = 10; lease = new DemonstrationLease(() => now); }

        private static DemonstrationControlDto Control(string operation, long generation = 1, string session = "one")
        {
            return new DemonstrationControlDto
            {
                SessionId = session, Generation = generation, Operation = operation, LeaseMilliseconds = 8000
            };
        }

        private static DemonstrationReferenceDto Reference(long generation = 1, string session = "one")
        {
            return new DemonstrationReferenceDto { SessionId = session, Generation = generation };
        }

        [Test] public void Acquire_RejectsLegacyAndForeignActions()
        {
            Assert.That(lease.Allows(null), Is.True);
            Assert.That(lease.Apply(Control("ACQUIRE")), Is.True);
            Assert.That(lease.Allows(null), Is.False);
            Assert.That(lease.Allows(Reference()), Is.True);
            Assert.That(lease.Allows(Reference(session: "other")), Is.False);
            Assert.That(lease.Allows(Reference(2)), Is.False);
        }

        [Test] public void Expiry_RejectsRenewalAndOldGenerationReacquisition()
        {
            lease.Apply(Control("ACQUIRE"));
            now = 18;
            Assert.That(lease.Allows(Reference()), Is.False);
            Assert.That(lease.Apply(Control("RENEW")), Is.False);
            Assert.That(lease.Expire(), Is.True);
            Assert.That(lease.Expire(), Is.False);
            Assert.That(lease.Apply(Control("ACQUIRE")), Is.False);
            Assert.That(lease.Apply(Control("ACQUIRE", 2)), Is.True);
        }

        [Test] public void ReleaseAndConnectionLoss_KeepGenerationFence()
        {
            lease.Apply(Control("ACQUIRE"));
            Assert.That(lease.Apply(Control("RELEASE", session: "other")), Is.False);
            Assert.That(lease.Apply(Control("RELEASE")), Is.True);
            Assert.That(lease.Apply(Control("ACQUIRE")), Is.False);
            Assert.That(lease.Apply(Control("ACQUIRE", 2)), Is.True);
            lease.Invalidate();
            Assert.That(lease.Allows(Reference(2)), Is.False);
            Assert.That(lease.Apply(Control("ACQUIRE", 2)), Is.False);
            Assert.That(lease.Allows(null), Is.True);
        }

        [Test] public void Renewal_ExtendsOnlyMatchingLiveLease()
        {
            lease.Apply(Control("ACQUIRE"));
            now = 17;
            Assert.That(lease.Apply(Control("RENEW", 2)), Is.False);
            Assert.That(lease.Apply(Control("RENEW")), Is.True);
            now = 20;
            Assert.That(lease.Allows(Reference()), Is.True);
            now = 25;
            Assert.That(lease.Allows(Reference()), Is.False);
        }

        [Test] public void ActionWire_PreservesOptionalDemonstrationReference()
        {
            var legacy = JsonConvert.DeserializeObject<ArtelRequestDto>("{\"type\":\"ACTION\",\"id\":42}");
            Assert.That(legacy.Demonstration, Is.Null);
            Assert.That(JsonConvert.SerializeObject(legacy), Does.Not.Contain("demonstration"));
            var request = JsonConvert.DeserializeObject<ArtelRequestDto>(
                "{\"type\":\"ACTION\",\"id\":42,\"demonstration\":{\"sessionId\":\"one\",\"generation\":123}}");
            Assert.That(request.Demonstration.Generation, Is.EqualTo(123));
            Assert.That(request.Demonstration.SessionId, Is.EqualTo("one"));
        }

        [Test] public void NormalizedPointer_ResolvesCurrentGamePixelsAndRejectsInvalidValues()
        {
            Assert.That(DemonstrationPointer.TryResolve(new List<object> { 0.25, 0.75 }, 1920, 1080, out var pixels), Is.True);
            Assert.That(pixels, Is.EqualTo(new List<object> { 480d, 810d }));
            Assert.That(DemonstrationPointer.TryResolve(new List<object> { 0.25, 0.75 }, 1280, 720, out pixels), Is.True);
            Assert.That(pixels, Is.EqualTo(new List<object> { 320d, 540d }));
            foreach (var invalid in new object[] { -0.1, 1.1, double.NaN, double.PositiveInfinity, "0.5" })
                Assert.That(DemonstrationPointer.TryResolve(new List<object> { invalid, 0.5 }, 1920, 1080, out pixels), Is.False);
        }
    }
}
