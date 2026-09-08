using System;
using System.Collections.Generic;
using System.IO;
using Artel.Tracking;
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
            return !WeavableAssemblies.IsSkipped(compiledAssembly.Name);
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
    }
}
