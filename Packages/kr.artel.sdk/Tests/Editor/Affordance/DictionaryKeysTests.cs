using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>dictionary 에서 꺼낸 key 가 상수 KeyCode 나 후보 목록으로 읽히는지 본다(ARTEL-1230).</summary>
    [TestFixture]
    internal sealed class DictionaryKeysTests
    {
        private AssemblyDefinition _assembly;
        private TypeDefinition _fixtures;

        [SetUp]
        public void ReadOwnAssembly()
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(typeof(UnityEngine.KeyCode).Assembly.Location));

            _assembly = AssemblyDefinition.ReadAssembly(
                typeof(KeyMappingFixtures).Assembly.Location, new ReaderParameters { AssemblyResolver = resolver });
            _fixtures = _assembly.MainModule.GetType(typeof(KeyMappingFixtures).FullName);
            DictionaryKeys.Forget();
        }

        [TearDown]
        public void Close()
        {
            DictionaryKeys.Forget();
            _assembly?.Dispose();
        }

        /// <summary>메서드 안의 <c>Input.GetKey*</c> 호출에 넘긴 인자를 올린 명령어.</summary>
        private Instruction KeyArgument(string method)
        {
            var body = _fixtures.Methods.Single(candidate => candidate.Name == method).Body;
            var call = body.Instructions.First(instruction =>
                instruction.Operand is MethodReference called &&
                // SDK 의 위버가 먼저 돌면 UnityEngine.Input 호출이 Artel.ArtelInput 으로 바뀌어 있다.
                (called.DeclaringType.FullName == "UnityEngine.Input" ||
                 called.DeclaringType.FullName == "Artel.ArtelInput") &&
                called.Name.StartsWith("GetKey"));
            return call.Previous;
        }

        [Test]
        public void AConstantKeyReadsAsTheKeyCodeItMapsTo()
        {
            Assert.That(DictionaryKeys.TryRead(KeyArgument("Up"), out var name, out var candidates), Is.True);
            Assert.That(name, Is.EqualTo("W"));
            Assert.That(candidates, Is.Null);
        }

        [Test]
        public void AVariableKeyReadsAsEveryValueTheDictionaryHolds()
        {
            Assert.That(DictionaryKeys.TryRead(KeyArgument("Held"), out var name, out var candidates), Is.True);
            Assert.That(name, Is.Null);
            CollectionAssert.AreEquivalent(new[] { "W", "S", "Return" }, candidates);
        }

        [Test]
        public void AStaticDictionaryWithStringKeysIsReadToo()
        {
            Assert.That(DictionaryKeys.TryRead(KeyArgument("Pause"), out var name, out _), Is.True);
            Assert.That(name, Is.EqualTo("Escape"));
        }

        [Test]
        public void CandidatesBecomeAChoiceOfKeysInTheCondition()
        {
            var read = new InputRead
            {
                Gesture = "key", Name = "(not a literal)", Phase = "down",
                Candidates = new List<string> { "W", "Space" }
            };

            var condition = Condition.FromGesture(read);

            Assert.That(condition.Kind, Is.EqualTo(ConditionKind.Either));
            CollectionAssert.AreEqual(
                new[] { "key:W (down)", "key:Space (down)" },
                condition.Parts.Select(part => part.Gesture.ToString()).ToArray());
        }

        [Test]
        public void NoCandidatePressedMeansNoneOfThem()
        {
            var read = new InputRead
            {
                Gesture = "key", Name = "(not a literal)", Phase = "down", Absent = true,
                Candidates = new List<string> { "W", "Space" }
            };

            Assert.That(Condition.FromGesture(read).Kind, Is.EqualTo(ConditionKind.Every));
        }
    }
}
