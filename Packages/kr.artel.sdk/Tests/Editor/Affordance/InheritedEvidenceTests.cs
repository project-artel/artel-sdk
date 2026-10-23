using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>
    /// 부모 class 의 기록이 그것을 물려받은 자식 type 에 실리고, 자식 진입점이 부른 부모 메서드의 기록이 자식 type 에
    /// 실리는지 본다(ARTEL-1228).
    /// </summary>
    [TestFixture]
    internal sealed class InheritedEvidenceTests
    {
        private AssemblyDefinition _assembly;
        private TypeDefinition _base;
        private TypeDefinition _plain;
        private TypeDefinition _ownStart;

        [SetUp]
        public void ReadOwnAssembly()
        {
            // 부모를 MonoBehaviour 까지 따라 올라가야 behaviour 로 판정되므로 UnityEngine 이 있는 곳을 알려 준다.
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(UnityEngine.MonoBehaviour).Assembly.Location));
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(InheritedBaseFixture).Assembly.Location));

            _assembly = AssemblyDefinition.ReadAssembly(
                typeof(InheritedBaseFixture).Assembly.Location,
                new ReaderParameters { AssemblyResolver = resolver });

            _base = _assembly.MainModule.GetType(typeof(InheritedBaseFixture).FullName);
            _plain = _assembly.MainModule.GetType(typeof(InheritedPlainFixture).FullName);
            _ownStart = _assembly.MainModule.GetType(typeof(InheritedOwnStartFixture).FullName);
        }

        [TearDown]
        public void Close()
        {
            _assembly?.Dispose();
        }

        private Variant OwnedByBase(string method)
        {
            var entry = _base.Methods.Single(candidate => candidate.Name == method);
            return new Variant
            {
                Method = entry.FullName,
                MethodId = MethodIdentity.Of(entry),
                Entry = entry.FullName,
                EntryId = MethodIdentity.Of(entry),
                Owner = _base
            };
        }

        [Test]
        public void AnEntryTheSubclassDoesNotRedeclareIsCarriedToIt()
        {
            var press = OwnedByBase("Press");
            var heirs = InheritedEvidence.Heirs(_assembly.MainModule, new List<Variant> { press });

            CollectionAssert.AreEquivalent(new[] { _plain, _ownStart }, heirs[press]);
        }

        [Test]
        public void AnEntryTheSubclassRedeclaresStaysWithTheParent()
        {
            var start = OwnedByBase("Start");
            var heirs = InheritedEvidence.Heirs(_assembly.MainModule, new List<Variant> { start });

            CollectionAssert.AreEqual(new[] { _plain }, heirs[start]);
        }

        [Test]
        public void ARecordThatStartedInAnotherTypeIsNotInherited()
        {
            var ready = _base.Methods.Single(candidate => candidate.Name == "Ready");
            var update = _ownStart.Methods.Single(candidate => candidate.Name == "Update");
            var reached = new Variant
            {
                Method = ready.FullName,
                Entry = update.FullName,
                EntryId = MethodIdentity.Of(update),
                Owner = _base
            };

            var heirs = InheritedEvidence.Heirs(_assembly.MainModule, new List<Variant> { reached });

            Assert.That(heirs.ContainsKey(reached), Is.False);
        }

        [Test]
        public void AParentStaticCalledFromASubclassEntryBelongsToTheSubclass()
        {
            var ready = _base.Methods.Single(candidate => candidate.Name == "Ready");
            var update = _ownStart.Methods.Single(candidate => candidate.Name == "Update");

            Assert.That(VariantBuilder.OwnerOf(ready, update, false), Is.SameAs(_ownStart));
        }

        [Test]
        public void AParentInstanceMethodOnAnotherObjectStaysWithTheParent()
        {
            var press = _base.Methods.Single(candidate => candidate.Name == "Press");
            var update = _ownStart.Methods.Single(candidate => candidate.Name == "Update");

            Assert.That(VariantBuilder.OwnerOf(press, update, false), Is.SameAs(_base));
            Assert.That(VariantBuilder.OwnerOf(press, update, true), Is.SameAs(_ownStart));
        }
    }
}
