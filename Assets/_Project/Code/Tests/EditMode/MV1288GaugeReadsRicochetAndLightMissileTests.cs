using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1288 — <see cref="PlayerHealth.ActivePrimaryEnergySource"/> only handled
    /// <see cref="WeaponCatalog.PrimaryKind.Lppe"/> and <see cref="WeaponCatalog.PrimaryKind.Undertow"/>
    /// explicitly (MV-1088's own fix); <see cref="WeaponCatalog.PrimaryKind.Ricochet"/> and
    /// <see cref="WeaponCatalog.PrimaryKind.LightMissile"/> fell through to the default case and read
    /// the idle <see cref="WaterBlaster"/>'s own untouched tank instead of <see cref="Ricochet"/>'s or
    /// <see cref="LightMissile"/>'s — the same shape of bug MV-1088 fixed for UNDERTOW, repeated for the
    /// two primaries added since.
    ///
    /// ONE EditMode test (MV-465 Rule 1) carrying every <see cref="WeaponCatalog.PrimaryKind"/> as
    /// sub-checks of the same regression, same shape as MV-1088's own
    /// <c>MV1088GaugeReadsActivePrimaryTankTests</c>. Every primary self-attaches unconditionally from
    /// <see cref="PlayerController.Awake"/> (see each weapon's own class doc), so one player GameObject
    /// carries all of them at once and the test simply switches <see cref="WeaponSystemState.ActivePrimary"/>
    /// between sub-checks via <see cref="WeaponSystemState.ApplyLoadout"/> rather than rebuilding the
    /// actor per world. Drives 2s of real firing through each weapon's own explicit-dt <c>Tick</c>, then
    /// reads everything back through PUBLIC properties only — <see cref="PlayerHealth.PrimaryEnergyNormalized"/>
    /// against each weapon's own public <c>EnergyNormalized</c>/<c>WaterNormalized</c>, never a reflected
    /// private field.
    ///
    /// Fails on base commit: the RICOCHET and LIGHT MISSILE sub-checks assert
    /// <c>health.PrimaryEnergyNormalized()</c> tracks each weapon's own <c>EnergyNormalized</c> and reads
    /// below 1.0 after 2s of real drain; on base commit the gauge falls through to the untouched
    /// WaterBlaster's tank, which never moved, so it reads 1.0 and the assertion fails.
    /// </summary>
    public sealed class MV1288GaugeReadsRicochetAndLightMissileTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterTick =
            typeof(WaterBlaster).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo PulseLaserTick =
            typeof(PulseLaser).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick =
            typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo RicochetAwake =
            typeof(Ricochet).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo RicochetTick =
            typeof(Ricochet).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo LightMissileAwake =
            typeof(LightMissile).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo LightMissileTick =
            typeof(LightMissile).GetMethod("Tick", NonPublicInstance);

        /// <summary>20 steps of 0.1s each — 2s of held fire, same small-step-loop idiom MV-1088's own
        /// test established (a single large <c>Tick</c> only ever spends one fire-cadence tick's worth
        /// of energy no matter how big <c>dt</c> is).</summary>
        private const int HoldSteps = 20;
        private const float StepSeconds = 0.1f;

        /// <summary>10 further 0.1s steps with firing stopped — 1.0s, comfortably past
        /// <see cref="BlasterTuning.RegenDelay"/> (0.35s), so regen has visibly moved the tank before the
        /// rise is asserted.</summary>
        private const int RegenSteps = 10;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            DevMode.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            DevMode.Reset();
        }

        [Test]
        public void GaugeTracksTheActivePrimarysOwnTank_ForEveryPrimaryKind_MV1288()
        {
            GameObject playerGo = null;
            try
            {
                playerGo = new GameObject("MV1288 Player", typeof(CharacterController), typeof(PlayerController));
                PlayerControllerAwake.Invoke(playerGo.GetComponent<PlayerController>(), null);

                var blaster = playerGo.AddComponent<WaterBlaster>();
                WaterBlasterAwake.Invoke(blaster, null);

                var pulseLaser = playerGo.GetComponent<PulseLaser>();
                var undertow = playerGo.GetComponent<Undertow>();
                var ricochet = playerGo.GetComponent<Ricochet>();
                var lightMissile = playerGo.GetComponent<LightMissile>();
                Assert.IsNotNull(pulseLaser, "LPPE must self-attach from PlayerController.Awake");
                Assert.IsNotNull(undertow, "UNDERTOW must self-attach from PlayerController.Awake");
                Assert.IsNotNull(ricochet, "RICOCHET must self-attach from PlayerController.Awake");
                Assert.IsNotNull(lightMissile, "LIGHT MISSILE must self-attach from PlayerController.Awake");

                // MV-1088-style explicit re-Awake (not just the self-attach's own AddComponent call):
                // guarantees each tank is freshly built from the loadout state above before firing.
                PulseLaserAwake.Invoke(pulseLaser, null);
                UndertowAwake.Invoke(undertow, null);
                RicochetAwake.Invoke(ricochet, null);
                LightMissileAwake.Invoke(lightMissile, null);

                var health = playerGo.AddComponent<PlayerHealth>();
                health.Initialize();

                DevMode.Enabled = true;
                DevMode.InfiniteEnergy = false;

                AssertGaugeTracksPrimary("RCDA", health, WeaponCatalog.PrimaryKind.Rcda,
                    blaster, WaterBlasterTick, blaster.SetFiring, () => blaster.WaterNormalized);
                AssertGaugeTracksPrimary("LPPE", health, WeaponCatalog.PrimaryKind.Lppe,
                    pulseLaser, PulseLaserTick, pulseLaser.SetFiring, () => pulseLaser.EnergyNormalized);
                AssertGaugeTracksPrimary("UNDERTOW", health, WeaponCatalog.PrimaryKind.Undertow,
                    undertow, UndertowTick, undertow.SetFiring, () => undertow.EnergyNormalized);
                AssertGaugeTracksPrimary("RICOCHET", health, WeaponCatalog.PrimaryKind.Ricochet,
                    ricochet, RicochetTick, ricochet.SetFiring, () => ricochet.EnergyNormalized);
                AssertGaugeTracksPrimary("LIGHT MISSILE", health, WeaponCatalog.PrimaryKind.LightMissile,
                    lightMissile, LightMissileTick, lightMissile.SetFiring, () => lightMissile.EnergyNormalized);
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
            }
        }

        /// <summary>Makes <paramref name="primary"/> active, holds fire on <paramref name="target"/> for
        /// <see cref="HoldSteps"/>, asserts the gauge reads that weapon's own tank and that it actually
        /// drained, then releases fire and asserts the gauge rises again past
        /// <see cref="BlasterTuning.RegenDelay"/>.</summary>
        private static void AssertGaugeTracksPrimary(
            string label, PlayerHealth health, WeaponCatalog.PrimaryKind primary,
            object target, MethodInfo tick, System.Action<bool> setFiring, System.Func<float> ownEnergyNormalized)
        {
            WeaponSystemState.ApplyLoadout(0, primary, SecondaryKind.WaterBalloon);

            DevMode.AutoFire = true;
            for (int i = 0; i < HoldSteps; i++) tick.Invoke(target, new object[] { StepSeconds });

            float drained = health.PrimaryEnergyNormalized();
            Assert.That(drained, Is.EqualTo(ownEnergyNormalized()).Within(0.01f),
                $"{label}: the gauge must read {label}'s own EnergyNormalized/WaterNormalized");
            Assert.That(drained, Is.LessThan(1f), $"{label}: 2s of real fire must have drained the tank");

            DevMode.AutoFire = false;
            setFiring(false);
            for (int i = 0; i < RegenSteps; i++) tick.Invoke(target, new object[] { StepSeconds });
            Assert.That(health.PrimaryEnergyNormalized(), Is.GreaterThan(drained),
                $"{label}: releasing fire past the regen delay must raise the gauge again");
        }
    }
}
