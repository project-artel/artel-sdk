using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.CodeGen
{
    public sealed class ArtelILPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => this;

        /// <remarks>
        /// 참조를 묻지 않는다. 예전에는 <c>Artel.Runtime</c> 이 참조 목록에 있는지 보았는데,
        /// 그것은 위빙할 것이 있는지와 무관한 물음이었다(ARTEL-383). 무엇을 건너뛸지는 각
        /// 위버가 <see cref="Process"/> 안에서 정한다. 거기서 내린 결정만이 진단으로 남을 수 있다.
        /// </remarks>
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return WeavableAssemblies.SupportsTarget(compiledAssembly.Defines) &&
                   !WeavableAssemblies.IsSkipped(compiledAssembly.Name);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            if (!WeavableAssemblies.SupportsTarget(compiledAssembly.Defines))
            {
                return new ILPostProcessResult(null, diagnostics);
            }
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
                    // WillProcess가 통과시켰다고 해서 위빙할 게 있다는 뜻은 아니다. 거기서 보는
                    // 컴파일러 참조 목록에는 autoReferenced 때문에 Artel.Runtime이 항상 들어 있고,
                    // 실제로 SDK 타입을 쓰는지는 IL 메타데이터를 열어 봐야 안다.
                    var actionWeaver = ActionMethodWeaver.TryCreate(assembly.MainModule, diagnostics);
                    var inputAttempt = InputMethodWeaver.TryCreate(
                        assembly.MainModule, compiledAssembly.References);

                    if (inputAttempt.Refusal != null)
                    {
                        // ILPP 의 경고는 빌드가 성공하면 콘솔에 뜨지 않고 Editor.log 에만 남는다.
                        // 사람이 보는 자리로 올리는 것은 별도 작업이다.
                        diagnostics.Add(new DiagnosticMessage
                        {
                            DiagnosticType = DiagnosticType.Warning,
                            MessageData = "[Artel] " + compiledAssembly.Name + ": " + inputAttempt.Refusal
                        });
                    }

                    var inputWeaver = inputAttempt.Weaver;

                    // 엔진 호출 훅(ARTEL-1283). 입력과 같은 pass 다 — 세 번째 ILPP 를 만들면 순서가 문제가 된다.
                    var engineAttempt = EngineCallWeaver.TryCreate(assembly.MainModule, compiledAssembly.References);

                    if (engineAttempt.Refusal != null)
                    {
                        diagnostics.Add(new DiagnosticMessage
                        {
                            DiagnosticType = DiagnosticType.Warning,
                            MessageData = "[Artel] " + compiledAssembly.Name + ": " + engineAttempt.Refusal
                        });
                    }

                    var engineWeaver = engineAttempt.Weaver;

                    // cheat 와 behavior 등록은 에디터와 DEVELOPMENT_BUILD 에서만 만든다. 출시 빌드에서는 attribute 가 아무 일도
                    // 하지 않고, 런타임의 네 action 이 "not available in this build" 로 거절한다.
                    var gameFunctionWeaver = IsDiscoveryBuild(compiledAssembly)
                        ? GameFunctionWeaver.TryCreate(assembly.MainModule)
                        : null;

                    if (actionWeaver == null && inputWeaver == null && gameFunctionWeaver == null && engineWeaver == null)
                    {
                        return new ILPostProcessResult(null, diagnostics);
                    }

                    var changed = actionWeaver != null && actionWeaver.Process();
                    changed |= inputWeaver != null && inputWeaver.Process();
                    changed |= engineWeaver != null && engineWeaver.Process();
                    changed |= gameFunctionWeaver != null && gameFunctionWeaver.Process();

                    if (gameFunctionWeaver != null)
                    {
                        foreach (var error in gameFunctionWeaver.Errors)
                        {
                            diagnostics.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = "[Artel] " + compiledAssembly.Name + ": " + error
                            });
                        }
                    }
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

        /// <summary>
        /// 에디터나 <c>DEVELOPMENT_BUILD</c> 컴파일인지. <c>AffordanceILPostProcessor</c> 에 같은 판단이 있지만, 두 CodeGen
        /// asmdef 은 서로를 참조하지 못해 여기에 다시 둔다.
        /// </summary>
        private static bool IsDiscoveryBuild(ICompiledAssembly compiledAssembly)
        {
            var defines = compiledAssembly.Defines;
            return defines != null &&
                   Array.Exists(defines, define => define == "UNITY_EDITOR" || define == "DEVELOPMENT_BUILD");
        }
    }
}
