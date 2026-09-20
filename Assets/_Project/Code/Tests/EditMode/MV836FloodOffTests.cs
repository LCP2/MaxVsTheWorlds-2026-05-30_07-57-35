using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-836 (Lee, 2026-09-17): "Comment this all out for now. No flood concept. Max and robots are
    /// not hurt or slowed down." Fails on the pre-ticket base commit (52b3cf2): <c>StormdrainFlood</c>
    /// has no <c>FloodEnabled</c> switch, so the runner ticks the real flood — after 600 s with all
    /// World 2 Replicators alive, Max (parked on <c>a18</c> — <c>a20</c> before MV-865 renumbered World
    /// 2's areas in play order — a non-sludge point banded to flood by MV-774's own "last third of the
    /// route" rule) takes real damage and
    /// <see cref="MapSlowZones"/> reads 0.6 there instead of 1.0; the Sludgequeen's own flood similarly
    /// still slows (<see cref="SludgequeenBoss.FloodSpeedMultiplierAt"/> reads 0.85, not 1.0 — see
    /// <see cref="MaxWorlds.Bosses.SludgequeenTuning.FloodSlowMultiplier"/>) and damages
    /// (<see cref="SludgequeenBoss.TickFloodDamage"/> deals 4, not 0) anyone standing in phase two's
    /// flooded floor.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), one narrative per AC bullet, same idiom
    /// as <c>MV774FloodTests</c>: AC1 loads the real, shipped World 2 config through
    /// <see cref="WorldLibrary"/>/<see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>/
    /// <see cref="WorldRunner"/> (no hand-set flood/Replicator state anywhere in this file) and drives
    /// the real <see cref="StormdrainFloodRunner.TickFlood"/> path; AC2 drives
    /// <see cref="SludgequeenBoss"/> into phase two the same reflection-driven way
    /// <c>MV696SludgequeenFloodTests.NewWokenBoss</c> does.
    /// </summary>
    public sealed class MV836FloodOffTests
    {
        private sealed class FakeReceiver : MonoBehaviour, IDamageable
        {
            public float TotalDamageTaken;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => TotalDamageTaken += info.Amount;
        }

        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            StormdrainFlood.Reset();
            FactoryCensus.Reset();
            SludgequeenBoss.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            StormdrainFlood.Reset();
            FactoryCensus.Reset();
            SludgequeenBoss.ResetRegistry();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void FloodSwitchedOff_NoDamage_NoSlow_AndTheBossFloodIsInert()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries once Build() actually runs — see other World2 EditMode tests' own note.
            LogAssert.ignoreFailingMessages = true;

            GameObject root = null, pathGo = null, playerGo = null, floodRunnerGo = null, bossGo = null, bossReceiverGo = null;
            try
            {
                // === AC1: the real World 2 loader, 600 simulated seconds with all Replicators alive
                // === — Max takes no flood damage, and a18 (banded to flood, no sludge of its own) reads
                // === full speed. ===
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
                Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

                root = new GameObject("MV836 Root");
                MapBuild build = MapRuntime.Build(map, root.transform);

                // StormdrainFlood.IsFlooded (what MapSlowZones/the runner's own damage tick both read)
                // resolves its zone off EnemyNavigation.Map, which finds the map through a live
                // BackyardPath, not through the local `map` this test already has — same stub MapRuntime
                // itself installs via BackyardPath.Awake in a real level load.
                pathGo = new GameObject("MV836-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                // Real production wiring for every Replicator this map built (health, spawner stop) —
                // AddComponent's own Awake never runs here (Replicator.Build's own doc comment).
                foreach (Replicator r in build.Replicators) r.Build();

                var runner = root.AddComponent<WorldRunner>();
                runner.Configure(cfg, map, build, null); // no AreaAccumulationDirector needed for this AC

                // MV-865 (World 2 re-author) re-authored the level's content, not just its numbering:
                // the Trolley Yard floor (now a13, was a6) alone now authors 21 Replicators, so World 2's
                // total rises from the MV-700/MV-852 count of 23 to 47 — a direct sum over cfg.areas
                // (see MV700World2ConfigTests), not a value this ticket's own renumbering can be blamed
                // for.
                Assert.AreEqual(47, FactoryCensus.ReplicatorsAlive,
                    "setup failure: every one of World 2's own Replicators must be standing for 'all Replicators alive' to mean anything");

                WorldArea a18 = cfg.Area("a18");
                Assert.IsNotNull(a18, "setup failure: World 2 must author area 'a18'");
                // Local (56, 72) sits inside a18's own footprint but outside its authored sludge rect
                // (WorldRectOf(9, 0, 6, 28) -> world x:[59,65] z:[62,90]) — a plain, non-sludge point.
                var a18Point = new Vector3(a18.XMin + 6f, 0f, a18.ZMin + 10f);
                Assert.IsFalse(new Rect(59f, 62f, 6f, 28f).Contains(new Vector2(a18Point.x, a18Point.z)),
                    "setup failure: the probe point must not be a18's own sludge rect, or this proves nothing about the flood");

                floodRunnerGo = new GameObject("StormdrainFloodRunner");
                var floodRunner = floodRunnerGo.AddComponent<StormdrainFloodRunner>();

                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.transform.position = a18Point;
                var receiver = playerGo.AddComponent<FakeReceiver>();

                const float simulatedSeconds = 600f;
                const float step = 1f;
                for (float t = 0f; t < simulatedSeconds; t += step) floodRunner.TickFlood(step);

                Assert.AreEqual(0f, receiver.TotalDamageTaken, 0.001f,
                    "MV-836: Max must take no flood damage over 600 s with the flood switched off");
                Assert.AreEqual(1f, MapSlowZones.Instance.SpeedMultiplierAt(a18Point), 0.001f,
                    "MV-836: a18 must read full speed with the flood switched off");

                // === AC2: the Sludgequeen's own flood, in phase two, slows and damages nobody. ===
                bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Collider stray = bossGo.GetComponent<BoxCollider>();
                if (stray != null) Object.DestroyImmediate(stray);
                var boss = bossGo.AddComponent<SludgequeenBoss>();

                // EditMode never calls Awake on a plain MonoBehaviour -- invoke it explicitly, same
                // idiom as MV696SludgequeenFloodTests.NewWokenBoss for the same boss type.
                typeof(SludgequeenBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(boss, null);
                var arenaBounds = new Rect(0f, 0f, 44f, 44f);
                boss.SetArenaBounds(arenaBounds);
                typeof(SludgequeenBoss).GetMethod("Wake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(boss, null);

                // Cross the 50% threshold, then elapse the 3 s phase-2 tell — same two-step dance
                // MV696SludgequeenFloodTests uses to land phase two for real.
                boss.TakeDamage(new DamageInfo(SludgequeenTuning.Health * 0.51f, boss.transform.position, Vector3.forward, Team.Player));
                typeof(SludgequeenBoss).GetMethod("TickPhaseTwoTell", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(boss, new object[] { 3.1f });
                Assert.IsTrue(boss.IsPhaseTwo, "setup failure: the boss must reach phase two for this point to be genuinely inside her flood rect");

                var wetPoint = new Vector3(22f, 0f, 10f); // plain south-half floor, no dry zone (same point MV696SludgequeenFloodTests uses)
                Assert.IsFalse(boss.IsDry(wetPoint), "setup failure: the probe point must be wet under phase two, or the assertions below prove nothing");

                Assert.AreEqual(1f, SludgequeenBoss.FloodSpeedMultiplierAt(wetPoint), 0.001f,
                    "MV-836: the Sludgequeen's flood must no longer slow anyone standing in it");

                bossReceiverGo = new GameObject("MV836 boss flood probe");
                var bossReceiver = bossReceiverGo.AddComponent<FakeReceiver>();
                boss.TickFloodDamage(1f, wetPoint, bossReceiver);
                Assert.AreEqual(0f, bossReceiver.TotalDamageTaken, 0.001f,
                    "MV-836: the Sludgequeen's flood must deal no damage");
            }
            finally
            {
                if (bossReceiverGo != null) Object.DestroyImmediate(bossReceiverGo);
                if (bossGo != null) Object.DestroyImmediate(bossGo);
                if (floodRunnerGo != null) Object.DestroyImmediate(floodRunnerGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (root != null) Object.DestroyImmediate(root);
            }
        }
    }
}
