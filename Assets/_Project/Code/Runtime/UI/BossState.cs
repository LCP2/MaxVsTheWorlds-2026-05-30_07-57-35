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
        public bool Active { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public int Phases { get; private set; } = 1;
        public float HpNormalized { get; private set; }

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
            Active = true;
            ActiveChanged?.Invoke(true);
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
