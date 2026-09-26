using System;
using UnityEngine;
using UnityEngine.InputSystem;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Player;
using MaxWorlds.UI;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Turns <see cref="DevMode"/> on and drives it (YT-60).
    ///
    /// Why this exists: the slice kills Max in ~10-15 seconds with zero robots destroyed, which
    /// makes the entire art queue unreviewable — you die before the effects you're supposed to be
    /// judging have happened. This removes the survival pressure so the VFX can actually be seen
    /// and filmed.
    ///
    /// OFF by default. Two ways in:
    ///   * add <c>?dev=1</c> to the WebGL URL, or
    ///   * press Ctrl+Shift+D (deliberately obscure — not something a player finds by accident).
    ///
    /// With it off, nothing here changes any behaviour: the guards in PlayerHealth and WaterBlaster
    /// read false and the game plays exactly as it shipped.
    ///
    /// It has since picked up a second job (YT-82): it's where the camera framing gets tuned. Hold
    /// <c>[</c> / <c>]</c> to pull the camera in and out live and the overlay reports the distance,
    /// so the framing can be found by eye on the play link rather than guessed at and rebuilt.
    ///
    /// MV-450 adds the same live-and-read-off idiom for pitch: hold <c>;</c> / <c>'</c> to sweep the
    /// angle, the overlay reports it to one decimal. The shipped 72° default doesn't move — this is
    /// purely so Lee can find the number he wants before a follow-up ticket bakes it in.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("overlay")]
    public sealed class DevModeController : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<DevModeController>() != null) return;
            new GameObject("DevMode").AddComponent<DevModeController>();
        }

        private void Awake()
        {
            DevMode.Reset();
            if (UrlRequestsDevMode()) Enable("URL ?dev=1");

            // MV-940: same "URL param on WebGL, always-available Settings control everywhere else"
            // idiom as DevMode itself above — see PerfLogEnabled/TryJumpToArea's own doc comments.
            if (UrlRequestsPerfLog()) PerfLogEnabled = true;
            if (TryGetUrlAreaParam(out string area)) _mv940PendingAreaSpec = area;
        }

        private void OnDestroy() => DevMode.Reset();

        /// <summary>WebGL hands us the page URL, so the query string is the natural switch — Lee can
        /// turn this on from the play link without a rebuild.</summary>
        private static bool UrlRequestsDevMode()
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return false;
            url = url.ToLowerInvariant();
            return url.Contains("dev=1") || url.Contains("dev=true");
        }

        private static void Enable(string why)
        {
            DevMode.Enabled = true;
            Debug.Log($"[DevMode] ON ({why}) — Max is invincible, blaster energy is infinite.");
        }

        private void Update()
        {
            // MV-940: independent of DevMode.Enabled below — the perf log/jump-to-area controls are
            // their own always-available surface (see PerfLogEnabled's own doc comment), not gated on
            // dev mode being on.
            TickMv940PerfDebug();

            var kb = Keyboard.current;
            if (kb == null) return;

            bool chord = (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) &&
                         (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

            if (chord && kb.dKey.wasPressedThisFrame)
            {
                if (DevMode.Enabled) { DevMode.Reset(); Debug.Log("[DevMode] OFF"); }
                else Enable("Ctrl+Shift+D");
            }

            if (!DevMode.Enabled) return;

            if (kb.f2Key.wasPressedThisFrame) DevMode.AutoFire = !DevMode.AutoFire;
            if (kb.f3Key.wasPressedThisFrame) DevMode.PauseSpawns = !DevMode.PauseSpawns;
            if (kb.f4Key.wasPressedThisFrame) ClearEnemies();

            // Live zoom (YT-82). Framing is a feel call and nobody gets it right by arithmetic, so
            // the point of this is that Lee dials it in on the play link — ?dev=1, hold a bracket
            // key until the yard looks right — and reads the number straight off the overlay. Held,
            // not tapped, because you find the framing by sweeping past it and coming back.
            if (kb.leftBracketKey.isPressed) NudgeZoom(-ZoomNudgePerSecond * Time.unscaledDeltaTime);
            if (kb.rightBracketKey.isPressed) NudgeZoom(ZoomNudgePerSecond * Time.unscaledDeltaTime);

            // Live pitch (MV-450), same held-not-tapped idiom as the bracket-key zoom, next pair of
            // keys along. Dev-mode only — the shipped 72° stays put, this is only for Lee to sweep an
            // angle by eye before a follow-up ticket bakes whatever he lands on.
            if (kb.semicolonKey.isPressed) NudgePitch(-PitchNudgePerSecond * Time.unscaledDeltaTime);
            if (kb.quoteKey.isPressed) NudgePitch(PitchNudgePerSecond * Time.unscaledDeltaTime);

            ApplySpawnPause();
        }

        /// <summary>Metres per second of pull-back while a bracket key is held.</summary>
        private const float ZoomNudgePerSecond = 8f;

        /// <summary>Degrees per second of pitch change while a `;`/`'` key is held.</summary>
        private const float PitchNudgePerSecond = 10f;

        private void NudgeZoom(float delta)
        {
            var rig = FindFirstObjectByType<FixedAngleCameraRig>();
            if (rig != null) rig.Nudge(delta);
        }

        private void NudgePitch(float delta)
        {
            var rig = FindFirstObjectByType<FixedAngleCameraRig>();
            if (rig != null) rig.NudgePitch(delta);
        }

        /// <summary>The spawner is a plain MonoBehaviour, so pausing it needs nothing from the
        /// gameplay stream — just switch it off.</summary>
        private void ApplySpawnPause()
        {
            foreach (var spawner in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
            {
                spawner.enabled = !DevMode.IsSpawnPaused;
            }
        }

        /// <summary>Clear the field so a single effect can be filmed against an empty arena.
        /// Deactivates rather than kills: a kill would fire the whole XP/score/VFX chain and taint
        /// whatever you were trying to look at.</summary>
        private void ClearEnemies()
        {
            int n = 0;
            foreach (var e in FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                e.gameObject.SetActive(false);
                n++;
            }
            Debug.Log($"[DevMode] cleared {n} enemies");
        }

        private void OnGUI()
        {
            if (!DevMode.Enabled) return;

            const float w = 430f;
            var rect = new Rect(Screen.width - w - 12f, 12f, w, 140f);
            GUI.color = new Color(1f, 0.9f, 0.3f);
            GUI.Box(rect, "");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, w - 20f, 22f),
                "DEV MODE — invincible, infinite energy");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 28f, w - 20f, 22f),
                $"F2 auto-fire: {(DevMode.AutoFire ? "ON" : "off")}   " +
                $"F3 spawns: {(DevMode.PauseSpawns ? "PAUSED" : "on")}   F4 clear");

            // The zoom readout is the deliverable, not a debug line: dial the framing with the
            // brackets, then paste this number into FixedAngleCameraRig.cameraDistance (YT-82).
            var rig = FindFirstObjectByType<FixedAngleCameraRig>();
            string zoom = rig != null
                ? $"[ / ] zoom: {rig.Distance:0.0} m  ({CameraFraming.AreaScaleForDistance(CameraFraming.PreviousDistance, rig.Distance):0.00}x arena)"
                : "[ / ] zoom: no camera rig in scene";
            GUI.Label(new Rect(rect.x + 10f, rect.y + 50f, w - 20f, 22f), zoom);

            // Pitch readout (MV-450) — same deliverable-not-debug-line contract as the zoom readout
            // above: sweep with ; / ', read the number here, paste it into pitchDegrees once a value
            // is settled on.
            string pitch = rig != null
                ? $"; / ' pitch: {rig.Pitch:0.0}°"
                : "; / ' pitch: no camera rig in scene";
            GUI.Label(new Rect(rect.x + 10f, rect.y + 72f, w - 20f, 22f), pitch);

            GUI.Label(new Rect(rect.x + 10f, rect.y + 94f, w - 20f, 22f),
                "Ctrl+Shift+D to turn off");
        }

        // =================================================================================================
        // MV-940 Phase 1 — "measure before changing anything". Two things Lee needs to reproduce the
        // World 2 upper-walkway fps collapse (a13 Up -> a12 Up) without a fresh TestFlight round-trip:
        //
        // 1. Jump to area: teleports Max to a chosen area/level with every earlier Replicator destroyed
        //    (the standing population a real playthrough would have left behind by the time it got
        //    there), reachable from the Settings panel (every platform) and a WebGL ?area= URL param.
        //
        // 2. MVPERF console log: one compact line every 2s with the same FrameCost per-system buckets
        //    the iPhone overlay shows, plus area id, level and fps, so the design chat can measure a
        //    WebGL deploy from a browser console with no device round-trip at all.
        //
        // Both default OFF and stay off until touched — deliberately NOT gated on DevMode.Enabled
        // (which is URL/Ctrl+Shift+D only, so an iPhone TestFlight tester with no keyboard could never
        // reach it) and no build-time gating either (this project has no App Store channel yet, only
        // Editor/WebGL/TestFlight, so there is nothing further to gate against — same reasoning the
        // MV-503/537/910 diagnostic overlay's own "hidden by default, present on TestFlight" precedent
        // already established). Folded into this existing file rather than a new one under Runtime/Dev/
        // per CC_AUTONOMY.md's guardrail against per-ticket dev-file proliferation (MV-592).
        // =================================================================================================

        /// <summary>Settings panel "Perf log" toggle — mirrors <c>?perf=1</c> for iOS, where there is no
        /// URL bar. Never persisted across a relaunch.</summary>
        public static bool PerfLogEnabled { get; set; }

        private const float Mv940PerfLogIntervalSeconds = 2f;
        private float _mv940PerfLogLoggedAt = float.NegativeInfinity;
        private string _mv940PendingAreaSpec;

        /// <summary>The world (map, player) isn't necessarily built yet the instant this installs —
        /// retries every frame, silently, until both exist, then jumps exactly once. A malformed spec or
        /// an unresolvable zone logs a warning at that point and is never retried again.</summary>
        private void TickMv940PerfDebug()
        {
            if (_mv940PendingAreaSpec != null)
            {
                if (EnemyNavigation.Map != null && GameObject.FindGameObjectWithTag("Player") != null)
                {
                    TryJumpToArea(_mv940PendingAreaSpec);
                    _mv940PendingAreaSpec = null;
                }
            }

            if (!PerfLogEnabled) return;

            float now = Time.realtimeSinceStartup;
            if (now - _mv940PerfLogLoggedAt < Mv940PerfLogIntervalSeconds) return;
            _mv940PerfLogLoggedAt = now;
            Debug.Log(BuildMvPerfLine());
        }

        private static bool UrlRequestsPerfLog()
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return false;
            url = url.ToLowerInvariant();
            return url.Contains("perf=1") || url.Contains("perf=true");
        }

        private static bool TryGetUrlAreaParam(out string area)
        {
            area = null;
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return false;

            int qIndex = url.IndexOf('?');
            if (qIndex < 0) return false;

            foreach (string pair in url.Substring(qIndex + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0) continue;
                if (!string.Equals(pair.Substring(0, eq), "area", StringComparison.OrdinalIgnoreCase)) continue;
                area = Uri.UnescapeDataString(pair.Substring(eq + 1));
                return !string.IsNullOrEmpty(area);
            }
            return false;
        }

        /// <summary>"a12", "a12up", "12", "12up" (case-insensitive) all parse — digits are the 1-based
        /// floor area index, an optional trailing "up" selects that area's deck overlay instead. Shared
        /// entry point for the Settings knob and <c>?area=</c>.</summary>
        public static bool TryJumpToArea(string areaSpec)
        {
            if (!TryParseMv940AreaSpec(areaSpec, out int index, out bool up))
            {
                Debug.LogWarning($"[MV-940] jump-to-area: could not parse \"{areaSpec}\"");
                return false;
            }

            MapData map = EnemyNavigation.Map;
            if (map == null) return false;

            MapZone zone = up ? FindMv940OverlayZone(map, index) : map.Zone($"area{index}");
            if (zone == null)
            {
                Debug.LogWarning($"[MV-940] jump-to-area: no zone for \"{areaSpec}\" (area{index}{(up ? " up" : "")})");
                return false;
            }

            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj == null) return false;
            Transform player = playerObj.transform;

            // Same collider-disable/teleport/re-enable shape WorldRunner.RespawnPlayer and
            // MapRuntime.Adopt already use — a CharacterController caches its own position and would
            // otherwise undo a plain transform.position set.
            var cc = player.GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            player.position = new Vector3(zone.x, player.position.y, zone.z);
            if (cc != null) cc.enabled = wasEnabled;

            DestroyMv940ReplicatorsBefore(index);

            // Best-effort sync — MapRuntime's own gate self-heal (Update) re-lights the correct zone off
            // Max's live position regardless, within one frame, whether or not this tracker agrees; this
            // just keeps population-facing systems keyed off it from reading a stale area.
            var director = FindFirstObjectByType<AreaAccumulationDirector>();
            director?.SetCurrentArea(index);

            Debug.Log($"[MV-940] jumped to {areaSpec} (area{index}{(up ? " up" : "")})");
            return true;
        }

        private static bool TryParseMv940AreaSpec(string spec, out int index, out bool up)
        {
            index = 0;
            up = false;
            if (string.IsNullOrEmpty(spec)) return false;

            string s = spec.Trim().ToLowerInvariant();
            if (s.Length > 0 && s[0] == 'a') s = s.Substring(1);

            up = s.EndsWith("up", StringComparison.Ordinal);
            if (up) s = s.Substring(0, s.Length - 2);

            return int.TryParse(s, out index) && index > 0;
        }

        private static MapZone FindMv940OverlayZone(MapData map, int floorIndex)
        {
            if (map.zones == null) return null;
            foreach (MapZone z in map.zones)
                if (z != null && z.overlayOfIndex == floorIndex) return z;
            return null;
        }

        /// <summary>"Every earlier Replicator destroyed" (MV-940 ticket text) — every registered
        /// Replicator whose own <see cref="Replicator.AreaIndex"/> is strictly before the jump target, so
        /// a fresh jump reproduces the standing population a real playthrough would have left behind by
        /// the time it reached there, not a pristine, never-played-through world.</summary>
        private static void DestroyMv940ReplicatorsBefore(int targetAreaIndex)
        {
            foreach (Replicator r in FactoryCensus.RegisteredReplicators)
            {
                if (r != null && r.IsAlive && r.AreaIndex < targetAreaIndex)
                    r.ApplyCheckpointDestroyed();
            }
        }

        private static string BuildMvPerfLine()
        {
            var player = FindFirstObjectByType<PlayerController>();
            MapData map = EnemyNavigation.Map;
            MapZone zone = player != null && map != null
                ? map.ZoneAt(player.transform.position.x, player.transform.position.y, player.transform.position.z)
                : null;

            string areaLabel = zone != null ? MinimapModel.AreaDisplayLabel(zone) : "?";
            string level = zone != null && zone.level > 0 ? "deck" : "floor";
            float fps = Bootstrap.ActiveMeter != null ? Bootstrap.ActiveMeter.Fps : 0f;

            // FrameCost.FormatLine() is multi-line (bucket/profiler/robot-sub-phase); collapsed to one
            // line here since the ticket asks for ONE compact console line per interval.
            string frameCostLine = FrameCost.FormatLine().Replace("\n", "  ");

            return $"MVPERF area={areaLabel} level={level} fps={fps:0.0}  {frameCostLine}  {PopulationReadout.BuildLine()}";
        }
    }
}
