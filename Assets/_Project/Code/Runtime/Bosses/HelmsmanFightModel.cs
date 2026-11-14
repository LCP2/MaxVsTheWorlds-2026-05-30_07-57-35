using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// MV-1372: the fight-length model <see cref="HelmsmanTuning.Health"/> is tuned against. A fully
    /// upgraded Max (Light Missile COUNT 5 / POWER 8 / RATE 5 / CAPACITY 5 with MV-1367's draining
    /// gauge, Orbitals 8 orbs at POWER 5) fires without stopping at the Helmsman; 60% of his shots reach
    /// it and the orbs are in reach 30% of the time. Every number is read from the real tables
    /// (<see cref="LightMissile"/>, <see cref="AbilityTuning"/>, <see cref="Orbitals"/>,
    /// <see cref="EnergyPool"/>), so retuning any of them moves the answer. The two 2 s invulnerable
    /// phase beats are added to the clock. Core 1.25x bonus is ignored (conservative).
    /// </summary>
    public static class HelmsmanFightModel
    {
        public const int MissileCount = 5, MissilePowerLevel = 8, MissileRateLevel = 5, MissileCapacityLevel = 5;
        public const int OrbCount = 8, OrbPowerLevel = 5;
        public const float ShotsOnBoss = 0.6f;
        public const float OrbitalsInReach = 0.3f;
        public const float Step = 0.05f;

        /// <summary>Seconds from the first hit to the death of a boss with <paramref name="health"/> HP.</summary>
        public static float TimeToKill(float health)
        {
            float max = WeaponCatalog.EffectiveMaxEnergy(BlasterTuning.MaxEnergy, MissileCapacityLevel,
                WeaponCatalog.DefaultLppeCapacityPerLevel);
            var tank = new EnergyPool(max, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);
            float volleyCost = LightMissile.VolleyCostFor(MissileCount);
            float interval = LightMissile.FireIntervalAtLevel(MissileRateLevel);
            float volleyDamage = MissileCount * LightMissile.DamagePerBallAtLevel(MissilePowerLevel) * ShotsOnBoss;
            float touch = AbilityTuning.OrbitalsTouchDamage(OrbPowerLevel,
                AbilityTuning.DefaultOrbitalsBaseTouchDamage, AbilityTuning.DefaultOrbitalsTouchDamagePerLevel);
            float orbDps = OrbCount * touch / Orbitals.PerTargetHitCooldownSeconds * OrbitalsInReach;

            float hp = health, t = 0f, timer = 0f;
            bool depleted = false;
            while (hp > 0f && t < 3600f)
            {
                tank.RegenSuspended = !depleted;
                tank.Tick(Step);
                if (depleted && tank.Normalized >= BlasterTuning.RechargeFraction) depleted = false;
                else if (!depleted && !tank.CanSpend(volleyCost)) depleted = true;

                if (depleted || !tank.CanSpend(volleyCost)) timer = 0f;
                else
                {
                    timer -= Step;
                    if (timer <= 0f)
                    {
                        timer += interval;
                        if (tank.TrySpend(volleyCost)) hp -= volleyDamage;
                    }
                }
                hp -= orbDps * Step;
                t += Step;
            }
            return t + 2f * HelmsmanTuning.TransitionSeconds;
        }
    }
}
