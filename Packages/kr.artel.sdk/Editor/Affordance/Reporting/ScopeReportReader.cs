using System;
using System.IO;
using System.Text;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Artel.Affordances.Editor
{
    /// <summary>
    /// Says what the analysis did, in the console, where it can be seen.
    /// </summary>
    /// <remarks>
    /// The analysis runs during compilation in a process of its own and cannot talk to the console
    /// from there. It leaves a file per assembly instead; this reads them once the reload that
    /// follows compilation brings the editor back, and clears them so the next compilation starts
    /// from nothing rather than repeating a stale answer.
    ///
    /// Speaking up matters more here than it looks. The previous build of this package had an
    /// analysis that silently did nothing, and the scan that followed reported no coverage gaps —
    /// which read as a clean result and was in fact the absence of any result at all.
    /// </remarks>
    internal static class ScopeReportReader
    {
        private const string ReportDirectory = "Library/ArtelScope";

        [DidReloadScripts]
        private static void Surface()
        {
            string[] reports;

            try
            {
                var directory = Path.Combine(Directory.GetCurrentDirectory(), ReportDirectory);
                if (!Directory.Exists(directory))
                {
                    return;
                }

                reports = Directory.GetFiles(directory, "*.txt");
            }
            catch (Exception)
            {
                return;
            }

            if (reports.Length == 0)
            {
                return;
            }

            var summary = new StringBuilder("[Artel] Scope survey");

            foreach (var report in reports)
            {
                try
                {
                    summary.Append('\n').Append(File.ReadAllText(report).TrimEnd());
                    File.Delete(report);
                }
                catch (Exception)
                {
                    // A report that cannot be read is one assembly unaccounted for, not a reason to
                    // drop the others.
                    summary.Append('\n').Append(Path.GetFileNameWithoutExtension(report))
                        .Append(": report could not be read.");
                }
            }

            Debug.Log(summary.ToString());
        }
    }
}
