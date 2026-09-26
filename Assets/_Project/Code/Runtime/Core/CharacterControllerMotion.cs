using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-386: the working hypothesis (this ticket's own investigation notes) is that
    /// <see cref="CharacterController.Move"/> can tunnel through a thin solid (a gate/fence wall is
    /// <c>MapData.wallThickness</c>, 0.4 m by default) when asked to cover an unusually large distance
    /// in one call — the kind of spike a single frame's <see cref="Time.deltaTime"/> gets from a WebGL
    /// tab losing focus, a GC pause, or a shader/asset compile stall, none of which an Editor session or
    /// a Windows-standalone smoke build reliably hits.
    ///
    /// <b>This has NOT been confirmed as the actual mechanism.</b> A set of isolated EditMode physics
    /// probes (<see cref="MaxWorlds.Tests.EditMode.CharacterControllerMotionTunnelingTests"/>) tried a
    /// raw oversized <c>cc.Move()</c> against an unattached wall at increasing distance, a wall thinner
    /// than the map format's own minimum, a huge diagonal displacement dominated by a WebGL-stall-sized
    /// fall, and 240 frames of continuous grinding contact — none of them reproduced a pass-through;
    /// Unity's own swept collision test caught every one. The live bug has only ever reproduced on a
    /// deployed WebGL build, which this worker cannot drive (CC_AUTONOMY.md forbids PlayMode and there
    /// is no browser access here), so the isolated-probe result doesn't rule the theory out either — it
    /// may need real map geometry, a live browser stall, or some other frame-order factor a synthetic
    /// two-collider scene can't reproduce.
    ///
    /// <see cref="SafeMove"/> is shipped anyway as a genuinely safe, zero-downside hardening: it is a
    /// drop-in replacement for <c>cc.Move(displacement)</c> that keeps every individual physics query
    /// under <see cref="MaxSafeStep"/>, splitting one oversized call into several smaller ones that sum
    /// to the same total displacement (normal frames cost exactly one <c>Move()</c> call, same as
    /// before). It also logs whenever it has to split, so a live WebGL playtest's browser console can
    /// show whether an oversized single-frame displacement is actually occurring at the moment a
    /// pass-through is observed — the correlation this ticket's own AC asks for, which only a live
    /// build can supply.
    /// </summary>
    public static class CharacterControllerMotion
    {
        /// <summary>Largest displacement, in metres, any single <see cref="CharacterController.Move"/>
        /// call is trusted to sweep-test correctly. Half the minimum wall/gate thickness
        /// (<c>MapData.wallThickness</c> defaults to 0.4 m) leaves a 2x margin against a collider
        /// that's already at the thin end.</summary>
        public const float MaxSafeStep = 0.2f;

        /// <summary>MV-954: the worst downward speed this game's own gravity model is assumed capable
        /// of producing — 2 seconds of uninterrupted freefall at <c>gravity</c> (20 m/s², the same value
        /// <see cref="MaxWorlds.Player.PlayerController"/>/<see cref="MaxWorlds.Enemies.RobotEnemy"/>/the
        /// bosses all use), the same "2s of falling during a stall" assumption
        /// <c>CharacterControllerMotionTunnelingTests</c> already builds its own worst-case displacement
        /// from (MV-386). Nothing in the game actually clamps <c>_verticalVel</c> to this — it is a
        /// documented WORST-CASE ASSUMPTION for the derivation below, not an enforced ceiling, chosen
        /// generously above the ~24 m/s a fall starting from the tallest authored deck (2.5 m) would
        /// actually reach before <see cref="MaxWorlds.Arena.FallSafetyNet"/>'s own grace window (0.5 s)
        /// recovers it.</summary>
        public const float TerminalFallSpeed = 40f;

        /// <summary>How many times <see cref="SafeMove"/> has called <c>CharacterController.Move</c>
        /// (test-only instrumentation, MV-870) — every call counts as 1 regardless of whether it got
        /// split into several steps, so a test can read the MEASURED number of physics sweeps a caller
        /// actually triggered rather than inferring it from tick counts.</summary>
        public static int CallCount;

        /// <summary>MV-926: hard cap on how many <c>cc.Move</c> sub-steps a single <see cref="SafeMove"/>
        /// call can issue, however large <paramref name="displacement"/> is. Before this existed, step
        /// count was <c>CeilToInt(dist / MaxSafeStep)</c> with no ceiling: once a WebGL frame-time
        /// stall inflated every moving robot's per-frame displacement, each one issued proportionally
        /// more physics queries AND logged a warning every single call, and the extra work (plus
        /// WebGL's expensive console logging) made the next frame slower still — a feedback loop that
        /// took a 93-robot scene from 17fps to 5.6fps (this ticket's own evidence). Capping the COUNT
        /// (not the per-step size) means one call's physics cost is now bounded regardless of distance;
        /// distances beyond <c>MaxSubSteps * MaxSafeStep</c> (0.8 m) trade step precision for that
        /// bound instead of adding more queries.
        ///
        /// <b>MV-954 re-derivation</b> (after <see cref="MaxWorlds.Arena.MapGeometry.FloorThickness"/>
        /// went from 0.1 m to 2.0 m — a floor that thin could be cleared by a single oversized vertical
        /// sub-step during a stall; see that constant's own comment): worst case, a fall already at
        /// <see cref="TerminalFallSpeed"/> (40 m/s) hits one frame stretched to
        /// <c>Time.maximumDeltaTime</c> (0.1 s, MV-883) — 40 * 0.1 = 4.0 m requested in a single
        /// <see cref="SafeMove"/> call. Split across these 4 sub-steps that is 1.0 m each, exactly half
        /// the floor's 2.0 m thickness — the bound this ticket's AC sets ("no single sub-step's vertical
        /// travel can exceed half the thinnest walkable slab's collider thickness"). Both this constant
        /// and <see cref="MaxSafeStep"/> stay as they were: MV-926 sized this one against a MEASURED perf
        /// regression, and raising it to buy more margin here would reopen that regression for every
        /// ordinary <see cref="SafeMove"/> call, not just a falling one — the floor thickness absorbed
        /// the whole fix instead. A deck's own top-collider slab
        /// (<see cref="MaxWorlds.Arena.MapGeometry.DeckThickness"/>, 0.15 m) is thinner still and is NOT
        /// covered by this derivation — out of this ticket's scope (the observed defect and the AC are
        /// both about the floor); a known gap left for a follow-up ticket.</summary>
        public const int MaxSubSteps = 4;

        /// <summary>MV-926: whether <see cref="SafeMove"/> has already logged its one-time oversized-move
        /// warning this session. <c>Debug.LogWarning</c> used to fire on every single oversized call —
        /// harmless in isolation, but WebGL's console logging is expensive enough that doing it every
        /// frame for every moving robot during a stall was itself a meaningful chunk of the feedback
        /// loop this ticket fixes. One warning per process is enough to tell a live browser console
        /// that the split path is firing at all (the original MV-386 diagnostic purpose); test-settable,
        /// same seam as <see cref="CallCount"/>.</summary>
        public static bool HasWarnedOversizedMove;

        /// <summary>MV-926 test-only instrumentation: how many actual <c>CharacterController.Move</c>
        /// sweeps <see cref="SafeMove"/> has performed. Unlike <see cref="CallCount"/> (which counts
        /// <see cref="SafeMove"/> entries, always 1 per call), this counts every individual physics
        /// query issued — the resolved value a test needs to prove <see cref="MaxSubSteps"/> actually
        /// bounds one call's physics cost, rather than inferring it from the displacement it was given.</summary>
        public static int MoveSweepCount;

        /// <summary>MV-955: how many recent <see cref="SafeMove"/> calls the rolling window below keeps
        /// — the ticket's own "preceding 10 frames". Deliberately global rather than per-entity:
        /// <see cref="SafeMove"/> is a stateless static utility with no caller identity to key a
        /// per-entity window on, and this exists as fall-recovery evidence (<see cref="MaxWorlds.Arena.FallEventLog"/>),
        /// not a gameplay-critical measurement — a fall's own log entry is written the instant that
        /// entity's own recovery fires, when its own recent Move calls dominate the window in practice.</summary>
        private const int RecentWindowCapacity = 10;

        private static readonly int[] _recentSteps = new int[RecentWindowCapacity];
        private static readonly float[] _recentStepSize = new float[RecentWindowCapacity];
        private static readonly bool[] _recentOversized = new bool[RecentWindowCapacity];
        private static int _recentIndex;
        private static int _recentCount;

        private static void RecordRecentStep(int steps, float stepSize, bool oversized)
        {
            _recentSteps[_recentIndex] = steps;
            _recentStepSize[_recentIndex] = stepSize;
            _recentOversized[_recentIndex] = oversized;
            _recentIndex = (_recentIndex + 1) % RecentWindowCapacity;
            if (_recentCount < RecentWindowCapacity) _recentCount++;
        }

        /// <summary>Largest sub-step COUNT any single <see cref="SafeMove"/> call issued, over the last
        /// <see cref="RecentWindowCapacity"/> calls.</summary>
        public static int LargestRecentSubStepCount()
        {
            int max = 0;
            for (int i = 0; i < _recentCount; i++)
                if (_recentSteps[i] > max) max = _recentSteps[i];
            return max;
        }

        /// <summary>Largest per-sub-step displacement any single <see cref="SafeMove"/> call issued,
        /// over the last <see cref="RecentWindowCapacity"/> calls.</summary>
        public static float LargestRecentSubStepSize()
        {
            float max = 0f;
            for (int i = 0; i < _recentCount; i++)
                if (_recentStepSize[i] > max) max = _recentStepSize[i];
            return max;
        }

        /// <summary>Whether any of the last <see cref="RecentWindowCapacity"/> <see cref="SafeMove"/>
        /// calls exceeded <see cref="MaxSafeStep"/> (the condition the oversized-move warning gates on)
        /// — read from this rolling window rather than <see cref="HasWarnedOversizedMove"/> itself, since
        /// that flag latches true for the rest of the process the first time it ever fires (MV-926) and
        /// so can't answer "did it happen near THIS fall" for any fall after the first one.</summary>
        public static bool AnyRecentOversizedMove()
        {
            for (int i = 0; i < _recentCount; i++)
                if (_recentOversized[i]) return true;
            return false;
        }

        /// <summary>Test hygiene only — mirrors <see cref="HasWarnedOversizedMove"/>'s own test-settable
        /// seam.</summary>
        public static void ResetRecentWindow()
        {
            _recentIndex = 0;
            _recentCount = 0;
        }

        /// <summary>Moves <paramref name="cc"/> by <paramref name="displacement"/>, splitting it into
        /// up to <see cref="MaxSubSteps"/> steps when it's larger than <see cref="MaxSafeStep"/>. Each
        /// step is its own swept collision test, so a stall-inflated single-frame displacement can't
        /// skip past a thin collider the way one oversized <c>Move()</c> call can.</summary>
        public static void SafeMove(CharacterController cc, Vector3 displacement)
        {
            CallCount++;
            float dist = displacement.magnitude;
            if (dist <= MaxSafeStep)
            {
                MoveSweepCount++;
                RecordRecentStep(1, dist, false);
                cc.Move(displacement);
                return;
            }

            if (!HasWarnedOversizedMove)
            {
                HasWarnedOversizedMove = true;
                // MV-386 diagnostic: this is the exact spike under investigation. Logged once (not
                // every call, MV-926) so a live WebGL browser console can still show that the split
                // path fired at all -- the correlation the ticket's own AC asks for -- without the
                // per-call logging cost that turned into part of the MV-926 feedback loop itself.
                Debug.LogWarning($"[CharacterControllerMotion] {cc.name}: oversized single-frame Move " +
                                  $"({dist:F2} m) split into steps (further occurrences this session are not logged)");
            }

            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / MaxSafeStep), 1, MaxSubSteps);
            Vector3 step = displacement / steps;
            RecordRecentStep(steps, step.magnitude, true);
            for (int i = 0; i < steps; i++)
            {
                MoveSweepCount++;
                cc.Move(step);
            }
        }
    }
}
