using System;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Backs the bottom-centre arena / sub-zone indicator (YT-30 HUD): "sub-zones
    /// cleared / factories destroyed". Pure logic + unit-testable. The real sub-zone
    /// and destructible-factory systems land in later tickets; for the slice the counts
    /// are driven off combat milestones (see <see cref="HudModel"/>). <see cref="Changed"/>
    /// carries whether the change should be shown prominently (the spec's ~1s pop on a
    /// sub-zone transition) vs. a quiet tick.
    /// </summary>
    public sealed class ArenaProgress
    {
        public int SubZonesTotal { get; }

        /// <summary>How many factories this run has. Not readonly, because the run does not know until
        /// the level is built: the map decides how many sources it has, and the HUD is constructed
        /// before the map has said (YT-92). <see cref="SetFactoriesTotal"/> is how it finds out.</summary>
        public int FactoriesTotal { get; private set; }

        public int SubZonesCleared { get; private set; }
        public int FactoriesDestroyed { get; private set; }

        /// <summary>MV-706: true for a world whose sources are Replicators with no sheds — the bottom
        /// banner reads "REPLICATORS n/N" instead of "FACTORIES n/N". Defaults false (World 1's
        /// wording), set once the map finishes building (<see cref="SetReplicatorWorld"/>).</summary>
        public bool IsReplicatorWorld { get; private set; }

        /// <summary>Fired on a count change. Arg = true when it should pop prominently
        /// (a sub-zone was cleared), false for a quiet factory tick.</summary>
        public event Action<bool> Changed;

        public ArenaProgress(int subZonesTotal, int factoriesTotal)
        {
            SubZonesTotal = Mathf.Max(1, subZonesTotal);
            FactoriesTotal = Mathf.Max(0, factoriesTotal);
        }

        /// <summary>True once every sub-zone is cleared and every factory destroyed.</summary>
        public bool Complete => SubZonesCleared >= SubZonesTotal && FactoriesDestroyed >= FactoriesTotal;

        /// <summary>Overall completion fraction (both counts weighted equally), 0..1.</summary>
        public float Fraction
        {
            get
            {
                int done = SubZonesCleared + FactoriesDestroyed;
                int total = SubZonesTotal + FactoriesTotal;
                return total > 0 ? Mathf.Clamp01((float)done / total) : 0f;
            }
        }

        /// <summary>Advance to the next sub-zone (prominent pop). Clamped at the total.</summary>
        public void ClearSubZone()
        {
            if (SubZonesCleared >= SubZonesTotal) return;
            SubZonesCleared++;
            Changed?.Invoke(true);
        }

        /// <summary>Tell the tracker how many factories the level actually has. Clamped at the number
        /// already destroyed, so a total can never be set below the progress the player has made.</summary>
        public void SetFactoriesTotal(int total)
        {
            FactoriesTotal = Mathf.Max(FactoriesDestroyed, total);
            Changed?.Invoke(false);
        }

        /// <summary>Register a destroyed factory (quiet tick). Clamped at the total.</summary>
        public void DestroyFactory()
        {
            if (FactoriesDestroyed >= FactoriesTotal) return;
            FactoriesDestroyed++;
            Changed?.Invoke(false);
        }

        /// <summary>MV-706: which word the banner uses. A quiet tick (Lee's eye isn't meant to be
        /// pulled by a label swap the way a count change pulls it) — but still notifies, so the HUD
        /// text rebuilds even if it happens to land on an otherwise-unchanged count.</summary>
        public void SetReplicatorWorld(bool isReplicatorWorld)
        {
            IsReplicatorWorld = isReplicatorWorld;
            Changed?.Invoke(false);
        }
    }
}
