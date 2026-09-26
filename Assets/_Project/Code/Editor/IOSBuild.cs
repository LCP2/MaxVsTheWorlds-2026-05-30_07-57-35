using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Code-driven iOS Player Settings (YT-98). The durable identity/orientation/backend values
    /// live in <c>ProjectSettings.asset</c>; this method reasserts them and adds the parts that are
    /// awkward to hand-edit in YAML — a generated placeholder app icon — so the iOS CI job (YT-99,
    /// GameCI on a macOS runner) can call it before building:
    /// <c>-executeMethod MaxWorlds.Editor.IOSBuild.ConfigureIOSPlayerSettings</c>.
    ///
    /// It is deliberately idempotent and guarded: on this Windows box (no iOS Build Support module)
    /// the icon step finds no icon slots and no-ops, while the identity settings still apply. On the
    /// macOS runner the same call produces the placeholder icon. Final visual confirmation of the
    /// icon / launch screen is Lee's device pass. Per <c>docs/CODE_DRIVEN_SCENES.md</c>: no manual
    /// inspector wiring — a fresh clone configures identically from code.
    /// </summary>
    public static class IOSBuild
    {
        private const string BundleId = "com.codynamics.maxvstheworlds";
        private const string MinIosVersion = "15.0"; // iPhone 6s and up — covers every phone we target.

        /// <summary>
        /// The placeholder icon, as a committed project asset. It has to be an asset, not a texture
        /// built in memory: PlayerSettings stores icons as asset GUID references, so assigning a
        /// `new Texture2D(...)` logs success and serialises nothing — which is exactly how the
        /// generated Xcode project ended up carrying Unity's default logo (YT-104).
        /// </summary>
        public const string IconAssetPath = "Assets/_Project/Art/Icons/AppIcon.png";

        /// <summary>Size Apple requires for the App Store icon, and what the asset is authored at.</summary>
        public const int MarketingIconSize = 1024;

        [MenuItem("MaxWorlds/iOS/Apply Player Settings")]
        public static void ConfigureIOSPlayerSettings()
        {
            // Identity + runtime. IL2CPP on Unity 6 iOS builds ARM64 only, so ARM64 needs no
            // separate switch — there is no armv7 option to pick anymore.
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = MinIosVersion;

            // Landscape-locked (this is a fixed-angle top-down game — no portrait).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            AssignPlaceholderIcon();

            AssetDatabase.SaveAssets();
            Debug.Log($"[IOSBuild] iOS Player Settings applied: id={BundleId}, IL2CPP/ARM64, " +
                      $"minOS={MinIosVersion}, landscape-locked.");
        }

        /// <summary>
        /// Fills every iOS icon slot of every supported kind with a generated greybox mark (dark
        /// block on warm Backyard orange). Guarded: with no iOS module there are no kinds and this
        /// is a no-op.
        ///
        /// YT-104: this used to fill only <c>IconKind.Application</c>, which leaves the 1024×1024
        /// App Store slot empty. The archive then signs and exports fine, and Apple rejects it at
        /// upload with "Missing app icon … 1024 by 1024 pixel PNG" (409 STATE_ERROR.VALIDATION_ERROR)
        /// — a failure that only ever shows up in a real TestFlight upload. Enumerating the kinds
        /// rather than naming them keeps that from re-breaking when Unity adds or renames a slot.
        /// </summary>
        private static void AssignPlaceholderIcon()
        {
            try
            {
                PlatformIconKind[] kinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS);
                if (kinds == null || kinds.Length == 0)
                {
                    Debug.Log("[IOSBuild] No iOS icon slots on this editor (module not installed) — " +
                              "skipping placeholder icon; the CI runner will generate it.");
                    return;
                }

                Texture2D source = LoadOrCreateIconAsset();
                if (source == null)
                {
                    Debug.LogWarning("[IOSBuild] Placeholder icon asset unavailable — skipping.");
                    return;
                }

                int filled = 0;
                foreach (PlatformIconKind kind in kinds)
                {
                    PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
                    if (icons == null || icons.Length == 0) continue;

                    foreach (PlatformIcon icon in icons)
                    {
                        // One 1024 source for every slot — Unity downscales per slot on export. A
                        // slot's layers are its light/dark/tinted variants; every one Unity asks for
                        // has to be non-empty or the slot still exports blank.
                        int layers = Mathf.Max(1, icon.maxLayerCount);
                        for (int layer = 0; layer < layers; layer++)
                        {
                            icon.SetTexture(source, layer);
                        }
                        filled++;
                    }

                    PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, icons);
                }

                Debug.Log($"[IOSBuild] Placeholder app icon assigned to {filled} iOS slot(s) across " +
                          $"{kinds.Length} kind(s).");
            }
            catch (Exception e)
            {
                // Never let a placeholder-icon hiccup fail the build; it is cosmetic and needs-lee.
                Debug.LogWarning($"[IOSBuild] Placeholder icon step skipped: {e.Message}");
            }
        }

        /// <summary>
        /// The committed placeholder icon, regenerated from code if it has gone missing so a fresh
        /// clone (or a deleted asset) still produces a valid build rather than a 15-minute archive
        /// that Apple rejects at the very end.
        /// </summary>
        private static Texture2D LoadOrCreateIconAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
            if (existing != null) return existing;

            Debug.LogWarning($"[IOSBuild] {IconAssetPath} is missing — regenerating it from code.");
            WriteIconAsset();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
        }

        /// <summary>
        /// The app icon at the requested size (YT-115). Delegates to <see cref="AppIconArt"/>, which
        /// is the single source of truth for what the icon looks like.
        ///
        /// That indirection is the point. Before this, the committed PNG and the code that
        /// regenerates it were two independent drawings of the same icon, and nothing compared them:
        /// a fresh clone whose LFS pull had not run would silently regenerate the OLD greybox and
        /// ship it, with every test still green. Now there is one drawing, and
        /// <c>IOSIconTests.CommittedIcon_MatchesWhatTheCodeDraws</c> holds the PNG to it.
        /// </summary>
        public static Texture2D MakeAppIcon(int size) => AppIconArt.Build(size);

        /// <summary>
        /// Redraw the committed icon asset from <see cref="AppIconArt"/>. Run this after changing the
        /// artwork — the PNG is what actually ships (PlayerSettings references it by GUID), so a
        /// change to the drawing code that is not followed by this is a change that never reaches a
        /// phone.
        /// </summary>
        [MenuItem("MaxWorlds/iOS/Regenerate App Icon")]
        public static void WriteIconAsset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconAssetPath));
            File.WriteAllBytes(IconAssetPath, MakeAppIcon(MarketingIconSize).EncodeToPNG());
            AssetDatabase.ImportAsset(IconAssetPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[IOSBuild] Wrote {MarketingIconSize}x{MarketingIconSize} app icon to {IconAssetPath}");
        }

        // -----------------------------------------------------------------------------------------
        // MV-970 item 5: retrieval with no tools. Application.persistentDataPath/telemetry (the CSVs
        // PerfSessionRecorder writes) is otherwise unreachable on a TestFlight build — no dev tools, no
        // cable. These two Info.plist keys are what makes it show up in Files -> On My iPhone -> MAX ->
        // telemetry, AirDrop-able straight off the device.
        // -----------------------------------------------------------------------------------------

        public const string FileSharingPlistKey = "UIFileSharingEnabled";
        public const string OpenInPlacePlistKey = "LSSupportsOpeningDocumentsInPlace";

        /// <summary>
        /// Plain string insertion into the generated Info.plist's root &lt;dict&gt; — same idiom as
        /// <see cref="IOSAppIconPostprocessor.AddMarketingIconEntry"/> above it in the same build phase,
        /// deliberately NOT the <c>com.unity.ios.xcode</c> PlistDocument API: that package isn't in
        /// <c>Packages/manifest.json</c>, and adding a Unity package outside a ticket that asks for it is
        /// off-limits for this worker (CC_AUTONOMY.md guardrails). Idempotent — a key already present is
        /// left untouched, so a build that runs this twice (or a re-run over an already-patched project)
        /// never duplicates an entry.
        /// </summary>
        public static string ApplyTelemetryPlistKeys(string plistXml)
        {
            if (plistXml == null) throw new ArgumentNullException(nameof(plistXml));
            string result = EnsureBoolKeyTrue(plistXml, FileSharingPlistKey);
            result = EnsureBoolKeyTrue(result, OpenInPlacePlistKey);
            return result;
        }

        private static string EnsureBoolKeyTrue(string plistXml, string key)
        {
            if (plistXml.Contains($"<key>{key}</key>")) return plistXml;

            int dictOpen = plistXml.IndexOf("<dict>", StringComparison.Ordinal);
            if (dictOpen < 0)
            {
                throw new InvalidOperationException(
                    $"[IOSBuild] Info.plist has no <dict> root — refusing to guess where to insert {key}.");
            }

            int insertAt = dictOpen + "<dict>".Length;
            string entry = $"\n\t<key>{key}</key>\n\t<true/>";
            return plistXml.Insert(insertAt, entry);
        }
    }

    /// <summary>
    /// Applies <see cref="IOSBuild.ApplyTelemetryPlistKeys"/> to the generated Xcode project's Info.plist
    /// — same build phase and callback order as <see cref="IOSAppIconPostprocessor"/>, which this class
    /// otherwise doesn't touch (kept a separate class, not folded into that one, since an app-icon
    /// failure and a missing Info.plist are unrelated build problems that shouldn't share a stack trace).
    /// </summary>
    public sealed class IOSTelemetryPlistPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;

            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                throw new BuildFailedException(
                    $"[IOSBuild] No Info.plist at {plistPath} — can't enable telemetry file sharing.");
            }

            string original = File.ReadAllText(plistPath);
            string patched = IOSBuild.ApplyTelemetryPlistKeys(original);
            if (patched == original)
            {
                Debug.Log("[IOSBuild] Info.plist already carries the telemetry file-sharing keys.");
                return;
            }

            File.WriteAllText(plistPath, patched);
            Debug.Log("[IOSBuild] Info.plist: UIFileSharingEnabled + LSSupportsOpeningDocumentsInPlace " +
                      "set to true (MV-970) — telemetry folder now reachable from Files on device.");
        }
    }
}
