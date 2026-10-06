using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1091 — Lee (device, World 2, REPLICATORS 8/41, the floor leg): "I died in area 10 and
    /// spawned in area 11 when it should have been 9." Root cause:
    /// <c>AreaAccumulationDirector.PredecessorOf</c> (MV-1002/MV-1065) validates a history candidate by
    /// "is this area SOME authored source of the death area", with no check that the matching GATE's
    /// elevation agreed with how Max actually got there. a10 has two inbound gates — g27 (a9-&gt;a10,
    /// floor) and g35 (a11-&gt;a10, flagged <c>primary[DECK]</c>, used much later on the deck leg). A
    /// player who steps a10-&gt;a11-&gt;a10 on the FLOOR (open the gate to a11, walk in, retreat) leaves
    /// history <c>[..., a9, a10, a11, a10]</c>; the old code accepted a11 as the predecessor purely
    /// because a11 is SOME source of a10 (via the deck gate it never actually used this trip), landing
    /// the death a whole area further on than where Max died.
    ///
    /// The fix: <c>PredecessorOf</c> gained a level-aware overload — for an in-place-deck area (one area
    /// id carrying both a floor and a deck garrison), only an inbound gate whose own elevation matches
    /// the LEVEL Max actually died on is accepted. <c>WorldRunner.OnPlayerDied</c> reads that level
    /// straight off Max's real position (<c>MapData.IsOnDeck</c>) and threads it through
    /// <c>ResolveRespawnPredecessor</c>. <c>WorldRunner.ResumeCheckpoint</c> is untouched (passes no
    /// level — MV-1065's own resume test still passes unmodified, since that scenario is already told
    /// apart by crossing through genuinely different gates, not by level).
    ///
    /// Must fail on d30d293 (CS1501 — no 2-arg <c>AreaAccumulationDirector.PredecessorOf</c> overload
    /// exists yet, and even a reflection-free 1-arg call resolves case (a) below to area11, not area9).
    /// </summary>
    public sealed class MV1091FloorDeckRespawnPredecessorTests
    {
        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            DeathRunState.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            var overlay = Object.FindFirstObjectByType<DeathOverlay>();
            if (overlay != null) Object.DestroyImmediate(overlay.gameObject);

            RobotEnemy.ResetRegistry();
            DeathRunState.Reset();
            DevTuning.Reset();
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static void InvokeOnPlayerDied(WorldRunner runner) =>
            typeof(WorldRunner).GetMethod("OnPlayerDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runner, null);

        /// <summary>A world position that resolves, via <see cref="MapData.ZoneAt(float,float,float)"/>,
        /// to <paramref name="zone"/> — its own centre at floor height when <paramref name="onDeck"/> is
        /// false, or the centre of its own Deck/Hatch entity at deck height when true. Unlike the
        /// MV-1002/MV-1065 helper of the same name, this is driven by an explicit flag rather than
        /// <c>zone.level</c> — an in-place-deck area (a10/a11/a12) keeps <c>zone.level == 0</c> at BOTH
        /// elevations (MV-692: the deck is authored directly inside the level-0 area, not as a separate
        /// overlay zone), so <c>zone.level</c> alone can never tell this test which height to probe.</summary>
        private static Vector3 ProbePositionFor(MapData map, MapZone zone, bool onDeck)
        {
            Assert.IsNotNull(zone, "setup failure: ProbePositionFor given a null zone");
            if (!onDeck) return new Vector3(zone.x, 0.5f, zone.z);

            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Deck && e.Kind != EntityKind.Hatch) continue;
                if (!zone.Contains(e.x, e.z)) continue;
                return new Vector3(e.x, map.deckHeight, e.z);
            }

            Assert.Fail($"setup failure: no Deck/Hatch entity found inside zone '{zone.id}' for a deck probe");
            return default;
        }

        [Test]
        public void DeathRespawnPredecessor_RespectsTheLevelMaxActuallyDiedOn()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig world2 = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(world2, out MapData map2, out string loadReason2), loadReason2);
            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();

            try
            {
                // --- (a) floor-only retreat: a9 -> a10 -> a11 -> (retreat) a10, death on the FLOOR.
                // a11 IS some authored source of a10 (g35, the deck gate) but NEVER the floor one — the
                // retreat here walks back through g28's own already-open floor doorway, not g35. Must
                // restore a9 (a10's own floor gate, g27), not a11.
                RunDeathScenario(world2, map2, "case (a), floor retreat",
                    walk: (director, player) =>
                    {
                        WalkFloor(director, map2, player, 2, 9);
                        WalkFloor(director, map2, player, 10, 10);
                        WalkFloor(director, map2, player, 11, 11);
                        // Retreat — a real, linked physical crossing (the doorway is open both ways),
                        // but not a fresh gate entry, so no EnterArea call (matches MV-1065's own idiom).
                        StepTo(director, map2, player, 10, onDeck: false, enter: false);
                    },
                    expectedRespawnArea: 9);

                // --- (b) deck arrival: a12 -> a11 -> a10 on the DECK (the descending gantry leg), death
                // on the DECK. The most recent real crossing into a10 was via g35 (from a11, on the
                // deck) — must restore a11, not fall through to a9's floor gate.
                RunDeathScenario(world2, map2, "case (b), deck arrival",
                    walk: (director, player) =>
                    {
                        WalkFloor(director, map2, player, 2, 12);
                        StepTo(director, map2, player, 11, onDeck: true, enter: false);
                        StepTo(director, map2, player, 10, onDeck: true, enter: false);
                    },
                    expectedRespawnArea: 11);

                // --- (c) regression guard: a9 -> a10 only, no retreat, death on the FLOOR — the simple
                // case the MV-1002/MV-1065 scan already got right, proving the new level filter doesn't
                // break it.
                RunDeathScenario(world2, map2, "case (c), no retreat",
                    walk: (director, player) => WalkFloor(director, map2, player, 2, 10),
                    expectedRespawnArea: 9);
            }
            finally
            {
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }

            // --- (d) World 1 and World 3's plain linear walks are unchanged: death in area N (sampled
            // at 3, 6 and 9 of each) still restores N-1. Both worlds author zero in-place-deck areas, so
            // the new level filter must be a complete no-op here — PredecessorOf(idx, false) must agree
            // with the pre-fix, level-less answer at every sample.
            AssertLinearWorldUnaffected(WorldLibrary.World1, new[] { 3, 6, 9 });
            AssertLinearWorldUnaffected(WorldLibrary.World3, new[] { 3, 6, 9 });
        }

        /// <summary>Builds a fresh World 2 scene, runs <paramref name="walk"/> against its director, then
        /// drives a real death (<c>OnPlayerDied</c>) and <c>Continue</c> — asserting the RESOLVED
        /// <c>CurrentArea</c> the respawn actually lands on, through the real death pipeline end to end
        /// (not a reflective peek at private state).</summary>
        private static void RunDeathScenario(WorldConfig cfg, MapData map, string label,
            System.Action<AreaAccumulationDirector, GameObject> walk, int expectedRespawnArea)
        {
            GameObject host = new GameObject($"MV1091 Host — {label}");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                var areaDirector = host.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg);
                areaDirector.Configure(map, built.Cover);

                var runner = host.AddComponent<WorldRunner>();
                runner.Configure(cfg, map, built, areaDirector);

                GameObject playerGo = new GameObject("Player") { tag = "Player" };
                try
                {
                    walk(areaDirector, playerGo);

                    InvokeOnPlayerDied(runner);
                    Assert.IsTrue(runner.HasPendingRespawn, $"{label}: precondition — a death must leave a respawn pending");
                    runner.Continue();

                    Assert.That(areaDirector.CurrentArea, Is.EqualTo(expectedRespawnArea),
                        $"MV-1091 {label}: respawn must land at area{expectedRespawnArea}, got area{areaDirector.CurrentArea}");
                }
                finally
                {
                    Object.DestroyImmediate(playerGo);
                }
            }
            finally
            {
                GameObject bodies = GameObject.Find("Area Robots");
                if (bodies != null) Object.DestroyImmediate(bodies);
                RobotEnemy.ResetRegistry();
                DeathRunState.Reset();
                var overlay = Object.FindFirstObjectByType<DeathOverlay>();
                if (overlay != null) Object.DestroyImmediate(overlay.gameObject);
                Object.DestroyImmediate(host);
            }
        }

        private static void WalkFloor(AreaAccumulationDirector director, MapData map, GameObject player,
            int fromInclusive, int toInclusive)
        {
            for (int areaIndex = fromInclusive; areaIndex <= toInclusive; areaIndex++)
                StepTo(director, map, player, areaIndex, onDeck: false, enter: true);
        }

        private static void StepTo(AreaAccumulationDirector director, MapData map, GameObject player,
            int areaIndex, bool onDeck, bool enter)
        {
            player.transform.position = ProbePositionFor(map, map.Zone($"area{areaIndex}"), onDeck);
            InvokePrivate(director, "Update");
            if (enter) director.EnterArea(areaIndex);
        }

        /// <summary>World 1/World 3 regression guard (AC d): walks the real linear route up to the
        /// highest <paramref name="sampleAreas"/> entry, calling the new 2-arg
        /// <c>PredecessorOf(idx, onDeck: false)</c> overload at each sample — must equal <c>idx - 1</c>,
        /// exactly as the level-less answer already did, proving the level filter is a no-op for a world
        /// that authors no in-place-deck area.</summary>
        private static void AssertLinearWorldUnaffected(string worldKey, int[] sampleAreas)
        {
            LogAssert.ignoreFailingMessages = true;
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();

            GameObject host = new GameObject($"MV1091 Linear Host — {worldKey}");
            GameObject playerGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                var areaDirector = host.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg);
                areaDirector.Configure(map, built.Cover);

                playerGo = new GameObject("Player") { tag = "Player" };

                int highest = 1;
                foreach (int s in sampleAreas) highest = Mathf.Max(highest, s);

                var toCheck = new System.Collections.Generic.HashSet<int>(sampleAreas);
                for (int areaIndex = 2; areaIndex <= highest; areaIndex++)
                {
                    playerGo.transform.position = ProbePositionFor(map, map.Zone($"area{areaIndex}"), onDeck: false);
                    InvokePrivate(areaDirector, "Update");
                    areaDirector.EnterArea(areaIndex);

                    if (!toCheck.Contains(areaIndex)) continue;

                    int predecessor = areaDirector.PredecessorOf(areaIndex, false);
                    Assert.That(predecessor, Is.EqualTo(areaIndex - 1),
                        $"MV-1091 AC(d) {worldKey}: a plain linear world must still resolve area{areaIndex}'s " +
                        $"predecessor to area{areaIndex - 1} with the new level-aware overload — got area{predecessor}");
                }
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                GameObject bodies = GameObject.Find("Area Robots");
                if (bodies != null) Object.DestroyImmediate(bodies);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
                RobotEnemy.ResetRegistry();
                DeathRunState.Reset();
            }
        }
    }
}
