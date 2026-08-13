using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Artel.Affordances.Editor
{
    /// <summary>
    /// Says so before a build that discovery will not survive.
    /// </summary>
    /// <remarks>
    /// Managed stripping set to High takes this package out of the player: measured on the sample
    /// project, the scan assembly, the attribute and the assembly declaring it were all absent from
    /// the build, and only the evidence resource remained. Nothing is left to read it and nothing is
    /// left to complain, so the game runs, writes no report, and looks exactly like a game the scan
    /// found nothing in.
    ///
    /// This runs in the editor, which is the last place that still knows both things — that
    /// discovery is on, and that the build about to be made will remove it.
    /// </remarks>
    internal sealed class ManagedStrippingWarning : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(report.summary.platform));

            if (PlayerSettings.GetManagedStrippingLevel(target) != ManagedStrippingLevel.High)
            {
                return;
            }

            Debug.LogWarning(
                "[Artel] Managed stripping is set to High, which removes this package from the " +
                "build. The game will run and write no report. Lower the stripping level, or turn " +
                "discovery off under Artel / Discovery so the absence is intended.");
        }
    }
}
