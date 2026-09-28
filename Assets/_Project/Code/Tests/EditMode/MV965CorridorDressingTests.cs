using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-965: the World 1 -> World 2 join corridor MV-964 built as three flat-coloured shells now
    /// carries real set dressing (garden/kerb+grate/culvert) parented onto the segment roots MV-964
    /// exposed. Built on the real World 1/2 configs through <see cref="WorldJoinSequence.Initialize"/>,
    /// the same test-facing entry point <c>MV849CorridorBlendTests</c> already uses.
    ///
    /// Fail-first: run against MV-964's bare shell (WorldJoinDressing.DressExit/DressArrival not yet
    /// wired into WorldJoinSequence.BuildExitCorridor/BuildArrivalShell) — AC1a failed on slice [0,3)
    /// with "no dressing renderer found outside the centre lane in slice [0, 3)", and AC1c failed on
    /// segment C's own first 4 m window with "no emissive renderer found in window [13, 17)". Both are
    /// exactly what the bare shell has none of (its only children are the floor and two walls per
    /// segment, all inside or straddling the centre lane, none emissive).
    /// </summary>
    public sealed class MV965CorridorDressingTests
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

        /// <summary>How far outward <paramref name="worldPos"/> sits from <paramref name="doorMouth"/>
        /// along <paramref name="wall"/>'s own outward axis — same wall-outward math
        /// <see cref="WorldJoinSequence"/> itself uses internally, kept deliberately separate here rather
        /// than exposed from production code. Written generic over the wall (MV-997: World 1's exit wall
        /// moved from N to E) so this test never has to hardcode a compass axis again.</summary>
        private static float AlongOf(Vector3 worldPos, Vector2 doorMouth, Wall wall) => wall switch
        {
            Wall.N => worldPos.z - doorMouth.y,
            Wall.S => doorMouth.y - worldPos.z,
            Wall.E => worldPos.x - doorMouth.x,
            Wall.W => doorMouth.x - worldPos.x,
            _ => 0f,
        };

        /// <summary>How far <paramref name="worldPos"/> sits off the door's own centreline, across
        /// <paramref name="wall"/>'s outward axis. Only ever compared by magnitude in this file, so the
        /// sign convention just needs to be consistent, not camera-relative.</summary>
        private static float AcrossOf(Vector3 worldPos, Vector2 doorMouth, Wall wall) => wall switch
        {
            Wall.N => worldPos.x - doorMouth.x,
            Wall.S => worldPos.x - doorMouth.x,
            Wall.E => worldPos.z - doorMouth.y,
            Wall.W => worldPos.z - doorMouth.y,
            _ => 0f,
        };

        /// <summary>The [min, max) span of <paramref name="bounds"/>'s own outward extent along
        /// <paramref name="wall"/>'s axis, in the same "metres outward from the door mouth" terms
        /// <see cref="AlongOf"/> uses for a single point.</summary>
        private static (float min, float max) AlongSpan(Bounds bounds, Vector2 doorMouth, Wall wall) => wall switch
        {
            Wall.N => (bounds.min.z - doorMouth.y, bounds.max.z - doorMouth.y),
            Wall.S => (doorMouth.y - bounds.max.z, doorMouth.y - bounds.min.z),
            Wall.E => (bounds.min.x - doorMouth.x, bounds.max.x - doorMouth.x),
            Wall.W => (doorMouth.x - bounds.max.x, doorMouth.x - bounds.min.x),
            _ => (0f, 0f),
        };

        [Test]
        public void CorridorAndArrivalShellCarryDressing_DensityColliderFreedomAndEmission()
        {
            // ---- build the exit side (World 1's own row into World 2) ----
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(fromCfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData fromMap, out string fromReason), fromReason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            PlayerController exitPlayer = SpawnPlayer(out _exitPlayerGo);
            _exitSequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _exitSequence.Initialize(fromCfg, fromMap, entry, fromWorldIndex: 0, exitPlayer, () => { });

            Transform segA = _exitSequence.CorridorSegmentRoot('A');
            Transform segB = _exitSequence.CorridorSegmentRoot('B');
            Transform segC = _exitSequence.CorridorSegmentRoot('C');
            Assert.IsNotNull(segA, "segment A must have been built");
            Assert.IsNotNull(segB, "segment B must have been built");
            Assert.IsNotNull(segC, "segment C must have been built");

            // ---- build the arrival side (World 2's own stub) ----
            WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(toCfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(toCfg, out MapData toMap, out string toReason), toReason);

            PlayerController arrivalPlayer = SpawnPlayer(out _arrivalPlayerGo);
            _arrivalSequence = new GameObject("WorldJoinSequence (Arrival)").AddComponent<WorldJoinSequence>();
            _arrivalSequence.InitializeArrival(toCfg, toMap, entry, fromWorldIndex: 0, arrivalPlayer, () => { });

            Transform arrivalRoot = _arrivalSequence.ArrivalRoot;
            Assert.IsNotNull(arrivalRoot, "the arrival shell must have been built");

            // ---- AC1a: every one of the ten 3 m slices of the exit corridor holds a dressing renderer
            // outside the 2.0 m centre lane. ----
            var corridorRenderers = new List<Renderer>();
            corridorRenderers.AddRange(segA.GetComponentsInChildren<Renderer>(true));
            corridorRenderers.AddRange(segB.GetComponentsInChildren<Renderer>(true));
            corridorRenderers.AddRange(segC.GetComponentsInChildren<Renderer>(true));

            for (int slice = 0; slice < 10; slice++)
            {
                float sliceMin = slice * 3f;
                float sliceMax = sliceMin + 3f;
                bool found = false;
                foreach (Renderer r in corridorRenderers)
                {
                    Vector3 c = r.bounds.center;
                    float along = AlongOf(c, doorMouth, entry.ExitWall);
                    float across = AcrossOf(c, doorMouth, entry.ExitWall);
                    if (along >= sliceMin && along < sliceMax && Mathf.Abs(across) >= 1.0f) { found = true; break; }
                }
                Assert.IsTrue(found, $"no dressing renderer found outside the centre lane in slice [{sliceMin}, {sliceMax})");
            }

            // ---- AC1b: no collider besides each segment's own floor + two walls sits under a corridor
            // segment root — dressing is scenery, full stop. (The end cap gate is a sibling of these
            // roots, not a child, so it never enters this count.) ----
            foreach (var (label, seg) in new[] { ("A", segA), ("B", segB), ("C", segC) })
            {
                var colliders = seg.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(3, colliders.Length,
                    $"segment {label} must carry exactly its own floor + two wall colliders, no dressing colliders — found {colliders.Length}");
            }

            // ---- AC1c: inside segment C, every 4 m window holds at least one emissive renderer. ----
            var segCRenderers = segC.GetComponentsInChildren<Renderer>(true);
            for (float w0 = entry.SegmentBEnd; w0 < entry.CorridorLength; w0 += 4f)
            {
                float w1 = Mathf.Min(w0 + 4f, entry.CorridorLength);
                bool found = false;
                foreach (Renderer r in segCRenderers)
                {
                    Material mat = r.sharedMaterial;
                    if (mat == null || !mat.IsKeywordEnabled("_EMISSION")) continue;
                    (float dMin, float dMax) = AlongSpan(r.bounds, doorMouth, entry.ExitWall);
                    if (dMin < w1 && dMax > w0) { found = true; break; }
                }
                Assert.IsTrue(found, $"no emissive renderer found in window [{w0}, {w1})");
            }

            // ---- AC1d: total renderers across the corridor and arrival roots stay under budget. ----
            int total = corridorRenderers.Count + arrivalRoot.GetComponentsInChildren<Renderer>(true).Length;
            Assert.LessOrEqual(total, 150, $"corridor + arrival renderer count {total} exceeds the 150 budget");
        }
    }
}
