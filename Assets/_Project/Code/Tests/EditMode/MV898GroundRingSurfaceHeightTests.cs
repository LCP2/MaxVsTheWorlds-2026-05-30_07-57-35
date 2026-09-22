using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-898 (Lee, 2026-09-22, live build): the ground rings under Max, Sentinels and robots draw at
    /// floor level even when their owner is standing on a raised World 2 deck, so the ring sits about
    /// 2.5 m below the actor it belongs to. <see cref="GroundAnchorVfx.Ground"/> flattened every owner's
    /// position straight to <c>y=0</c> — a fixed floor plane — instead of consulting
    /// <see cref="MapData.SnapToWalkableSurface"/>'s own "on a deck" test, the same one MV-864/MV-896
    /// already trust for a Sentinel's own footing.
    ///
    /// One consolidated EditMode test (testing policy MV-465, Rule 1): builds World 2 through the real
    /// <see cref="WorldLibrary"/> config (so this proves the actual shipped deck, not a hand-built
    /// fixture) and drives <see cref="GroundAnchorVfx.LateUpdate"/> itself via reflection — the same
    /// proven-in-EditMode path <c>MV871PlayerScanRadiusTests</c> already uses, including the real
    /// <see cref="Physics.OverlapSphereNonAlloc"/> query. Three owner kinds (Max, a Sentinel, a robot),
    /// each a bare <see cref="CharacterController"/> + <see cref="IDamageable"/> — <c>Ground()</c> does
    /// not (and per <see cref="GroundAnchorVfx"/>'s own discovery contract, must not) care which concrete
    /// type its owner is, so this is what "covers all three owner kinds" means for a height fix that is
    /// keyed on position, not on type.
    ///
    /// Tier 2 (resolved values): reads the world position off the actual pooled <see cref="GroundRing"/>
    /// objects <c>LateUpdate</c> placed, never a constant.
    ///
    /// Fails on base commit 40590cf: with <c>Ground</c> still flattening to a fixed <c>y=0</c>, every
    /// ring on the deck resolves to <c>y=Lift</c> (~0.05) instead of <c>y=deckHeight+Lift</c> (~2.55) —
    /// see the fix comment for the exact quoted failure.
    /// </summary>
    public sealed class MV898GroundRingSurfaceHeightTests
    {
        private sealed class TestActor : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Enemy;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo LateUpdateMethod =
            typeof(GroundAnchorVfx).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        [TearDown]
        public void Clear() => EnemyNavigation.Reset();

        private static GameObject SpawnActor(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.5f;
            go.AddComponent<TestActor>();
            return go;
        }

        [Test]
        public void RingsForAllThreeOwnerKinds_ResolveToTheSurfaceTheyStandOn_DeckOrFloor()
        {
            Assert.IsNotNull(LateUpdateMethod, "GroundAnchorVfx.LateUpdate went missing");
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone zone = map.Zone("area11");
            Assert.IsNotNull(zone, "MV-898: world2_config.json must still author area 'a11' (zone id 'area11')");
            MapEntity deck = map.Entity("a11_deck1");
            Assert.IsNotNull(deck, "MV-898: world2_config.json must still author 'a11_deck1'");

            // A floor probe point: inside a11's own zone, but clear of the deck's rect — pick whichever
            // end of the zone (in Z) sits furthest from the deck, then confirm both facts hold rather
            // than assuming them.
            float farZ = Mathf.Abs(zone.ZMin - deck.z) > Mathf.Abs(zone.ZMax - deck.z) ? zone.ZMin + 1f : zone.ZMax - 1f;
            var floorPos = new Vector3(zone.x, 0f, farZ);
            Assert.Greater(Mathf.Abs(floorPos.z - deck.z), deck.depth * 0.5f + 0.5f,
                "test setup: the floor probe point must sit outside the deck's own rect");
            Assert.IsTrue(zone.Contains(floorPos.x, floorPos.z),
                "test setup: the floor probe point must sit inside a11's own zone");

            var deckPos = new Vector3(deck.x, deck.height, deck.z);

            GameObject pathGo = null, directorGo = null;
            GameObject max = null, sentinel = null, robot = null;

            try
            {
                pathGo = new GameObject("MV898-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                directorGo = new GameObject("MV898 Director");
                var director = directorGo.AddComponent<GroundAnchorVfx>();

                // --- Phase 1: all three owners standing on a11_deck1 ---------------------------------
                max = SpawnActor("MV898 Max", deckPos);
                sentinel = SpawnActor("MV898 Sentinel", deckPos);
                robot = SpawnActor("MV898 Robot", deckPos);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)

                LateUpdateMethod.Invoke(director, null);

                var deckRings = director.GetComponentsInChildren<GroundRing>(includeInactive: true)
                    .Where(r => r.gameObject.activeSelf && r.name == "AnchorRing").ToArray();

                Assert.AreEqual(3, deckRings.Length,
                    "exactly one ring per owner should be placed for the three deck-standing actors");
                foreach (GroundRing r in deckRings)
                    Assert.AreEqual(deck.height, r.transform.position.y - GroundAnchorTuning.RingLift, 0.05f,
                        $"{r.transform.parent}: ring '{r.name}' at {r.transform.position} is not within " +
                        $"0.05m of a11_deck1's own top ({deck.height})");

                // --- Phase 2: all three owners standing on the area floor -----------------------------
                max.transform.position = floorPos;
                sentinel.transform.position = floorPos;
                robot.transform.position = floorPos;
                Physics.SyncTransforms();

                LateUpdateMethod.Invoke(director, null);

                var floorRings = director.GetComponentsInChildren<GroundRing>(includeInactive: true)
                    .Where(r => r.gameObject.activeSelf && r.name == "AnchorRing").ToArray();

                Assert.AreEqual(3, floorRings.Length,
                    "exactly one ring per owner should be placed for the three floor-standing actors");
                foreach (GroundRing r in floorRings)
                    Assert.AreEqual(0f, r.transform.position.y - GroundAnchorTuning.RingLift, 0.05f,
                        $"ring '{r.name}' at {r.transform.position} is not within 0.05m of the area floor");
            }
            finally
            {
                if (directorGo != null) Object.DestroyImmediate(directorGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (max != null) Object.DestroyImmediate(max);
                if (sentinel != null) Object.DestroyImmediate(sentinel);
                if (robot != null) Object.DestroyImmediate(robot);
            }
        }
    }
}
