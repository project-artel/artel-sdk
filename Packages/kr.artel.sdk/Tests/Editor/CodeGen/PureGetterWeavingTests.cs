using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Artel.Affordances;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;

namespace Artel.CodeGen.Tests
{
    /// <summary>
    /// 계산 프로퍼티 중 getter 가 순수한 것에만 <c>ArtelPureGetterAttribute</c> 가 붙는지(ARTEL-1294). 게임 어셈블리는
    /// <see cref="EngineCallWeavingTests"/> 처럼 Cecil 로 만든다. <c>Lock</c> 은 WordVenture 의 <c>Core.InteractionLock</c> 과
    /// 같은 IL 이다(<c>IsLocked =&gt; locked || IsPaused</c>, <c>IsPaused =&gt; paused || Time.frameCount == resumedFrame</c>).
    /// </summary>
    [TestFixture]
    internal sealed class PureGetterWeavingTests
    {
        private const string Mark = "Artel.Affordances.ArtelPureGetterAttribute";

        [Test]
        public void 읽기와_산술과_허용된_엔진_읽기만_하는_getter_는_표시되고_게임_getter_는_재귀로_검사한다()
        {
            using (var module = BuildGameAssembly())
            {
                var weaver = PureGetterWeaver.TryCreate(module, RealReferences());

                Assert.That(weaver, Is.Not.Null);
                Assert.That(weaver.Process(), Is.True);

                Assert.That(Marked(module), Is.EquivalentTo(new[] { "Lock::IsPaused", "Lock::IsLocked", "Unit::Hp" }));
                Assert.That(weaver.Proven, Is.EqualTo(3));
                Assert.That(weaver.Refused, Is.EqualTo(4));
            }
        }

        [Test]
        public void 쓰기와_생성과_모르는_호출과_override_될_수_있는_호출은_이유와_함께_떨어진다()
        {
            using (var module = BuildGameAssembly())
            {
                var purity = new GetterPurity(module);

                Assert.That(purity.Refusal(Getter(module, "Lock", "Cached")), Does.StartWith("writes Game.Lock::cache"));
                Assert.That(purity.Refusal(Getter(module, "Lock", "Saved")), Is.EqualTo("calls UnityEngine.PlayerPrefs::HasKey"));
                Assert.That(purity.Refusal(Getter(module, "Lock", "Fresh")), Is.EqualTo("creates System.Object"));
                Assert.That(purity.Refusal(Getter(module, "Unit", "Shown")), Does.Contain("which a subclass may override"));
                Assert.That(purity.Refusal(Getter(module, "Lock", "IsLocked")), Is.Null);
            }
        }

        [Test]
        public void 자동_프로퍼티와_이미_ArtelObserve_가_있는_프로퍼티는_건드리지_않는다()
        {
            using (var module = BuildGameAssembly())
            {
                PureGetterWeaver.TryCreate(module, RealReferences()).Process();

                Assert.That(Marked(module), Does.Not.Contain("Unit::Title"));
                Assert.That(Marked(module), Does.Not.Contain("Unit::Observed"));
            }
        }

        [Test]
        public void 표시_타입이_든_어셈블리를_못_찾으면_위버를_만들지_않는다()
        {
            using (var module = BuildGameAssembly())
            {
                Assert.That(PureGetterWeaver.TryCreate(module, new[] { typeof(object).Assembly.Location }), Is.Null);
            }
        }

        private static string[] RealReferences()
        {
            return new[]
            {
                typeof(ArtelPureGetterAttribute).Assembly.Location,
                typeof(Time).Assembly.Location,
                typeof(object).Assembly.Location
            };
        }

        private static string[] Marked(ModuleDefinition module)
        {
            return module.GetTypes().SelectMany(type => type.Properties
                    .Where(property => property.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == Mark))
                    .Select(property => type.Name + "::" + property.Name))
                .ToArray();
        }

        private static MethodDefinition Getter(ModuleDefinition module, string type, string property)
        {
            return module.GetTypes().Single(candidate => candidate.Name == type)
                .Properties.Single(candidate => candidate.Name == property).GetMethod;
        }

        private static ModuleDefinition BuildGameAssembly()
        {
            var module = ModuleDefinition.CreateModule("Assembly-CSharp", new ModuleParameters { Kind = ModuleKind.Dll });
            var types = module.TypeSystem;

            var lockType = new TypeDefinition("Game", "Lock",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class, types.Object);
            module.Types.Add(lockType);

            var locked = Field(lockType, "locked", types.Boolean, true);
            var paused = Field(lockType, "paused", types.Boolean, true);
            var resumedFrame = Field(lockType, "resumedFrame", types.Int32, true);
            var cache = Field(lockType, "cache", types.Int32, true);

            var isPaused = Property(lockType, "IsPaused", types.Boolean, true, il =>
            {
                var yes = il.Create(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldsfld, paused);
                il.Emit(OpCodes.Brtrue_S, yes);
                il.Emit(OpCodes.Call, module.ImportReference(typeof(Time).GetProperty("frameCount").GetGetMethod()));
                il.Emit(OpCodes.Ldsfld, resumedFrame);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ret);
                il.Append(yes);
                il.Emit(OpCodes.Ret);
            });

            Property(lockType, "IsLocked", types.Boolean, true, il =>
            {
                var yes = il.Create(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldsfld, locked);
                il.Emit(OpCodes.Brtrue_S, yes);
                il.Emit(OpCodes.Call, isPaused);
                il.Emit(OpCodes.Ret);
                il.Append(yes);
                il.Emit(OpCodes.Ret);
            });

            // lazy 캐시: 값을 쓴다.
            Property(lockType, "Cached", types.Int32, true, il =>
            {
                il.Emit(OpCodes.Ldc_I4_7);
                il.Emit(OpCodes.Stsfld, cache);
                il.Emit(OpCodes.Ldsfld, cache);
                il.Emit(OpCodes.Ret);
            });

            // 허용 목록에 없는 엔진 호출.
            Property(lockType, "Saved", types.Boolean, true, il =>
            {
                il.Emit(OpCodes.Ldstr, "slot");
                il.Emit(OpCodes.Call, module.ImportReference(typeof(PlayerPrefs).GetMethod("HasKey")));
                il.Emit(OpCodes.Ret);
            });

            // 참조 타입 생성.
            Property(lockType, "Fresh", types.Object, true, il =>
            {
                il.Emit(OpCodes.Newobj, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)));
                il.Emit(OpCodes.Ret);
            });

            var unit = new TypeDefinition("Game", "Unit", TypeAttributes.Public | TypeAttributes.Class, types.Object);
            module.Types.Add(unit);
            var hp = Field(unit, "hp", types.Int32, false);

            var hpGetter = Property(unit, "Hp", types.Int32, false, il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, hp);
                il.Emit(OpCodes.Ret);
            }, isVirtual: true);

            // override 될 수 있는 getter 를 부른다: 실제로 어느 몸체가 돌지 빌드 때 모른다.
            Property(unit, "Shown", types.Int32, false, il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, hpGetter);
                il.Emit(OpCodes.Ret);
            });

            // 자동 프로퍼티: backing field 를 읽는 컴파일러 생성 getter.
            var backing = Field(unit, "<Title>k__BackingField", types.String, false);
            var title = Property(unit, "Title", types.String, false, il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, backing);
                il.Emit(OpCodes.Ret);
            });
            title.CustomAttributes.Add(new CustomAttribute(
                module.ImportReference(typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes))));

            // 게임이 이미 허락한 계산 getter.
            var observed = Property(unit, "Observed", types.Int32, false, il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, hp);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ret);
            });
            unit.Properties.Single(property => property.Name == "Observed").CustomAttributes.Add(new CustomAttribute(
                module.ImportReference(typeof(ArtelObserveAttribute).GetConstructor(Type.EmptyTypes))));

            using (var stream = new MemoryStream())
            {
                module.Write(stream);
                module.Dispose();

                return ModuleDefinition.ReadModule(new MemoryStream(stream.ToArray()), new ReaderParameters(ReadingMode.Immediate));
            }
        }

        private static FieldDefinition Field(TypeDefinition type, string name, TypeReference fieldType, bool isStatic)
        {
            var field = new FieldDefinition(name,
                FieldAttributes.Private | (isStatic ? FieldAttributes.Static : 0), fieldType);
            type.Fields.Add(field);
            return field;
        }

        private static MethodDefinition Property(TypeDefinition type, string name, TypeReference propertyType, bool isStatic,
            Action<ILProcessor> body, bool isVirtual = false)
        {
            var attributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName;
            if (isStatic)
            {
                attributes |= MethodAttributes.Static;
            }
            if (isVirtual)
            {
                attributes |= MethodAttributes.Virtual | MethodAttributes.NewSlot;
            }

            var getter = new MethodDefinition("get_" + name, attributes, propertyType);
            type.Methods.Add(getter);
            body(getter.Body.GetILProcessor());

            var property = new PropertyDefinition(name, PropertyAttributes.None, propertyType) { GetMethod = getter };
            type.Properties.Add(property);
            return getter;
        }
    }
}
