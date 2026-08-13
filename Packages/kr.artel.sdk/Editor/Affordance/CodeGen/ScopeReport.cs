using System;
using System.IO;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Leaves the survey somewhere the editor can pick it up.
    /// </summary>
    /// <remarks>
    /// A <see cref="Unity.CompilationPipeline.Common.Diagnostics.DiagnosticType.Warning"/> raised
    /// here does not reach the console. It is printed inside the output of the build step that ran
    /// the post-processor, and a step that succeeded has its output folded away — only an error,
    /// which fails the step and takes the build with it, is surfaced. So the one channel available
    /// from inside the compilation pipeline is the one nobody reads.
    ///
    /// Writing a file and letting an editor-side script announce it after the reload is the way
    /// out. The post-processor runs in its own process with the project root as its working
    /// directory, which is what makes the relative path below line up on both sides.
    ///
    /// One file per assembly, because assemblies are post-processed concurrently and appending to
    /// a shared file would interleave them.
    /// </remarks>
    internal static class ScopeReport
    {
        internal const string ReportDirectory = "Library/ArtelScope";

        internal static bool TryWrite(string assemblyName, string message)
        {
            try
            {
                Directory.CreateDirectory(ReportDirectory);
                File.WriteAllText(Path.Combine(ReportDirectory, assemblyName + ".txt"), message);
                return true;
            }
            catch (Exception)
            {
                // The diagnostic still goes to the editor log. Losing the readable channel is worth
                // saying out loud, but not worth failing a compilation over.
                return false;
            }
        }
    }
}
