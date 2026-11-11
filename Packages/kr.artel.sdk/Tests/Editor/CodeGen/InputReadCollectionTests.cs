using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;

namespace Artel.CodeGen.Tests
{
    /// <summary>
    /// 위버가 호출 지점에서 게임이 읽는 키와 축을 모으고, 그것을 알리는 등록 메서드를 만드는지(ARTEL-1412).
    /// </summary>
    /// <remarks>
    /// 리터럴만 믿는다. 인자가 리터럴이 아니거나 분기에서 합쳐지면 "모른다"로 남겨야, 실행 중 판정이 읽히는 키를 막지 않는다.
    /// </remarks>
    [TestFixture]
    internal sealed class InputReadCollectionTests
    {
        [Test]
        public void CollectsLiteralKeysAndAxes_AndMarksValuesItCannotRead()
        {
            using (var module = BuildGameAssembly(il =>
                   {
                       Call(il, OpCodes.Ldc_I4, (int)KeyCode.UpArrow, "GetKeyDown", typeof(KeyCode));
                       Call(il, OpCodes.Ldc_I4_S, (sbyte)KeyCode.Space, "GetKey", typeof(KeyCode));
                       // KeyCode.Backspace 는 8 이라 컴파일러가 operand 없는 ldc.i4.8 을 쓴다.
                       il.Emit(OpCodes.Ldc_I4_8);
                       il.Emit(OpCodes.Call, InputMethod(il, "GetKey", typeof(KeyCode)));
                       il.Emit(OpCodes.Pop);
                       CallString(il, "escape", "GetKeyUp");
                       CallString(il, "Jump", "GetButtonDown");
                       CallString(il, "Horizontal", "GetAxis");
                   }))
            {
                var weaver = Weave(module);

                Assert.That(weaver.Reads.KeyCodes, Is.EquivalentTo(
                    new[] { (int)KeyCode.Backspace, (int)KeyCode.Space, (int)KeyCode.UpArrow }));
                Assert.That(weaver.Reads.KeyNames, Is.EquivalentTo(new[] { "escape" }));
                Assert.That(weaver.Reads.AxisNames, Is.EquivalentTo(new[] { "Horizontal", "Jump" }));
                Assert.That(weaver.Reads.ReadsUnknownKeys, Is.False);
                Assert.That(weaver.Reads.ReadsUnknownAxes, Is.False);
            }
        }

        [Test]
        public void AKeyOrAxisFromAVariable_IsUnknown()
        {
            using (var module = BuildGameAssembly(il =>
                   {
                       il.Body.Variables.Add(new VariableDefinition(il.Body.Method.Module.TypeSystem.Int32));
                       il.Body.Variables.Add(new VariableDefinition(il.Body.Method.Module.TypeSystem.String));
                       il.Emit(OpCodes.Ldloc_0);
                       il.Emit(OpCodes.Call, InputMethod(il, "GetKey", typeof(KeyCode)));
                       il.Emit(OpCodes.Pop);
                       il.Emit(OpCodes.Ldloc_1);
                       il.Emit(OpCodes.Call, InputMethod(il, "GetButton", typeof(string)));
                       il.Emit(OpCodes.Pop);
                   }))
            {
                var weaver = Weave(module);

                Assert.That(weaver.Reads.ReadsUnknownKeys, Is.True);
                Assert.That(weaver.Reads.ReadsUnknownAxes, Is.True);
                Assert.That(weaver.Reads.KeyCodes, Is.Empty);
            }
        }

        [Test]
        public void AnyKey_MeansAnyKeyMayBeRead()
        {
            using (var module = BuildGameAssembly(il =>
                   {
                       il.Emit(OpCodes.Call, il.Body.Method.Module.ImportReference(
                           typeof(Input).GetProperty("anyKeyDown").GetGetMethod()));
                       il.Emit(OpCodes.Pop);
                   }))
            {
                Assert.That(Weave(module).Reads.ReadsUnknownKeys, Is.True);
            }
        }

        /// <summary>
        /// <c>Input.GetKey(fast ? KeyCode.A : KeyCode.B)</c> 는 호출 바로 앞에 B 의 상수만 보인다. A 를 놓치면 A 를 보내는
        /// 명령이 잘못 실패한다.
        /// </summary>
        [Test]
        public void AValueMergedFromABranch_IsUnknown()
        {
            using (var module = BuildGameAssembly(il =>
                   {
                       var pushB = il.Create(OpCodes.Ldc_I4, (int)KeyCode.B);
                       var call = il.Create(OpCodes.Call, InputMethod(il, "GetKey", typeof(KeyCode)));
                       il.Emit(OpCodes.Ldc_I4_0);
                       il.Emit(OpCodes.Brfalse, pushB);
                       il.Emit(OpCodes.Ldc_I4, (int)KeyCode.A);
                       il.Emit(OpCodes.Br, call);
                       il.Append(pushB);
                       il.Append(call);
                       il.Emit(OpCodes.Pop);
                   }))
            {
                var reads = Weave(module).Reads;

                Assert.That(reads.ReadsUnknownKeys, Is.True);
                Assert.That(reads.KeyCodes, Is.Empty);
            }
        }

        [Test]
        public void EmitsARegistrationThatDeclaresWhatWasRead()
        {
            using (var module = BuildGameAssembly(il =>
                   {
                       Call(il, OpCodes.Ldc_I4, (int)KeyCode.UpArrow, "GetKeyDown", typeof(KeyCode));
                       CallString(il, "Jump", "GetButtonDown");
                   }))
            {
                Weave(module);

                var registration = module.GetType(
                    InputMethodWeaver.RegistrationNamespace + "." + InputMethodWeaver.RegistrationTypeName);
                Assert.That(registration, Is.Not.Null, "등록 타입이 없다.");

                var register = registration.Methods.Single(method => method.Name == "Register");
                Assert.That(register.IsStatic, Is.True);
                Assert.That(
                    register.CustomAttributes.Select(attribute => attribute.AttributeType.FullName),
                    Does.Contain("UnityEngine.RuntimeInitializeOnLoadMethodAttribute"));

                var instructions = register.Body.Instructions;
                var declare = instructions.Single(instruction => instruction.OpCode == OpCodes.Call);
                Assert.That(((MethodReference)declare.Operand).DeclaringType.FullName, Is.EqualTo("Artel.InputReadManifest"));
                var strings = instructions.Where(instruction => instruction.OpCode == OpCodes.Ldstr)
                    .Select(instruction => (string)instruction.Operand).ToArray();
                Assert.That(strings, Is.EqualTo(new[] { "Assembly-CSharp", ((int)KeyCode.UpArrow).ToString(), "", "Jump" }));
            }
        }

        /// <summary>테스트 assembly 의 Input 호출이 실행 중 판정에 섞이지 않는다.</summary>
        [Test]
        public void ATestAssembly_GetsNoRegistration()
        {
            using (var module = BuildGameAssembly(
                       il => Call(il, OpCodes.Ldc_I4, (int)KeyCode.UpArrow, "GetKeyDown", typeof(KeyCode)),
                       referenceNUnit: true))
            {
                Assert.That(Weave(module).Process(), Is.False, "같은 모듈을 두 번 위빙했다.");
                Assert.That(
                    module.GetType(InputMethodWeaver.RegistrationNamespace + "." + InputMethodWeaver.RegistrationTypeName),
                    Is.Null);
            }
        }

        private static InputMethodWeaver Weave(ModuleDefinition module)
        {
            var attempt = InputMethodWeaver.TryCreate(module, RealReferences());
            Assert.That(attempt.Refusal, Is.Null);
            Assert.That(attempt.Weaver, Is.Not.Null);
            Assert.That(attempt.Weaver.Process(), Is.True);
            return attempt.Weaver;
        }

        private static void Call(ILProcessor il, OpCode push, object value, string name, Type parameter)
        {
            if (push == OpCodes.Ldc_I4_S) il.Emit(push, (sbyte)value);
            else il.Emit(push, (int)value);
            il.Emit(OpCodes.Call, InputMethod(il, name, parameter));
            il.Emit(OpCodes.Pop);
        }

        private static void CallString(ILProcessor il, string value, string name)
        {
            il.Emit(OpCodes.Ldstr, value);
            il.Emit(OpCodes.Call, InputMethod(il, name, typeof(string)));
            il.Emit(OpCodes.Pop);
        }

        private static MethodReference InputMethod(ILProcessor il, string name, Type parameter)
        {
            return il.Body.Method.Module.ImportReference(typeof(Input).GetMethod(name, new[] { parameter }));
        }

        private static string[] RealReferences()
        {
            return new[]
            {
                typeof(ArtelInput).Assembly.Location,
                typeof(Input).Assembly.Location,
                typeof(UnityEngine.EventSystems.EventSystem).Assembly.Location,
                typeof(object).Assembly.Location
            };
        }

        private static ModuleDefinition BuildGameAssembly(Action<ILProcessor> body, bool referenceNUnit = false)
        {
            var module = ModuleDefinition.CreateModule(
                "Assembly-CSharp", new ModuleParameters { Kind = ModuleKind.Dll });
            if (referenceNUnit)
                module.AssemblyReferences.Add(new AssemblyNameReference("nunit.framework", new Version(3, 5, 0, 0)));

            var type = new TypeDefinition("Game", "Player", TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(type);
            var method = new MethodDefinition("Update", MethodAttributes.Public, module.TypeSystem.Void);
            type.Methods.Add(method);
            var il = method.Body.GetILProcessor();
            body(il);
            il.Emit(OpCodes.Ret);

            // 등록 메서드는 RuntimeInitializeOnLoadMethodAttribute 를 UnityEngine.CoreModule 에서 찾는다. ILPP 의 resolver 처럼
            // 엔진 assembly 폴더를 볼 수 있게 한다.
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Input).Assembly.Location));
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(ArtelInput).Assembly.Location));
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));

            using (var stream = new MemoryStream())
            {
                module.Write(stream);
                module.Dispose();
                return ModuleDefinition.ReadModule(
                    new MemoryStream(stream.ToArray()),
                    new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = resolver });
            }
        }
    }
}
