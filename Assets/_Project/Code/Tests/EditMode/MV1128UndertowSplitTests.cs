using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1128 (the one new test): World 3's old SPREAD track (<c>p_spr</c>) widened a 3-degree gate
    /// cone nothing player-facing ever noticed (Lee, device playtest: "the weapon has no spread"). It is
    /// recut as SPLIT — a plain beam count, 1/2/3 — and the floor wedge (<see cref="AimReticle"/>), which
    /// was showing World 1's stale Water Blaster numbers in World 3, now shows UNDERTOW's own.
    ///
    /// Drives the real World 3 loadout (<see cref="WeaponSystemState.ApplyWeaponCoreMorph"/>(2), the
    /// real <c>rig_board.world3.json</c>) through the real entry points: a live <see cref="Undertow"/>
    /// ticked via its own explicit-dt <c>Tick</c> (the same reflection-driven idiom every other Undertow
    /// EditMode test already uses), and a live <see cref="WaterBlaster"/> sharing the same
    /// [DisallowMultipleComponent] <see cref="AimReticle"/> instance, exactly as both weapons do on the
    /// real player GameObject.
    ///
    /// Fails on the commit before this ticket: <c>Undertow.BeamCount</c> does not exist (does not
    /// compile), <c>Undertow.ConeHalfAngle</c> still reads the Spread track, World 3's <c>p_spr</c> is
    /// still SPREAD/maxLevel 6/startLevel 0, and <c>Undertow</c> never self-attaches an
    /// <see cref="AimReticle"/> at all — the ground wedge in World 3 is whatever <see cref="WaterBlaster"/>
    /// last fit it to.
    /// </summary>
    public sealed class MV1128UndertowSplitTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick =
            typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo UndertowRefreshReticle =
            typeof(Undertow).GetMethod("RefreshReticle", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyCcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyHealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);

        private GameObject _playerGo;
        private GameObject _robotAGo;
        private GameObject _robotBGo;
        private GameObject _robotCGo;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            RigBoardLayout.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            RigBoard.ResetForTests();
            RigBoardLayout.ResetForTests();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_robotAGo != null) Object.DestroyImmediate(_robotAGo);
            if (_robotBGo != null) Object.DestroyImmediate(_robotBGo);
            if (_robotCGo != null) Object.DestroyImmediate(_robotCGo);
        }

        private static void InvokeTick(Undertow undertow, float dt) =>
            UndertowTick.Invoke(undertow, new object[] { dt });

        private static RigAbilityLayout FindAbility(System.Collections.Generic.IReadOnlyList<RigAbilityLayout> abilities, string id)
        {
            foreach (var a in abilities)
                if (a.Id == id) return a;
            return null;
        }

        private static RobotEnemy BuildRobot(Vector3 position, out GameObject go)
        {
            go = new GameObject("MV1128 Robot");
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            RobotEnemyCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            RobotEnemyHealthField.SetValue(robot, 100000f); // survives every tick in this test
            return robot;
        }

        [Test]
        public void SplitDrivesBeamCountDamageAndTheRealAimReticle_MV1128()
        {
            // --- The real World 3 loadout: World 1 -> 2 -> 3, the same morph sequence the game plays.
            WeaponSystemState.ApplyWeaponCoreMorph(1);
            WeaponSystemState.ApplyWeaponCoreMorph(2);
            DevMode.Enabled = true;
            DevMode.InfiniteEnergy = true; // isolate beam/damage timing from tank depletion

            _playerGo = new GameObject("MV1128 Player");
            var undertow = _playerGo.AddComponent<Undertow>();
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode
            undertow.SetFiring(true); // no PlayerController/aimSource -- Max never rotates during this test

            // --- AC1a: a fresh World 3 grant starts SPLIT 1 (one beam), resolved maximum 3, label SPLIT.
            Assert.AreEqual(1, RigState.Level("p_spr"), "a fresh World 3 grant must own p_spr (SPLIT) at level 1, same shape as p_dmg");
            Assert.AreEqual(1, undertow.BeamCount, "SPLIT 1 must resolve to exactly one beam");
            Assert.AreEqual(3, RigBoard.MaxLevel("p_spr"), "SPLIT's resolved maximum must be 3");

            RigBoardLayout.UseWorld(2);
            var splitLayout = FindAbility(RigBoardLayout.Abilities, "p_spr");
            Assert.IsNotNull(splitLayout, "World 3's board must still carry a p_spr node");
            Assert.AreEqual("SPLIT", splitLayout.Label, "World 3's p_spr must resolve its label as SPLIT, not SPREAD");

            // --- AC1b: levels 1, 2 and 3 give 1, 2 and 3 beams.
            Assert.IsTrue(WeaponSystemState.RaiseLevelById("p_spr"), "test precondition: p_spr must be raisable to level 2");
            Assert.AreEqual(2, undertow.BeamCount, "SPLIT 2 must resolve to exactly two beams");
            Assert.IsTrue(WeaponSystemState.RaiseLevelById("p_spr"), "test precondition: p_spr must be raisable to level 3");
            Assert.AreEqual(3, undertow.BeamCount, "SPLIT 3 must resolve to exactly three beams");

            // --- AC1c/d: three robots inside the 35-degree acquire cone, at distinct angles/distances so
            // beam 0 picks the dead-ahead one first, beam 1 the next-smallest angle, beam 2 the last.
            Vector3 origin = _playerGo.transform.position;
            Vector3 aimDir = _playerGo.transform.forward;

            RobotEnemy robotA = BuildRobot(origin + aimDir * 5f, out _robotAGo); // 0 degrees, 5m
            RobotEnemy robotB = BuildRobot(origin + (Quaternion.AngleAxis(12f, Vector3.up) * aimDir) * 5f, out _robotBGo); // 12 degrees, 5m
            RobotEnemy robotC = BuildRobot(origin + (Quaternion.AngleAxis(-12f, Vector3.up) * aimDir) * 5.5f, out _robotCGo); // -12 degrees, 5.5m
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

            float elapsed = 0f;
            const float dt = 0.05f;
            const float observeWindow = 2f;
            while (elapsed < observeWindow && !(undertow.IsBeamLatched(0) && undertow.IsBeamLatched(1) && undertow.IsBeamLatched(2)))
            {
                InvokeTick(undertow, dt);
                elapsed += dt;
            }

            Assert.IsTrue(undertow.IsBeamLatched(0) && undertow.IsBeamLatched(1) && undertow.IsBeamLatched(2),
                $"all three beams must latch onto a robot within {observeWindow}s at SPLIT 3 with three robots in the acquire cone");

            GameObject beam0Target = undertow.LatchedTransformAt(0).gameObject;
            GameObject beam1Target = undertow.LatchedTransformAt(1).gameObject;
            GameObject beam2Target = undertow.LatchedTransformAt(2).gameObject;
            Assert.AreSame(robotA.gameObject, beam0Target, "beam 0 (chooses first) must take the dead-ahead robot");
            Assert.AreNotSame(beam0Target, beam1Target, "beam 1 must not share beam 0's target");
            Assert.AreNotSame(beam0Target, beam2Target, "beam 2 must not share beam 0's target");
            Assert.AreNotSame(beam1Target, beam2Target, "beam 2 must not share beam 1's target -- three beams, three different robots");

            // --- AC1d: over 10 damage ticks, beam 0's robot loses 10x a full tick; beams 1/2's robots
            // each lose 10x SplitSecondaryDamageFraction of a tick. All three beams share one fire-tick
            // cadence, so counting beam 0's own tick count also counts beams 1/2's.
            RobotEnemy robotOnBeam0 = beam0Target.GetComponent<RobotEnemy>();
            RobotEnemy robotOnBeam1 = beam1Target.GetComponent<RobotEnemy>();
            RobotEnemy robotOnBeam2 = beam2Target.GetComponent<RobotEnemy>();

            float health0Start = robotOnBeam0.HealthCurrent;
            float health1Start = robotOnBeam1.HealthCurrent;
            float health2Start = robotOnBeam2.HealthCurrent;

            int ticksCounted = 0;
            int guard = 0;
            float lastHealth0 = health0Start;
            while (ticksCounted < 10 && guard++ < 400)
            {
                InvokeTick(undertow, dt);
                float health0Now = robotOnBeam0.HealthCurrent;
                if (health0Now < lastHealth0 - 0.001f) ticksCounted++;
                lastHealth0 = health0Now;
            }
            Assert.AreEqual(10, ticksCounted, "beam 0's robot must take exactly 10 counted damage ticks within the guard budget");

            float damage0 = health0Start - robotOnBeam0.HealthCurrent;
            float damage1 = health1Start - robotOnBeam1.HealthCurrent;
            float damage2 = health2Start - robotOnBeam2.HealthCurrent;
            float fullTick = undertow.EffectiveDamagePerTick;

            Assert.AreEqual(10f * fullTick, damage0, 1f, "beam 0's robot must lose 10x a full tick's damage");
            Assert.AreEqual(10f * fullTick * Undertow.SplitSecondaryDamageFraction, damage1, 1f,
                "beam 1's robot must lose 10x SplitSecondaryDamageFraction of a tick");
            Assert.AreEqual(10f * fullTick * Undertow.SplitSecondaryDamageFraction, damage2, 1f,
                "beam 2's robot must lose 10x SplitSecondaryDamageFraction of a tick");

            // --- AC1e: with only one robot in reach, exactly one beam is drawn -- the other two have no
            // candidate left to claim (spec #5: "a beam with no candidate is not drawn"). Move all three
            // far out of range/cone first (never destroy a GameObject a beam still holds a live latch on
            // -- the next tick's FireLatchedTick would call TakeDamage on a destroyed object) and let
            // every beam drop its own latch naturally before destroying anything.
            _robotAGo.transform.position += Vector3.right * 1000f;
            _robotBGo.transform.position += Vector3.right * 1000f;
            _robotCGo.transform.position += Vector3.right * 1000f;
            Physics.SyncTransforms();
            for (int i = 0; i < 20; i++) InvokeTick(undertow, dt); // ~1s -- lets every beam drop its now-out-of-range latch

            Assert.IsFalse(undertow.IsBeamLatched(0) || undertow.IsBeamLatched(1) || undertow.IsBeamLatched(2),
                "test precondition: every beam must have dropped its latch before the old robots are destroyed");

            Object.DestroyImmediate(_robotAGo); _robotAGo = null;
            Object.DestroyImmediate(_robotBGo); _robotBGo = null;
            Object.DestroyImmediate(_robotCGo); _robotCGo = null;

            GameObject soloRobotGo;
            BuildRobot(origin + aimDir * 5f, out soloRobotGo);
            Physics.SyncTransforms();
            _robotAGo = soloRobotGo;

            for (int i = 0; i < 20; i++) InvokeTick(undertow, dt); // ~1s -- enough to settle/latch

            Assert.IsTrue(undertow.IsBeamDrawn(0), "beam 0 must always be drawn while emitting");
            Assert.IsFalse(undertow.IsBeamDrawn(1), "with one robot in reach, beam 1 has no candidate and must not be drawn");
            Assert.IsFalse(undertow.IsBeamDrawn(2), "with one robot in reach, beam 2 has no candidate and must not be drawn");

            // --- AC1f: the AimReticle mesh in World 3 resolves to UNDERTOW's own numbers, not Water
            // Blaster's -- the shared [DisallowMultipleComponent] component both weapons self-attach to.
            var waterBlaster = _playerGo.AddComponent<WaterBlaster>();
            WaterBlasterAwake.Invoke(waterBlaster, null); // ActivePrimary is still Undertow -- must no-op on the shared reticle

            var reticle = _playerGo.GetComponent<AimReticle>();
            Assert.IsNotNull(reticle, "Undertow.Awake must self-attach a shared AimReticle");
            Assert.AreEqual(undertow.Range, reticle.ResolvedRange, 0.1f,
                "in World 3 the floor wedge must show UNDERTOW's own Range, not Water Blaster's");
            Assert.AreEqual(35f, reticle.ResolvedHalfAngleDeg, 0.5f,
                "in World 3 the floor wedge must show UNDERTOW's own acquire half-angle (35 degrees), not the narrow gate cone");

            // --- AC1g: under the World 1 loadout, the reticle and p_spr's own label/start level/maximum
            // resolve to the values they have today -- the World 3 fix must not touch World 1's weapon.
            WeaponSystemState.Reset(); // back to World 1 baseline -- ActivePrimary flips to Rcda
            waterBlaster.RefreshUpgrades(); // the real re-fit entry point WeaponSystemState.Changed drives
            UndertowRefreshReticle.Invoke(undertow, null); // ActivePrimary is no longer Undertow -- must no-op

            Assert.AreEqual(waterBlaster.Range, reticle.ResolvedRange, 0.01f,
                "under World 1, the reticle must show Water Blaster's own Range again");
            Assert.AreEqual(waterBlaster.ConeHalfAngle, reticle.ResolvedHalfAngleDeg, 0.01f,
                "under World 1, the reticle must show Water Blaster's own ConeHalfAngle again");

            RigBoardLayout.UseWorld(0);
            var world1SpreadLayout = FindAbility(RigBoardLayout.Abilities, "p_spr");
            Assert.IsNotNull(world1SpreadLayout, "World 1's board must still carry its own p_spr node");
            Assert.AreEqual("SPREAD", world1SpreadLayout.Label, "World 1's p_spr must keep its SPREAD label");
            Assert.AreEqual(0, RigBoard.StartLevel("p_spr"), "World 1's p_spr must keep its own start level (0)");
            Assert.AreEqual(4, RigBoard.MaxLevel("p_spr"), "World 1's p_spr must keep its own maximum (4)");
        }
    }
}
