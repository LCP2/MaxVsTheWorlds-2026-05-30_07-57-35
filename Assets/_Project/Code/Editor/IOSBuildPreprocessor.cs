using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Applies <see cref="IOSBuild.ConfigureIOSPlayerSettings"/> to every iOS build (YT-104).
    ///
    /// The settings were previously only reachable via the MaxWorlds/iOS menu item or an explicit
    /// <c>-executeMethod</c>, and the CI job runs GameCI's own builder
    /// (<c>UnityBuilderAction.Builder.BuildProject</c>) — so nothing ever called it on the runner
    /// and the generated Xcode project shipped with no app icon at all. Hooking the build itself
    /// means it cannot be forgotten by whoever invokes the build, which is the point of
    /// <c>docs/CODE_DRIVEN_SCENES.md</c>.
    /// </summary>
    public sealed class IOSBuildPreprocessor : IPreprocessBuildWithReport
    {
        // Early: the icon slots must be populated before Unity writes the Xcode asset catalog.
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;

            Debug.Log("[IOSBuild] iOS build detected — applying Player Settings + placeholder icon.");
            IOSBuild.ConfigureIOSPlayerSettings();
        }
    }
}
