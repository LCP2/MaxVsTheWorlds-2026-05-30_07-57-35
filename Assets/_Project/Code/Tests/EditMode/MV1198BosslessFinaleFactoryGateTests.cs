using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Save;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1198 (the one new test, per CC_AUTONOMY's testing policy): Lee's TestFlight report, 16 Oct — a
    /// DEV start into the City (W3) opened the exit door and swapped RICOCHET for Undertow ~20s into the
    /// run, while the area's 18 garrison robots and its own factory were still mostly alive.
    /// <c>WorldFinaleGate.OnEnemyKilled</c> (pre-fix) only ever checked
    /// <c>AreaAccumulationDirector.ActiveCountForArea</c> against the clear threshold — it never checked
    /// whether the area's own robot SOURCE (its factory) had been destroyed at all, so a factory still
    /// pumping out new robots could not stop a dwindling ambient/garrison count from opening the door.
    ///
    /// Fails on the base commit (pre-MV-1198): with a live <c>MowerHutch</c> still standing and the
    /// area's robot count already at the clear threshold, the gate opens anyway — this test's first
    /// assertion after the 7th kill (`gate.IsOpen` false) fails, since the base code has no factory
    /// check at all and opens immediately.
    /// </summary>
    public sealed class MV1198BosslessFinaleFactoryGateTests
    {
        private string _dir;
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ytgame-mv1198-tests");
            if (System.IO.Directory.Exists(_dir)) System.IO.Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;

            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            ClearSpawned();
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
        }

        private void ClearSpawned()
        {
            foreach (Object o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        // OnEnable isn't reliably invoked for a plain AddComponent outside Play mode -- drive it
        // directly, same idiom CityToReefTests/MV1183SimplifiedFinaleTests use.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(c, null);

        private RobotEnemy NewDormantGarrisonRobot(string name, int areaIndex)
        {
            var go = new GameObject(name);
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            InvokeOnEnable(e); // runs ResetState(); SetAreaIndex/BeginDormant must come after.
            e.SetAreaIndex(areaIndex);
            e.BeginDormant(authoredSlot: true); // the same authored-slot garrison placement uses.
            _spawned.Add(go);
            return e;
        }

        private MowerHutch NewLiveFactory(string id)
        {
            var go = new GameObject($"hutch_{id}");
            var hutch = go.AddComponent<MowerHutch>(); // RequireComponent brings EnemySpawner.
            hutch.Build(); // registers with FactoryCensus — Awake never runs via AddComponent outside Play mode.
            hutch.SetId(id);
            _spawned.Add(go);
            return hutch;
        }

        private static void KillByCombat(RobotEnemy robot) =>
            robot.TakeDamage(new DamageInfo(9999f, robot.transform.position, Vector3.forward, Team.Player));

        private static void DestroyFactory(MowerHutch hutch) =>
            hutch.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));

        private static WorldConfig BosslessExitConfig() => new WorldConfig
        {
            dials = new WorldDials { areaCount = 1 },
            areas = new[]
            {
                new WorldArea
                {
                    id = "a1", index = 1, role = "exit",
                    origin = new WorldAreaOrigin(), size = new WorldAreaSize(),
                },
            },
        };

        private WorldFinaleGate BuildGate(out AreaAccumulationDirector areaDirector)
        {
            var root = new GameObject("MV-1198 Probe Root");
            _spawned.Add(root);

            areaDirector = root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(BosslessExitConfig(), worldIndex: 0);

            var gate = root.AddComponent<WorldFinaleGate>();
            InvokeOnEnable(gate);
            return gate;
        }

        [Test]
        public void BosslessExitWaitsOnTheFactoryEvenAtTheClearThreshold()
        {
            // ------------------------------------------------------------ Arrangement 1: count first.
            WorldFinaleGate gate = BuildGate(out AreaAccumulationDirector areaDirector);
            MowerHutch hutch = NewLiveFactory("a1_hutch");

            var robots = new RobotEnemy[10];
            for (int i = 0; i < robots.Length; i++)
                robots[i] = NewDormantGarrisonRobot($"a1_robot{i}", areaIndex: 1);

            Assert.AreEqual(10, areaDirector.ActiveCountForArea(1), "precondition: ten living, dormant a1 robots");
            WeaponCatalog.PrimaryKind primaryBefore = WeaponSystemState.ActivePrimary;

            // Kill 7, leaving 3 dormant robots and the live factory -- AT the clear threshold, but the
            // factory (a robot source) is still standing.
            for (int i = 0; i < 7; i++) KillByCombat(robots[i]);

            Assert.AreEqual(3, areaDirector.ActiveCountForArea(1), "precondition: three living a1 robots remain");
            Assert.IsFalse(gate.IsOpen,
                "the clear threshold alone must not open the door while the area's own factory is still alive");
            Assert.AreEqual(primaryBefore, WeaponSystemState.ActivePrimary,
                "ActivePrimary must be untouched while the finale hasn't resolved");

            // Destroying the factory, with no further robot death, must resolve the finale on its own.
            DestroyFactory(hutch);

            Assert.IsTrue(gate.IsOpen, "destroying the last source while already at/under the threshold must open the door");
            for (int i = 7; i < 10; i++)
                Assert.IsFalse(robots[i].IsAlive, "every remaining a1 robot must be destroyed once the finale resolves");

            ClearSpawned();
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();

            // ------------------------------------------------------------ Arrangement 2: factory first.
            WorldFinaleGate gate2 = BuildGate(out AreaAccumulationDirector areaDirector2);
            MowerHutch hutch2 = NewLiveFactory("a1_hutch_2");

            var robots2 = new RobotEnemy[10];
            for (int i = 0; i < robots2.Length; i++)
                robots2[i] = NewDormantGarrisonRobot($"b_a1_robot{i}", areaIndex: 1);

            Assert.AreEqual(10, areaDirector2.ActiveCountForArea(1), "precondition: ten living a1 robots, second arrangement");

            // Destroying the factory first, while 10 robots are still alive, must not trigger anything --
            // the count is nowhere near the threshold yet.
            DestroyFactory(hutch2);
            Assert.IsFalse(gate2.IsOpen, "destroying the factory alone, far above the clear threshold, must not open the door");

            // Killing down to the threshold, with the factory already gone, must now resolve the finale.
            for (int i = 0; i < 7; i++) KillByCombat(robots2[i]);

            Assert.IsTrue(gate2.IsOpen, "reaching the clear threshold after the last source is already gone must open the door");
        }
    }
}
