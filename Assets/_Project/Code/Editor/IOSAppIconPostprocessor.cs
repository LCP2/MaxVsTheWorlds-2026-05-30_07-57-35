using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Guarantees the generated Xcode project carries a 1024×1024 App Store icon (YT-104).
    ///
    /// Apple refuses the upload without one — <c>Validation failed (409) Missing app icon</c> — and
    /// that check only fires at the very end of a ~15-minute archive-and-upload, long after the
    /// build that caused it. Unity's own icon export has proven unreliable here (it emitted only the
    /// five device slots and no <c>ios-marketing</c> entry at all), so rather than trust it, this
    /// asserts the entry exists and writes it if it doesn't. The failure mode becomes a loud CI
    /// error next to the cause instead of an opaque rejection from Apple twenty minutes later.
    /// </summary>
    public sealed class IOSAppIconPostprocessor : IPostprocessBuildWithReport
    {
        // Late: run after Unity has finished writing the asset catalog.
        public int callbackOrder => 100;

        private const string MarketingIconFile = "Icon-iOS-Marketing-1024.png";
        private const string MarketingIdiom = "ios-marketing";

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;

            string appIconSet = Path.Combine(
                report.summary.outputPath, "Unity-iPhone", "Images.xcassets", "AppIcon.appiconset");
            string contentsPath = Path.Combine(appIconSet, "Contents.json");

            if (!File.Exists(contentsPath))
            {
                throw new BuildFailedException(
                    $"[IOSBuild] No app icon catalog at {contentsPath}. Apple rejects an upload " +
                    "without a 1024x1024 App Store icon, so this build could never ship.");
            }

            string contents = File.ReadAllText(contentsPath);
            if (contents.Contains(MarketingIdiom))
            {
                Debug.Log("[IOSBuild] App Store 1024 icon present in the asset catalog.");
                return;
            }

            string source = Path.GetFullPath(IOSBuild.IconAssetPath);
            if (!File.Exists(source))
            {
                throw new BuildFailedException(
                    $"[IOSBuild] Unity emitted no {MarketingIdiom} icon and the placeholder source " +
                    $"{IOSBuild.IconAssetPath} is missing, so it can't be supplied either.");
            }

            File.Copy(source, Path.Combine(appIconSet, MarketingIconFile), overwrite: true);
            File.WriteAllText(contentsPath, AddMarketingIconEntry(contents, MarketingIconFile));

            Debug.LogWarning(
                $"[IOSBuild] Unity exported no {MarketingIdiom} icon — injected {MarketingIconFile} " +
                "into the asset catalog so Apple accepts the upload.");
        }

        /// <summary>
        /// Inserts an <c>ios-marketing</c> 1024×1024 entry at the head of the catalog's images array.
        /// Returns <paramref name="contentsJson"/> unchanged if one is already there, so a build that
        /// runs twice over the same output doesn't accumulate duplicates.
        /// </summary>
        public static string AddMarketingIconEntry(string contentsJson, string filename)
        {
            if (contentsJson == null) throw new ArgumentNullException(nameof(contentsJson));
            if (contentsJson.Contains(MarketingIdiom)) return contentsJson;

            int images = contentsJson.IndexOf("\"images\"", StringComparison.Ordinal);
            int arrayStart = images < 0 ? -1 : contentsJson.IndexOf('[', images);
            if (arrayStart < 0)
            {
                throw new BuildFailedException(
                    "[IOSBuild] App icon Contents.json has no \"images\" array — refusing to guess " +
                    "at its shape. Inspect the generated Xcode project.");
            }

            // An empty array needs no separator; a populated one does, or the JSON is malformed.
            int next = arrayStart + 1;
            while (next < contentsJson.Length && char.IsWhiteSpace(contentsJson[next])) next++;
            bool hasSiblings = next < contentsJson.Length && contentsJson[next] != ']';

            string entry =
                "\n\t\t{\n" +
                $"\t\t\t\"filename\" : \"{filename}\",\n" +
                $"\t\t\t\"idiom\" : \"{MarketingIdiom}\",\n" +
                "\t\t\t\"scale\" : \"1x\",\n" +
                "\t\t\t\"size\" : \"1024x1024\"\n" +
                "\t\t}" + (hasSiblings ? "," : "\n\t");

            return contentsJson.Insert(arrayStart + 1, entry);
        }
    }
}
