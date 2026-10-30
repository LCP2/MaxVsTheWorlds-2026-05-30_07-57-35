using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1195 (the one new test, Rule 1): fires one RICOCHET disc at 45 degrees into a fixture wall
    /// corner with the City board's own start levels (p_ric=1, p_dmg=1, p_aim=0 — MV-1325 lowered
    /// p_ric's own start from 2) and proves, on RESOLVED values (MV-465 Tier 2): the disc bounces
    /// exactly once (off the corner wall) then ends on contact with a robot placed on its second leg;
    /// that robot's own measured health loss from the bank-bonused hit (8 base damage x 1.2, one bounce
    /// already taken); and that the City's own Weapon Core morph actually resolves
    /// <see cref="WeaponCatalog.PrimaryKind.Ricochet"/> as the active primary, not a leftover UNDERTOW.
    ///
    /// Fails on base commit 84c2e27 (main HEAD before this ticket) — it cannot compile: RICOCHET
    /// (<see cref="Ricochet"/>, <see cref="RicochetDisc"/>, <see cref="WeaponCatalog.PrimaryKind.Ricochet"/>)
    /// does not exist there at all, and the City's own row still grants UNDERTOW.
    /// </summary>
    public sealed class MV1195RicochetBounceDamageMorphTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RicochetAwake = typeof(Ricochet).GetMethod("Awake", NonPublicInstance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo HealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private GameObject _weaponGo;
        private GameObject _wallA;
        private GameObject _boundaryN, _boundaryS, _boundaryE, _boundaryW;
        private GameObject _robotGo;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RobotEnemy.ResetRegistry();
            RigBoard.ResetForTests();
            RicochetDisc.DestroyPoolForTests();
            if (_weaponGo != null) Object.DestroyImmediate(_weaponGo);
            if (_wallA != null) Object.DestroyImmediate(_wallA);
            if (_boundaryN != null) Object.DestroyImmediate(_boundaryN);
            if (_boundaryS != null) Object.DestroyImmediate(_boundaryS);
            if (_boundaryE != null) Object.DestroyImmediate(_boundaryE);
            if (_boundaryW != null) Object.DestroyImmediate(_boundaryW);
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
        }

        [Test]
        public void DiscBouncesTwiceDamagesARobotOnItsSecondLegAndTheCityMorphGrantsRicochet()
        {
            // --- the real entry point: the City's own Weapon Core morph, World1 baseline first.
            WeaponSystemState.ApplyWeaponCoreMorph(WorldCatalog.IndexOf(WorldIds.City));

            Assert.AreEqual(WeaponCatalog.PrimaryKind.Ricochet, WeaponSystemState.ActivePrimary,
                "the City's own Weapon Core morph must resolve RICOCHET as the active primary");

            // --- test precondition: the City board's own start levels, exactly AC1's "p_ric=1, p_dmg=1,
            // p_aim=0" -- all three are the board's own startLevel, untouched by the morph above.
            Assert.AreEqual(1, RigState.Level("p_ric"), "test precondition: p_ric must start at 1");
            Assert.AreEqual(1, RigState.Level("p_dmg"), "test precondition: p_dmg must start at 1");
            Assert.AreEqual(0, RigState.Level("p_aim"), "test precondition: p_aim must start unowned at 0");

            _weaponGo = new GameObject("MV1195 Ricochet");
            Ricochet weapon = _weaponGo.AddComponent<Ricochet>();
            RicochetAwake.Invoke(weapon, null); // Awake doesn't run for AddComponent outside Play mode

            Assert.AreEqual(1, weapon.BouncesPerDisc, "test precondition: BouncesPerDisc must read p_ric directly");
            Assert.AreEqual(8f, weapon.EffectiveDamagePerDisc, 1e-4f, "test precondition: POWER L1 must be the authored base, 8");
            Assert.AreEqual(0f, weapon.AimTurnRateDegPerSec, 1e-4f, "test precondition: AIMING at level 0 must be dead straight");

            // --- the fixture: a corner wall the 45-degree shot hits first (spending its one bounce,
            // MV-1325), then a robot on the reflected second leg that ends the disc on contact. The
            // big enclosing room is a leftover safety net from before MV-1325 (when a third contact was
            // needed) -- harmless to keep, since this disc never reaches it.
            _wallA = BuildWall(new Vector3(3f, 1f, 0f), new Vector3(0.02f, 2f, 34f));
            _boundaryN = BuildWall(new Vector3(0f, 1f, 17f), new Vector3(40f, 2f, 0.6f));
            _boundaryS = BuildWall(new Vector3(0f, 1f, -17f), new Vector3(40f, 2f, 0.6f));
            _boundaryE = BuildWall(new Vector3(17f, 1f, 0f), new Vector3(0.6f, 2f, 40f));
            _boundaryW = BuildWall(new Vector3(-17f, 1f, 0f), new Vector3(0.6f, 2f, 40f));

            RobotEnemy robot = NewRegisteredEnemy(new Vector3(0f, 0f, 6f));
            _robotGo = robot.gameObject;
            HealthField.SetValue(robot, 1000f); // generous headroom -- the damage must never clamp at 0
            float healthBefore = robot.HealthCurrent;

            // Every collider above was built at CreatePrimitive's own default (0,0,0) before being
            // moved -- without this, the physics engine's own bounds for them can still read stale at
            // the origin for the disc's very first SphereCast, registering spurious contacts right at
            // its own spawn point instead of the real fixture geometry several metres out.
            Physics.SyncTransforms();

            Vector3 direction45 = new Vector3(1f, 0f, 1f).normalized;
            // VfxMaterials.ParticleShader logs an informational line the FIRST time any test in the
            // whole run resolves it (a one-time, process-wide cache fill) -- harmless, but Unity's test
            // framework fails a test on any log it didn't expect. Same ignoreFailingMessages idiom
            // MV768WeaponNodesAreWiredTests already uses for its own expected EditMode-only noise.
            LogAssert.ignoreFailingMessages = true;
            RicochetDisc disc;
            try
            {
                disc = RicochetDisc.Spawn(Vector3.zero, direction45, Ricochet.DefaultSpeed,
                    weapon.BouncesPerDisc, powerLevel: 1, aimLevel: 0,
                    weapon.EffectiveDamagePerDisc, weapon.BankBonusPerBounce,
                    weapon.AimTurnRateDegPerSec, weapon.AimLookaheadMetres, Ricochet.DefaultRadius,
                    Ricochet.DefaultLifetimeSeconds);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsNotNull(disc, "test precondition: the pool must have room for one disc");

            const float step = 1f / 60f;
            const float budgetSeconds = 3f;
            int maxSteps = Mathf.CeilToInt(budgetSeconds / step);
            for (int i = 0; i < maxSteps && disc.IsActiveDisc; i++) disc.Tick(step);

            Assert.IsFalse(disc.IsActiveDisc,
                "the disc must have retired (bounces exhausted on contact with the robot) well within " +
                "its own 2.5s lifetime");
            Assert.AreEqual(1, disc.BouncesTaken,
                "it bounces exactly once -- off the corner wall -- then ends on contact with the robot " +
                "on its second leg (MV-1325: BouncesPerDisc is now 1)");

            float damage = healthBefore - robot.HealthCurrent;
            Assert.AreEqual(9.6f, damage, 0.01f,
                "the robot on the disc's second leg must take 8 (POWER L1, MV-1325) x 1.2 (one bounce " +
                "already taken) = 9.6 damage");
        }

        private static GameObject BuildWall(Vector3 center, Vector3 size)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "MV1195 Wall";
            wall.transform.position = center;
            wall.transform.localScale = size;
            CoverLayer.Assign(wall);
            return wall;
        }

        /// <summary>Same idiom every other weapon test here uses: a collider-backed robot, seeded into
        /// <see cref="RobotEnemy.Active"/> (AddComponent doesn't reliably run <c>OnEnable</c> outside
        /// Play mode).</summary>
        private static RobotEnemy NewRegisteredEnemy(Vector3 position)
        {
            var go = new GameObject("MV1195 Robot");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(e, null);
            return e;
        }
    }
}
