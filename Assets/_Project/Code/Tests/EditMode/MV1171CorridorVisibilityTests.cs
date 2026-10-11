using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1171 (the one new test, testing policy Rule 1): the World 2 -> World 3 exit corridor, built
    /// through the real <see cref="WorldJoinSequence.Initialize"/> entry point (same idiom
    /// MV845/1123 already establish), with Max placed at the corridor's own midpoint.
    ///
    /// Fails on base commit 234dca4:
    /// <code>
    /// 'Ocean_Manta' measures (15.00, 5.40, 0.00) -- it inherited its parent window's own non-uniform scale instead of its authored 6 m width
    /// Expected: less than or equal to 8.0f
    /// But was:  15.0f
    /// </code>
    /// </summary>
    public sealed class MV1171CorridorVisibilityTests
    {
        private GameObject _camGo;
        private GameObject _playerGo;
        private GameObject _rigGo;
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
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        [Test]
        public void MidCorridorMaxIsOnFloorAndUnobstructed()
        {
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(fromCfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(1);
            Assert.IsNotNull(entry, "World 2 must author a WorldTransitions entry into World 3.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            _sequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _sequence.Initialize(fromCfg, map, entry, fromWorldIndex: 1, player, () => { });

            // World 2's own exit wall is E (WorldCatalog), so the corridor travels +X.
            float mid = entry.CorridorLength * 0.5f;
            Vector3 feet = new Vector3(doorMouth.x + mid, 0f, doorMouth.y);
            player.transform.position = feet;

            // ---- AC1a: Max's feet are within 0.1 m of the corridor floor's top surface. ----
            Transform segB = _sequence.CorridorSegmentRoot('B');
            Assert.IsNotNull(segB, "segment B must have been built — the corridor's own midpoint sits in it");
            Transform floorB = segB.Find("Segment B Floor");
            Assert.IsNotNull(floorB, "segment B must build its own floor");
            float floorTopY = floorB.GetComponent<Renderer>().bounds.max.y;
            Assert.AreEqual(floorTopY, feet.y, 0.1f,
                "Max's feet must sit within 0.1 m of the corridor floor's top surface");

            // ---- AC1c: the camera keeps the normal play pitch (60 deg, MV-468's committed framing) —
            // nothing about a corridor walk may carry a bespoke angle. ----
            _rigGo = new GameObject("FixedAngleCameraRig");
            var rig = _rigGo.AddComponent<FixedAngleCameraRig>();
            Assert.AreEqual(60f, rig.Pitch, 0.01f, "the corridor must use the normal play pitch");

            // ---- AC1b: no collider or renderer other than the corridor's own walls may hang over the
            // corridor far past its own authored footprint -- the real defect this clause exists to
            // catch. WorldJoinDressing.DressObservationWindows parents ReefKit.BuildOceanBackdrop
            // directly onto the observation window's own primitive-cube transform, which carries that
            // window's own non-uniform (2.5, height, 0.1) local scale (ReefKit.BuildObservationWindow).
            // Every "Ocean_*" backdrop quad is authored at a flat local scale of (6, 3, 1) against an
            // UNSCALED parent (MV713ReefKitTests' own fixture, and ReefKit.BuildOceanBackdrop's own doc
            // comment: "four small per-window parallax layers") -- inheriting the window's scale instead
            // blows each layer up to world-space (15, ~5.4, 0.1) at World 2's own wallHeight (3 m), and
            // lifts it 1.5 m x the window's own Y-scale above the window itself: the cyan slab Lee saw
            // "covering the top third to half of the screen."
            Renderer[] oceanLayers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r != null && r.enabled && r.name.StartsWith("Ocean_")).ToArray();
            Assert.IsNotEmpty(oceanLayers, "the hull segment's observation windows must build their own ocean backdrop");

            const float maxLayerWidth = 8f;    // > the authored 6 m width, well short of the bugged ~15 m
            const float maxLayerHeight = 5f;   // > the authored 3 m height, well short of the bugged ~5.4-9 m
            foreach (Renderer r in oceanLayers)
            {
                Vector3 size = r.bounds.size;
                Assert.LessOrEqual(size.x, maxLayerWidth,
                    $"'{r.name}' measures {size} -- it inherited its parent window's own non-uniform scale instead of its authored 6 m width");
                Assert.LessOrEqual(size.y, maxLayerHeight,
                    $"'{r.name}' measures {size} -- it inherited its parent window's own non-uniform scale instead of its authored 3 m height");
            }
        }
    }
}
