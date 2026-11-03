using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;

namespace Artel.CodeGen.Tests
{
    /// <summary>
    /// 엔진 API 호출이 <c>ArtelEngine</c> 프록시로 바뀌는지(ARTEL-1283). <see cref="InputWeavingTests"/> 와 같은 방식으로
    /// Cecil 로 게임 어셈블리를 만들어 먹인다.
    /// </summary>
    [TestFixture]
    internal sealed class EngineCallWeavingTests
    {
        private const string ProxyTypeName = "Artel.ArtelEngine";

        [Test]
        public void 인스턴스_호출은_인스턴스를_첫_인자로_받는_static_프록시로_바뀌고_callvirt_는_call_이_된다()
        {
            using (var module = BuildGameAssembly(callsEngine: true))
            {
                var attempt = EngineCallWeaver.TryCreate(module, RealReferences());

                Assert.That(attempt.Refusal, Is.Null);
                Assert.That(attempt.Weaver, Is.Not.Null);
                Assert.That(attempt.Weaver.Process(), Is.True);

                var calls = Calls(module);
                var play = calls.Single(call => ((MethodReference)call.Operand).Name == "Play");

                Assert.That(((MethodReference)play.Operand).DeclaringType.FullName, Is.EqualTo(ProxyTypeName));
                Assert.That(((MethodReference)play.Operand).Parameters[0].ParameterType.FullName, Is.EqualTo("UnityEngine.AudioSource"));
                Assert.That(play.OpCode, Is.EqualTo(OpCodes.Call));
            }
        }

        [Test]
        public void static_호출과_generic_Instantiate_도_바뀐다()
        {
            using (var module = BuildGameAssembly(callsEngine: true))
            {
                EngineCallWeaver.TryCreate(module, RealReferences()).Weaver.Process();

                var calls = Calls(module);
                var save = calls.Single(call => ((MethodReference)call.Operand).Name == "Save");
                var instantiate = calls.Single(call => ((MethodReference)call.Operand).Name == "Instantiate");

                Assert.That(((MethodReference)save.Operand).DeclaringType.FullName, Is.EqualTo(ProxyTypeName));
                Assert.That(((MethodReference)instantiate.Operand).DeclaringType.FullName, Is.EqualTo(ProxyTypeName));
                Assert.That(instantiate.Operand, Is.InstanceOf<GenericInstanceMethod>());
                Assert.That(((GenericInstanceMethod)instantiate.Operand).GenericArguments[0].FullName, Is.EqualTo("UnityEngine.GameObject"));

                Assert.That(CalledTypeNames(module), Does.Not.Contain("UnityEngine.AudioSource"));
                Assert.That(CalledTypeNames(module), Does.Not.Contain("UnityEngine.PlayerPrefs"));
                Assert.That(CalledTypeNames(module), Does.Not.Contain("UnityEngine.Object"));
            }
        }

        [Test]
        public void 엔진_호출이_없는_어셈블리는_그대로_둔다()
        {
            using (var module = BuildGameAssembly(callsEngine: false))
            {
                var attempt = EngineCallWeaver.TryCreate(module, RealReferences());

                // 타입 참조(필드·매개변수)만으로는 위버가 만들어질 수 있다. 바꾸는 것이 없어야 한다.
                Assert.That(attempt.Refusal, Is.Null);
                Assert.That(attempt.Weaver == null || !attempt.Weaver.Process(), Is.True, "바꿀 호출이 없는데 바꿨다.");
            }
        }

        [Test]
        public void 런타임을_못_찾으면_소리_내어_거절한다()
        {
            using (var module = BuildGameAssembly(callsEngine: true))
            {
                var attempt = EngineCallWeaver.TryCreate(module, new[] { typeof(AudioSource).Assembly.Location });

                Assert.That(attempt.Weaver, Is.Null);
                Assert.That(attempt.Refusal, Is.Not.Null.And.Contains("Artel.Runtime"));
            }
        }

        private static string[] RealReferences()
        {
            return new[]
            {
                typeof(ArtelEngine).Assembly.Location,
                typeof(AudioSource).Assembly.Location,
                typeof(object).Assembly.Location
            };
        }

        /// <summary><c>source.Play()</c>, <c>PlayerPrefs.Save()</c>, <c>Object.Instantiate&lt;GameObject&gt;(prefab)</c> 를 부르는 어셈블리.</summary>
        private static ModuleDefinition BuildGameAssembly(bool callsEngine)
        {
            var module = ModuleDefinition.CreateModule("Assembly-CSharp", new ModuleParameters { Kind = ModuleKind.Dll });

            var type = new TypeDefinition("Game", "Spawner", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);

            var method = new MethodDefinition("Fire", MethodAttributes.Public, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(AudioSource))));
            method.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(GameObject))));
            type.Methods.Add(method);

            var il = method.Body.GetILProcessor();

            if (callsEngine)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(AudioSource).GetMethod("Play", System.Type.EmptyTypes)));

                il.Emit(OpCodes.Call, module.ImportReference(typeof(PlayerPrefs).GetMethod("Save")));

                var instantiate = typeof(Object).GetMethods()
                    .Single(candidate => candidate.Name == "Instantiate" && candidate.IsGenericMethodDefinition &&
                                         candidate.GetParameters().Length == 1)
                    .MakeGenericMethod(typeof(GameObject));
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, module.ImportReference(instantiate));
                il.Emit(OpCodes.Pop);
            }

            il.Emit(OpCodes.Ret);

            using (var stream = new MemoryStream())
            {
                module.Write(stream);
                module.Dispose();

                return ModuleDefinition.ReadModule(new MemoryStream(stream.ToArray()), new ReaderParameters(ReadingMode.Immediate));
            }
        }

        private static Instruction[] Calls(ModuleDefinition module)
        {
            return module.Types.SelectMany(type => type.Methods).Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Where(instruction => instruction.Operand is MethodReference)
                .ToArray();
        }

        private static string[] CalledTypeNames(ModuleDefinition module)
        {
            return Calls(module).Select(call => ((MethodReference)call.Operand).DeclaringType.FullName).ToArray();
        }
    }
}
