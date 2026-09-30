using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1044: UNDERTOW's <c>FireLanceTick</c> tested every target — gate included — at its own
    /// transform centre and asked line of sight of that same transform, never picking up MV-302/MV-386's
    /// gate-aware hit test (<see cref="WaterBlaster.FireTick"/>). A gate's leaf collider is deliberately
    /// off the Cover layer (only its <see cref="AreaGate.ThresholdObject"/> is on Cover), so a Cover-
    /// masked line of sight to the leaf always found the threshold sitting across the same doorway first
    /// and read every shot as blocked — World 3's stream could never open its own gates.
    ///
    /// Loads World 3 through the real production route (<see cref="WorldMapLoader"/> -&gt;
    /// <see cref="MapRuntime.Build"/>, the same idiom <see cref="MV1013LastWorldFinaleTests"/> and
    /// <see cref="MV743World3DesignedLevelTests"/> already use) rather than a synthetic fixture, so this
    /// exercises the SHIPPED gate <c>g1</c> and its real Cover-layer threshold — not a hand-built stand-in
    /// that could quietly drift out of sync with what MapRuntime.BuildAreaGate actually assembles. Max is
    /// stood 5m from g1's centre but aimed at a point 1.2 m off it along its own width axis (g1 links
    /// a1's S wall to a2's N wall, a constant-Z doorway spanning world X) -- the OLD centre-only hit test
    /// (gate.transform.position) sees this aim as ~13.5 degrees off-axis, over both weapons' cone
    /// half-angles, while the fix's contact point resolves back onto the aim axis. Both weapons hold fire
    /// through their own real per-tick update path
    /// (<c>Undertow.Tick</c>'s explicit-<c>dt</c> overload; <c>WaterBlaster.FireTick</c> invoked directly,
    /// the same idiom <c>WaterBlasterGateDamageTests</c> already uses since <c>Update</c>'s
    /// <c>Time.deltaTime</c>-gated cadence cannot be driven from EditMode) until the gate opens, and the
    /// elapsed time is asserted against the gate's own HP/DPS, +20% (AC1) — a RESOLVED value, never an
    /// authored constant (MV-465 Tier 2).
    ///
    /// Fails on a2c4fe2 (the commit this ticket's observation cites): UNDERTOW's lance never opens g1 at
    /// all — it hangs at the timeout below with the gate still closed, since the leaf-only hit test both
    /// mis-cones an off-centre shot and reads every on-cone one as sight-blocked by the gate's own
    /// threshold.
    /// </summary>
    public sealed class MV1044UndertowOpensGateTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo AreaGateAwake = typeof(AreaGate).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake = typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick = typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterAwake = typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterFireTick = typeof(WaterBlaster).GetMethod("FireTick", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            DevMode.Reset();
            DevTuning.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            DevMode.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void UndertowOpensGateG1AimedOffCentre_AndRcdaStillDoesToo()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV1044 World3 Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsTrue(built.Actors.TryGetValue("g1", out GameObject gateGo) && gateGo != null,
                    "world3_config's gate 'g1' was not built");
                var gate = gateGo.GetComponent<AreaGate>();
                Assert.IsNotNull(gate, "g1 built no AreaGate component");
                // AddComponent never fires Awake in this project's synchronous EditMode harness
                // (confirmed empirically -- see AreaGateTests' own MV-386 note), so MapRuntime.
                // BuildAreaGate's own AddComponent<AreaGate>() call left g1 with no _health and no
                // ThresholdObject yet -- its own CoverLayer.Assign(gate.ThresholdObject) call ran
                // against that still-null ThresholdObject and was a no-op. Invoke Awake directly (the
                // real production wiring) and repeat that one Assign call, exactly the two-step
                // WaterBlasterGateDamageTests already established for this same harness limitation.
                AreaGateAwake.Invoke(gate, null);
                CoverLayer.Assign(gate.ThresholdObject);
                Assert.IsTrue(gate.IsAlive && !gate.IsOpen, "precondition: gate g1 must start closed and intact");

                // g1's doorway runs along world X (MapRuntime.GateRunsAlongX's N/S-wall case), so "1.2m
                // off its centre" is an X offset. Max stands 5m from the centre, on the same side he
                // actually plays it from (AwayFromPlayerDirection points from a1, the room before the
                // gate, toward a2 beyond it, so the approach side is the opposite way), and aims AT the
                // off-centre point rather than standing offset himself -- the OLD centre-only hit test
                // (gate.transform.position, ignoring where the collider was actually hit) sees this aim
                // as ~13.5 degrees off-axis, over both weapons' cone half-angles, and MV-1044's fix
                // (GateHitResolver.ContactPoint) resolves the actual hit back onto the aim axis.
                Vector3 gateCentre = gateGo.transform.position;
                Vector3 approach = gate.AwayFromPlayerDirection.sqrMagnitude > 1e-4f
                    ? gate.AwayFromPlayerDirection.normalized
                    : Vector3.back;
                Vector3 origin = gateCentre - approach * 5f;
                Vector3 aimPoint = gateCentre + Vector3.right * 1.2f;
                Quaternion aim = Quaternion.LookRotation((aimPoint - origin).normalized, Vector3.up);

                // --- AC1: UNDERTOW (World 3's own loadout) must open g1 within HP/DPS + 20%. ---
                WeaponSystemState.ApplyWorldLoadout(2);
                var undertowGo = new GameObject("MV1044 Undertow");
                try
                {
                    undertowGo.transform.SetPositionAndRotation(origin, aim);
                    var undertow = undertowGo.AddComponent<Undertow>();
                    UndertowAwake.Invoke(undertow, null);
                    undertow.SetFiring(true);
                    Physics.SyncTransforms();

                    // The tank never gates the lance here -- InfiniteEnergy is the default DevMode state
                    // (see DevMode.Reset), just needs Enabled to actually apply.
                    DevMode.Enabled = true;

                    float expectedSeconds = gate.MaxHp / undertow.DamagePerSecond * 1.2f;
                    float elapsed = FireUntilOpen(gate, expectedSeconds, undertow.FireInterval,
                        () => UndertowTick.Invoke(undertow, new object[] { undertow.FireInterval }));

                    Assert.IsTrue(gate.IsOpen,
                        "UNDERTOW's lance never opened gate g1 aimed 1.2m off centre -- MV-1044's own regression");
                    Assert.LessOrEqual(elapsed, expectedSeconds,
                        $"UNDERTOW took {elapsed:0.00}s to open g1 -- over its expected {expectedSeconds:0.00}s (HP/DPS +20%)");
                }
                finally
                {
                    Object.DestroyImmediate(undertowGo);
                }

                // Reset g1 back to intact for the RCDA check below, exactly like Max dying and the
                // arena resetting (AreaGate.Reclose's own documented use).
                gate.Reclose();
                Assert.IsTrue(gate.IsAlive && !gate.IsOpen, "precondition: Reclose must restore g1 before the RCDA check");

                // --- AC1's second case: WeaponSystemState.ApplyWorldLoadout(0) confirms the RCDA
                // still opens it too (this fix must not have moved WaterBlaster's own behaviour). ---
                WeaponSystemState.ApplyWorldLoadout(0);
                var blasterGo = new GameObject("MV1044 WaterBlaster");
                try
                {
                    blasterGo.transform.SetPositionAndRotation(origin, aim);
                    var blaster = blasterGo.AddComponent<WaterBlaster>();
                    WaterBlasterAwake.Invoke(blaster, null);
                    Physics.SyncTransforms();

                    float expectedSeconds = gate.MaxHp / blaster.DamagePerSecond * 1.2f;
                    float elapsed = FireUntilOpen(gate, expectedSeconds, blaster.FireInterval,
                        () => WaterBlasterFireTick.Invoke(blaster, null));

                    Assert.IsTrue(gate.IsOpen, "the RCDA must still open gate g1 aimed 1.2m off centre after this fix");
                    Assert.LessOrEqual(elapsed, expectedSeconds,
                        $"the RCDA took {elapsed:0.00}s to open g1 -- over its expected {expectedSeconds:0.00}s (HP/DPS +20%)");
                }
                finally
                {
                    Object.DestroyImmediate(blasterGo);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Invokes <paramref name="fireOneTick"/> once per <paramref name="tickSeconds"/> until
        /// <paramref name="gate"/> opens or the budget (plus a small safety margin) is exhausted, and
        /// returns the simulated elapsed time.</summary>
        private static float FireUntilOpen(AreaGate gate, float budgetSeconds, float tickSeconds, System.Action fireOneTick)
        {
            float elapsed = 0f;
            int maxTicks = Mathf.CeilToInt(budgetSeconds / tickSeconds) + 4;
            for (int i = 0; i < maxTicks && !gate.IsOpen; i++)
            {
                fireOneTick();
                elapsed += tickSeconds;
            }
            return elapsed;
        }
    }
}
