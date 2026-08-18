using System;
using System.Collections.Generic;
using MaxWorlds.Core;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The re-cut's weapon/ability backbone (WV-230). MV-422 collapses what used to be four separate
    /// per-enum dictionaries (<see cref="WeaponTrackKind"/>, the old <c>WaterBalloonTrackKind</c>
    /// dictionary, the old <c>SentinelTrackKind</c> dictionary, <see cref="AbilityKind"/>) into one
    /// shared node model, <see cref="RigState"/>, keyed by THE RIG's own string ids
    /// (<see cref="RigBoard"/>) — this class is now a thin, enum-typed compatibility layer over that
    /// single source of truth, so every existing call site (<c>WaterBlaster</c>, <c>WeaponsScreen</c>,
    /// <c>HudController</c>, ...) keeps compiling unchanged while the actual levels/gating live in one
    /// place. <see cref="AbilityKind.WeaponCooldown"/> has no node in the canonical <c>rig_board.json</c>
    /// (it names exactly 23 abilities, none of them a global cooldown-reduction ability) — MV-422
    /// retires it: <see cref="MapId(AbilityKind)"/> returns null for it, so it can never be acquired,
    /// never appears in <see cref="Unacquired"/>, and <see cref="EffectiveCooldownSeconds"/> always
    /// multiplies by 1x (unchanged code, since <see cref="AbilityTuning.CooldownMultiplier"/> at level
    /// 0 is already a no-op).
    /// </summary>
    public static class WeaponSystemState
    {
        // MV-333: the order abilities were actually granted in, not the catalog's fixed order — the
        // weapons screen's slots are keyed off this so a slot, once filled, never moves when a later
        // ability is acquired. RigState has no concept of "acquisition order" (it is a tree, not a
        // queue), so this stays local state here, alongside the enum<->id mapping.
        private static readonly List<AbilityKind> s_acquisitionOrder = new List<AbilityKind>();

        /// <summary>Fired whenever a track levels up, an ability is acquired, an ability levels up, or
        /// the state is reset. Systems that cache a derived value rebuild on this.</summary>
        public static event Action Changed;

        // ---------------------------------------------------------------- enum <-> RIG id mapping

        private static string MapId(WeaponTrackKind kind) => kind switch
        {
            WeaponTrackKind.Range => "p_rng",
            WeaponTrackKind.Spread => "p_spr",
            WeaponTrackKind.Damage => "p_dmg",
            WeaponTrackKind.DepletionRate => "p_flw",
            _ => null,
        };

        private static string MapId(WaterBalloonTrackKind kind) => kind switch
        {
            WaterBalloonTrackKind.Range => "s_lob",
            WaterBalloonTrackKind.SplashArea => "s_spl",
            WaterBalloonTrackKind.RepeatFire => "s_rte",
            _ => null,
        };

        /// <summary>Null for <see cref="AbilityKind.WeaponCooldown"/> — see the class doc comment.</summary>
        private static string MapId(AbilityKind kind) => kind switch
        {
            AbilityKind.Speed => "m_spd",
            AbilityKind.Teleport => "m_tp",
            AbilityKind.WaterBalloon => "s_bal",
            AbilityKind.WaterBalloonAutoFire => "s_aut",
            AbilityKind.ForceField => "e_ff",
            AbilityKind.Sentinels => "u_sen",
            _ => null,
        };

        // ---------------------------------------------------------------- RCDA tracks

        /// <summary>An RCDA track's current level — <c>p_dmg</c> starts at 1 (run start), the rest at
        /// 0 until reached/spent (MV-422: only <c>p_dmg</c> is owned at run start now; Range/Spread/
        /// Flow are stats that become spendable once their own parent is at level &gt;= 1).</summary>
        public static int TrackLevel(WeaponTrackKind kind) => RigState.Level(MapId(kind));

        /// <summary>Spend a part to raise a track by one level (WV-228). Fails if the underlying RIG
        /// node isn't reached yet (MV-422: e.g. Spread needs Range &gt;= 1 first) or is already at its
        /// cap.</summary>
        public static bool LevelUpTrack(WeaponTrackKind kind)
        {
            if (!RigState.TrySpendPart(MapId(kind))) return false;
            Changed?.Invoke();
            return true;
        }

        // ---------------------------------------------------------------- abilities

        /// <summary>0 if not yet acquired from a shed; 1..cap once owned. Always 0 for
        /// <see cref="AbilityKind.WeaponCooldown"/> (retired, MV-422).</summary>
        public static int AbilityLevel(AbilityKind kind)
        {
            string id = MapId(kind);
            return id == null ? 0 : RigState.Level(id);
        }

        public static bool IsAcquired(AbilityKind kind)
        {
            string id = MapId(kind);
            return id != null && RigState.IsOwned(id);
        }

        /// <summary>Every ability Max currently owns, in the order they were acquired (MV-333) — the
        /// weapons screen's Abilities section (WV-232) grows from this; unacquired abilities are never
        /// shown, no locked teasers. Acquisition order, not catalog order, so a slot never moves once
        /// filled.</summary>
        public static IEnumerable<AbilityKind> Acquired => s_acquisitionOrder;

        /// <summary>Abilities Max doesn't own yet AND are reached — the pool a destroyed shed draws
        /// from (WV-229). MV-380/MV-422: Auto-fire is never offered until Water Balloon itself is
        /// already owned — this used to be a hard-coded special case; now it falls straight out of
        /// <c>s_aut</c>'s RIG parent being <c>s_bal</c>, the same reached-ness rule every other node
        /// uses, no special-casing needed.</summary>
        public static IEnumerable<AbilityKind> Unacquired
        {
            get
            {
                foreach (var kind in WeaponCatalog.AllAbilityKinds)
                {
                    string id = MapId(kind);
                    if (id == null) continue; // WeaponCooldown — retired, no RIG node
                    if (RigState.IsOwned(id)) continue;
                    if (!RigState.IsReached(id)) continue;
                    yield return kind;
                }
            }
        }

        private static bool s_waterBalloonAutoFireEnabled = true;

        /// <summary>Player-facing on/off toggle for Water Balloon's auto-fire (MV-380 AC3) — separate
        /// from <see cref="AbilityKind.WaterBalloonAutoFire"/> being OWNED at all: acquiring the
        /// upgrade turns auto-fire on by default (the MV-373 payoff for investing in it), but the
        /// player can switch it off without losing the unlock. Has no effect while the ability itself
        /// isn't acquired — <see cref="MaxWorlds.UI.WaterBalloonJoystickControl"/> checks both.</summary>
        public static bool WaterBalloonAutoFireEnabled
        {
            get => s_waterBalloonAutoFireEnabled;
            set
            {
                if (s_waterBalloonAutoFireEnabled == value) return;
                s_waterBalloonAutoFireEnabled = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Grant an ability at Level 1 — a shed's device (WV-229; a Morphing Module draft in
        /// RIG terms). Idempotent: granting an already-owned ability, or one whose RIG node isn't
        /// reached, is a no-op (returns false); a shed should draw from <see cref="Unacquired"/> so
        /// this shouldn't normally happen. Always fails for <see cref="AbilityKind.WeaponCooldown"/>
        /// (retired, MV-422).</summary>
        public static bool Acquire(AbilityKind kind)
        {
            string id = MapId(kind);
            if (id == null) return false;
            if (!RigState.AcquireCap(id)) return false;
            s_acquisitionOrder.Add(kind);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Spend a part to raise an OWNED ability by one level (WV-228), up to its RIG level
        /// cap. An unacquired ability can't be leveled (returns false) — "unowned/locked items can't
        /// be upgraded".</summary>
        public static bool LevelUpAbility(AbilityKind kind)
        {
            string id = MapId(kind);
            if (id == null) return false;
            if (!RigState.TrySpendPart(id)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The Weapon Cooldown ability's own reduction-per-level fraction, read through
        /// <see cref="DevTuning"/> so the panel can dial it live (WV-234). Retained for
        /// <see cref="EffectiveCooldownSeconds"/>'s formula shape even though the ability itself is
        /// retired (MV-422) — <see cref="AbilityLevel(AbilityKind)"/> for
        /// <see cref="AbilityKind.WeaponCooldown"/> is always 0, so this multiplier is always a no-op
        /// (1x) in practice now.</summary>
        private static float WeaponCooldownReductionPerLevel => DevTuning.Or(
            DevTuning.WeaponCooldownReductionPerLevel, AbilityTuning.DefaultWeaponCooldownReductionPerLevel);

        /// <summary>An ability's cooldown after the (now-retired) Weapon Cooldown ability's per-level
        /// reduction — the number its on-screen control (WV-240) sweeps against. Passive abilities
        /// have a base cooldown of 0 and always return 0.</summary>
        public static float EffectiveCooldownSeconds(AbilityKind kind) =>
            WeaponCatalog.BaseCooldownSeconds(kind) *
            AbilityTuning.CooldownMultiplier(AbilityLevel(AbilityKind.WeaponCooldown), WeaponCooldownReductionPerLevel);

        // ---------------------------------------------------------------- Water Balloon tracks (MV-370)

        /// <summary>A Water Balloon track's current level (MV-370/MV-422). <c>s_spl</c>/<c>s_lob</c>
        /// are reached once <c>s_bal</c> (the Balloon itself) is owned; <c>s_rte</c> additionally needs
        /// <c>s_aut</c> (Auto-Fire) owned — the throw's own tracks are no longer free-standing "owned
        /// from run start" tracks the way they were pre-MV-422.</summary>
        public static int WaterBalloonTrackLevel(WaterBalloonTrackKind kind) => RigState.Level(MapId(kind));

        /// <summary>Spend a part to raise a Water Balloon track by one level (MV-370). Fails if its RIG
        /// node isn't reached yet or is already at its cap.</summary>
        public static bool LevelUpWaterBalloonTrack(WaterBalloonTrackKind kind)
        {
            if (!RigState.TrySpendPart(MapId(kind))) return false;
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

        /// <summary>Back to a fresh run's baseline: THE RIG's own start levels (today, every node 0
        /// except <c>p_dmg</c> at 1), no abilities owned, acquisition order cleared. Fires
        /// <see cref="Changed"/> so the live systems (the blaster's track reads, the weapons screen)
        /// re-fit.</summary>
        public static void Reset()
        {
            RigState.Reset();
            s_acquisitionOrder.Clear();
            s_waterBalloonAutoFireEnabled = true;
            Changed?.Invoke();
        }
    }
}
