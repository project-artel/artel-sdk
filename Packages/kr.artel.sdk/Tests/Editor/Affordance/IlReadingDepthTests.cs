using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>
    /// <see cref="IlReading"/> 이 필드를 따라 읽는 깊이를 끝까지 세는지.
    /// </summary>
    /// <remarks>
    /// 깊이를 세지 못하면 stack overflow 가 나고, 그것은 예외로 잡히지 않는다. Unity 의 IL post-processor 프로세스가
    /// 죽고 Assembly-CSharp.dll 이 compile error 로 보고된다. 그래서 이 테스트의 실패는 단언 실패가 아니라 테스트
    /// 프로세스 종료로 나타난다.
    /// </remarks>
    [TestFixture]
    internal sealed class IlReadingDepthTests
    {
        private AssemblyDefinition _assembly;
        private TypeDefinition _fixtures;

        [SetUp]
        public void ReadOwnAssembly()
        {
            _assembly = AssemblyDefinition.ReadAssembly(typeof(SelfReferencingFixtures).Assembly.Location);
            _fixtures = _assembly.MainModule.GetType(typeof(SelfReferencingFixtures).FullName);
        }

        [TearDown]
        public void Close()
        {
            _assembly?.Dispose();
        }

        /// <remarks>
        /// <c>count</c> 를 이름 대려 하면 <c>WhichScene</c> 이 그 필드를 쓰는 유일한 대입 <c>count = count + 1</c> 로
        /// 간다. 그 식의 <c>count</c> 가 다시 <c>WhichScene</c> 으로 들어오는데, <c>Read</c> 가 깊이를 0 으로
        /// 되돌려 넘기던 때에는 가드가 한 번도 걸리지 않았다.
        /// </remarks>
        [Test]
        public void FieldAssignedFromItselfIsReadWithoutRunningAway()
        {
            var getter = Method("get_Reached");
            var load = FieldLoad(getter, "count");

            string described = null;

            Assert.That(
                () => described = IlReading.Describe(load, getter.Body.Instructions[0], getter),
                Throws.Nothing);
            Assert.That(described, Is.EqualTo("SelfReferencingFixtures.count"));
        }

        private static Instruction FieldLoad(MethodDefinition method, string field)
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code == Code.Ldfld &&
                    instruction.Operand is FieldReference reference &&
                    reference.Name == field)
                {
                    return instruction;
                }
            }

            Assert.Fail("no load of " + field + " in " + method.Name);
            return null;
        }

        private MethodDefinition Method(string name)
        {
            foreach (var method in _fixtures.Methods)
            {
                if (method.Name == name)
                {
                    return method;
                }
            }

            Assert.Fail("no method named " + name + " on the fixtures");
            return null;
        }
    }
}
