using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-980: the ONE place a Dormant robot's wake test now runs from. <see cref="RobotEnemy.Tick"/>
    /// returns immediately for a Dormant robot every frame (no gravity/SafeMove, no LineOfSight, no
    /// fall-recovery tick, no separation-grid write) — this is what wakes it instead, on a single
    /// central, bounded pass rather than each instance paying its own per-frame MonoBehaviour dispatch.
    ///
    /// Replaces MV-936's own per-instance round-robin: that throttle bounded only the wake CHECK itself
    /// (one call in <see cref="TickIntervalCalls"/>) while everything else in a Dormant robot's own
    /// TickBody — gravity, the separation-grid write, and above all the LineOfSight raycast underneath
    /// <c>Perception.Tick</c> — kept running every single frame regardless, for every dormant robot in
    /// reach (MV-966 parks everything OUTSIDE it; this ticket's own Observation is what's left INSIDE:
    /// up to 100 garrison robots per area in Worlds 1/2, where World 3's own budget-queue composition
    /// never lets more than ~30 robots exist at all).
    ///
    /// Ticked from ONE static, testable <see cref="Tick"/> (the MonoBehaviour's own Update is a thin
    /// wrapper reading <see cref="Time.deltaTime"/> — same "a test drives the static method with an
    /// explicit dt" idiom every Tick* method in this codebase already uses) rather than a real frame
    /// clock, so a dropped/resumed frame, or an EditMode test with no player loop at all, behaves
    /// identically. Self-installs the same way <see cref="MaxWorlds.VFX.GroundAnchorVfx"/> does.
    /// </summary>
    [MaxWorlds.Core.PerfSection("robot")]
    public sealed class DormantWakeScheduler : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<DormantWakeScheduler>() != null) return;
            new GameObject("DormantWakeScheduler").AddComponent<DormantWakeScheduler>();
        }

        /// <summary>The ticket's own "at most 10 Hz" — a ceiling on how often a pass may run, not a
        /// target rate: a single large dt still only ever spends ONE pass (see <see cref="Tick"/>), so
        /// this bounds worst-case cost rather than promising a catch-up burst after a stall.</summary>
        public const float TickInterval = 0.1f;

        private static float s_accumulator;

        /// <summary>How many passes have actually run — test-only diagnostic (MV-980), same "cache
        /// MISS, not a claim" idiom as <see cref="MaxWorlds.VFX.GroundAnchorVfx.AnchorRecomputeCount"/>.</summary>
        public static int PassCount { get; private set; }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real body — testable with an explicit dt. Accumulates real time and only walks
        /// the dormant population once <see cref="TickInterval"/> has elapsed; never more than once per
        /// call, however large <paramref name="dt"/> is, so a single call can never spend more than one
        /// pass's worth of cost regardless of how much simulated time it covers.</summary>
        public static void Tick(float dt)
        {
            s_accumulator += dt;
            if (s_accumulator < TickInterval) return;
            s_accumulator -= TickInterval;

            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                if (r != null && r.IsDormant) r.CentralWakeCheck(TickInterval);
            }
            PassCount++;
        }

        /// <summary>Test-only reset (MV-980) — same "static state needs its own hygiene hook" idiom as
        /// <see cref="RobotEnemy.ResetRegistry"/>.</summary>
        public static void ResetForTests()
        {
            s_accumulator = 0f;
            PassCount = 0;
        }
    }
}
