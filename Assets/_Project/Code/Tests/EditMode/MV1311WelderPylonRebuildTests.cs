using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1311 — the Welder's repair target is now generalized (<see cref="IRepairable"/> +
    /// <see cref="RepairableRegistry"/>) to also cover a DESTROYED City <see cref="Pylon"/>: it walks to
    /// within 2 m and beams it for 6 s, then the pylon stands again at full health and its relay's live
    /// feeder count is back to 1. Fails to compile on the base commit — no <c>IRepairable</c>, no
    /// <c>RepairableRegistry</c>, no <c>Pylon.AreaIndex</c>/<c>Pylon.Heal</c> exist there at all — the same
    /// "brand new member, fails to compile" proof <c>MV1244WelderRepairTests</c> itself used for the brand
    /// new <see cref="EnemyKind.Welder"/>. Tier 2 (resolved values): asserts <see cref="Pylon.IsAlive"/>,
    /// <see cref="Pylon.Normalized"/> and <see cref="Pylon.LiveFeedersOf"/> — resolved engine state, never
    /// an authored constant (never the authored 120 HP ceiling itself).
    ///
    /// ONE new EditMode test (MV-465 Rule 1) — one continuous scenario (destroy, beam, assert rebuilt),
    /// not split into separate tickets/tests.
    /// </summary>
    // Guards MV-1311
    public sealed class MV1311WelderPylonRebuildTests
    {
        private const string RelayId = "mv1311_relay";

        private static readonly Vector3 PylonPos = new Vector3(-91234f, 0f, 68421f);
        private static readonly Vector3 WelderPos = PylonPos + new Vector3(1.5f, 0f, 0f); // 1.5 m, within the 2 m arrival range

        private static readonly MethodInfo CreateInstanceMethod = typeof(EnemySpawner).GetMethod(
            "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _pylonGo;
        private RobotEnemy _welder;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            RepairableRegistry.ResetRegistry();
            Pylon.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_welder != null) Object.DestroyImmediate(_welder.gameObject);
            if (_pylonGo != null) Object.DestroyImmediate(_pylonGo);
            RobotEnemy.ResetRegistry();
            RepairableRegistry.ResetRegistry();
            Pylon.ResetRegistry();
        }

        [Test]
        public void TickWelderRepair_RebuildsADestroyedPylonWithinRange_RestoringItsLiveFeederCount()
        {
            _pylonGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _pylonGo.name = "MV1311 Pylon";
            _pylonGo.transform.position = PylonPos;
            var pylon = _pylonGo.AddComponent<Pylon>();
            pylon.Build(); // AddComponent's own Awake never runs outside Play mode
            pylon.Configure(RelayId);

            pylon.TakeDamage(new DamageInfo(120f, pylon.transform.position, Vector3.down, Team.Player));
            Assert.IsFalse(pylon.IsAlive, "setup failure: 120 damage must destroy a 120 HP pylon");
            Assert.AreEqual(0, Pylon.LiveFeedersOf(RelayId),
                "setup failure: a destroyed pylon's relay must read 0 live feeders before the Welder acts");

            _welder = SpawnWelder(WelderPos);
            Assert.AreEqual(RobotEnemy.State.Chase, _welder.Current,
                "setup failure: a freshly activated Welder must already be awake");

            _welder.Tick(6.1f); // the ticket's own "beams it for 6 s", plus a small margin

            Assert.IsTrue(pylon.IsAlive,
                "MV-1311: a Welder beaming a destroyed pylon for 6 s must rebuild it");
            Assert.AreEqual(1f, pylon.Normalized, 0.001f,
                "MV-1311: a rebuilt pylon must stand again at full health");
            Assert.AreEqual(1, Pylon.LiveFeedersOf(RelayId),
                "MV-1311: a rebuilt pylon must count again as a live feeder of its relay");
        }

        /// <summary>Drives the REAL, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection — same technique <c>MV1244WelderRepairTests.SpawnWelder</c> uses.</summary>
        private static RobotEnemy SpawnWelder(Vector3 position)
        {
            var spawnerGo = new GameObject("MV1311 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                Assert.IsNotNull(CreateInstanceMethod, "EnemySpawner.CreateInstance went missing");

                var e = (RobotEnemy)CreateInstanceMethod.Invoke(spawner, new object[] { EnemyArchetype.Welder });

                e.transform.SetParent(null, worldPositionStays: false);
                e.transform.position = position;
                e.gameObject.SetActive(true);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
                return e;
            }
            finally
            {
                Object.DestroyImmediate(spawnerGo);
            }
        }
    }
}
