using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1244 — the Welder's own repair tick. Fails to compile on base commit 699786e: no
    /// <see cref="EnemyKind.Welder"/> value exists there at all (the same "brand new kind, fails to
    /// compile" proof <c>MV1223AssemblyCradleTests</c> uses for a brand new TYPE). Tier 2 (resolved
    /// values): asserts the cradle's own resolved <see cref="AssemblyCradle.HealthCurrent"/>, never an
    /// authored constant.
    ///
    /// ONE new EditMode test (MV-465 Rule 1) — both halves of AC1 (repairs while alive, stops the
    /// instant it's dead) are one continuous scenario on one cradle/Welder pair, not two tests.
    /// </summary>
    // Guards MV-1244
    public sealed class MV1244WelderRepairTests
    {
        // Same "distinctive far-off origin" idiom MV1223AssemblyCradleTests/EnemySpawnerTests use.
        private static readonly Vector3 CradlePos = new Vector3(-83214f, 0f, 61234f);
        private static readonly Vector3 WelderPos = CradlePos + new Vector3(5f, 0f, 0f); // 5 m, within the 20 m repair range

        private static readonly MethodInfo CreateInstanceMethod = typeof(EnemySpawner).GetMethod(
            "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _cradleGo;
        private RobotEnemy _welder;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            AssemblyCradle.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_welder != null) Object.DestroyImmediate(_welder.gameObject);
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            RobotEnemy.ResetRegistry();
            AssemblyCradle.ResetRegistry();
        }

        [Test]
        public void TickWelderRepair_HealsADamagedCradleWithinRange_AndStopsTheInstantTheWelderDies()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.name = "MV1244 Cradle";
            _cradleGo.transform.position = CradlePos;
            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build(); // AddComponent's own Awake never runs outside Play mode

            const float cradleMax = 474.75f; // AssemblyCradle's own authored CradleHealth
            cradle.TakeDamage(new DamageInfo(cradleMax * 0.5f, cradle.transform.position, Vector3.forward, Team.Player));
            Assert.AreEqual(0.5f, cradle.Normalized, 0.001f, "setup failure: the cradle must start at exactly 50% health");
            float healthAtStart = cradle.HealthCurrent;

            _welder = SpawnWelder(WelderPos);
            Assert.AreEqual(RobotEnemy.State.Chase, _welder.Current,
                "setup failure: a freshly activated Welder must already be awake");

            _welder.Tick(2f); // explicit dt — 2 s at the ticket's own 15 HP/s rate

            Assert.AreEqual(healthAtStart + 30f, cradle.HealthCurrent, 1f,
                "a Welder within 20 m of a damaged cradle must heal it ~30 HP over 2 s at 15 HP/s");

            float healthAfterRepair = cradle.HealthCurrent;
            _welder.KillWithoutDrops();

            _welder.Tick(2f); // the same 2 s again, but the Welder is now dead

            Assert.AreEqual(healthAfterRepair, cradle.HealthCurrent, 0.01f,
                "a dead Welder must heal nothing further — the cradle must not gain any more health");
        }

        /// <summary>Drives the REAL, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection — the normal pool/factory path every in-game robot is built through (same
        /// technique <c>MV984AwakeRobotsTakeDamageTests.SpawnViaPool</c> uses). Activating it runs the
        /// real <c>OnEnable</c>/<c>ResetState</c>, landing it in a genuinely awake <c>State.Chase</c>.</summary>
        private static RobotEnemy SpawnWelder(Vector3 position)
        {
            var spawnerGo = new GameObject("MV1244 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                Assert.IsNotNull(CreateInstanceMethod, "EnemySpawner.CreateInstance went missing");

                var e = (RobotEnemy)CreateInstanceMethod.Invoke(spawner, new object[] { EnemyArchetype.Welder });

                // Detach before the spawner is torn down below, or DestroyImmediate(spawnerGo) cascades
                // and destroys the very GameObject this method hands back (same note MV984's own
                // SpawnViaPool carries).
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
