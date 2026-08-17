using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Artel.Tracking;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.CodeGen
{
    public sealed class ArtelILPostProcessor : ILPostProcessor
    {
        private const string RuntimeAssemblyName = "Artel.Runtime";
        private const string AttributesAssemblyName = "Artel.Attributes";
        private const string CodeGenAssemblyName = "Unity.Artel.CodeGen";
        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name == RuntimeAssemblyName ||
                compiledAssembly.Name == AttributesAssemblyName ||
                compiledAssembly.Name == CodeGenAssemblyName)
            {
                return false;
            }

            return compiledAssembly.References.Any(IsSdkAssembly);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            using (var resolver = new CompiledAssemblyResolver(compiledAssembly.References))
            using (var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData))
            using (var pdbStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData ?? Array.Empty<byte>()))
            {
                var hasSymbols = pdbStream.Length > 0;
                var reader = new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Immediate,
                    ReadSymbols = hasSymbols,
                    SymbolStream = hasSymbols ? pdbStream : null,
                    SymbolReaderProvider = hasSymbols ? new PortablePdbReaderProvider() : null
                };

                using (var assembly = AssemblyDefinition.ReadAssembly(peStream, reader))
                {
                    // 릴리스 빌드에서는 Artel.Runtime이 defineConstraints 때문에 컴파일되지 않는다.
                    // 위빙이 심는 타입이 존재하지 않으므로 여기서 멈춰야 한다. 게임 코드는
                    // 어트리뷰트만 남은 채 그대로 컴파일된다.
                    var runtimeModule = TryResolveRuntimeModule(assembly.MainModule);
                    if (runtimeModule == null)
                    {
                        ReportMissingRuntime(compiledAssembly, assembly.MainModule, diagnostics);
                        return new ILPostProcessResult(null, diagnostics);
                    }

                    // WillProcess가 통과시켰다고 해서 위빙할 게 있다는 뜻은 아니다. 거기서 보는
                    // 컴파일러 참조 목록에는 autoReferenced 때문에 SDK 어셈블리가 항상 들어 있고,
                    // 실제로 SDK 타입을 쓰는지는 IL 메타데이터를 열어 봐야 안다.
                    var actionWeaver = ActionMethodWeaver.TryCreate(assembly.MainModule, runtimeModule, diagnostics);
                    var inputWeaver = InputMethodWeaver.TryCreate(assembly.MainModule, runtimeModule);
                    if (actionWeaver == null && inputWeaver == null)
                    {
                        return new ILPostProcessResult(null, diagnostics);
                    }

                    var changed = actionWeaver != null && actionWeaver.Process();
                    changed |= inputWeaver != null && inputWeaver.Process();
                    if (!changed)
                    {
                        return new ILPostProcessResult(null, diagnostics);
                    }

                    using (var outputPe = new MemoryStream())
                    using (var outputPdb = new MemoryStream())
                    {
                        assembly.Write(outputPe, new WriterParameters
                        {
                            WriteSymbols = hasSymbols,
                            SymbolStream = hasSymbols ? outputPdb : null,
                            SymbolWriterProvider = hasSymbols ? new PortablePdbWriterProvider() : null
                        });

                        return new ILPostProcessResult(
                            new InMemoryAssembly(outputPe.ToArray(), hasSymbols ? outputPdb.ToArray() : null),
                            diagnostics);
                    }
                }
            }
        }

        private static bool IsSdkAssembly(string reference)
        {
            var name = Path.GetFileNameWithoutExtension(reference);
            return string.Equals(name, RuntimeAssemblyName, StringComparison.Ordinal) ||
                   string.Equals(name, AttributesAssemblyName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 위빙이 심을 타입이 사는 <c>Artel.Runtime</c> 모듈. 컴파일되지 않았으면 null.
        /// </summary>
        /// <remarks>
        /// 대상 어셈블리의 IL 참조 목록이 아니라 컴파일러에 넘어온 참조 경로에서 이름으로 찾는다.
        /// 어트리뷰트만 쓰는 게임 어셈블리는 <c>Artel.Attributes</c>만 IL에 남기므로, IL 참조로
        /// 찾으면 위빙이 필요한 어셈블리에서 런타임을 못 찾는다.
        /// </remarks>
        private static ModuleDefinition TryResolveRuntimeModule(ModuleDefinition module)
        {
            try
            {
                var reference = new AssemblyNameReference(RuntimeAssemblyName, new Version(0, 0, 0, 0));
                return module.AssemblyResolver.Resolve(reference).MainModule;
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }

        /// <summary>
        /// 런타임이 없는데 <c>[ArtelAction]</c>이 붙어 있는, 릴리스가 아닌 컴파일을 에러로 알린다.
        /// </summary>
        /// <remarks>
        /// 릴리스 빌드에서 런타임이 없는 것은 설계대로다. 반면 Editor나 개발 빌드에서 없다면
        /// 대상 asmdef가 <c>Artel.Runtime</c>을 참조하지 않은 설정 실수이고, 조용히 넘어가면
        /// 액션 추적이 말없이 죽는다.
        /// </remarks>
        private static void ReportMissingRuntime(
            ICompiledAssembly compiledAssembly,
            ModuleDefinition module,
            List<DiagnosticMessage> diagnostics)
        {
            var sdkCompiles = compiledAssembly.Defines != null &&
                              compiledAssembly.Defines.Any(define =>
                                  define == "UNITY_EDITOR" || define == "DEVELOPMENT_BUILD");
            if (!sdkCompiles || !ActionMethodWeaver.ContainsActionAttribute(module))
            {
                return;
            }

            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Error,
                MessageData = "[Artel] " + compiledAssembly.Name +
                              " uses [ArtelAction] but does not reference the " + RuntimeAssemblyName +
                              " assembly. Add it to the assembly definition references."
            });
        }
    }
}
