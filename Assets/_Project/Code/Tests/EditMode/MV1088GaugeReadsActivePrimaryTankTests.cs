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
    /// MV-1088 — the floating gauge over Max must read whichever primary is actually equipped, not fall
    /// through to the RCDA's own tank for anything that isn't the LPPE.
    /// <see cref="PlayerHealth.PrimaryEnergyNormalized"/> used to special-case
    /// <see cref="WeaponCatalog.PrimaryKind.Lppe"/> and read <see cref="WaterBlaster.WaterNormalized"/>
    /// for every other primary, which silently included <see cref="WeaponCatalog.PrimaryKind.Undertow"/>
    /// once World 3 shipped it: UNDERTOW's own tank (<see cref="Undertow.EnergyNormalized"/>) drained
    /// under fire exactly as intended, but the gauge kept showing the idle WaterBlaster's untouched full
    /// tank. Fixed by routing the gauge through <see cref="IPrimaryEnergy"/>, resolved from
    /// <see cref="WeaponSystemState.ActivePrimary"/>, generically for every primary.
    ///
    /// ONE EditMode test (MV-465 Rule 1) carrying every world's case as sub-checks of the same
    /// regression. Reaches each world's primary through the real
    /// <see cref="WeaponSystemState.ApplyWorldLoadout"/> loader, drives 2s of real firing through each
    /// weapon's own explicit-dt <c>Tick</c> (RCDA's/LPPE's own extracted out of <c>Update</c> by this same
    /// ticket, same shape UNDERTOW's pre-existing one already used), and reads everything back through
    /// PUBLIC properties only — <see cref="PlayerHealth.PrimaryEnergyNormalized"/> (made public by this
    /// ticket so a test can read exactly what the gauge is given) against each weapon's own public
    /// EnergyNormalized/WaterNormalized, never a reflected private field.
    ///
    /// Fails on base commit: the UNDERTOW case asserts <c>health.PrimaryEnergyNormalized()</c> tracks
    /// <c>undertow.EnergyNormalized</c> and reads below 1.0 after 2s of real drain; on base commit the
    /// gauge falls through to the untouched WaterBlaster's tank, which never moved, so it reads 1.0 and
    /// the assertion fails.
    /// </summary>
    public sealed class MV1088GaugeReadsActivePrimaryTankTests
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

        /// <summary>20 steps of 0.1s each — 2s of held fire run through the real per-tick cadence, not
        /// one big explicit-dt call: a single large <c>Tick</c> only ever spends ONE fire-cadence tick's
        /// worth of energy no matter how big <c>dt</c> is (every weapon here shares that "no catch-up
        /// loop" shape — see <see cref="Undertow"/>'s own class doc) — the same small-step-loop idiom
        /// <c>UndertowTests</c> already established for "hold fire".</summary>
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
        public void GaugeTracksTheActivePrimarysOwnTank_ForEveryWorld_MV1088()
        {
            // ================= World 0 — RCDA =================
            // try/finally throughout (not just a trailing DestroyImmediate): a leaked player GameObject
            // from an assertion that throws mid-block would otherwise survive into every later test in
            // the same batch -- exactly what happened the first time this test was written, taking down
            // three unrelated physics-scan tests (MV871/MV898/Mv935) that count scene actors by scanning
            // for IDamageable, which PlayerHealth itself implements.
            GameObject playerGoW0 = null;
            try
            {
                WeaponSystemState.ApplyWorldLoadout(0);
                playerGoW0 = new GameObject("MV1088 Player W0", typeof(CharacterController), typeof(PlayerController));
                PlayerControllerAwake.Invoke(playerGoW0.GetComponent<PlayerController>(), null);

                var blaster = playerGoW0.AddComponent<WaterBlaster>();
                WaterBlasterAwake.Invoke(blaster, null);
                var health = playerGoW0.AddComponent<PlayerHealth>();
                health.Initialize();

                DevMode.Enabled = true;
                DevMode.AutoFire = true;
                DevMode.InfiniteEnergy = false;

                for (int i = 0; i < HoldSteps; i++) WaterBlasterTick.Invoke(blaster, new object[] { StepSeconds });

                float drained = health.PrimaryEnergyNormalized();
                Assert.That(drained, Is.EqualTo(blaster.WaterNormalized).Within(0.01f),
                    "World 0: the gauge must read the RCDA's own WaterNormalized");
                Assert.That(drained, Is.LessThan(1f), "World 0: 2s of real RCDA fire must have drained the tank");

                // MV-1088 test note: WaterBlaster.Awake never resolves an aimSource (unlike PulseLaser/
                // Undertow), so with none attached IsFiring only ever moves via SetFiring/DevMode.AutoFire
                // -- DevMode.AutoFire alone does not drive it back to false, it just stops forcing it true.
                DevMode.AutoFire = false;
                blaster.SetFiring(false);
                for (int i = 0; i < RegenSteps; i++) WaterBlasterTick.Invoke(blaster, new object[] { StepSeconds });
                Assert.That(health.PrimaryEnergyNormalized(), Is.GreaterThan(drained),
                    "World 0: releasing fire past the regen delay must raise the gauge again");
            }
            finally
            {
                if (playerGoW0 != null) Object.DestroyImmediate(playerGoW0);
            }

            // ================= World 1 — LPPE =================
            GameObject playerGoW1 = null;
            try
            {
                WeaponSystemState.ApplyWorldLoadout(1);
                playerGoW1 = new GameObject("MV1088 Player W1", typeof(CharacterController), typeof(PlayerController));
                PlayerControllerAwake.Invoke(playerGoW1.GetComponent<PlayerController>(), null);

                var pulseLaser = playerGoW1.GetComponent<PulseLaser>();
                Assert.IsNotNull(pulseLaser, "World 1: the LPPE must self-attach from PlayerController.Awake");
                PulseLaserAwake.Invoke(pulseLaser, null);
                var health = playerGoW1.AddComponent<PlayerHealth>();
                health.Initialize();

                DevMode.Enabled = true;
                DevMode.AutoFire = true;
                DevMode.InfiniteEnergy = false;

                for (int i = 0; i < HoldSteps; i++) PulseLaserTick.Invoke(pulseLaser, new object[] { StepSeconds });

                float drained = health.PrimaryEnergyNormalized();
                Assert.That(drained, Is.EqualTo(pulseLaser.EnergyNormalized).Within(0.01f),
                    "World 1: the gauge must read the LPPE's own EnergyNormalized");
                Assert.That(drained, Is.LessThan(1f), "World 1: 2s of real LPPE fire must have drained the tank");

                DevMode.AutoFire = false;
                pulseLaser.SetFiring(false);
                for (int i = 0; i < RegenSteps; i++) PulseLaserTick.Invoke(pulseLaser, new object[] { StepSeconds });
                Assert.That(health.PrimaryEnergyNormalized(), Is.GreaterThan(drained),
                    "World 1: releasing fire past the regen delay must raise the gauge again");
            }
            finally
            {
                if (playerGoW1 != null) Object.DestroyImmediate(playerGoW1);
            }

            // ================= World 2 — UNDERTOW =================
            GameObject playerGoW2 = null;
            try
            {
                WeaponSystemState.ApplyWorldLoadout(2);
                playerGoW2 = new GameObject("MV1088 Player W2", typeof(CharacterController), typeof(PlayerController));
                PlayerControllerAwake.Invoke(playerGoW2.GetComponent<PlayerController>(), null);

                var undertow = playerGoW2.GetComponent<Undertow>();
                Assert.IsNotNull(undertow, "World 2: UNDERTOW must self-attach from PlayerController.Awake");
                UndertowAwake.Invoke(undertow, null);
                var health = playerGoW2.AddComponent<PlayerHealth>();
                health.Initialize();

                DevMode.Enabled = true;
                DevMode.AutoFire = true;
                DevMode.InfiniteEnergy = false;

                for (int i = 0; i < HoldSteps; i++) UndertowTick.Invoke(undertow, new object[] { StepSeconds });

                float drained = health.PrimaryEnergyNormalized();
                Assert.That(drained, Is.EqualTo(undertow.EnergyNormalized).Within(0.01f),
                    "World 2: the gauge must read UNDERTOW's own EnergyNormalized, not the idle RCDA's " +
                    "untouched tank it fell through to on the base commit");
                Assert.That(drained, Is.LessThan(1f), "World 2: 2s of real UNDERTOW fire must have drained the tank");

                DevMode.AutoFire = false;
                undertow.SetFiring(false);
                for (int i = 0; i < RegenSteps; i++) UndertowTick.Invoke(undertow, new object[] { StepSeconds });
                Assert.That(health.PrimaryEnergyNormalized(), Is.GreaterThan(drained),
                    "World 2: releasing fire past the regen delay must raise the gauge again");
            }
            finally
            {
                if (playerGoW2 != null) Object.DestroyImmediate(playerGoW2);
            }
        }
    }
}
