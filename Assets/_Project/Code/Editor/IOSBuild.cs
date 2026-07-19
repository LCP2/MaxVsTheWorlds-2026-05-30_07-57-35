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

                int filled = 0;
                foreach (PlatformIconKind kind in kinds)
                {
                    PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
                    if (icons == null || icons.Length == 0) continue;

                    foreach (PlatformIcon icon in icons)
                    {
                        // A slot's layers are its light/dark/tinted variants; every one Unity asks
                        // for has to be non-empty or the slot still exports blank.
                        int layers = Mathf.Max(1, icon.maxLayerCount);
                        Texture2D tex = MakePlaceholderIcon(Mathf.Max(1, icon.width));
                        for (int layer = 0; layer < layers; layer++)
                        {
                            icon.SetTexture(tex, layer);
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
        /// Square greybox icon at the requested size. RGB24, not RGBA32: Apple rejects an App Store
        /// icon that carries an alpha channel, so the placeholder must not have one to give away.
        /// </summary>
        public static Texture2D MakePlaceholderIcon(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
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
