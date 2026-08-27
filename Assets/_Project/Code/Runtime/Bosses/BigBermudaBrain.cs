using UnityEngine;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Pure fight-state ticker for the slice Big Bermuda boss (MV-588). The old attack cycle
    /// (Reposition → ChargeWindup → Charge → Recover) is gone entirely — the boss just walks at Max and
    /// stops at a standoff (<see cref="BigBermudaBoss"/>) — so what is left to track is time-based:
    ///
    ///   * <see cref="Enraged"/> — HP threshold, unchanged from before.
    ///   * <see cref="SpawnLevel"/> — how far the brood volley's composition has escalated, purely off
    ///     seconds alive since the fight started ticking (never off anything the player does; see
    ///     <see cref="BroodSpawnLevels"/> for what each level actually flings).
    ///
    /// No MonoBehaviour, so both are unit-testable without a scene.
    /// </summary>
    public sealed class BigBermudaBrain
    {
        private readonly float _enrageThreshold;
        private float _aliveSeconds;

        /// <summary>True while HP is at/below the enrage threshold — drives blade-rain + speed.</summary>
        public bool Enraged { get; private set; }

        /// <summary>1..<see cref="BossTuning.MaxSpawnLevel"/>. Escalates by
        /// <see cref="BossTuning.SpawnLevelInterval"/> seconds alive, capped.</summary>
        public int SpawnLevel { get; private set; } = 1;

        /// <summary>Progress toward the NEXT level, 0..1 — pinned at 1 once <see cref="SpawnLevel"/> is
        /// capped, so the HUD bar's active segment can show a fully-lit last segment rather than one
        /// that looks stuck mid-fill.</summary>
        public float SpawnLevelProgress01 { get; private set; }

        public BigBermudaBrain(float enrageThreshold = BossTuning.EnrageThreshold)
        {
            _enrageThreshold = Mathf.Clamp01(enrageThreshold);
        }

        /// <summary>Advance the clock. Call once per frame with deltaTime and current HP fraction.</summary>
        public void Tick(float dt, float hpNormalized)
        {
            Enraged = hpNormalized <= _enrageThreshold;
            _aliveSeconds += Mathf.Max(0f, dt);

            float raw = _aliveSeconds / Mathf.Max(0.01f, BossTuning.SpawnLevelInterval);
            SpawnLevel = Mathf.Clamp(1 + Mathf.FloorToInt(raw), 1, BossTuning.MaxSpawnLevel);
            SpawnLevelProgress01 = SpawnLevel >= BossTuning.MaxSpawnLevel
                ? 1f
                : Mathf.Clamp01(raw - (SpawnLevel - 1));
        }
    }
}
