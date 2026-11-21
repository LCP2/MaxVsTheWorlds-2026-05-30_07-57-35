using System.Globalization;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-1400: what a RIG node's next level does, as "name  current -> next". Every value is computed
    /// by the SAME level-parametrised gameplay function the consumer reads (<c>Ricochet.PowerDamageByLevel</c>,
    /// <c>WeaponCatalog.EffectiveRange</c>, <c>AbilityTuning.*</c>, ...) at an arbitrary level - there is
    /// no duplicated literal table here. A node with no numeric gameplay read (booleans such as ARC /
    /// CLUSTER / SENTINEL, and the TRAP nodes whose radius/slots are inline literals inside
    /// <c>PlayerAbilities</c>) resolves nothing and shows no effect line.
    /// </summary>
    public static class RigEffectLines
    {
        public const string Max = "MAX";

        /// <summary>Resolves <paramref name="id"/>'s gameplay value at <paramref name="level"/> for the
        /// given equipped primary/secondary pair. <paramref name="unit"/> is "", "%", "m", "/s", "s" or
        /// a degree sign. False when the node has no gameplay read.</summary>
        public static bool TryValue(WeaponCatalog.PrimaryKind primary, SecondaryKind secondary,
            string id, int level, out float value, out string unit)
        {
            value = 0f; unit = "";
            if (string.IsNullOrEmpty(id)) return false;
            int l = Mathf.Max(0, level);
            if (id.StartsWith("p_")) return Primary(primary, id, l, out value, out unit);
            if (id.StartsWith("s_")) return Secondary(secondary, id, l, out value, out unit);
            return Shared(id, l, out value, out unit);
        }

        private static bool Primary(WeaponCatalog.PrimaryKind kind, string id, int l, out float v, out string u)
        {
            v = 0f; u = "";
            switch (kind)
            {
                case WeaponCatalog.PrimaryKind.Rcda:
                    switch (id)
                    {
                        case "p_dmg": v = WeaponCatalog.EffectiveDamagePerTick(WaterBlaster.DefaultDamagePerTick, l, WeaponCatalog.DefaultRcdaDamagePerLevel); return true;
                        case "p_rng": v = WeaponCatalog.EffectiveRange(WaterBlaster.DefaultRange, l, WeaponCatalog.DefaultRcdaRangePerLevel); u = "m"; return true;
                        case "p_spr": v = 2f * WeaponCatalog.EffectiveConeHalfAngle(WaterBlaster.DefaultConeHalfAngle, l, WeaponCatalog.DefaultRcdaSpreadPerLevel); u = "°"; return true;
                        case "p_flw":
                            v = WeaponCatalog.EffectiveDrainPerSecond(DevTuning.Or(DevTuning.PrimaryDepletionRate, BlasterTuning.EnergyPerSecond), l, WeaponCatalog.DefaultRcdaDepletionRatePerLevel);
                            u = "/s"; return true;
                    }
                    return false;
                case WeaponCatalog.PrimaryKind.Lppe:
                    switch (id)
                    {
                        case "p_dmg": v = WeaponCatalog.EffectiveDamagePerTick(PulseLaser.DefaultDamagePerPulse, l, WeaponCatalog.DefaultRcdaDamagePerLevel); return true;
                        case "p_rng": v = WeaponCatalog.EffectiveRange(PulseLaser.DefaultLockRange, l, WeaponCatalog.DefaultRcdaRangePerLevel); u = "m"; return true;
                        case "p_rof": v = 1f / WeaponCatalog.EffectivePulseInterval(PulseLaser.DefaultPulseInterval, l, PulseLaser.DefaultRateFloorInterval, WeaponCatalog.MaxLevel(LppeTrackKind.Rate)); u = "/s"; return true;
                        case "p_cap": v = WeaponCatalog.EffectiveMaxEnergy(BlasterTuning.MaxEnergy, l, WeaponCatalog.DefaultLppeCapacityPerLevel); return true;
                    }
                    return false;
                case WeaponCatalog.PrimaryKind.Undertow:
                    switch (id)
                    {
                        case "p_dmg": v = WeaponCatalog.EffectiveDamagePerTick(Undertow.DefaultDamagePerTick, l, WeaponCatalog.DefaultRcdaDamagePerLevel); return true;
                        case "p_rng": v = WeaponCatalog.EffectiveRange(Undertow.DefaultRange, l, WeaponCatalog.DefaultRcdaRangePerLevel); u = "m"; return true;
                        case "p_spr": v = Mathf.Clamp(l, Undertow.MinBeamCount, Undertow.MaxBeamCount); return true;
                    }
                    return false;
                case WeaponCatalog.PrimaryKind.Ricochet:
                    switch (id)
                    {
                        case "p_dmg": v = Ricochet.PowerDamageByLevel[Mathf.Clamp(l, 1, Ricochet.PowerDamageByLevel.Length) - 1]; return true;
                        case "p_ric": v = l; return true;   // the board's own level IS the bounce count (Ricochet.BouncesPerDisc)
                        case "p_aim": v = Ricochet.AimTurnRateDegPerSecByLevel[Mathf.Clamp(l, 0, Ricochet.AimTurnRateDegPerSecByLevel.Length - 1)]; u = "°/s"; return true;
                        case "p_rof": v = Ricochet.RateDiscsPerSecondByLevel[Mathf.Clamp(l, 1, Ricochet.RateDiscsPerSecondByLevel.Length) - 1]; u = "/s"; return true;
                        case "p_cap": v = WeaponCatalog.EffectiveMaxEnergy(BlasterTuning.MaxEnergy, l, WeaponCatalog.DefaultLppeCapacityPerLevel); return true;
                    }
                    return false;
                case WeaponCatalog.PrimaryKind.LightMissile:
                    switch (id)
                    {
                        case "p_dmg": v = LightMissile.DamagePerBallAtLevel(l); return true;
                        case "p_cnt": v = Mathf.Max(1, l); return true;
                        case "p_trk": v = LightMissile.TrackingBaseDegPerSecByLevel[Mathf.Clamp(l, 0, LightMissile.TrackingBaseDegPerSecByLevel.Length - 1)]; u = "°/s"; return true;
                        case "p_rof": v = 1f / LightMissile.FireIntervalAtLevel(l); u = "/s"; return true;
                        case "p_cap": v = WeaponCatalog.EffectiveMaxEnergy(BlasterTuning.MaxEnergy, l, WeaponCatalog.DefaultLppeCapacityPerLevel); return true;
                    }
                    return false;
            }
            return false;
        }

        private static bool Secondary(SecondaryKind kind, string id, int l, out float v, out string u)
        {
            v = 0f; u = "";
            switch (kind)
            {
                case SecondaryKind.WaterBalloon:
                    switch (id)
                    {
                        case "s_lob": v = AbilityTuning.WaterBalloonDistance(l,
                            DevTuning.Or(DevTuning.WaterBalloonBaseDistance, AbilityTuning.DefaultWaterBalloonBaseDistance),
                            DevTuning.Or(DevTuning.WaterBalloonDistancePerLevel, AbilityTuning.DefaultWaterBalloonDistancePerLevel)); u = "m"; return true;
                        case "s_spl": v = AbilityTuning.WaterBalloonSplashRadius(EnemyArchetype.Bruiser.ColliderRadius, l,
                            DevTuning.Or(DevTuning.WaterBalloonSplashMult, AbilityTuning.DefaultWaterBalloonSplashMult),
                            DevTuning.Or(DevTuning.WaterBalloonSplashAreaPerLevel, AbilityTuning.DefaultWaterBalloonSplashAreaPerLevel)); u = "m"; return true;
                        case "s_rte": v = AbilityTuning.WaterBalloonCooldownSeconds(l, WeaponCatalog.WaterBalloonBaseCooldownSeconds(),
                            DevTuning.Or(DevTuning.WaterBalloonRepeatFirePerLevel, AbilityTuning.DefaultWaterBalloonRepeatFirePerLevel)); u = "s"; return true;
                    }
                    return false;
                case SecondaryKind.ShoulderRack:
                    switch (id)
                    {
                        case "s_rkt":
                            int w = RigBoard.ActiveWorldIndex;
                            v = AbilityTuning.ShoulderRackRocketDamage(l, AbilityTuning.ShoulderRackBaseDamageForWorld(w), AbilityTuning.ShoulderRackDamagePerLevelForWorld(w)); return true;
                        case "s_sal": v = AbilityTuning.ShoulderRackSalvoCount(l, AbilityTuning.DefaultShoulderRackMaxSalvoCount); return true;
                        case "s_rld": v = AbilityTuning.ShoulderRackReloadSeconds(l, AbilityTuning.DefaultShoulderRackBaseReloadSeconds,
                            AbilityTuning.DefaultShoulderRackReloadFloorSeconds, WeaponCatalog.MaxLevel(ShoulderRackTrackKind.Reload)); u = "s"; return true;
                        case "s_spl": v = AbilityTuning.ShoulderRackSplashRadius(l, AbilityTuning.DefaultShoulderRackBaseSplashRadius,
                            AbilityTuning.DefaultShoulderRackMaxSplashRadius, WeaponCatalog.MaxLevel(WaterBalloonTrackKind.SplashArea)); u = "m"; return true;
                    }
                    return false;
                case SecondaryKind.Orbitals:
                    switch (id)
                    {
                        case "s_orb": v = AbilityTuning.OrbitalsOrbCount(l, AbilityTuning.DefaultOrbitalsBaseOrbCount, AbilityTuning.DefaultOrbitalsOrbCountPerLevel); return true;
                        case "s_rad": v = AbilityTuning.OrbitalsReachMetres(l, AbilityTuning.DefaultOrbitalsBaseReachMetres, AbilityTuning.DefaultOrbitalsReachPerLevel); u = "m"; return true;
                        case "s_odm": v = AbilityTuning.OrbitalsTouchDamage(l, AbilityTuning.DefaultOrbitalsBaseTouchDamage, AbilityTuning.DefaultOrbitalsTouchDamagePerLevel); return true;
                        case "s_spd": v = AbilityTuning.OrbitalsSpinDegPerSec(l, AbilityTuning.DefaultOrbitalsBaseSpinDegPerSec, AbilityTuning.DefaultOrbitalsSpinPerLevel); u = "°/s"; return true;
                    }
                    return false;
            }
            return false;
        }

        private static bool Shared(string id, int l, out float v, out string u)
        {
            v = 0f; u = "";
            switch (id)
            {
                case "e_ff": v = AbilityTuning.ForceFieldAbsorbCap(l,
                    DevTuning.Or(DevTuning.ForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCap),
                    DevTuning.Or(DevTuning.ForceFieldAbsorbCapPerLevel, AbilityTuning.DefaultForceFieldAbsorbCapPerLevel)); return true;
                case "e_cel": v = PickupWallet.DefaultCapacity + l * PickupWallet.PowerCellCapacityPerLevel; return true;
                case "e_mag":
                case "e_cmg": v = AbilityTuning.MagnetoPullRadius(l, AbilityTuning.DefaultMagnetoPullRadiusBase, AbilityTuning.DefaultMagnetoPullRadiusPerLevel); u = "m"; return true;
                case "m_spd": v = 100f * AbilityTuning.SpeedMultiplier(l, DevTuning.Or(DevTuning.SpeedMultiplierPerLevel, AbilityTuning.DefaultSpeedMultiplierPerLevel)); u = "%"; return true;
                case "m_tp": v = AbilityTuning.TeleportDistance(l,
                    DevTuning.Or(DevTuning.TeleportBaseDistance, AbilityTuning.DefaultTeleportBaseDistance),
                    DevTuning.Or(DevTuning.TeleportDistancePerLevel, AbilityTuning.DefaultTeleportDistancePerLevel)); u = "m"; return true;
                case "u_dmg": v = AbilityTuning.SentinelDamagePerShot(l, AbilityTuning.DefaultSentinelBaseDamage, AbilityTuning.DefaultSentinelDamagePerLevel); return true;
                case "u_rng": v = AbilityTuning.SentinelRange(l, AbilityTuning.DefaultSentinelRange, AbilityTuning.DefaultSentinelRangePerLevel); u = "m"; return true;
                case "u_hp": v = AbilityTuning.SentinelMaxHp(l, AbilityTuning.DefaultSentinelBaseHp, AbilityTuning.DefaultSentinelHpPerLevel); return true;
                case "u_mov": v = AbilityTuning.SentinelMoveSpeed(l, AbilityTuning.DefaultSentinelMoveSpeedPerLevel); u = "m/s"; return true;
                case "u_cst": v = AbilityTuning.SentinelCost(l, AbilityTuning.DefaultSentinelCost, AbilityTuning.DefaultSentinelCostReductionPerLevel); return true;
                case "u_slt": v = AbilityTuning.SentinelDeploymentSlots(l); return true;
            }
            return false;
        }

        /// <summary>Plain-number format: whole numbers without a decimal, else up to two, trailing zeros
        /// trimmed, plus the unit.</summary>
        public static string FormatValue(float value, string unit)
        {
            string n = Mathf.Abs(value - Mathf.Round(value)) < 0.005f
                ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
            return n + unit;
        }

        /// <summary>"12 → 15" while <paramref name="level"/> &lt; <paramref name="maxLevel"/>, else
        /// "40 MAX". Null when the node has no gameplay read.</summary>
        public static string ValueText(WeaponCatalog.PrimaryKind primary, SecondaryKind secondary,
            string id, int level, int maxLevel)
        {
            if (!TryValue(primary, secondary, id, level, out float cur, out string unit)) return null;
            if (level >= maxLevel) return FormatValue(cur, unit) + " " + Max;
            TryValue(primary, secondary, id, level + 1, out float next, out _);
            return FormatValue(cur, unit) + " → " + FormatValue(next, unit);
        }
    }
}
