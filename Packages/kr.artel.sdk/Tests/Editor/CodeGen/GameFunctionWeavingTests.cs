using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Artel.CodeGen.Tests
{
    /// <summary>
    /// <see cref="GameFunctionWeaver"/> 가 무엇을 거절하고, 받아들인 것으로 무엇을 만드는지.
    /// </summary>
    /// <remarks>
    /// 잘못 쓴 함수는 Unity 가 컴파일하는 어셈블리에 둘 수 없다. 그 어셈블리의 컴파일이 실패하기 때문이다. 그래서 오류 경우는
    /// Cecil 로 만든 모듈로 확인한다. 잘된 경우는 만들어진 모듈을 <c>Assembly.Load</c> 로 올려 등록 코드를 실제로 돌린다 —
    /// IL 이 검증을 통과하는지는 돌려 봐야 안다. Unity 의 ILPP 를 거친 결과는 <c>Artel.GameFunctions.Fixtures</c> 가 따로
    /// 확인한다.
    /// </remarks>
    [TestFixture]
    internal sealed class GameFunctionWeavingTests
    {
        [TestCase("GiveGold", "give_gold")]
        [TestCase("GiveXPNow", "give_xp_now")]
        [TestCase("Level2Up", "level2_up")]
        [TestCase("flee", "flee")]
        public void Names_AreSnakeCase(string methodName, string expected)
        {
            Assert.That(GameFunctionWeaver.SnakeCase(methodName), Is.EqualTo(expected));
        }

        [Test]
        public void AValidStaticCheat_IsRegistered_AndCallable()
        {
            var builder = new FixtureModule();
            var type = builder.AddStaticType("DebugCheats");
            var giveGold = builder.AddMethod(type, "GiveGold", builder.Module.TypeSystem.String, isStatic: true,
                ("amount", builder.Module.TypeSystem.Int32));
            builder.AddRange(giveGold.Parameters[0], 1, 1000);
            builder.MarkCheat(giveGold, "Adds gold.", when: "HasWallet");
            builder.AddMethod(type, "HasWallet", builder.Module.TypeSystem.Boolean, isStatic: true);

            using (var module = builder.Build())
            {
                var weaver = GameFunctionWeaver.TryCreate(module);
                Assert.That(weaver.Process(), Is.True, string.Join("\n", weaver.Errors));

                var registry = new GameFunctionRegistry();
                using (GameFunctionRegistry.Replace(registry))
                {
                    RunRegistration(module);
                }

                Assert.That(registry.TryGet(GameFunctionKind.Cheat, "give_gold", out var function), Is.True);
                Assert.That(function.Description, Is.EqualTo("Adds gold."));
                Assert.That(function.Params.Single().Min, Is.EqualTo(1));
                Assert.That(function.Params.Single().Max, Is.EqualTo(1000));
                Assert.That(function.Call(null, new object[] { 5 }), Is.EqualTo("ok"));
                Assert.That(function.Condition(null), Is.EqualTo(true));
                Assert.That(function.ConditionName, Is.EqualTo("HasWallet"));
            }
        }

        [Test]
        public void AnAssemblyWithoutGameFunctions_IsLeftAlone()
        {
            var builder = new FixtureModule();
            // Artel 타입을 parameter 로 쓰기만 하고 attribute 는 없다. 런타임 참조가 남아 위버가 만들어지는 경우다.
            builder.AddMethod(builder.AddStaticType("Plain"), "Nothing", builder.Module.TypeSystem.Void, isStatic: true,
                ("context", builder.Module.ImportReference(typeof(ArtelBehaviorContext))));

            using (var module = builder.Build())
            {
                var weaver = GameFunctionWeaver.TryCreate(module);

                Assert.That(weaver.Process(), Is.False);
                Assert.That(weaver.Errors, Is.Empty);
                Assert.That(module.GetType("Artel.Generated.GameFunctionRegistration"), Is.Null);
            }
        }

        [Test]
        public void AnUnsupportedParameterType_IsACompileError()
        {
            AssertRefused(builder =>
            {
                var method = builder.AddMethod(builder.AddStaticType("Cheats"), "Warp", builder.Module.TypeSystem.Void,
                    isStatic: true, ("distance", builder.Module.TypeSystem.Double));
                builder.MarkCheat(method, "Warps.");
            }, "has the parameter distance of type System.Double");
        }

        [Test]
        public void DuplicateNames_AreACompileError()
        {
            AssertRefused(builder =>
            {
                var type = builder.AddStaticType("Cheats");
                builder.MarkCheat(builder.AddMethod(type, "GiveGold", builder.Module.TypeSystem.Void, isStatic: true), "a");
                builder.MarkCheat(builder.AddMethod(type, "Other", builder.Module.TypeSystem.Void, isStatic: true), "b", name: "give_gold");
            }, "uses the name give_gold");
        }

        [Test]
        public void AMissingWhen_IsACompileError()
        {
            AssertRefused(builder =>
            {
                var method = builder.AddMethod(builder.AddStaticType("Cheats"), "GiveGold", builder.Module.TypeSystem.Void, isStatic: true);
                builder.MarkCheat(method, "a", when: "HasWallet");
            }, "names when: HasWallet");
        }

        [Test]
        public void AWhenWithTheWrongReturnType_IsACompileError()
        {
            AssertRefused(builder =>
            {
                var type = builder.AddStaticType("Cheats");
                builder.MarkCheat(builder.AddMethod(type, "GiveGold", builder.Module.TypeSystem.Void, isStatic: true), "a", when: "Ready");
                builder.AddMethod(type, "Ready", builder.Module.TypeSystem.Int32, isStatic: true);
            }, "must return bool or string");
        }

        [Test]
        public void AStaticFunctionWithAnInstanceWhen_IsACompileError()
        {
            AssertRefused(builder =>
            {
                var type = builder.AddStaticType("Cheats");
                builder.MarkCheat(builder.AddMethod(type, "GiveGold", builder.Module.TypeSystem.Void, isStatic: true), "a", when: "Ready");
                builder.AddMethod(type, "Ready", builder.Module.TypeSystem.Boolean, isStatic: false);
            }, "must be static too");
        }

        [Test]
        public void ACheatReturningSomethingElse_IsACompileError()
        {
            AssertRefused(builder =>
            {
                builder.MarkCheat(builder.AddMethod(builder.AddStaticType("Cheats"), "Count", builder.Module.TypeSystem.Int32, isStatic: true), "a");
            }, "must return void, string or System.Collections.IEnumerator");
        }

        [Test]
        public void ABehaviorNotReturningIEnumerator_IsACompileError()
        {
            AssertRefused(builder =>
            {
                builder.MarkBehavior(builder.AddMethod(builder.AddStaticType("Behaviors"), "Flee", builder.Module.TypeSystem.Void, isStatic: true), "a");
            }, "must return System.Collections.IEnumerator");
        }

        [Test]
        public void AnInvalidOverrideName_IsACompileError()
        {
            AssertRefused(builder =>
            {
                builder.MarkCheat(builder.AddMethod(builder.AddStaticType("Cheats"), "GiveGold", builder.Module.TypeSystem.Void, isStatic: true), "a", name: "Give-Gold");
            }, "must match ^[a-z][a-z0-9_]*$");
        }

        [Test]
        public void AnInstanceMethodOutsideAMonoBehaviour_IsACompileError()
        {
            AssertRefused(builder =>
            {
                var type = new TypeDefinition("Game", "Plain", TypeAttributes.Public | TypeAttributes.Class, builder.Module.TypeSystem.Object);
                builder.Module.Types.Add(type);
                builder.MarkCheat(builder.AddMethod(type, "Heal", builder.Module.TypeSystem.Void, isStatic: false), "a");
            }, "must derive from UnityEngine.MonoBehaviour");
        }

        [Test]
        public void ContextOnACheat_IsACompileError()
        {
            AssertRefused(builder =>
            {
                var context = builder.Module.ImportReference(typeof(ArtelBehaviorContext));
                var method = builder.AddMethod(builder.AddStaticType("Cheats"), "GiveGold", builder.Module.TypeSystem.Void,
                    isStatic: true, ("context", context));
                builder.MarkCheat(method, "a");
            }, "may take ArtelBehaviorContext only as the last parameter of a behavior");
        }

        private static void AssertRefused(Action<FixtureModule> arrange, string expectedFragment)
        {
            var builder = new FixtureModule();
            arrange(builder);

            using (var module = builder.Build())
            {
                var weaver = GameFunctionWeaver.TryCreate(module);

                Assert.That(weaver.Process(), Is.False);
                Assert.That(weaver.Errors, Has.Some.Contains(expectedFragment), string.Join("\n", weaver.Errors));
                Assert.That(module.GetType("Artel.Generated.GameFunctionRegistration"), Is.Null,
                    "a module with errors must be left unchanged");
            }
        }

        private static void RunRegistration(ModuleDefinition module)
        {
            using (var stream = new MemoryStream())
            {
                module.Write(stream);
                var assembly = Assembly.Load(stream.ToArray());
                var registration = assembly.GetType("Artel.Generated.GameFunctionRegistration");
                Assert.That(registration, Is.Not.Null);
                Assert.That(registration.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)
                        .GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false),
                    Has.Length.EqualTo(1));
                registration.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
        }

        /// <summary>
        /// 함수 몇 개만 든 게임 어셈블리를 Cecil 로 만든다. 메서드 본문은 <c>"ok"</c>, <c>true</c>, 기본값을 돌려줄 뿐이다.
        /// </summary>
        private sealed class FixtureModule
        {
            public ModuleDefinition Module { get; }

            public FixtureModule()
            {
                Module = ModuleDefinition.CreateModule(
                    "GameFunctionFixture" + Guid.NewGuid().ToString("N"), new ModuleParameters { Kind = ModuleKind.Dll });
            }

            public TypeDefinition AddStaticType(string name)
            {
                var type = new TypeDefinition(
                    "Game", name,
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                    Module.TypeSystem.Object);
                Module.Types.Add(type);
                return type;
            }

            public MethodDefinition AddMethod(
                TypeDefinition type, string name, TypeReference returns, bool isStatic,
                params (string name, TypeReference type)[] parameters)
            {
                var attributes = MethodAttributes.Private | MethodAttributes.HideBySig;
                if (isStatic)
                {
                    attributes |= MethodAttributes.Static;
                }

                var method = new MethodDefinition(name, attributes, returns);
                foreach (var (parameterName, parameterType) in parameters)
                {
                    method.Parameters.Add(new ParameterDefinition(parameterName, ParameterAttributes.None, parameterType));
                }

                var il = method.Body.GetILProcessor();
                switch (returns.MetadataType)
                {
                    case MetadataType.Void:
                        break;
                    case MetadataType.String:
                        il.Emit(OpCodes.Ldstr, "ok");
                        break;
                    case MetadataType.Boolean:
                        il.Emit(OpCodes.Ldc_I4_1);
                        break;
                    case MetadataType.Int32:
                        il.Emit(OpCodes.Ldc_I4_0);
                        break;
                    default:
                        il.Emit(OpCodes.Ldnull);
                        break;
                }

                il.Emit(OpCodes.Ret);
                type.Methods.Add(method);
                return method;
            }

            public void MarkCheat(MethodDefinition method, string description, string name = null, string when = null)
            {
                Mark(method, typeof(ArtelCheatAttribute), description, name, when);
            }

            public void MarkBehavior(MethodDefinition method, string description)
            {
                Mark(method, typeof(ArtelBehaviorAttribute), description, null, null, null);
            }

            public void AddRange(ParameterDefinition parameter, double min, double max)
            {
                var attribute = new CustomAttribute(Module.ImportReference(typeof(CheatRangeAttribute).GetConstructors()[0]));
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(Module.TypeSystem.Double, min));
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(Module.TypeSystem.Double, max));
                parameter.CustomAttributes.Add(attribute);
            }

            private void Mark(MethodDefinition method, Type attributeType, params string[] arguments)
            {
                var attribute = new CustomAttribute(Module.ImportReference(attributeType.GetConstructors()[0]));
                foreach (var argument in arguments)
                {
                    attribute.ConstructorArguments.Add(new CustomAttributeArgument(Module.TypeSystem.String, argument));
                }

                method.CustomAttributes.Add(attribute);
            }

            /// <summary>
            /// 바이트로 쓴 뒤 다시 읽는다. ILPP 는 언제나 컴파일된 바이트를 읽으므로 그쪽에 맞춘다. resolver 에는 컴파일러가
            /// 넘기는 것처럼 실제 파일 경로를 준다.
            /// </summary>
            public ModuleDefinition Build()
            {
                using (var stream = new MemoryStream())
                {
                    Module.Write(stream);
                    Module.Dispose();

                    var references = new List<string>
                    {
                        typeof(ArtelCheatAttribute).Assembly.Location,
                        typeof(MonoBehaviour).Assembly.Location,
                        typeof(object).Assembly.Location
                    };
                    var netstandard = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(assembly => assembly.GetName().Name == "netstandard");
                    if (netstandard != null)
                    {
                        references.Add(netstandard.Location);
                    }

                    return ModuleDefinition.ReadModule(
                        new MemoryStream(stream.ToArray()),
                        new ReaderParameters(ReadingMode.Immediate)
                        {
                            AssemblyResolver = new CompiledAssemblyResolver(references)
                        });
                }
            }
        }
    }
}
