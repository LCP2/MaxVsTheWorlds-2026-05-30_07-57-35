using System;
using UnityEditor;
using UnityEditor.Build;
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
        /// Fills every iOS application-icon slot with a generated greybox mark (dark block on warm
        /// Backyard orange). Guarded: with no iOS module the slot list is empty and this is a no-op.
        /// </summary>
        private static void AssignPlaceholderIcon()
        {
            try
            {
                int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.iOS, IconKind.Application);
                if (sizes == null || sizes.Length == 0)
                {
                    Debug.Log("[IOSBuild] No iOS icon slots on this editor (module not installed) — " +
                              "skipping placeholder icon; the macOS CI runner will generate it.");
                    return;
                }

                var icons = new Texture2D[sizes.Length];
                for (int i = 0; i < sizes.Length; i++)
                {
                    icons[i] = MakePlaceholderIcon(Mathf.Max(1, sizes[i]));
                }
                PlayerSettings.SetIcons(NamedBuildTarget.iOS, icons, IconKind.Application);
                Debug.Log($"[IOSBuild] Placeholder app icon assigned to {sizes.Length} iOS slot(s).");
            }
            catch (Exception e)
            {
                // Never let a placeholder-icon hiccup fail the build; it is cosmetic and needs-lee.
                Debug.LogWarning($"[IOSBuild] Placeholder icon step skipped: {e.Message}");
            }
        }

        private static Texture2D MakePlaceholderIcon(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var bg = new Color(0.96f, 0.62f, 0.20f, 1f); // warm Backyard orange
            var fg = new Color(0.10f, 0.11f, 0.14f, 1f); // dark centred block
            var px = new Color[size * size];
            int inset = Mathf.RoundToInt(size * 0.28f);
            for (int y = 0; y < size; y++)
            {
                bool yIn = y >= inset && y < size - inset;
                for (int x = 0; x < size; x++)
                {
                    bool xIn = x >= inset && x < size - inset;
                    px[y * size + x] = (xIn && yIn) ? fg : bg;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
