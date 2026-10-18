using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1223 — the Mothership Assembly Cradle's build/cap/scrap loop. Fails to compile on base commit
    /// 697a4f0: no <see cref="AssemblyCradle"/> type exists there at all. Tier 2 (resolved values):
    /// asserts the spawner's own resolved live count and the pickup director's own resolved live-pickup
    /// list, never an authored constant.
    ///
    /// ONE new EditMode test (MV-465 Rule 1) — all three AC sub-checks (cap-then-stop, respawn-within-
    /// one-cycle-of-a-kill, scrap-on-mid-build-destroy) are one continuous scenario on one cradle, not
    /// three separate tests.
    /// </summary>
    // Guards MV-1223
    public sealed class MV1223AssemblyCradleTests
    {
        // Same "distinctive far-off origin" idiom ReplicatorTests/EnemySpawnerTests use — EditMode tests
        // share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-91234f, 0f, 55678f);

        private GameObject _cradleGo;
        private GameObject _pickupDirectorGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            PickupWallet.Reset(); // isolates the Parts/Cells wallet-capacity gate from whatever a prior test left it at
        }

        [TearDown]
        public void TearDown()
        {
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            if (_pickupDirectorGo != null) Object.DestroyImmediate(_pickupDirectorGo);
            RobotEnemy.ResetRegistry();
            PickupWallet.Reset();
        }

        [Test]
        public void Tick_BuildsToCapThenStops_RespawnsOnKillWithinOneCycle_AndScrapsAMidBuildDestroy()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.name = "AssemblyCradle";
            _cradleGo.transform.position = RigOrigin;
            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build(); // AddComponent's own Awake never runs outside Play mode

            var spawner = _cradleGo.GetComponent<EnemySpawner>();
            spawner.ConfigureAreaComposition(new WorldComposition { rusher = 1 });
            cradle.SetMaxInArea(true);

            // (a) six full build cycles must produce exactly six live robots, then stop.
            for (int i = 0; i < AssemblyCradle.MaxAliveSpawned; i++)
                cradle.Tick(AssemblyCradle.BuildSeconds);
            Assert.AreEqual(AssemblyCradle.MaxAliveSpawned, spawner.LiveCount,
                "six full build cycles must produce exactly six live robots");

            cradle.Tick(AssemblyCradle.BuildSeconds); // a would-be 7th cycle while already at the cap
            Assert.AreEqual(AssemblyCradle.MaxAliveSpawned, spawner.LiveCount,
                "capped at six — a 7th must never queue while all six slots are full");

            // (b) killing one of the six frees a slot; the replacement must land within one full 4.0 s
            // cycle of that kill — not before, and not meaningfully after.
            FieldInfo liveField = typeof(EnemySpawner).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (IList)liveField.GetValue(spawner);
            var victim = (RobotEnemy)live[0];
            victim.KillWithoutDrops();
            Assert.AreEqual(AssemblyCradle.MaxAliveSpawned - 1, spawner.LiveCount,
                "the kill must free its slot immediately");

            cradle.Tick(AssemblyCradle.BuildSeconds - 0.05f);
            Assert.AreEqual(AssemblyCradle.MaxAliveSpawned - 1, spawner.LiveCount,
                "the replacement must not land early — a freed slot still runs a full cycle, not a partial one");

            cradle.Tick(0.10f); // crosses BuildSeconds within the ticket's own 4.0 s +/- 0.05 s tolerance
            Assert.AreEqual(AssemblyCradle.MaxAliveSpawned, spawner.LiveCount,
                "the 7th robot must land within 4.0 s +/- 0.05 of the kill that freed its slot");

            // (c) destroyed mid-build (BuildProgress 0.5): the robot in progress must never spawn, and
            // exactly 3 Parts (PickupKind.PowerCell) must drop at the bay instead of the ordinary payout.
            // Free one slot first — at the cap (from step (b)) the build timer is held at zero and a
            // fresh cycle can never start, so there would be no build in progress to interrupt.
            var victim2 = (RobotEnemy)live[0];
            victim2.KillWithoutDrops();
            int liveRobotsBefore = spawner.LiveCount;

            _pickupDirectorGo = new GameObject("PickupDirector");
            var director = _pickupDirectorGo.AddComponent<PickupDirector>();
            FieldInfo pickupsField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);

            cradle.Tick(AssemblyCradle.BuildSeconds * 0.5f); // BuildProgress 0.5 — a build is mid-way
            Assert.AreEqual(0.5f, cradle.BuildProgress, 0.001f, "the cradle must be exactly half-built");

            cradle.TakeDamage(new DamageInfo(999999f, cradle.transform.position, Vector3.forward, Team.Player));

            Assert.IsFalse(cradle.IsAlive, "lethal damage must destroy the cradle");
            Assert.AreEqual(liveRobotsBefore, spawner.LiveCount,
                "the robot mid-build when the cradle died must never spawn");

            var pickups = (IList)pickupsField.GetValue(director);
            int parts = 0;
            foreach (Pickup p in pickups)
                if (p.Kind == PickupKind.PowerCell) parts++;
            Assert.AreEqual(AssemblyCradle.ScrapParts, parts,
                "a cradle destroyed mid-build must drop exactly 3 Parts (PickupKind.PowerCell), not the ordinary payout");
        }
    }
}
