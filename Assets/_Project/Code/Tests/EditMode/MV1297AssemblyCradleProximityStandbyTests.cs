using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1297 — Lee (19 Oct): "the cradles don't do anything — they can be destroyed but otherwise
    /// don't function." Three bugs in one cradle: <see cref="AssemblyCradle.Tick"/> (base commit
    /// 2b30ec6) built whenever Max stood anywhere in its area, with no distance check at all; a build
    /// animation played its full 4 s even when the field-wide robot budget had no room, then silently
    /// spawned nothing (<see cref="EnemySpawner.SpawnExact"/>'s own room check only bites at the very
    /// end); and <c>ApplyDestructionEffects</c> never hid the body or disabled the collider, so a "dead"
    /// cradle stood there, solid, forever. Fails to compile on base commit 2b30ec6 (quoted in this
    /// ticket's fix comment): no <c>AssemblyCradle.ProximityRadius</c>, <c>AssemblyCradle.BuildStage.Standby</c>,
    /// <c>EnemySpawner.HasGlobalRoom</c>, or <c>EnemySpawner.ResetCradleReservations</c> (used in this
    /// test's own SetUp/TearDown) exist there. Tier 2 (resolved values): every assertion reads
    /// the spawner's own resolved live count, the cradle's own resolved <c>Stage</c>/<c>BuildProgress</c>,
    /// or the cradle's own resolved <c>Collider.enabled</c> — never an authored constant re-read against
    /// itself.
    ///
    /// ONE new EditMode test (MV-465 Rule 1): proximity gating, STANDBY under a full budget, STANDBY's
    /// own recovery once room appears, and the death collider are one continuous scenario on one cradle,
    /// not four separate tests.
    /// </summary>
    // Guards MV-1297
    public sealed class MV1297AssemblyCradleProximityStandbyTests
    {
        // Same "distinctive far-off origin" idiom MV1223AssemblyCradleTests/MV1246AssemblyCradleTurretTests
        // use — EditMode tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-54321f, 0f, 67890f);

        private GameObject _cradleGo;
        private GameObject _maxGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetCradleReservations();
            DevTuning.Reset(); // isolates the global robot budget this test dials from whatever a prior test left it at
        }

        [TearDown]
        public void TearDown()
        {
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetCradleReservations();
            DevTuning.Reset();
        }

        [Test]
        public void Tick_GatesOnProximity_StandsByOnFullBudget_RecoversOnRoom_AndDisablesColliderOnDeath()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.name = "AssemblyCradle";
            _cradleGo.transform.position = RigOrigin;
            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build(); // AddComponent's own Awake never runs outside Play mode

            var spawner = _cradleGo.GetComponent<EnemySpawner>();
            spawner.ConfigureAreaComposition(new WorldComposition { rusher = 1 });
            cradle.SetMaxInArea(true);

            _maxGo = new GameObject("MV1297 Max") { tag = "Player" };

            // (a) Max 12 m away — outside ProximityRadius (10 m) — no build must ever start.
            _maxGo.transform.position = RigOrigin + new Vector3(12f, 0f, 0f);
            for (int i = 0; i < 50; i++) cradle.Tick(0.1f); // 5.0 simulated seconds — well past one cycle
            Assert.AreEqual(0, spawner.LiveCount, "Max 12 m away must never start a build");
            Assert.AreEqual(0f, cradle.BuildProgress, 0.001f, "no build in progress while Max is out of range");

            // (b) Max 8 m away — inside ProximityRadius — a robot must spawn within 4.5 s.
            _maxGo.transform.position = RigOrigin + new Vector3(8f, 0f, 0f);
            for (int i = 0; i < 45 && spawner.LiveCount == 0; i++) cradle.Tick(0.1f); // up to 4.5 s
            Assert.AreEqual(1, spawner.LiveCount, "Max 8 m away must spawn a robot within 4.5 s");

            // (c) Field-wide budget full — the cradle must report STANDBY and spawn nothing, never play
            // the build animation on a promise it can't keep.
            DevTuning.GlobalRobotBudget = 0f;
            for (int i = 0; i < 10; i++) cradle.Tick(0.1f); // past the first 0.5 s retry window
            Assert.AreEqual(AssemblyCradle.BuildStage.Standby, cradle.Stage,
                "a room-starved cradle must report STANDBY, not silently play a build it can't finish");
            Assert.AreEqual(1, spawner.LiveCount, "STANDBY must spawn nothing — still just the one robot from (b)");

            // (d) Room appears — the cradle must spawn within 4.5 s of that, not stay stuck in STANDBY.
            DevTuning.GlobalRobotBudget = 20f;
            for (int i = 0; i < 45 && spawner.LiveCount == 1; i++) cradle.Tick(0.1f); // up to 4.5 s
            Assert.AreEqual(2, spawner.LiveCount, "room appearing must free STANDBY and spawn within 4.5 s");
            Assert.AreNotEqual(AssemblyCradle.BuildStage.Standby, cradle.Stage,
                "once room exists the cradle must leave STANDBY");

            // (e) Death — the collider must be disabled (the body no longer stands there to be hit).
            Assert.IsTrue(cradle.IsAlive, "the cradle must still be alive going into the lethal hit");
            cradle.TakeDamage(new DamageInfo(999999f, cradle.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(cradle.IsAlive, "lethal damage must destroy the cradle");
            Assert.IsFalse(_cradleGo.GetComponent<Collider>().enabled,
                "a destroyed cradle's collider must be disabled");
        }
    }
}
