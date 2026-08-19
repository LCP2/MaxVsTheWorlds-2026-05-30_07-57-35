using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;
using MaxWorlds.Weapons;
using MaxWorlds.Pickups;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Renders fixed-state screenshots of the game's self-installing UI screens (MV-421), separate
    /// from <see cref="PressKitDirector"/>'s live-gameplay press kit. <see cref="PressKitDirector"/>
    /// can't reach these: its capture technique is a manual <c>Camera.Render()</c>, which never draws
    /// a <c>ScreenSpaceOverlay</c> canvas (WeaponsScreen, HomeScreen, ResultScreen, SettingsPanel are
    /// each a separate self-installing GameObject with their own such canvas — none reachable via
    /// <c>HudController</c> the way the press kit's own <c>ShowHud</c> flip works). This director
    /// instead resizes the actual back-buffer with <see cref="Screen.SetResolution"/> and reads it
    /// straight off the framebuffer with <see cref="ScreenCapture.CaptureScreenshotAsTexture()"/>,
    /// which sees every canvas exactly as a player would, with no per-screen render-mode plumbing.
    ///
    /// INERT in a normal session, same idiom as PressKitDirector: it installs only behind the
    /// <c>-uiscreens</c> command-line flag or a <c>Temp/uiscreens.arm</c> marker (see
    /// <see cref="UiScreensCapture"/>), and never both directors at once — the two marker files are
    /// deliberately distinct so a capture run only ever arms the one it asked for.
    ///
    /// Scope for MV-421: THE RIG board — the screen the ticket calls out as "the one that matters
    /// most" and the one MV-433/424/425/426 need real evidence against. MV-425 extends it with the
    /// HUD's WEAPONS button (its own four alert states); HomeScreen and ResultScreen remain
    /// follow-up work, not rebuilt here.
    /// </summary>
    public sealed class UiScreensDirector : MonoBehaviour
    {
        private const string DoneMarker = "_uiscreens_done.txt";

        /// <summary>MV-441 AC4: the Cowork design chat's reachable folder — same directory
        /// <c>CC_AUTONOMY.md</c> already grants this worker read/write on, no credentials, no CI, no
        /// <c>ui-screens</c> branch needed. Overwritten in place every run; best-effort (see
        /// <see cref="TryWriteSecondary"/>) so a CI box with no such drive doesn't fail the whole job.</summary>
        private const string DesignImagesScreensDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

        private string _outDir;
        private string _outDir2;
        private readonly StringBuilder _manifest = new StringBuilder();
        private readonly List<string> _failures = new List<string>();
        private int _shotsWritten;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<UiScreensDirector>() != null) return;
            new GameObject("UiScreensDirector").AddComponent<UiScreensDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-uiscreens", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "uiscreens.arm")); }
            catch { return false; }
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Arg("-uiscreensOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
            Directory.CreateDirectory(_outDir);

            try { Directory.CreateDirectory(DesignImagesScreensDir); _outDir2 = DesignImagesScreensDir; }
            catch (Exception e) { LogWarn($"secondary output dir unavailable ({DesignImagesScreensDir}): {e.Message}"); _outDir2 = null; }

            Log($"ui-screens capture starting → {_outDir}" + (_outDir2 != null ? $" (+ {_outDir2})" : ""));

            yield return CaptureRigBoard();
            yield return CaptureWeaponsButton();

            Finish();
        }

        // --- THE RIG (MV-421 scope) -------------------------------------------------------------

        /// <summary>Three shots: the 16:9 reference frame (1:1 comparable to MV-423.png), the 1.6:1
        /// narrowest-desktop-browser frame (does SUPPORT clip?), and a parts=0 variant of the 16:9
        /// frame (does the amber '+' badge / empty tray render correctly?).</summary>
        private IEnumerator CaptureRigBoard()
        {
            var weapons = FindFirstObjectByType<WeaponsScreen>();
            if (weapons == null) { LogWarn("rig: no WeaponsScreen in the scene"); yield break; }

            yield return CaptureFixtureScreen("rig-16x9", 1920, 1080, ApplyRigFixture, weapons.Open, weapons.Close);
            yield return CaptureFixtureScreen("rig-16x10", 1728, 1080, ApplyRigFixture, weapons.Open, weapons.Close);
            yield return CaptureFixtureScreen("rig-noparts-16x9", 1920, 1080, ApplyRigFixtureNoParts, weapons.Open, weapons.Close);
        }

        /// <summary>Matches the state shown in MV-423.png node-for-node (MV-421's own spec), so the
        /// capture and the design image are directly comparable. Every node not spent here is left at
        /// its <see cref="RigState.Reset"/> baseline — that alone gives p_prc/s_bal/e_mag/m_spd/m_tp
        /// their "reached, not owned" states and u_slt its "not reached" one, off the same
        /// parent-level rule <see cref="RigState.IsReached"/> already enforces, with nothing extra to
        /// stage for them. Public (and static — no scene/canvas needed) so <c>UiScreensFixtureTests</c>
        /// can assert the resulting <see cref="RigState"/>/<see cref="PickupWallet"/> values directly,
        /// without a play-mode capture.</summary>
        public static void ApplyRigFixture()
        {
            ResetRunForFixture();
            SpendRigFixtureLevels();
            PickupWallet.SetPowerCells(28);   // needs e_cel spent to 1 first — Capacity reads RigState
            for (int i = 0; i < 4; i++) PickupWallet.AddPart();   // parts banked: 4
        }

        /// <summary>The same board state, minus the 4 banked parts — comparing this against the
        /// parts=4 shot is how the amber '+' badge and the parts-tray fill state get evidenced.</summary>
        public static void ApplyRigFixtureNoParts()
        {
            ResetRunForFixture();
            SpendRigFixtureLevels();
            PickupWallet.SetPowerCells(28);
        }

        private static void ResetRunForFixture()
        {
            RigState.Reset();
            PickupWallet.Reset();
        }

        private static void SpendRigFixtureLevels()
        {
            SpendToLevel("p_dmg", 4);
            RigState.AcquireCap("p_rng");
            SpendToLevel("p_rng", 3);
            RigState.AcquireCap("p_flw");
            SpendToLevel("p_flw", 2);

            RigState.AcquireCap("e_ff");
            SpendToLevel("e_ff", 2);
            RigState.AcquireCap("e_cel");
            RigState.AcquireCap("e_cd");
            SpendToLevel("e_cd", 3);

            RigState.AcquireCap("u_sen");
            RigState.AcquireCap("u_dmg");
            SpendToLevel("u_dmg", 2);
            RigState.AcquireCap("u_rng");
            SpendToLevel("u_rng", 1);
        }

        private static void SpendToLevel(string id, int target)
        {
            while (RigState.Level(id) < target)
            {
                if (!RigState.TrySpendPart(id))
                {
                    LogWarn($"rig fixture: could not raise {id} to {target} (stuck at {RigState.Level(id)})");
                    break;
                }
            }
        }

        // --- WEAPONS button (MV-425 scope) ------------------------------------------------------

        /// <summary>Four shots, one per <see cref="HudController.WeaponsButtonAlert"/> state — the
        /// "real evidence" MV-425's own AC asks for. HudController is already live in this scene (it's
        /// not self-installing like WeaponsScreen), so there's no open/close pair — the fixture drives
        /// PickupWallet/AbilityCreditBank/PendingMorphingModule directly, the same static-state-fixture
        /// idiom <see cref="ApplyRigFixture"/> already uses for RigState, and the button reacts to the
        /// real signal chain (OnParts/OnPendingModuleChanged) exactly as it would from a live pickup.</summary>
        private IEnumerator CaptureWeaponsButton()
        {
            var hud = FindFirstObjectByType<HudController>();
            if (hud == null) { LogWarn("weapons-button: no HudController in the scene"); yield break; }

            yield return CaptureFixtureScreen("weapons-button-idle", 1920, 1080, ApplyWeaponsButtonIdleFixture, null, null);
            yield return CaptureFixtureScreen("weapons-button-parts", 1920, 1080, ApplyWeaponsButtonPartsFixture, null, null);
            yield return CaptureFixtureScreen("weapons-button-module", 1920, 1080, ApplyWeaponsButtonModuleFixture, null, null);
            yield return CaptureFixtureScreen("weapons-button-both", 1920, 1080, ApplyWeaponsButtonBothFixture, null, null);

            ApplyWeaponsButtonIdleFixture();   // leave the scene in a clean state once the pass is done
        }

        public static void ApplyWeaponsButtonIdleFixture()
        {
            PickupWallet.Reset();
            AbilityCreditBank.Reset();
            PendingMorphingModule.Reset();
        }

        /// <summary>4 parts banked — matches the "4" badge in MV-425.png.</summary>
        public static void ApplyWeaponsButtonPartsFixture()
        {
            ApplyWeaponsButtonIdleFixture();
            for (int i = 0; i < 4; i++) PickupWallet.AddPart();
        }

        public static void ApplyWeaponsButtonModuleFixture()
        {
            ApplyWeaponsButtonIdleFixture();
            PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });
        }

        public static void ApplyWeaponsButtonBothFixture()
        {
            ApplyWeaponsButtonIdleFixture();
            for (int i = 0; i < 4; i++) PickupWallet.AddPart();
            PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });
        }

        // --- capture -----------------------------------------------------------------------------

        /// <summary>Resizes the real back-buffer to <paramref name="w"/>x<paramref name="h"/>,
        /// applies a fixture, opens the screen and reads the framebuffer straight off
        /// <see cref="ScreenCapture.CaptureScreenshotAsTexture()"/> — sees any canvas render mode,
        /// so no screen needs a public Canvas accessor or a render-mode flip to be captured. Every
        /// wait after <paramref name="open"/> uses <see cref="WaitForSecondsRealtime"/>, never
        /// <see cref="WaitForSeconds"/> — <c>WeaponsScreen.Open()</c> sets <c>Time.timeScale = 0</c>,
        /// so a scaled wait would never elapse.</summary>
        private IEnumerator CaptureFixtureScreen(string name, int w, int h, Action applyFixture, Action open, Action close)
        {
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            yield return null;
            yield return new WaitForSecondsRealtime(0.15f);   // let the resolution change land

            Exception staged = null;
            try
            {
                applyFixture?.Invoke();
                open?.Invoke();
            }
            catch (Exception e) { staged = e; }
            if (staged != null) { LogWarn($"{name}: staging failed — {staged.Message}"); yield break; }

            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.1f);   // paused (timeScale 0) — real time only

            // MV-441: the shot must be the ONLY high-sorting-order screen up — HomeScreen's own
            // sortingOrder=220 canvas sat over every ui-screens capture uncaught until this ran.
            // A shot that opens a screen (rig-*) expects exactly that one; a HUD-only shot (the
            // WEAPONS button states) opens nothing, so it expects zero — the HUD's own canvas is
            // pinned at exactly 100 (HudController.cs), below this ">100" threshold, by design.
            int expectedOverlays = open != null ? 1 : 0;
            string overlayError = CheckSingleActiveOverlay(expectedOverlays);
            if (overlayError != null)
            {
                string msg = $"{name}: canvas-overlay assertion failed — {overlayError}";
                LogWarn(msg);
                _manifest.AppendLine(msg);
                _failures.Add(msg);
                try { close?.Invoke(); } catch (Exception e) { LogWarn($"{name}: close failed — {e.Message}"); }
                yield break;
            }

            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    if (tex.width != w || tex.height != h)
                        LogWarn($"{name}: captured {tex.width}x{tex.height}, expected {w}x{h} — Screen.SetResolution did not take in this environment");
                    byte[] png = tex.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), png);
                    TryWriteSecondary(name, png);
                    _manifest.AppendLine($"{name}.png ({tex.width}x{tex.height})");
                    _shotsWritten++;
                    Log($"wrote {name}.png ({tex.width}x{tex.height})");

                    if (name == "rig-16x9") RunOutsideBackgroundProbe(tex);
                }
                finally { Destroy(tex); }
            }
            catch (Exception e) { LogWarn($"{name}: capture failed — {e.Message}"); }
            finally
            {
                try { close?.Invoke(); } catch (Exception e) { LogWarn($"{name}: close failed — {e.Message}"); }
            }
        }

        private void TryWriteSecondary(string name, byte[] png)
        {
            if (_outDir2 == null) return;
            try { File.WriteAllBytes(Path.Combine(_outDir2, name + ".png"), png); }
            catch (Exception e) { LogWarn($"{name}: secondary write to {_outDir2} failed — {e.Message}"); }
        }

        /// <summary>Asserts exactly <paramref name="expectedCount"/> <c>ScreenSpaceOverlay</c> canvases
        /// above the HUD's own sortingOrder=100 are actually rendering something — the generalised form
        /// of the HomeScreen check MV-441 asked for, so a future screen with a higher sorting order
        /// breaks this job loudly instead of silently poisoning the evidence the same way again.
        ///
        /// "Rendering something" (<see cref="HasVisibleContent"/>), not merely "GameObject active", is
        /// the right test: WeaponsScreen/UpgradeScreen/SettingsPanel each self-install their Canvas once
        /// at boot and represent Close() by <c>SetActive(false)</c> on an inner content root, not by
        /// deactivating the Canvas GameObject itself — so their Canvas reads <c>isActiveAndEnabled</c>
        /// forever, open or closed, and a naive active-canvas count found 3 "active" canvases on every
        /// single shot including the ones that open nothing (caught live running this fix — see the
        /// MV-441 fix comment). An empty, contentless canvas draws nothing and isn't an occlusion risk;
        /// only a canvas with an active, non-transparent <see cref="Graphic"/> under it actually paints
        /// over the frame.
        ///
        /// Returns null on success, a named error (every offending canvas + its sortingOrder) on
        /// failure.</summary>
        private static string CheckSingleActiveOverlay(int expectedCount)
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var offenders = new List<string>();
            foreach (var c in canvases)
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (c.sortingOrder <= 100) continue;
                if (!c.isActiveAndEnabled) continue;
                if (!HasVisibleContent(c)) continue;
                offenders.Add($"{c.name}(order={c.sortingOrder})");
            }
            if (offenders.Count == expectedCount) return null;
            return $"expected {expectedCount} visibly-rendering ScreenSpaceOverlay canvas(es) with sortingOrder>100, found {offenders.Count} [{string.Join(", ", offenders)}]";
        }

        /// <summary>A canvas above sortingOrder 100 is only an occlusion risk if something on it
        /// actually covers a meaningful share of the frame — a full-screen scrim/panel, the HomeScreen
        /// defect's own shape. <c>SettingsPanel</c> keeps a permanent 96x96 gear button on its own
        /// sortingOrder=200 canvas (0.4% of a 1920x1080 frame) so it's reachable from any screen; that
        /// is real, intentional always-on chrome, the same category as the HUD's own excluded
        /// sortingOrder=100 canvas, not a defect — flagging it was a real false-positive caught live
        /// running this fix (see the MV-441 fix comment). <c>GetComponentsInChildren</c> with
        /// <c>includeInactive: false</c> already skips every <see cref="Graphic"/> under an inactive
        /// ancestor, which is exactly how each self-installing screen represents "closed" — no
        /// reflection into any screen's private root needed.</summary>
        private const float OcclusionAreaFraction = 0.10f;   // a modal panel/scrim clears this easily; an icon/badge never does

        private static bool HasVisibleContent(Canvas c)
        {
            var canvasRect = c.transform as RectTransform;
            float canvasArea = canvasRect != null ? canvasRect.rect.width * canvasRect.rect.height : 0f;
            if (canvasArea <= 0f) return false;

            foreach (var g in c.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled || g.color.a <= 0.001f) continue;
                Rect r = g.rectTransform.rect;
                if (r.width * r.height >= canvasArea * OcclusionAreaFraction) return true;
            }
            return false;
        }

        /// <summary>MV-421 pixel probe 6, fixed by MV-441 (it never ran — this is its first
        /// implementation): a point that sits left of the region rect's own <c>padX</c> margin and
        /// clear of every category/ability node must equal <c>colours.base</c> exactly. This is the
        /// probe that would have caught the HomeScreen occlusion with nobody looking at a screenshot —
        /// the panel's colour is nowhere near the board's near-black base. Runs only against
        /// <c>rig-16x9</c>, the one shot whose 1920x1080 texture maps 1:1 onto rig_board.json's own
        /// canvas coordinates with no letterbox/scale to account for.</summary>
        private void RunOutsideBackgroundProbe(Texture2D tex)
        {
            const int probeJsonX = 20, probeJsonY = 400;   // rig_board.json coords (y measured from the top)
            int texX = probeJsonX;
            int texY = tex.height - probeJsonY;             // Texture2D is bottom-left origin
            if (texX < 0 || texX >= tex.width || texY < 0 || texY >= tex.height)
            {
                string skip = "probe6 (outside-background==colours.base): SKIPPED — coordinates fall outside the captured texture";
                LogWarn(skip);
                _manifest.AppendLine(skip);
                return;
            }

            Color actual = tex.GetPixel(texX, texY);
            Color expected = RigBoardLayout.Colour("base");
            bool pass = ColorsMatch(actual, expected);
            string msg = $"probe6 (outside-background==colours.base): {(pass ? "PASS" : "FAIL")} — expected {ColorHex(expected)}, got {ColorHex(actual)} at tex({texX},{texY})";
            _manifest.AppendLine(msg);
            if (pass) { Log(msg); }
            else { LogWarn(msg); _failures.Add(msg); }
        }

        private static bool ColorsMatch(Color a, Color b, float tolerance = 2f / 255f)
        {
            return Mathf.Abs(a.r - b.r) <= tolerance
                && Mathf.Abs(a.g - b.g) <= tolerance
                && Mathf.Abs(a.b - b.b) <= tolerance;
        }

        private static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // --- lifecycle / reporting ----------------------------------------------------------------

        private void Finish()
        {
            // "ok" means at least one screenshot actually landed (every shot throwing, as
            // ScreenCapture.CaptureScreenshotAsTexture did with no attached display when this was
            // last run locally, see MV-421's fix comment, must fail the job rather than report a
            // false ok with an empty manifest) AND no assertion — the canvas-overlay check or probe 6
            // — failed. A shot that "succeeded" over an occluding HomeScreen is exactly the false
            // green MV-441 exists to close off.
            string status;
            if (_failures.Count > 0)
                status = $"fail: {_failures.Count} assertion(s) failed — " + string.Join(" | ", _failures);
            else if (_shotsWritten == 0)
                status = $"fail: 0 of {ExpectedShotCount} screenshots captured";
            else
                status = "ok";

            string doneText = status + "\n" + _manifest.ToString();
            File.WriteAllText(Path.Combine(_outDir, DoneMarker), doneText, Encoding.UTF8);
            if (_outDir2 != null)
            {
                try { File.WriteAllText(Path.Combine(_outDir2, DoneMarker), doneText, Encoding.UTF8); }
                catch (Exception e) { LogWarn($"secondary done-marker write to {_outDir2} failed — {e.Message}"); }
            }
            Log($"ui-screens capture complete ({_shotsWritten}/{ExpectedShotCount} shots, {_failures.Count} failure(s))");
        }

        private const int ExpectedShotCount = 7;   // 3 THE RIG (MV-421) + 4 WEAPONS button states (MV-425)

        private static void Log(string m) => Debug.Log("[UiScreens] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[UiScreens] " + m);
    }
}
