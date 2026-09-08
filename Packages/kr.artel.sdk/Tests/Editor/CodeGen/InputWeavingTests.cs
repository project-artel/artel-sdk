using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;

namespace Artel.CodeGen.Tests
{
    /// <summary>
    /// 위버가 어느 어셈블리를 건드리는지에 대한 테스트다. 무엇으로 바꾸는지가 아니라
    /// 누구를 대상으로 삼는지가 ARTEL-383 에서 틀렸던 부분이다.
    /// </summary>
    /// <remarks>
    /// Cecil 로 게임 어셈블리 하나를 만들어 먹인다. 게임을 빌드해 DLL 을 뒤지는 대신 이렇게
    /// 하는 것은, 실패했을 때 어느 조건이 안 맞았는지가 곧바로 나오기 때문이다.
    ///
    /// ILPP 진입점인 <c>ArtelILPostProcessor</c> 를 부르지는 않는다. 그러려면
    /// <c>Unity.CompilationPipeline.Common</c> 이 필요한데, 그 어셈블리는 이름이
    /// <c>*.CodeGen</c> 인 어셈블리에만 열려 있어 테스트가 참조할 수 없다. 그래서 위버는
    /// 그 타입들에 기대지 않게 만들어 두었다.
    /// </remarks>
    [TestFixture]
    internal sealed class InputWeavingTests
    {
        private const string ProxyTypeName = "Artel.ArtelInput";
        private const string UnityInputTypeName = "UnityEngine.Input";

        /// <summary>
        /// ARTEL-383 이 다시 열린 이유. SDK 타입을 한 번도 쓰지 않는 게임 어셈블리도
        /// <c>Input</c> 을 부르기만 하면 위빙 대상이다.
        /// </summary>
        [Test]
        public void Weaves_AnAssemblyThatCallsInput_EvenWithNoArtelReference()
        {
            using (var module = BuildGameAssembly(callsInput: true))
            {
                Assert.That(
                    module.AssemblyReferences.Select(reference => reference.Name),
                    Does.Not.Contain("Artel.Runtime"),
                    "이 테스트의 전제는 Artel 참조가 없는 어셈블리다.");

                var attempt = InputMethodWeaver.TryCreate(module, RealReferences());

                Assert.That(attempt.Refusal, Is.Null);
                Assert.That(attempt.Weaver, Is.Not.Null, "위버가 만들어지지 않았다.");
                Assert.That(attempt.Weaver.Process(), Is.True, "바꾼 것이 없다.");

                Assert.That(CalledTypeNames(module), Does.Contain(ProxyTypeName));
                Assert.That(CalledTypeNames(module), Does.Not.Contain(UnityInputTypeName));
            }
        }

        /// <summary>
        /// 참조는 위빙의 전제가 아니라 결과다. 교체가 그것을 만들어 준다.
        /// </summary>
        [Test]
        public void AddsTheRuntimeReference_AsAResultOfWeaving()
        {
            using (var module = BuildGameAssembly(callsInput: true))
            {
                InputMethodWeaver.TryCreate(module, RealReferences()).Weaver.Process();

                Assert.That(
                    module.AssemblyReferences.Select(reference => reference.Name),
                    Does.Contain("Artel.Runtime"));
            }
        }

        /// <summary>
        /// 바꿀 것이 없는 어셈블리는 그대로 둔다. 할 말도 없다.
        /// </summary>
        [Test]
        public void LeavesAnAssemblyAlone_WhenItNeverCallsInput()
        {
            using (var module = BuildGameAssembly(callsInput: false))
            {
                var attempt = InputMethodWeaver.TryCreate(module, RealReferences());

                Assert.That(attempt.Weaver, Is.Null);
                Assert.That(attempt.Refusal, Is.Null, "손댈 것이 없는 것은 알릴 일이 아니다.");
            }
        }

        /// <summary>
        /// 바꿀 호출이 있는데 프록시를 못 찾았으면 조용히 지나가지 않는다. 예전의 침묵이
        /// TrashDash 에서 키 입력이 죽은 것을 아무도 모르게 만들었다.
        /// </summary>
        [Test]
        public void Refuses_OutLoud_WhenTheRuntimeAssemblyIsNowhereAmongTheReferences()
        {
            using (var module = BuildGameAssembly(callsInput: true))
            {
                var attempt = InputMethodWeaver.TryCreate(
                    module, new[] { typeof(Input).Assembly.Location });

                Assert.That(attempt.Weaver, Is.Null);
                Assert.That(attempt.Refusal, Is.Not.Null.And.Contains("Artel.Runtime"));
            }
        }

        /// <summary>
        /// 자기 어셈블리는 건드리지 않는다. <c>ArtelInput</c> 자신이 <c>Input</c> 을 부르므로,
        /// 이 가드가 없으면 프록시가 자기를 부르게 된다.
        /// </summary>
        [Test]
        public void SkipsItsOwnAndEngineAssemblies()
        {
            Assert.That(WeavableAssemblies.IsSkipped("Artel.Runtime"), Is.True,
                "ArtelInput 이 사는 곳이다. 걸러 내지 않으면 프록시가 자기를 부른다.");
            Assert.That(WeavableAssemblies.IsSkipped("Unity.Artel.CodeGen"), Is.True);
            Assert.That(WeavableAssemblies.IsSkipped("UnityEngine.UI"), Is.True,
                "엔진의 입력 경로를 갈아 끼우는 것은 이 위버가 할 일이 아니다.");

            Assert.That(WeavableAssemblies.IsSkipped("Artel.Runtime.Tests"), Is.False,
                "위빙이 일어났는지 확인하는 테스트들이 제 어셈블리가 갈아 끼워진 것을 보고 판정한다.");
            Assert.That(WeavableAssemblies.IsSkipped("Artel.Runtime.PlayModeTests"), Is.False);
            Assert.That(WeavableAssemblies.IsSkipped("Assembly-CSharp"), Is.False);
            Assert.That(WeavableAssemblies.IsSkipped("Game.Input"), Is.False);
            Assert.That(WeavableAssemblies.IsSkipped("UnityLike"), Is.False,
                "접두어는 이름 경계에서 맞춰야 한다. 그저 같은 글자로 시작하는 게임 어셈블리는 대상이다.");
        }

        /// <summary>
        /// 컴파일러가 넘기는 참조 목록을 흉내 낸다. 위버가 런타임 어셈블리를 경로로 찾는 것이
        /// 요점이므로, 실제로 로드돼 있는 파일의 경로를 그대로 준다.
        /// </summary>
        private static string[] RealReferences()
        {
            return new[]
            {
                typeof(ArtelInput).Assembly.Location,
                typeof(Input).Assembly.Location,
                typeof(object).Assembly.Location
            };
        }

        /// <summary>
        /// <c>Input.GetKey</c> 를 부르는 것 말고는 아무것도 하지 않는 어셈블리를 만든다.
        /// </summary>
        private static ModuleDefinition BuildGameAssembly(bool callsInput)
        {
            var module = ModuleDefinition.CreateModule(
                "Assembly-CSharp", new ModuleParameters { Kind = ModuleKind.Dll });

            var type = new TypeDefinition(
                "Game", "Player",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(type);

            var method = new MethodDefinition(
                "Update", MethodAttributes.Public, module.TypeSystem.Void);
            type.Methods.Add(method);

            var il = method.Body.GetILProcessor();

            if (callsInput)
            {
                var getKey = module.ImportReference(
                    typeof(Input).GetMethod("GetKey", new[] { typeof(KeyCode) }));

                il.Emit(OpCodes.Ldc_I4, (int)KeyCode.Space);
                il.Emit(OpCodes.Call, getKey);
                il.Emit(OpCodes.Pop);
            }

            il.Emit(OpCodes.Ret);

            // 바이트로 쓴 뒤 다시 읽는다. 갓 만든 모듈은 TypeRef 표가 비어 있어, 위버가 보는
            // 것과 다른 모양이 된다. ILPP 는 언제나 컴파일된 바이트를 읽으므로 그쪽에 맞춘다.
            using (var stream = new MemoryStream())
            {
                module.Write(stream);
                module.Dispose();

                return ModuleDefinition.ReadModule(
                    new MemoryStream(stream.ToArray()),
                    new ReaderParameters(ReadingMode.Immediate));
            }
        }

        private static IEnumerable<string> CalledTypeNames(ModuleDefinition module)
        {
            return module.Types
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Select(instruction => instruction.Operand as MethodReference)
                .Where(called => called != null)
                .Select(called => called.DeclaringType.FullName)
                .ToArray();
        }
    }
}
