#if !ARTEL_INPUT_SYSTEM_INSTALLED
using System;
using System.Linq;
using NUnit.Framework;

namespace Artel.Tests
{
    public sealed class OptionalInputDependencyTests
    {
        [Test]
        public void LegacyWithoutPackage_DoesNotLoadOrReferenceInputSystem()
        {
            var names = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name);
            Assert.That(names, Does.Not.Contain("Unity.InputSystem"));
            Assert.That(names, Does.Not.Contain("Artel.InputSystem"));
            var references = typeof(ArtelInput).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);
            Assert.That(references, Does.Not.Contain("Unity.InputSystem"));
            Assert.That(references, Does.Not.Contain("Artel.InputSystem"));
            Assert.That(NewInputMouse.IsActive, Is.False);
            Assert.That(NewInputMouse.ValidationError, Is.Null);
        }
    }
}
#endif
