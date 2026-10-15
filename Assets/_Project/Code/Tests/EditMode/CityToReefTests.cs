using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1179 (the one new test, per CC_AUTONOMY's testing policy): the City's Market Street (a1) is a
    /// bossless finale (<c>WorldArea.IsExitRole</c>, no boss) — MV-1183's shared
    /// <c>WorldFinaleGate.DestroyRestAndOpenDoor</c> step exists precisely so this area can call it from
    /// its own trigger instead of duplicating it, but nothing wired that trigger up until this ticket.
    ///
    /// Part one: once a1's own living robot count drops to 3 or fewer (Lee, 14 Oct, option A), the rest
    /// are destroyed and the exit opens in the same call — no Core, no weapon morph, no clean-up
    /// count/ring/arrow (MV-1183 already deleted the classes that built those). A resume afterward must
    /// restore the open door, which needed its own fix: <c>ResumeFromCheckpoint</c>'s old guard
    /// (<c>BossCensus.IsAreaDefeated</c>) can never read true for an area with no boss to register.
    ///
    /// Part two: the Core-less run-start bug this ticket also fixes — arriving in the Reef from the
    /// City never applied a Weapon Core morph (the City grants none), so
    /// <c>RigBoard.ActiveWorldIndex</c> stayed pinned on the City forever and every world-indexed lookup
    /// (<c>AbilityTuning.ShoulderRackBaseDamageForWorld</c>, the checkpoint's own board index) kept
    /// reading the City's row instead of the Reef's.
    ///
    /// Fails on the current base (pre-MV-1179): <c>WorldFinaleGate</c> has no <c>EnemyKilled</c>
    /// subscription at all, so dropping a1 to 3 living robots never opens the door — part one's
    /// <c>WeaponMomentResolved</c>/<c>IsOpen</c> assertions fail. <c>BackyardPath.ApplyPendingMorphAtRunStart</c>
    /// only ever calls <c>WeaponSystemState.OpenWeaponCoreMorphIfPending</c>, a no-op with nothing
    /// banked, so <c>RigBoard.ActiveWorldIndex</c> never advances off the City — part two's own
    /// <c>ActiveWorldIndex</c>/<c>ShoulderRackBaseDamageForWorld</c> assertions fail too.
    /// </summary>
    public sealed class CityToReefTests
    {
        private string _dir;
        private GameObject _root;
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ytgame-mv1179-tests");
            if (System.IO.Directory.Exists(_dir)) System.IO.Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-1179 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
            Object.DestroyImmediate(_root);

            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
        }

        // OnEnable isn't reliably invoked for a plain AddComponent outside Play mode -- drive it
        // directly, same idiom MV1183SimplifiedFinaleTests/Mv915WorldOneFinaleSequenceTests use.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(c, null);

        private RobotEnemy NewAliveRobot(string name, int areaIndex)
        {
            var go = new GameObject(name);
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            InvokeOnEnable(e); // runs ResetState(); SetAreaIndex must come after, or it gets clobbered
            e.SetAreaIndex(areaIndex);
            _spawned.Add(go);
            return e;
        }

        private static void KillByCombat(RobotEnemy robot) =>
            robot.TakeDamage(new DamageInfo(9999f, robot.transform.position, Vector3.forward, Team.Player));

        private static Pickup[] LivePickups() =>
            Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        private static WorldConfig CityConfig() => new WorldConfig
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

        [Test]
        public void ClearingMarketStreetOpensTheWayAndTheReefPlaysByItsOwnRules()
        {
            // ---------------------------------------------------------------- Part one: the bossless clear.
            WorldConfig cfg = CityConfig();

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);

            var gate = new GameObject("WorldFinaleGate Test").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(gate);
            _spawned.Add(gate.gameObject);

            RobotEnemy r1 = NewAliveRobot("a1_robot1", 1);
            RobotEnemy r2 = NewAliveRobot("a1_robot2", 1);
            RobotEnemy r3 = NewAliveRobot("a1_robot3", 1);
            RobotEnemy r4 = NewAliveRobot("a1_robot4", 1);

            WeaponCatalog.PrimaryKind primaryBefore = WeaponSystemState.ActivePrimary;

            Assert.AreEqual(4, areaDirector.ActiveCountForArea(1), "precondition: four living a1 robots");
            Assert.IsFalse(gate.WeaponMomentResolved,
                "4 living robots is still above the clear threshold -- nothing should trigger yet");

            // Kill the 4th through the ordinary combat path (TakeDamage -> Die -> HudSignals.EnemyKilled),
            // dropping a1 to 3 living robots -- AT the clear threshold. No further frames advance.
            KillByCombat(r4);

            Assert.IsTrue(gate.WeaponMomentResolved,
                "dropping to 3 living robots must resolve the finale in the SAME call the 4th robot died in");
            Assert.IsFalse(PendingMorphingModule.WeaponCorePending,
                "the City's bossless finale must never bank a Weapon Core morph");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "no Weapon Core pickup must ever exist for a bossless finale");
            Assert.AreEqual(primaryBefore, WeaponSystemState.ActivePrimary,
                "no weapon change -- the City grants no Core or morph");

            Assert.AreEqual(0, areaDirector.ActiveCountForArea(1),
                "every remaining a1 robot must be destroyed in the same call");
            Assert.IsFalse(r1.IsAlive);
            Assert.IsFalse(r2.IsAlive);
            Assert.IsFalse(r3.IsAlive);
            Assert.IsTrue(gate.IsOpen, "the exit door must report open");
            // MV-1183 deleted the clean-up count/ring/arrow classes (EdgeArrow, GroundChevron) outright
            // -- there is nothing left to build, so nothing left to assert a count of zero against.

            // A resume afterward must restore the open door -- the City's a1 has no boss, so the old
            // BossCensus.IsAreaDefeated guard this method used to gate on can never read true for it.
            gate.ResumeFromCheckpoint(weaponGranted: false, exitOpen: true);
            Assert.IsTrue(gate.IsOpen, "MV-1179: a bossless finale's open door must never re-lock after a RESUME");

            // ---------------------------------------------------------------- Part two: the Reef re-anchor.
            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);

            RigBoard.UseWorld(cityIndex);
            RigState.RestoreSnapshot(new Dictionary<string, int> { ["p_dmg"] = 4 }, new[] { "PRIMARY" });

            BackyardPath.ApplyPendingMorphAtRunStart(reefIndex);

            Assert.AreEqual(reefIndex, RigBoard.ActiveWorldIndex,
                "arriving in the Reef from the City must re-anchor RigBoard.ActiveWorldIndex, even with no Core to morph");
            Assert.AreEqual(4, RigState.Level("p_dmg"), "levels must be untouched -- this is a re-anchor, not a morph");
            Assert.AreEqual(AbilityTuning.DefaultShoulderRackBaseDamageWorld3,
                AbilityTuning.ShoulderRackBaseDamageForWorld(RigBoard.ActiveWorldIndex),
                "Reef rockets must read the Reef's own (lower) base damage, not the City/World 2 rate");
        }
    }
}
