using System;
using System.Collections.Generic;
using MaxWorlds.Core;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The re-cut's weapon/ability backbone (WV-230): the RCDA primary's four tracks (owned from run
    /// start, all Level 1) and the six shed-acquired abilities (Level 0 = not owned). Same
    /// static/event-driven shape as <see cref="MaxWorlds.Upgrades.UpgradeState"/> — one Max, several
    /// systems reading the same numbers — since this is the system that gradually replaces it as
    /// WV-226/228/229/231 land. <see cref="Reset"/> for a new run and test isolation.
    /// </summary>
    public static class WeaponSystemState
    {
        private static readonly Dictionary<WeaponTrackKind, int> s_trackLevels = new Dictionary<WeaponTrackKind, int>();
        private static readonly Dictionary<AbilityKind, int> s_abilityLevels = new Dictionary<AbilityKind, int>();
        private static readonly Dictionary<WaterBalloonTrackKind, int> s_waterBalloonTrackLevels = new Dictionary<WaterBalloonTrackKind, int>();
        // MV-333: the order abilities were actually granted in, not the catalog's fixed order — the
        // weapons screen's slots are keyed off this so a slot, once filled, never moves when a later
        // ability is acquired (the catalog order previously resorted "Acquired" every Refresh, which
        // both showed the wrong ability first when a late-catalog one like Weapon Cooldown was granted
        // alone, and reshuffled it out of slot 1 the moment an earlier-catalog ability arrived).
        private static readonly List<AbilityKind> s_acquisitionOrder = new List<AbilityKind>();

        static WeaponSystemState() => ResetLevels();

        /// <summary>Fired whenever a track levels up, an ability is acquired, an ability levels up, or
        /// the state is reset. Systems that cache a derived value rebuild on this.</summary>
        public static event Action Changed;

        /// <summary>An RCDA track's current level. Every track starts at 1 and is owned from run
        /// start — unlike an ability, there is no "not owned" state for the primary.</summary>
        public static int TrackLevel(WeaponTrackKind kind) => s_trackLevels[kind];

        /// <summary>Spend a part to raise a track by one level (WV-228), up to its
        /// <see cref="WeaponCatalog.MaxLevel(WeaponTrackKind)"/> cap. No-ops (returns false) already
        /// at the cap.</summary>
        public static bool LevelUpTrack(WeaponTrackKind kind)
        {
            int level = s_trackLevels[kind];
            if (level >= WeaponCatalog.MaxLevel(kind)) return false;
            s_trackLevels[kind] = level + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>0 if not yet acquired from a shed; 1..cap once owned.</summary>
        public static int AbilityLevel(AbilityKind kind) => s_abilityLevels[kind];

        /// <summary>A Water Balloon track's current level (MV-370). Every track starts at 1 and is
        /// owned from run start, same as an RCDA track — Water Balloon is a primary add-on now, not a
        /// shed-acquired ability.</summary>
        public static int WaterBalloonTrackLevel(WaterBalloonTrackKind kind) => s_waterBalloonTrackLevels[kind];

        /// <summary>Spend a part to raise a Water Balloon track by one level (MV-370), up to its
        /// <see cref="WeaponCatalog.MaxLevel(WaterBalloonTrackKind)"/> cap. No-ops (returns false)
        /// already at the cap.</summary>
        public static bool LevelUpWaterBalloonTrack(WaterBalloonTrackKind kind)
        {
            int level = s_waterBalloonTrackLevels[kind];
            if (level >= WeaponCatalog.MaxLevel(kind)) return false;
            s_waterBalloonTrackLevels[kind] = level + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The Water Balloon's own Repeat Fire track's cooldown-cut-per-level fraction, read
        /// through <see cref="DevTuning"/> so the panel can dial it live (MV-370, same idiom as
        /// <see cref="WeaponCooldownReductionPerLevel"/>).</summary>
        private static float WaterBalloonRepeatFirePerLevel => DevTuning.Or(
            DevTuning.WaterBalloonRepeatFirePerLevel, AbilityTuning.DefaultWaterBalloonRepeatFirePerLevel);

        /// <summary>Water Balloon's throw cooldown after its own Repeat Fire track (MV-370) — the
        /// on-screen control's cooldown sweep reads this, same role <see cref="EffectiveCooldownSeconds"/>
        /// plays for the AbilityKind-gated controls Water Balloon left behind.</summary>
        public static float WaterBalloonEffectiveCooldownSeconds() => AbilityTuning.WaterBalloonCooldownSeconds(
            WaterBalloonTrackLevel(WaterBalloonTrackKind.RepeatFire),
            WeaponCatalog.WaterBalloonBaseCooldownSeconds(),
            WaterBalloonRepeatFirePerLevel);

        public static bool IsAcquired(AbilityKind kind) => s_abilityLevels[kind] > 0;

        /// <summary>Every ability Max currently owns, in the order they were acquired (MV-333) — the
        /// weapons screen's Abilities section (WV-232) grows from this; unacquired abilities are never
        /// shown, no locked teasers. Acquisition order, not catalog order, so a slot never moves once
        /// filled.</summary>
        public static IEnumerable<AbilityKind> Acquired => s_acquisitionOrder;

        /// <summary>Abilities Max doesn't own yet — the pool a destroyed shed draws from (WV-229):
        /// "one random ability Max doesn't already own".</summary>
        public static IEnumerable<AbilityKind> Unacquired
        {
            get
            {
                foreach (var kind in WeaponCatalog.AllAbilityKinds)
                    if (!IsAcquired(kind)) yield return kind;
            }
        }

        /// <summary>Grant an ability at Level 1 — a shed's device (WV-229). Idempotent: granting an
        /// already-owned ability is a no-op (returns false); a shed should draw from
        /// <see cref="Unacquired"/> so this shouldn't normally happen.</summary>
        public static bool Acquire(AbilityKind kind)
        {
            if (s_abilityLevels[kind] > 0) return false;
            s_abilityLevels[kind] = 1;
            s_acquisitionOrder.Add(kind);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Spend a part to raise an OWNED ability by one level (WV-228), up to its
        /// <see cref="WeaponCatalog.MaxLevel(AbilityKind)"/> cap. An unacquired ability can't be
        /// leveled (returns false) — "unowned/locked items can't be upgraded".</summary>
        public static bool LevelUpAbility(AbilityKind kind)
        {
            int level = s_abilityLevels[kind];
            if (level <= 0) return false;
            if (level >= WeaponCatalog.MaxLevel(kind)) return false;
            s_abilityLevels[kind] = level + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The Weapon Cooldown ability's own reduction-per-level fraction, read through
        /// <see cref="DevTuning"/> so the panel can dial it live (WV-234).</summary>
        private static float WeaponCooldownReductionPerLevel => DevTuning.Or(
            DevTuning.WeaponCooldownReductionPerLevel, AbilityTuning.DefaultWeaponCooldownReductionPerLevel);

        /// <summary>An ability's cooldown after the Weapon Cooldown ability's per-level reduction —
        /// the number its on-screen control (a controls-ticket concern, WV-240) sweeps against.
        /// Passive abilities have a base cooldown of 0 and always return 0.</summary>
        public static float EffectiveCooldownSeconds(AbilityKind kind) =>
            WeaponCatalog.BaseCooldownSeconds(kind) *
            AbilityTuning.CooldownMultiplier(AbilityLevel(AbilityKind.WeaponCooldown), WeaponCooldownReductionPerLevel);

        /// <summary>Back to a fresh run's baseline: every track at Level 1, no abilities owned. Fires
        /// <see cref="Changed"/> so the live systems (the blaster's Power Efficiency read, a future
        /// weapons screen) re-fit.</summary>
        public static void Reset()
        {
            ResetLevels();
            Changed?.Invoke();
        }

        private static void ResetLevels()
        {
            foreach (var kind in WeaponCatalog.AllTrackKinds) s_trackLevels[kind] = 1;
            foreach (var kind in WeaponCatalog.AllAbilityKinds) s_abilityLevels[kind] = 0;
            foreach (var kind in WeaponCatalog.AllWaterBalloonTrackKinds) s_waterBalloonTrackLevels[kind] = 1;
            s_acquisitionOrder.Clear();
        }
    }
}
