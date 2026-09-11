using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-774: makes World 2's FLOOD bar (captioned "THE STORMDRAIN IS FILLING") real. Before this
    /// ticket the dial filled across the run and nothing in the world changed — a fully built piece of
    /// UI promising a mechanic that did not exist. Now, as the drain fills, the lowest ground floods and
    /// becomes hostile (the existing sludge slow, plus damage-over-time — nothing new), and the player
    /// routes against that clock.
    ///
    /// Global and static, same shape as <see cref="MaxWorlds.Enemies.DifficultyDirector"/>: one flood
    /// for the run, ticked once by <see cref="StormdrainFloodRunner"/>. <see cref="MaxWorlds.Arena.Map.MapRuntime.Build"/>
    /// resets it and hands it this world's authored combat-area count, the same point it resets
    /// <c>DifficultyDirector</c>.
    ///
    /// "Which ground is lowest" is banded by an area's 1-based index among the world's authored combat
    /// areas (<see cref="BandForAreaIndex"/>) — a stand-in for real floor height, exactly as the ticket's
    /// own "one line to replace when the redraw lands" describes.
    /// </summary>
    public static class StormdrainFlood
    {
        /// <summary>First surge: the lowest band (closest to the Wet Well) floods.</summary>
        public const float Band1Threshold = 0.25f;

        /// <summary>Second surge: the next band up floods too.</summary>
        public const float Band2Threshold = 0.50f;

        /// <summary>Surviving pump housings start draining the bar past here.</summary>
        public const float PumpDrainThreshold = 0.75f;

        /// <summary>The Wet Well opens regardless of readiness.</summary>
        public const float WetWellThreshold = 1f;

        /// <summary>Rule 1: every surge gets this much warning — the siren caption, the sludge lip
        /// brightening, the water visibly climbing the kerbs (presentation, left to a follow-up; the
        /// mechanical guarantee this ticket's ACs cover is that the ground itself stays harmless for at
        /// least this long after crossing a threshold) — before the ground there actually turns
        /// harmful. A flood that kills without a tell is a cheap death.</summary>
        public const float WarningSeconds = 3f;

        /// <summary>Flooded ground applies the existing sludge slow and nothing new — the same value
        /// already authored as <see cref="WorldDials.sludgeSpeedMultiplier"/>'s default.</summary>
        public const float SpeedMultiplier = 0.6f;

        public const float DamagePerSecond = 6f;

        /// <summary>Damage ticks on a fixed interval, not per-frame, so it is frame-rate independent
        /// and testable — the same idiom MV-769 gives the puddle's own tick (this ticket's own "reuse
        /// that path").</summary>
        public const float DamageTickInterval = 0.25f;

        /// <summary>The "Rate" section's own calibration: an unhurried full clear (no Replicators ever
        /// built) reaches this fraction of the bar by the time the run's authored length elapses. Tied
        /// to <see cref="DifficultyDirector.RunLengthSeconds"/> rather than a second authored duration,
        /// so the two clocks can never quietly disagree about how long a run is.</summary>
        public const float UnhurriedFillAtRunLength = 0.85f;

        /// <summary>Fill added per live Replicator, per second — what finally makes the "REPLICATORS
        /// n/25" counter mean something: the machines are why the water is rising.</summary>
        public const float ReplicatorFillBonusPerSecond = 0.01f;

        /// <summary>Fill removed per live pump housing, per second, once <see cref="PumpDrainThreshold"/>
        /// is crossed. No pump housing entity is authored into a world yet (a follow-up), so this is
        /// always multiplied by 0 in the shipped game today — the hook exists so this ticket's own
        /// "every live pump housing slows the bar" rule is already correct the day one is.</summary>
        public const float PumpHousingDrainPerSecond = 0.02f;

        private static float _level01;
        private static float _band1SurgeElapsed = -1f;
        private static float _band2SurgeElapsed = -1f;
        private static int _totalCombatAreas;

        /// <summary>0 (dry) .. 1 (Wet Well opens) — what the HUD's FLOOD bar shows for real now.</summary>
        public static float Level01 => _level01;

        /// <summary>Back to a fresh run's flood. Called when a level starts building, same reasoning as
        /// <see cref="DifficultyDirector.Reset"/>: a new run must not inherit the last one's water.</summary>
        public static void Reset()
        {
            _level01 = 0f;
            _band1SurgeElapsed = -1f;
            _band2SurgeElapsed = -1f;
            _totalCombatAreas = 0;
        }

        /// <summary>This world's authored combat-area count (<c>WorldDials.areaCount</c>), so
        /// <see cref="BandForAreaIndex"/> can band an area without threading the live <c>WorldConfig</c>
        /// through every caller. 0 (a world with no route, or a hand-built test fixture) means every
        /// area reads as band 0 — never flooded.</summary>
        public static void Configure(int totalCombatAreas) => _totalCombatAreas = Mathf.Max(0, totalCombatAreas);

        private static float BaseFillPerSecond =>
            UnhurriedFillAtRunLength / DifficultyDirector.RunLengthSeconds;

        /// <summary>Advance the flood by one frame. Rule 3: <paramref name="dt"/> must already be scaled
        /// time (0 while the run is paused or a screen is open) — same contract as
        /// <see cref="DifficultyDirector.Tick"/> — so passing 0 here is exactly "the flood does not
        /// advance". <paramref name="liveReplicators"/>/<paramref name="livePumpHousings"/> are read
        /// live so a built/killed one changes the rate from the very next tick.</summary>
        public static void Tick(float dt, int liveReplicators, int livePumpHousings)
        {
            dt = Mathf.Max(0f, dt);
            if (dt > 0f)
            {
                float rate = BaseFillPerSecond + Mathf.Max(0, liveReplicators) * ReplicatorFillBonusPerSecond;
                if (_level01 >= PumpDrainThreshold)
                    rate -= Mathf.Max(0, livePumpHousings) * PumpHousingDrainPerSecond;
                _level01 = Mathf.Clamp01(_level01 + rate * dt);
            }

            TickSurge(ref _band1SurgeElapsed, _level01 >= Band1Threshold, dt);
            TickSurge(ref _band2SurgeElapsed, _level01 >= Band2Threshold, dt);
        }

        private static void TickSurge(ref float elapsed, bool surging, float dt)
        {
            if (!surging) { elapsed = -1f; return; }
            elapsed = elapsed < 0f ? 0f : elapsed + dt;
        }

        /// <summary>The threshold has been crossed, whether or not the warning has finished — what a
        /// surge's own tell (siren/lip/kerbs) would key off.</summary>
        public static bool Band1Surging => _level01 >= Band1Threshold;
        public static bool Band2Surging => _level01 >= Band2Threshold;

        /// <summary>The warning has finished — the ground is actually harmful now (Rule 1).</summary>
        public static bool Band1Flooded => _band1SurgeElapsed >= WarningSeconds;
        public static bool Band2Flooded => _band2SurgeElapsed >= WarningSeconds;

        public static bool PumpsDraining => _level01 >= PumpDrainThreshold;
        public static bool WetWellOpen => _level01 >= WetWellThreshold;

        /// <summary>Which flood band an authored combat area belongs to, by its 1-based index among
        /// <paramref name="totalAreas"/> (MV-774's own stand-in rule): the last third of the route
        /// (closest to the Wet Well) is band 1, the middle third is band 2, the first third never floods
        /// this slice. Pure and unit-testable with no live world.</summary>
        public static int BandForAreaIndex(int areaIndex, int totalAreas)
        {
            if (totalAreas <= 0 || areaIndex <= 0) return 0;
            float frac = areaIndex / (float)totalAreas;
            if (frac > 2f / 3f) return 1;
            if (frac > 1f / 3f) return 2;
            return 0;
        }

        public static bool IsBandFlooded(int band) => band switch
        {
            1 => Band1Flooded,
            2 => Band2Flooded,
            _ => false,
        };

        /// <summary>True if the authored combat area at <paramref name="areaIndex"/> (of this world's
        /// own <see cref="Configure"/>d total) is currently flooded.</summary>
        public static bool IsAreaIndexFlooded(int areaIndex) =>
            IsBandFlooded(BandForAreaIndex(areaIndex, _totalCombatAreas));

        /// <summary>The flood's own contribution to <see cref="MaxWorlds.Arena.MapSlowZones.SpeedMultiplierAt"/>
        /// — 1 (unaffected) outside a flooded area's zone, else <see cref="SpeedMultiplier"/>. Resolves
        /// the world position to a zone/area the same way <see cref="MaxWorlds.Bosses.SludgequeenBoss.FloodSpeedMultiplierAt"/>
        /// and <see cref="MaxWorlds.Bosses.BigBermudaBoss"/> already do.</summary>
        public static float SpeedMultiplierAt(Vector3 worldPosition) =>
            IsFlooded(worldPosition) ? SpeedMultiplier : 1f;

        /// <summary>True if <paramref name="worldPosition"/> falls inside a currently-flooded area.</summary>
        public static bool IsFlooded(Vector3 worldPosition)
        {
            MapData map = EnemyNavigation.Map;
            MapZone zone = map?.ZoneAt(worldPosition.x, worldPosition.y, worldPosition.z);
            if (zone == null) return false;
            return IsAreaIndexFlooded(AreaAccumulationDirector.AreaIndexOf(zone.id));
        }
    }

    /// <summary>Per-receiver damage-over-time state for standing in flooded ground (MV-774) — a struct,
    /// not shared global state, so Max and every live robot each carry their own independent 0.25 s
    /// clock with no cross-talk (the trap a single shared accumulator on <see cref="StormdrainFlood"/>
    /// itself would fall into the moment more than one receiver is ever flooded at once).</summary>
    public struct FloodDamageTicker
    {
        private float _accumulator;

        /// <summary>One evaluation. A no-op — and the clock resets — the instant
        /// <paramref name="isFlooded"/> reads false, so leaving flooded ground and re-entering later
        /// never "banks" a partial tick against the next visit.</summary>
        public void Tick(float dt, bool isFlooded, IDamageable receiver, Vector3 position)
        {
            if (!isFlooded) { _accumulator = 0f; return; }
            if (dt <= 0f || receiver == null || !receiver.IsAlive) return;

            _accumulator += dt;
            while (_accumulator >= StormdrainFlood.DamageTickInterval)
            {
                _accumulator -= StormdrainFlood.DamageTickInterval;
                float amount = StormdrainFlood.DamagePerSecond * StormdrainFlood.DamageTickInterval;
                receiver.TakeDamage(new DamageInfo(amount, position, Vector3.up, Team.Neutral));
            }
        }
    }
}
