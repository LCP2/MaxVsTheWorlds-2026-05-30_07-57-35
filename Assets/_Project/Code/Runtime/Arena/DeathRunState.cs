using System;
using System.Collections.Generic;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Per-run state that must survive a death (MV-427: death continues the run rather than ending
    /// it) but resets on a genuinely fresh run — same static/event idiom as <see cref="MaxWorlds.Pickups.PickupWallet"/>/
    /// <see cref="MaxWorlds.Weapons.WeaponSystemState"/>, reset from <c>HomeScreen.StartSlot</c>.
    ///
    /// Its whole job is the rule that stops a restored arena from being farmed: an area's one
    /// guaranteed Bruiser-kill part, and a shed's Morphing Module, must each be granted at most once
    /// EVER, not once per (possibly repeated) clear. A shed's own destroyed state already can't
    /// un-destroy (<see cref="MaxWorlds.Factories.DestructibleHealth"/> never revives), so only the
    /// area-part rule needs a flag here.
    /// </summary>
    public static class DeathRunState
    {
        private static readonly HashSet<int> s_partGrantedAreas = new HashSet<int>();

        /// <summary>How many times Max has died so far this run — the new personal-best discriminator
        /// (<c>SaveSlotData.BestDeathsToVictory</c>) now that a death no longer ends the run.</summary>
        public static int DeathsTaken { get; private set; }

        /// <summary>Fired whenever <see cref="DeathsTaken"/> changes (a death, or a reset).</summary>
        public static event Action<int> DeathsChanged;

        /// <summary>An area's last-Bruiser part reward is granted at most once, ever — call this
        /// exactly when the reward is about to be handed out; it returns true the first time for a
        /// given <paramref name="areaIndex"/> and false every time after, including after that area's
        /// robots are wiped and respawned by a death (<c>AreaAccumulationDirector.RestoreArea</c>).</summary>
        public static bool TryGrantAreaPart(int areaIndex)
        {
            return s_partGrantedAreas.Add(areaIndex);
        }

        /// <summary>Whether <paramref name="areaIndex"/> has already had its part granted this run.</summary>
        public static bool HasGrantedAreaPart(int areaIndex) => s_partGrantedAreas.Contains(areaIndex);

        /// <summary>Max fell. Bumps the death counter this run's "continue" flow reports at the end.</summary>
        public static void RecordDeath()
        {
            DeathsTaken++;
            DeathsChanged?.Invoke(DeathsTaken);
        }

        /// <summary>Back to a fresh run's baseline: no area has granted its part, no deaths taken.
        /// Test isolation and a new run (a fresh <c>HomeScreen.StartSlot</c> pick).</summary>
        public static void Reset()
        {
            s_partGrantedAreas.Clear();
            DeathsTaken = 0;
            DeathsChanged?.Invoke(DeathsTaken);
        }
    }
}
