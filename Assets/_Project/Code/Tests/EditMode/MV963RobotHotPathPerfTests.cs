using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-963 — a single root-cause review of the iOS fps/heat problem read four defects on a robot's
    /// hot path: <c>MapEnums</c>-backed <c>Kind</c>/<c>Shape</c>/<c>Dressing</c>/<c>CoverKind</c>
    /// properties re-parsed their string every read instead of caching it once; <see cref="MapGeometry.SpeedMultiplierAt"/>
    /// and <see cref="MapData"/>'s deck lookup walked every entity in the map instead of a
    /// pre-filtered array; <see cref="AreaAccumulationDirector.AreaIndexOf"/> did a culture-aware,
    /// allocating string parse; and <see cref="RobotEnemy.IsWellBehindPlayer"/> repeated all of the
    /// above up to three times in a single tick. This proves the fix on the REAL, shipped World 2 map
    /// (no hand-authored fixture — the whole point is the actual entity/zone counts the field sees) —
    /// zero net behaviour change, zero per-frame allocation.
    ///
    /// (a) Ticks 100 robots (50 Dormant, 50 Chasing) for 60 frames, after a 10-frame warm-up (so every
    /// cache this ticket adds is already built before the measured window starts), and asserts the
    /// managed heap grew by exactly zero bytes across it (<see cref="AllocationAssert"/>). Robots are
    /// driven through a bound, non-boxing delegate onto the private <c>Tick</c> method — a
    /// <c>MethodInfo.Invoke</c> call in the measured window would box its own <c>float</c> argument and
    /// allocate a fresh args array every call, which would fail this test for a reason that has nothing
    /// to do with the fix. Chasing robots keep <c>target</c>/<c>_playerTarget</c> null — no
    /// "Player"-tagged object exists anywhere in this test — so <c>TickChase</c> takes its own
    /// documented null-target early return every tick (the same deterministic, minimal-surface choice
    /// <c>MV870DormantFarTickThrottleTests</c> already makes, for the same reason: pinning the measured
    /// path to code this ticket actually touches, not also to chase-steering internals it doesn't).
    ///
    /// (b) Independently re-derives <see cref="MapGeometry.SpeedMultiplierAt"/>, <see cref="MapData.ZoneAt(float, float, float)"/>
    /// and <see cref="MapData.IsOnDeck"/> at 500 grid points spanning the map (alternating floor/deck
    /// height) using a reference implementation that walks the map's raw, UNCACHED <c>zones</c>/<c>entities</c>
    /// arrays directly — bypassing every cache this ticket adds — and asserts it agrees with the real,
    /// cached methods at every point. A caching bug (a stale sentinel, a wrongly filtered deck/sludge
    /// array) would show up here as a mismatch, not merely as an allocation.
    ///
    /// Fails on 9dfa4e6 (the commit before this ticket): the managed-heap delta in (a) is non-zero (the
    /// per-frame string parsing and whole-map scans this ticket removes).
    /// </summary>
    public sealed class MV963RobotHotPathPerfTests
    {
        private GameObject _pathGo;
        private GameObject _playerStandIn;
        private readonly System.Collections.Generic.List<GameObject> _robotGos = new();

        private static readonly MethodInfo TickMethod =
            typeof(RobotEnemy).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TargetField =
            typeof(RobotEnemy).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PlayerTargetField =
            typeof(RobotEnemy).GetField("_playerTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo GravityField =
            typeof(RobotEnemy).GetField("gravity", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            foreach (GameObject go in _robotGos)
                if (go != null) Object.DestroyImmediate(go);
            _robotGos.Clear();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_playerStandIn != null) Object.DestroyImmediate(_playerStandIn);
        }

        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV963-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        /// <summary>The first authored Deck/Hatch entity's rect centre — "a deck/floor overlap", the
        /// same spot <see cref="MapData.ZoneAt(float, float, float)"/>/<see cref="MapData.DeckEntityAt"/>
        /// (private) actually branch on. Whichever World 2 area happens to carry it, rather than a
        /// hard-coded area id, so a future re-layout can't quietly stop this test from exercising the
        /// deck path at all.</summary>
        private static Vector2 FindADeckCentre(MapData map)
        {
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                EntityKind kind = MapEnums.Entity(e.kind);
                if (kind == EntityKind.Deck || kind == EntityKind.Hatch) return new Vector2(e.x, e.z);
            }
            Assert.Fail("setup failure: World 2 must author at least one Deck/Hatch entity for this test to probe");
            return Vector2.zero;
        }

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV963-robot");
            _robotGos.Add(go);
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly.

            // No floor collider exists in this test (only MapData is loaded, never MapRuntime.Build()'s
            // actual geometry) — without this, every robot free-falls forever and eventually trips
            // FallRecoveryState's own recovery path, which allocates (FallEventLog.Record's formatted
            // Debug.LogWarning line) by its own explicit design ("a fall itself is already the rare case
            // this only runs on"). Zeroing gravity keeps every robot exactly where this test placed it,
            // which is also closer to what the test actually wants to exercise (fixed floor/deck
            // positions), rather than a real, unrelated pre-existing allocation this ticket never touched.
            GravityField.SetValue(e, 0f);
            return e;
        }

        [Test]
        public void HundredRobotsOnRealWorld2Map_TickAllocationFree_AndMatchAnUncachedReference()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            InstallMap(map);

            Vector2 overlap = FindADeckCentre(map);

            _playerStandIn = new GameObject("MV963-player-stand-in");
            _playerStandIn.transform.position = new Vector3(overlap.x, map.deckHeight, overlap.y);

            // ---- (a) 100 robots (50 Dormant, 50 Chasing), scattered on a 10x10 grid around the deck,
            // alternating floor/deck height so both ZoneAt branches — and both DeckEntities()/AreaIndex
            // caches — actually get exercised, not just the floor one.
            var tickDelegates = new System.Action<float>[100];
            int index = 0;
            for (int ix = 0; ix < 10; ix++)
            {
                for (int iz = 0; iz < 10; iz++)
                {
                    bool dormant = index % 2 == 0;
                    float y = dormant ? map.deckHeight : 0f;
                    Vector3 pos = new Vector3(overlap.x + (ix - 4.5f) * 0.8f, y, overlap.y + (iz - 4.5f) * 0.8f);

                    RobotEnemy e = NewRobot(pos);
                    if (dormant)
                    {
                        e.BeginDormant();
                        TargetField.SetValue(e, _playerStandIn.transform);
                        PlayerTargetField.SetValue(e, _playerStandIn.transform);
                    }
                    // Chasing robots (the odd half) keep target/_playerTarget null on purpose — see the
                    // class doc comment.

                    tickDelegates[index] = (System.Action<float>)System.Delegate.CreateDelegate(
                        typeof(System.Action<float>), e, TickMethod);
                    index++;
                }
            }

            const float dt = 1f / 60f;

            // Warm-up: every cache this ticket adds (MapZone/MapEntity's Kind/Shape/Dressing/CoverKind,
            // MapData's deck/sludge entity arrays, MapZone.AreaIndex, RobotEnemy's per-tick
            // IsWellBehindPlayer cache) builds itself on first touch — this is deliberately OUTSIDE the
            // measured window, exactly as the ticket's own AC specifies.
            for (int frame = 0; frame < 10; frame++)
                for (int i = 0; i < tickDelegates.Length; i++)
                    tickDelegates[i](dt);

            long allocated = AllocationAssert.MeasureAllocatedBytes(() =>
            {
                for (int frame = 0; frame < 60; frame++)
                    for (int i = 0; i < tickDelegates.Length; i++)
                        tickDelegates[i](dt);
            });

            Assert.AreEqual(0L, allocated,
                $"MV-963: ticking 100 robots (50 Dormant, 50 Chasing) for 60 frames allocated {allocated} " +
                "managed bytes — the per-frame string parsing / whole-map scans this ticket removes must " +
                "leave this path allocation-free");

            // ---- (b) SpeedMultiplierAt/ZoneAt/IsOnDeck must agree with an UNCACHED reference at 500
            // points spanning the map, alternating floor/deck height.
            Rect bounds = map.Bounds();
            const int gridX = 25, gridZ = 20; // 25 * 20 = 500
            for (int gx = 0; gx < gridX; gx++)
            {
                for (int gz = 0; gz < gridZ; gz++)
                {
                    float x = bounds.xMin + bounds.width * (gx + 0.5f) / gridX;
                    float z = bounds.yMin + bounds.height * (gz + 0.5f) / gridZ;
                    float y = (gx + gz) % 2 == 0 ? 0f : map.deckHeight;
                    string at = $"(x={x:0.##}, y={y:0.##}, z={z:0.##})";

                    float actualSpeed = MapGeometry.SpeedMultiplierAt(map, x, y, z);
                    float referenceSpeed = ReferenceSpeedMultiplierAt(map, x, y, z);
                    Assert.AreEqual(referenceSpeed, actualSpeed, 0.0001f,
                        $"MV-963: SpeedMultiplierAt diverged from the uncached reference at {at}");

                    MapZone actualZone = map.ZoneAt(x, y, z);
                    MapZone referenceZone = ReferenceZoneAt(map, x, y, z);
                    Assert.AreEqual(referenceZone?.id, actualZone?.id,
                        $"MV-963: ZoneAt diverged from the uncached reference at {at}");

                    bool actualOnDeck = map.IsOnDeck(x, y, z);
                    bool referenceOnDeck = ReferenceIsOnDeck(map, x, y, z);
                    Assert.AreEqual(referenceOnDeck, actualOnDeck,
                        $"MV-963: IsOnDeck diverged from the uncached reference at {at}");
                }
            }
        }

        // ---------------------------------------------------------------- uncached reference

        /// <summary>Same rect test <c>MapData.DeckEntityAt</c> (private) uses, but walking the raw
        /// <c>entities</c> array directly and re-parsing <c>e.kind</c> through <see cref="MapEnums.Entity"/>
        /// every call — never <see cref="MapEntity.Kind"/>'s cache, never <c>MapData.DeckEntities()</c>'s
        /// cached array.</summary>
        private static bool ReferenceIsOverDeckSurface(MapData map, float px, float pz)
        {
            if (map.entities == null) return false;
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                EntityKind kind = MapEnums.Entity(e.kind);
                if (kind != EntityKind.Deck && kind != EntityKind.Hatch) continue;

                float halfW = e.width * 0.5f, halfD = e.depth * 0.5f;
                if (px >= e.x - halfW && px <= e.x + halfW && pz >= e.z - halfD && pz <= e.z + halfD)
                    return true;
            }
            return false;
        }

        private static bool ReferenceIsOnDeck(MapData map, float px, float py, float pz) =>
            py >= map.deckHeight - 0.5f && ReferenceIsOverDeckSurface(map, px, pz);

        private static MapZone ReferenceZoneAt(MapData map, float px, float py, float pz)
        {
            if (map.zones == null) return null;

            bool onDeck = py >= map.deckHeight - 0.5f;
            MapZone floorMatch = null;

            foreach (MapZone z in map.zones)
            {
                if (z == null || !z.Contains(px, pz)) continue;
                if (onDeck && z.level > 0 && ReferenceIsOverDeckSurface(map, px, pz)) return z;
                if (z.level == 0) floorMatch = z;
            }

            return floorMatch;
        }

        private static float ReferenceSpeedMultiplierAt(MapData map, float x, float y, float z)
        {
            float multiplier = 1f;
            if (map?.entities == null) return multiplier;
            if (y >= map.deckHeight - 0.5f) return multiplier;

            foreach (MapEntity e in map.entities)
            {
                if (e == null || MapEnums.Entity(e.kind) != EntityKind.Sludge) continue;
                var footprint = new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                if (footprint.Contains(new Vector2(x, z)))
                    multiplier = Mathf.Min(multiplier, e.SludgeSpeedMultiplier);
            }
            return multiplier;
        }
    }
}
