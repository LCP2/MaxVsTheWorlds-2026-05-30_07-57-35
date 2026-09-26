using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-953 (triage, read at c04a337a): Sentinels have no gravity of their own --
    /// <see cref="Sentinel.SnapToLevelSurface"/> (<see cref="MapData.SnapToWalkableSurface"/>) used to
    /// hand back Max's own raw Y unclamped whenever he was off a deck, floor level (0) only because his
    /// Y coincidentally already sat there. When Max fell through the world at World 1 g20 (a20-&gt;a21)
    /// that Y was -50, and every following Sentinel was dragged straight down with him on its very next
    /// tick.
    ///
    /// One EditMode test (testing policy MV-465 Rule 1): builds World 1 through the real
    /// <see cref="WorldMapLoader"/> path (so this proves the actual shipped config, not a hand-built
    /// fixture) and drives a following Sentinel's own private <c>TickMovement</c> directly -- the same
    /// reflection idiom <c>MV624SentinelCollisionTests</c> already uses -- with its follow target's Y set
    /// to -50 over a floor (non-deck) XZ in area a1. Deliberately does NOT build any
    /// <see cref="MapRuntime"/> geometry: the map's own floor collider would stop a
    /// <see cref="CharacterController"/>'s swept <c>Move</c> at y~0 either way (the sentinel is already
    /// resting on it), passing this test whether or not the fix landed -- collision would mask the exact
    /// RESOLUTION defect (a bad Y coming out of <see cref="MapData.SnapToWalkableSurface"/>) this ticket
    /// fixes. Tier 2 (testing policy MV-465): asserts the Sentinel's own RESOLVED transform after the
    /// tick, not the helper's return value alone, per this ticket's own AC.
    /// </summary>
    public sealed class MV953SentinelNeverFollowsBelowFloorTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        private static void TickMovement(Sentinel sentinel, float dt) =>
            typeof(Sentinel).GetMethod("TickMovement", NonPublicInstance).Invoke(sentinel, new object[] { dt });

        [Test]
        public void FollowingSentinelIsNotDraggedBelowTheFloorWhenMaxFallsThroughTheWorld()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            // WorldMapLoader renames a combat area's authored id ("a1") to the old engine's "area<N>"
            // convention (see its own comment) -- "area1" is the zone id at runtime, not "a1".
            MapZone a1 = map.Zone("area1");
            Assert.IsNotNull(a1, "MV-953: world1_config.json must still author area index 1 (a plain floor area, no deck)");

            GameObject pathGo = null, followGo = null, sentinelGo = null;
            try
            {
                pathGo = new GameObject("MV953-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                typeof(BackyardPath).GetField("_map", NonPublicInstance).SetValue(path, map);

                // Max's transform after falling through the world at g20 -- 50m below the floor, still
                // over a1's own XZ.
                followGo = new GameObject("Max-fallen-through-world");
                followGo.transform.position = new Vector3(a1.x, -50f, a1.z);

                sentinelGo = new GameObject("Following-Sentinel");
                var sentinel = sentinelGo.AddComponent<Sentinel>();
                // 5m off Max's XZ in Z -- past DefaultSentinelReactDistance (1.5m) so the standoff-follow
                // step runs, not the close-range sidestep react.
                sentinel.Init(new Vector3(a1.x, 0f, a1.z + 5f), maxHp: 999f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 3f, standoffDistance: AbilityTuning.DefaultSentinelStandoffDistance,
                    followTarget: followGo.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)

                TickMovement(sentinel, 0.05f);

                Assert.That(sentinel.transform.position.y, Is.EqualTo(0f).Within(0.05f),
                    "MV-953: a following Sentinel must snap to the floor's own walkable surface (y=0), " +
                    $"never Max's own raw Y ({followGo.transform.position.y}) -- got {sentinel.transform.position.y:0.###}");
            }
            finally
            {
                Sentinel.DestroyAllActive();
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                if (followGo != null) Object.DestroyImmediate(followGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
            }
        }
    }
}
