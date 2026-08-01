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

        public static bool IsAcquired(AbilityKind kind) => s_abilityLevels[kind] > 0;

        /// <summary>Every ability Max currently owns, in catalog order — the weapons screen's
        /// Abilities section (WV-232) grows from this; unacquired abilities are never shown, no
        /// locked teasers.</summary>
        public static IEnumerable<AbilityKind> Acquired
        {
            get
            {
                foreach (var kind in WeaponCatalog.AllAbilityKinds)
                    if (IsAcquired(kind)) yield return kind;
            }
        }

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
        }
    }
}
