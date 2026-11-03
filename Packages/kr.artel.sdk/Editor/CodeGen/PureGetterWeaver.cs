using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.CodeGen
{
    /// <summary>
    /// 계산 프로퍼티 중 getter 가 순수하다고 증명된 것에 <c>ArtelPureGetterAttribute</c> 를 붙인다(ARTEL-1294).
    /// </summary>
    /// <remarks>
    /// 자동 프로퍼티는 건너뛴다 — <c>Lookup</c> 이 backing field 를 바로 읽는다. 이미 <c>[ArtelObserve]</c> 가 붙은 것도 건너뛴다.
    /// 인덱서는 이름으로 청할 수 없어 건너뛴다.
    ///
    /// 다른 위버보다 먼저 돈다. 엔진 호출 위버가 <c>PlayerPrefs.SetInt</c> 같은 호출을 <c>ArtelEngine</c> 프록시로 바꾸고 나면
    /// 이 판정이 원래 무엇을 불렀는지 보지 못한다.
    /// </remarks>
    internal sealed class PureGetterWeaver
    {
        private const string MarkName = "Artel.Affordances.ArtelPureGetterAttribute";
        private const string ObserveName = "Artel.Affordances.ArtelObserveAttribute";

        private readonly ModuleDefinition module;
        private readonly MethodReference markConstructor;

        internal int Proven { get; private set; }

        internal int Refused { get; private set; }

        private PureGetterWeaver(ModuleDefinition module, MethodReference markConstructor)
        {
            this.module = module;
            this.markConstructor = markConstructor;
        }

        // 표시 타입이 사는 어셈블리. `[ArtelObserve]` 와 같은 곳이고, autoReferenced 라 게임 어셈블리가 늘 참조한다.
        private const string MarkAssemblyName = "Artel.Affordances.Runtime";

        /// <summary>표시 타입이 든 어셈블리를 못 찾거나 그것이 표시 타입을 모르면 null — 옛 SDK 와 함께 컴파일되는 경우다.</summary>
        internal static PureGetterWeaver TryCreate(ModuleDefinition module, string[] references)
        {
            var runtimePath = references?.FirstOrDefault(reference =>
                !string.IsNullOrEmpty(reference) &&
                Path.GetFileNameWithoutExtension(reference) == MarkAssemblyName && File.Exists(reference));

            if (runtimePath == null || module.Assembly?.Name.Name == MarkAssemblyName)
            {
                return null;
            }

            using (var runtime = AssemblyDefinition.ReadAssembly(
                       runtimePath, new ReaderParameters(ReadingMode.Deferred) { AssemblyResolver = module.AssemblyResolver }))
            {
                var mark = runtime.MainModule.GetType(MarkName);
                var constructor = mark?.Methods.FirstOrDefault(method => method.IsConstructor && !method.HasParameters);

                return constructor == null ? null : new PureGetterWeaver(module, module.ImportReference(constructor));
            }
        }

        internal bool Process()
        {
            var purity = new GetterPurity(module);
            var changed = false;

            foreach (var type in module.GetTypes())
            {
                foreach (var property in type.Properties)
                {
                    var getter = property.GetMethod;

                    if (getter == null || !getter.HasBody || getter.HasParameters || IsAuto(getter) || Marked(property))
                    {
                        continue;
                    }

                    if (purity.Refusal(getter) != null)
                    {
                        Refused++;
                        continue;
                    }

                    property.CustomAttributes.Add(new CustomAttribute(markConstructor));
                    Proven++;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>컴파일러가 만든 자동 프로퍼티 getter — backing field 를 읽고 반환할 뿐이다.</summary>
        private static bool IsAuto(MethodDefinition getter)
        {
            return getter.CustomAttributes.Any(attribute =>
                       attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute") &&
                   getter.Body.Instructions.Any(instruction =>
                       (instruction.OpCode.Code == Code.Ldfld || instruction.OpCode.Code == Code.Ldsfld) &&
                       ((FieldReference)instruction.Operand).Name.EndsWith(">k__BackingField"));
        }

        private static bool Marked(PropertyDefinition property)
        {
            return property.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == MarkName || attribute.AttributeType.FullName == ObserveName);
        }
    }
}
