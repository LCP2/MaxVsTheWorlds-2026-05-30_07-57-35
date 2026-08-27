using System;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// State behind the boss bar + name card (YT-30 HUD). The named boss (Big Bermuda)
    /// and its real fight land in a later ticket; for the slice this is engaged off an
    /// arena milestone so the bar's appearance, name card, phase segmentation, and drain
    /// are all observable. Pure logic + unit-testable.
    /// </summary>
    public sealed class BossState
    {
        /// <summary>How many segments the spawn-level bar always shows (MV-588) — a HUD-layer constant,
        /// independent of any single boss's own escalation curve, same as <see cref="Phases"/> being a
        /// stand-in number rather than something read off a real boss.</summary>
        public const int MaxSpawnLevel = 4;

        public bool Active { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public int Phases { get; private set; } = 1;
        public float HpNormalized { get; private set; }

        /// <summary>1..<see cref="MaxSpawnLevel"/> — how far the brood volley's composition has
        /// escalated (MV-588).</summary>
        public int SpawnLevel { get; private set; } = 1;

        /// <summary>Progress toward the next spawn level, 0..1.</summary>
        public float SpawnLevelProgress01 { get; private set; }

        /// <summary>Fired when the boss engages (arg=true) or is defeated/cleared (arg=false).</summary>
        public event Action<bool> ActiveChanged;

        /// <summary>Fired whenever boss HP changes while active.</summary>
        public event Action Changed;

        /// <summary>Begin the fight: show the bar full, with a name card and phase count.</summary>
        public void Engage(string name, int phases)
        {
            Name = name ?? string.Empty;
            Phases = Mathf.Max(1, phases);
            HpNormalized = 1f;
            SpawnLevel = 1;
            SpawnLevelProgress01 = 0f;
            Active = true;
            ActiveChanged?.Invoke(true);
        }

        /// <summary>Update the spawn-level bar from the real boss's escalation (MV-588). Ignored while
        /// not active, same guard <see cref="SetNormalized"/>/<see cref="Damage"/> use.</summary>
        public void SetSpawnLevel(int level, float progress01)
        {
            if (!Active) return;
            SpawnLevel = Mathf.Clamp(level, 1, MaxSpawnLevel);
            SpawnLevelProgress01 = Mathf.Clamp01(progress01);
        }

        /// <summary>Set the boss bar directly from a real boss's HP (YT-27). Defeats it at 0.</summary>
        public void SetNormalized(float normalized)
        {
            if (!Active) return;
            HpNormalized = Mathf.Clamp01(normalized);
            Changed?.Invoke();
            if (HpNormalized <= 0f) Defeat();
        }

        /// <summary>Drain the boss by a fraction of its bar (0..1). Defeats it at 0.</summary>
        public void Damage(float fraction)
        {
            if (!Active) return;
            HpNormalized = Mathf.Clamp01(HpNormalized - Mathf.Max(0f, fraction));
            Changed?.Invoke();
            if (HpNormalized <= 0f) Defeat();
        }

        /// <summary>Which phase segment the current HP sits in, 1..Phases (phase 1 = full HP).</summary>
        public int CurrentPhase
        {
            get
            {
                if (!Active) return 0;
                int segment = Mathf.Clamp(Mathf.FloorToInt((1f - HpNormalized) * Phases), 0, Phases - 1);
                return segment + 1;
            }
        }

        public void Defeat()
        {
            if (!Active) return;
            Active = false;
            HpNormalized = 0f;
            ActiveChanged?.Invoke(false);
        }
    }
}
