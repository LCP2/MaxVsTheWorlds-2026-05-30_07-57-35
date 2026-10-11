using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1126: the World 1 -> World 2 exit corridor's own garden-to-storm-drain event -- the lawn
    /// stops at a concrete kerb (d 12), a ramp descends into a pipe-mouth ring (d 18-19), and the
    /// storm-drain walkway continues to the far door (d 30). Built through the real
    /// <see cref="WorldJoinSequence.Initialize"/> entry point, same idiom MV965/MV1123 already
    /// establish for this exact corridor.
    ///
    /// Fallback branch taken (see the ticket's own escape hatch and the PR comment): the floor is NOT
    /// actually lowered 1.5 m -- the ramp is drawn as a shallow visual wedge on the existing flat floor,
    /// so AC1a's "equal" branch applies rather than its "1.5 m lower" branch.
    ///
    /// Fails on base commit c0bf6a1: no "Pipe Mouth Ring" object exists at all (AC1d), and both
    /// "Lawn Grate" and "Portal Spill" objects still exist (AC1e).
    /// </summary>
    public sealed class MV1126StormDrainCorridorTests
    {
        private GameObject _camGo;
        private GameObject _playerGo;
        private WorldJoinSequence _sequence;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_sequence != null) Object.DestroyImmediate(_sequence.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        /// <summary>Same wall-outward convention <see cref="WorldJoinSequence"/> itself uses internally
        /// (its own private PointAtXZ) -- kept deliberately separate here, same idiom every other test in
        /// this suite already uses rather than exposing it from production code.</summary>
        private static Vector3 PointAlong(Vector2 doorMouth, Wall wall, float along) => wall switch
        {
            Wall.N => new Vector3(doorMouth.x, 0f, doorMouth.y + along),
            Wall.S => new Vector3(doorMouth.x, 0f, doorMouth.y - along),
            Wall.E => new Vector3(doorMouth.x + along, 0f, doorMouth.y),
            Wall.W => new Vector3(doorMouth.x - along, 0f, doorMouth.y),
            _ => new Vector3(doorMouth.x, 0f, doorMouth.y),
        };

        private static float AlongOf(Vector3 worldPos, Vector2 doorMouth, Wall wall) => wall switch
        {
            Wall.N => worldPos.z - doorMouth.y,
            Wall.S => doorMouth.y - worldPos.z,
            Wall.E => worldPos.x - doorMouth.x,
            Wall.W => doorMouth.x - worldPos.x,
            _ => 0f,
        };

        /// <summary>Floor height directly under the corridor centreline at <paramref name="d"/>, read
        /// off whichever of this corridor's own 3 structural floor colliders (the real, resolved shell
        /// geometry) actually covers that point -- never an authored constant.</summary>
        private static float ResolvedFloorHeightAt(Transform corridorRoot, Vector2 doorMouth, Wall wall, float d)
        {
            Vector3 probe = PointAlong(doorMouth, wall, d);
            foreach (Collider c in corridorRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!c.name.EndsWith("Floor")) continue;
                Bounds b = c.bounds;
                if (probe.x >= b.min.x - 0.01f && probe.x <= b.max.x + 0.01f &&
                    probe.z >= b.min.z - 0.01f && probe.z <= b.max.z + 0.01f)
                    return b.max.y;
            }
            Assert.Fail($"no structural floor collider covers d={d}");
            return float.NaN;
        }

        private static bool ReallyOverlaps(Bounds a, Bounds b, float epsilon) =>
            a.min.x < b.max.x - epsilon && a.max.x > b.min.x + epsilon
            && a.min.y < b.max.y - epsilon && a.max.y > b.min.y + epsilon
            && a.min.z < b.max.z - epsilon && a.max.z > b.min.z + epsilon;

        [Test]
        public void GardenKerbRampAndPipeMouthResolveCorrectly()
        {
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(fromCfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            _playerGo = new GameObject("Max");
            CharacterController cc = _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            _sequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _sequence.Initialize(fromCfg, map, entry, fromWorldIndex: 0, player, () => { });
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

            Transform segA = _sequence.CorridorSegmentRoot('A');
            Transform segB = _sequence.CorridorSegmentRoot('B');
            Transform segC = _sequence.CorridorSegmentRoot('C');
            Assert.IsNotNull(segA, "segment A must have been built");
            Assert.IsNotNull(segB, "segment B must have been built");
            Assert.IsNotNull(segC, "segment C must have been built");
            Transform corridorRoot = segA.parent;
            Assert.IsNotNull(corridorRoot, "segment A must be parented under the corridor's own root");

            // ---- AC1a: floor under d=25 vs floor under d=5. The fallback (flat floor, no 1.5 m drop)
            // was built this ticket -- see the PR comment -- so these must resolve EQUAL. ----
            float floorAt5 = ResolvedFloorHeightAt(corridorRoot, doorMouth, entry.ExitWall, 5f);
            float floorAt25 = ResolvedFloorHeightAt(corridorRoot, doorMouth, entry.ExitWall, 25f);
            Assert.AreEqual(floorAt5, floorAt25, 0.05f,
                "AC1a (fallback branch): floor under d=25 must equal floor under d=5 — the PR states a flat-floor fallback was built");

            // ---- AC1b: the ramp's resolved slope is at most 20 degrees. Measured from the real built
            // "Ramp" renderer's own AABB (reflects its actual applied rotation), not an authored angle. ----
            Renderer rampRenderer = segB.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Ramp");
            Assert.IsNotNull(rampRenderer, "a 'Ramp' renderer must exist in segment B");
            Bounds rampBounds = rampRenderer.bounds;
            float alongExtent = entry.ExitWall == Wall.E || entry.ExitWall == Wall.W ? rampBounds.size.x : rampBounds.size.z;
            float measuredSlopeDeg = Mathf.Atan2(rampBounds.size.y, alongExtent) * Mathf.Rad2Deg;
            Assert.LessOrEqual(measuredSlopeDeg, 20f, $"AC1b: the ramp's measured slope {measuredSlopeDeg:0.0} deg exceeds 20 deg");

            // ---- AC1d: a ring mesh at d 18-19 with a resolved outer diameter of 5 m (+-0.3). The
            // across-axis extent is unaffected by the ring's own backward lean (that only foreshortens
            // the other two axes), so it's the honest diameter read. ----
            Renderer ringRenderer = corridorRoot.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Pipe Mouth Ring");
            Assert.IsNotNull(ringRenderer, "AC1d: a 'Pipe Mouth Ring' renderer must exist");
            float ringAlong = AlongOf(ringRenderer.bounds.center, doorMouth, entry.ExitWall);
            Assert.GreaterOrEqual(ringAlong, 18f - 0.1f, "AC1d: the ring must sit at d 18-19");
            Assert.LessOrEqual(ringAlong, 19f + 0.1f, "AC1d: the ring must sit at d 18-19");
            float ringAcrossExtent = entry.ExitWall == Wall.E || entry.ExitWall == Wall.W
                ? ringRenderer.bounds.size.z : ringRenderer.bounds.size.x;
            Assert.AreEqual(5f, ringAcrossExtent, 0.3f, $"AC1d: resolved outer diameter {ringAcrossExtent:0.00} m must be 5 m (+-0.3)");

            // ---- AC1e: no object named for the portal spill or the flat grate remains. ----
            Renderer[] allCorridorRenderers = corridorRoot.GetComponentsInChildren<Renderer>(true);
            Assert.IsFalse(allCorridorRenderers.Any(r => r.name == "Portal Spill"), "AC1e: no 'Portal Spill' object may remain");
            Assert.IsFalse(allCorridorRenderers.Any(r => r.name == "Lawn Grate"), "AC1e: no 'Lawn Grate' object may remain");
            GameObject[] allCorridorGos = corridorRoot.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray();
            Assert.IsFalse(allCorridorGos.Any(g => g.name == "Lawn Grate"), "AC1e: no 'Lawn Grate' hierarchy root may remain");

            // ---- AC1c: a body with the player's own resolved controller radius, swept along the
            // corridor's centre-line from d=1 to d=29, is never blocked and never falls. Checked against
            // the real built colliders (Tier 2 resolved), excluding Max's own CharacterController. ----
            float radius = cc.radius;
            Collider[] allColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c != null && c.enabled && !c.isTrigger && c.transform != _playerGo.transform)
                .ToArray();

            for (float d = 1f; d <= 29f; d += 0.5f)
            {
                Vector3 center = PointAlong(doorMouth, entry.ExitWall, d);

                var bodyBox = new Bounds(center + Vector3.up * 1.1f, new Vector3(radius * 2f, 1.4f, radius * 2f));
                foreach (Collider c in allColliders)
                {
                    if (ReallyOverlaps(bodyBox, c.bounds, 0.02f))
                        Assert.Fail($"AC1c: sweep blocked at d={d} by collider '{c.name}'");
                }

                var footProbe = new Bounds(center + Vector3.up * 0.02f, new Vector3(radius * 1.5f, 0.08f, radius * 1.5f));
                bool floorFound = allColliders.Any(c => c.name.EndsWith("Floor") && ReallyOverlaps(footProbe, c.bounds, -0.03f));
                Assert.IsTrue(floorFound, $"AC1c: no floor found under d={d} — the sweep would fall");
            }
        }
    }
}
