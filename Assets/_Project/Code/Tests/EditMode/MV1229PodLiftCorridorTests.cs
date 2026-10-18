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
    /// MV-1229 (the one new test, per CC_AUTONOMY's testing policy): the Reef's own finale exit becomes a
    /// real corridor into the Mothership for the first time. Built through the real
    /// <see cref="WorldJoinSequence.Initialize"/>/<see cref="WorldJoinSequence.InitializeArrival"/> entry
    /// points (<c>ClimbCorridorTests</c>' own idiom), exit side from the Reef's own world3_config, arrival
    /// side into the Mothership's own one-area placeholder config.
    ///
    /// Asserts only RESOLVED geometry (Testing Policy v2, Tier 2 -- never an authored constant re-read
    /// against itself): (a) the corridor's three floor segments run continuously from the exit door
    /// threshold to the far door threshold with no gap over 1 cm at either seam, and the arrival shell's
    /// floor likewise reaches the Mothership stub's own door threshold with no gap; (b) every
    /// collider <see cref="WorldJoinSequence"/> itself builds (corridor/arrival floor, walls, far/arrival
    /// gate) has its XZ footprint inside the in-play rect the sequence registers with
    /// <see cref="FallSafetyNet"/> (MV-1194) -- the mechanism that keeps a corridor built outside the
    /// FROM/TO world's own <c>MapData.Bounds</c> from reading as "fallen out of the world," which is this
    /// ticket's own AC1's "lies inside the map bounds."
    ///
    /// Fails to compile on base commit bb62bc0 (before this ticket): CS1061 "'WorldJoinSequence' does not
    /// contain a definition for 'InPlayFootprint'" -- that property, and the Reef's own Corridor exit
    /// into the Mothership this test exercises, don't exist yet.
    /// </summary>
    public sealed class MV1229PodLiftCorridorTests
    {
        private GameObject _camGo;
        private GameObject _exitPlayerGo;
        private GameObject _arrivalPlayerGo;
        private WorldJoinSequence _exitSequence;
        private WorldJoinSequence _arrivalSequence;

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
            if (_exitSequence != null) Object.DestroyImmediate(_exitSequence.gameObject);
            if (_arrivalSequence != null) Object.DestroyImmediate(_arrivalSequence.gameObject);
            if (_exitPlayerGo != null) Object.DestroyImmediate(_exitPlayerGo);
            if (_arrivalPlayerGo != null) Object.DestroyImmediate(_arrivalPlayerGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        private static PlayerController SpawnPlayer(out GameObject go)
        {
            go = new GameObject("Max");
            go.AddComponent<CharacterController>();
            return go.AddComponent<PlayerController>();
        }

        /// <summary>Same outward-axis convention <see cref="WorldJoinSequence"/> itself uses internally
        /// (its own private <c>AlongDistance</c>) -- every exit/arrival wall is N, E, S or W, so a world
        /// position always resolves to a signed "metres outward from the door mouth" value regardless of
        /// which wall this row's own transition cuts.</summary>
        private static float Along(Vector3 worldPos, Vector2 doorMouth, Wall wall)
        {
            Vector3 dir = wall switch
            {
                Wall.N => Vector3.forward,
                Wall.S => Vector3.back,
                Wall.E => Vector3.right,
                Wall.W => Vector3.left,
                _ => Vector3.forward,
            };
            bool travelAlongZ = dir.x == 0f;
            float raw = travelAlongZ ? worldPos.z - doorMouth.y : worldPos.x - doorMouth.x;
            return raw * (travelAlongZ ? dir.z : dir.x);
        }

        /// <summary>The renderer's own [near, far] extent in "along" units -- taking the min/max of both
        /// its bounding box's along-axis corners rather than assuming a travel direction's sign, so this
        /// reads correctly for either an exit wall (outward +X/+Z) or an arrival wall (outward -X/-Z).</summary>
        private static (float near, float far) AlongExtent(Bounds bounds, Vector2 doorMouth, Wall wall)
        {
            Vector3 dir = wall switch
            {
                Wall.N => Vector3.forward,
                Wall.S => Vector3.back,
                Wall.E => Vector3.right,
                Wall.W => Vector3.left,
                _ => Vector3.forward,
            };
            bool travelAlongZ = dir.x == 0f;
            float a = Along(travelAlongZ ? new Vector3(doorMouth.x, 0f, bounds.min.z) : new Vector3(bounds.min.x, 0f, doorMouth.y), doorMouth, wall);
            float b = Along(travelAlongZ ? new Vector3(doorMouth.x, 0f, bounds.max.z) : new Vector3(bounds.max.x, 0f, doorMouth.y), doorMouth, wall);
            return (Mathf.Min(a, b), Mathf.Max(a, b));
        }

        private static Renderer FindOwnFloor(Transform segRoot, string label) =>
            segRoot.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(r => r.transform.parent == segRoot && r.name == $"Segment {label} Floor");

        private static bool WithinFootprint(Bounds bounds, Rect footprint)
        {
            return footprint.Contains(new Vector2(bounds.min.x, bounds.min.z))
                && footprint.Contains(new Vector2(bounds.max.x, bounds.min.z))
                && footprint.Contains(new Vector2(bounds.min.x, bounds.max.z))
                && footprint.Contains(new Vector2(bounds.max.x, bounds.max.z));
        }

        [Test]
        public void ReefCorridorFloorRunsThroughBothDoorsWithEveryColliderInPlay()
        {
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);

            // ---- exit side: the Reef's own row ----
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(fromCfg, "world3_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData fromMap, out string fromReason), fromReason);

            WorldTransitionEntry entry = WorldTransitions.For(reefIndex);
            Assert.IsNotNull(entry, "the Reef must author a WorldTransitions entry into the Mothership");
            Vector2 exitDoorMouth = entry.ExitDoorMouth(fromCfg);

            PlayerController exitPlayer = SpawnPlayer(out _exitPlayerGo);
            _exitSequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _exitSequence.Initialize(fromCfg, fromMap, entry, fromWorldIndex: reefIndex, exitPlayer, () => { });

            Transform segA = _exitSequence.CorridorSegmentRoot('A');
            Transform segB = _exitSequence.CorridorSegmentRoot('B');
            Transform segC = _exitSequence.CorridorSegmentRoot('C');
            Assert.IsNotNull(segA, "segment A must have been built");
            Assert.IsNotNull(segB, "segment B must have been built");
            Assert.IsNotNull(segC, "segment C must have been built");

            Renderer floorA = FindOwnFloor(segA, "A");
            Renderer floorB = FindOwnFloor(segB, "B");
            Renderer floorC = FindOwnFloor(segC, "C");
            Assert.IsNotNull(floorA, "segment A must build its own floor renderer");
            Assert.IsNotNull(floorB, "segment B must build its own floor renderer");
            Assert.IsNotNull(floorC, "segment C must build its own floor renderer");

            const float maxGap = 0.01f;

            var (nearA, farA) = AlongExtent(floorA.bounds, exitDoorMouth, entry.ExitWall);
            Assert.AreEqual(0f, nearA, maxGap, "segment A's floor must reach the exit door threshold with no gap");

            var (nearB, farB) = AlongExtent(floorB.bounds, exitDoorMouth, entry.ExitWall);
            Assert.AreEqual(farA, nearB, maxGap, "segment A/B's floor seam must carry no gap over 1 cm");

            var (nearC, farC) = AlongExtent(floorC.bounds, exitDoorMouth, entry.ExitWall);
            Assert.AreEqual(farB, nearC, maxGap, "segment B/C's floor seam must carry no gap over 1 cm");

            Assert.GreaterOrEqual(farC, entry.CorridorLength - maxGap,
                "segment C's floor must reach the far door threshold with no gap");

            // ---- arrival side: the Mothership's own stub ----
            WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(toCfg, "mothership_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(toCfg, out MapData toMap, out string toReason), toReason);

            PlayerController arrivalPlayer = SpawnPlayer(out _arrivalPlayerGo);
            _arrivalSequence = new GameObject("WorldJoinSequence (Arrival)").AddComponent<WorldJoinSequence>();
            _arrivalSequence.InitializeArrival(toCfg, toMap, entry, fromWorldIndex: reefIndex, arrivalPlayer, () => { });

            Transform arrivalRoot = _arrivalSequence.ArrivalRoot;
            Assert.IsNotNull(arrivalRoot, "the arrival shell must have been built");

            Renderer floorArrival = FindOwnFloor(arrivalRoot, "Arrival");
            Assert.IsNotNull(floorArrival, "the arrival shell must build its own floor renderer");

            Vector2 arrivalDoorMouth = entry.ArrivalDoorMouth(toCfg);
            var (nearArrival, _) = AlongExtent(floorArrival.bounds, arrivalDoorMouth, entry.ArrivalWall);
            Assert.AreEqual(0f, nearArrival, maxGap, "the arrival shell's floor must reach the stub's own door threshold with no gap");

            // ---- AC1b: every collider either sequence itself builds stays inside its own registered
            // in-play footprint (FallSafetyNet, MV-1194) -- the mechanism that keeps geometry built
            // outside the FROM/TO world's own MapData.Bounds from reading as "fallen out of the world."
            // Physics.SyncTransforms() first: autoSyncTransforms is off project-wide (DynamicsManager.asset,
            // ClimbCorridorTests' own established note), so a freshly-positioned collider's .bounds would
            // otherwise still read its stale pre-move state.
            Physics.SyncTransforms();
            Assert.IsTrue(_exitSequence.InPlayFootprint.HasValue, "the exit sequence must register an in-play footprint");
            Rect exitFootprint = _exitSequence.InPlayFootprint.Value;
            foreach (Collider c in _exitSequence.GetComponentsInChildren<Collider>(true))
            {
                Assert.IsTrue(WithinFootprint(c.bounds, exitFootprint),
                    $"exit collider '{c.name}' bounds {c.bounds} falls outside the registered in-play footprint {exitFootprint}");
            }

            Assert.IsTrue(_arrivalSequence.InPlayFootprint.HasValue, "the arrival sequence must register an in-play footprint");
            Rect arrivalFootprint = _arrivalSequence.InPlayFootprint.Value;
            foreach (Collider c in _arrivalSequence.GetComponentsInChildren<Collider>(true))
            {
                Assert.IsTrue(WithinFootprint(c.bounds, arrivalFootprint),
                    $"arrival collider '{c.name}' bounds {c.bounds} falls outside the registered in-play footprint {arrivalFootprint}");
            }
        }
    }
}
