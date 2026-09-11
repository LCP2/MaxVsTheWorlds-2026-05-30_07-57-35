using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-770 — the LPPE bolt and Shoulder Rack rocket read as 4-pixel lines because they measured
    /// 0.08m/0.16m at a ~48px/m play camera, both used a LIT surface material in a world made
    /// deliberately dark, and neither weapon ever called the hitstop/shake systems <c>Runtime/Feel/</c>
    /// already has. This is the one new EditMode test the testing policy allows this ticket; every
    /// bullet the AC lists lands in one method, all assertions on RESOLVED values (MV-465 Tier 2).
    ///
    /// Fails on 8ca754a (main HEAD before this fix):
    /// - Bolt: the "Bolt" child's resolved renderer bounds are 0.08m across and 0.35m long (3.8px/
    ///   16.8px at the play camera), and its material is <c>MaterialLibrary.Tinted(SurfaceKind.Metal,
    ///   ...)</c> — the same lit surface shader every stylised prop in the yard uses.
    /// - Rocket: the resolved combined renderer bounds are ~0.32m long (a 0.16m capsule).
    /// - Shock flash: <c>CombatVfxTuning.LppeShockImpact()</c> returns <c>FlashSize: 0.85f,
    ///   FlashLifetime: 0.16f</c>.
    /// - Shock feel: <c>HudSignals</c> has no <c>ShockPulseLanded</c> member at all — this test does not
    ///   compile against that commit.
    /// - Salvo stagger: <c>ShoulderRack.FireSalvo</c> is a single same-frame for-loop — every rocket in
    ///   a salvo lands in <c>PlayerRocket.Active</c> on the SAME <c>Tick</c> call, so the span between
    ///   the first and last launch times is exactly 0.
    /// </summary>
    public sealed class MV770WeaponPresenceTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RegisterHitMethod =
            typeof(PulseLaser).GetMethod("RegisterHit", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RigBoard.ResetForTests();
        }

        [Test]
        public void WeaponsHaveWeight_BoltAndRocketAreSizedAndLit_AndShockAndSalvoDriveTheFeelSystems()
        {
            AssertBoltBounds();
            AssertRocketBounds();
            AssertShockFlash();
            AssertShockFeel();
            AssertSalvoStagger();
        }

        // ------------------------------------------------------------ bolt

        private static void AssertBoltBounds()
        {
            SeekerPulse pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f,
                turnRateDegPerSec: 360f, lifetime: 0.01f, damage: 9f, lockRange: 14f, lockHalfAngleDeg: 35f);
            try
            {
                Transform bolt = pulse.transform.Find("Bolt");
                Assert.IsNotNull(bolt, "test precondition: SeekerPulse must build a child named 'Bolt'");

                MeshRenderer renderer = bolt.GetComponent<MeshRenderer>();
                Bounds bounds = renderer.bounds;

                Assert.That(bounds.size.x, Is.GreaterThanOrEqualTo(0.24f),
                    $"the LPPE bolt's resolved cross-section must be at least 0.24m across (was " +
                    $"{bounds.size.x:0.000}m) — the old 0.08m sliver read as 3.8px at the play camera");
                Assert.That(bounds.size.z, Is.GreaterThanOrEqualTo(0.85f),
                    $"the LPPE bolt's resolved length must be at least 0.85m (was {bounds.size.z:0.000}m)");

                Assert.AreNotEqual(MaterialLibrary.SurfaceShader, renderer.sharedMaterial.shader,
                    "the bolt must not use the lit surface shader — a weapon bolt in a world made " +
                    "deliberately dark must be its own light source (MV-770)");
            }
            finally
            {
                // Forces _age >= lifetime -> Retire(), which destroys the bolt/trail AND the ground
                // glow (a separate top-level object the test has no handle on) in one step.
                pulse.Tick(1f);
            }
        }

        // ------------------------------------------------------------ rocket

        private static void AssertRocketBounds()
        {
            PlayerRocket.DestroyAllActive();
            PlayerRocket rocket = PlayerRocket.Fire(Vector3.zero, target: null, speed: 14f, damage: 30f,
                splashRadius: 2f, cluster: false);
            try
            {
                Renderer[] renderers = rocket.GetComponentsInChildren<Renderer>();
                Assert.That(renderers.Length, Is.GreaterThan(0),
                    "test precondition: the rocket must have at least one renderer");

                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);

                Assert.That(combined.size.z, Is.GreaterThanOrEqualTo(0.45f),
                    $"the rocket's resolved body length must be at least 0.45m (was " +
                    $"{combined.size.z:0.000}m) — the old 0.16m capsule read as 7.7px at the play camera");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
            }
        }

        // ------------------------------------------------------------ shock flash

        private static void AssertShockFlash()
        {
            CombatVfxTuning.LppeImpactTuning shock = CombatVfxTuning.LppeShockImpact();
            Assert.That(shock.FlashSize, Is.GreaterThanOrEqualTo(2.0f),
                $"the SHOCK flash must resolve to at least 2.0m (was {shock.FlashSize}m)");
            Assert.That(shock.FlashLifetime, Is.GreaterThanOrEqualTo(0.28f),
                $"the SHOCK flash must resolve to at least 0.28s (was {shock.FlashLifetime}s)");
        }

        // ------------------------------------------------------------ shock feel

        private static void AssertShockFeel()
        {
            var laserGo = new GameObject("PulseLaser_ShockFeelTest");
            PulseLaser laser = laserGo.AddComponent<PulseLaser>();
            RobotEnemy target = NewPhysicsEnemy(EnemyArchetype.Rusher, Vector3.zero);

            int requests = 0;
            void OnShock(Vector3 pos) => requests++;
            HudSignals.ShockPulseLanded += OnShock;

            try
            {
                RegisterHitMethod.Invoke(laser, new object[] { target, 9f }); // hit 1
                RegisterHitMethod.Invoke(laser, new object[] { target, 9f }); // hit 2
                RegisterHitMethod.Invoke(laser, new object[] { target, 9f }); // hit 3
                Assert.AreEqual(0, requests,
                    "a normal pulse hit must request neither hitstop nor shake (MV-770)");

                RegisterHitMethod.Invoke(laser, new object[] { target, 9f }); // hit 4 -- SHOCK
                Assert.AreEqual(1, requests,
                    "the pulse that lands SHOCK (every 4th hit) must request hitstop+shake exactly once");
            }
            finally
            {
                HudSignals.ShockPulseLanded -= OnShock;
                // The SHOCK hit's own RobotEnemy.Stun spawns a top-level ShockZigzagVfx that
                // self-destroys from Update() — which EditMode never pumps — so it has to be cleaned
                // up by hand here.
                foreach (var zigzag in Object.FindObjectsByType<ShockZigzagVfx>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(zigzag.gameObject);
                Object.DestroyImmediate(target.gameObject);
                Object.DestroyImmediate(laserGo);
            }
        }

        // ------------------------------------------------------------ salvo stagger

        private static void AssertSalvoStagger()
        {
            RigBoard.UseWorld(1); // rig_board.world2.json — s_rkt/s_sal live only here
            WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 3 } },
                new[] { "SECONDARY" });
            PickupWallet.SetPowerCellSecondary(1);

            var maxGo = new GameObject("Max_SalvoStaggerTest");
            ShoulderRack rack = maxGo.AddComponent<ShoulderRack>();
            RobotEnemy targetEnemy = NewPhysicsEnemy(EnemyArchetype.Rusher, new Vector3(6f, 0f, 0f));

            try
            {
                Physics.SyncTransforms();

                const float dt = 1f / 240f;
                const float cap = 2.5f; // base 1.8s reload window + the 0.24s stagger window + margin
                var launchTimes = new List<float>();
                float elapsed = 0f;
                int lastCount = 0;

                while (elapsed < cap && launchTimes.Count < 3)
                {
                    rack.Tick(dt);
                    elapsed += dt;
                    int count = PlayerRocket.Active.Count;
                    for (int i = lastCount; i < count; i++) launchTimes.Add(elapsed);
                    lastCount = count;
                }

                Assert.That(launchTimes.Count, Is.EqualTo(3),
                    "test precondition: a full (s_sal=3) salvo must fire exactly 3 rockets");

                float span = launchTimes[launchTimes.Count - 1] - launchTimes[0];
                Assert.That(span, Is.GreaterThanOrEqualTo(0.2f),
                    $"a full Rack salvo's rockets must have distinct launch times spanning at least " +
                    $"0.2s (span was {span:0.000}s) — firing on the same Tick reads as a spawn, not a " +
                    "weapon system (MV-770)");
            }
            finally
            {
                Object.DestroyImmediate(targetEnemy.gameObject);
                Object.DestroyImmediate(maxGo);
                PlayerRocket.DestroyAllActive();
            }
        }

        // ------------------------------------------------------------ helpers

        /// <summary>A collider-backed robot with no <see cref="RobotEnemy.Active"/> registration — same
        /// shape <c>MV768WeaponNodesAreWiredTests.NewPhysicsEnemy</c> uses.</summary>
        private static RobotEnemy NewPhysicsEnemy(in EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"Enemy {archetype.Kind}");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(archetype);
            return e;
        }
    }
}
