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
    /// MV-967: the World 2 -> World 3 join corridor MV-964 built as three flat-coloured shells now
    /// carries real set dressing (outfall/breach/hull) parented onto the segment roots MV-964 exposes.
    /// Built on the real World 2/3 configs through <see cref="WorldJoinSequence.Initialize"/>, the same
    /// test-facing entry point <see cref="MV965CorridorDressingTests"/> already uses for the World 1 -> 2
    /// row.
    ///
    /// Fail-first: run against MV-964's bare shell (WorldJoinDressing.DressExitReef/DressArrivalReef not
    /// yet wired into WorldJoinSequence.BuildExitCorridor/BuildArrivalShell) — AC1a failed on slice
    /// [0, 3) with "no dressing renderer found outside the centre lane in slice [0, 3)", and AC1c failed
    /// on segment C's own first 4 m window with "no emissive renderer found in window [16, 20)". Both are
    /// exactly what the bare shell has none of (its only children are the floor and two walls per
    /// segment, all inside or straddling the centre lane, none emissive).
    /// </summary>
    public sealed class MV967CorridorDressingTests
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

        [Test]
        public void CorridorAndArrivalShellCarryDressing_DensityColliderFreedomAndEmission()
        {
            // ---- build the exit side (World 2's own row into World 3) ----
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(fromCfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData fromMap, out string fromReason), fromReason);

            WorldTransitionEntry entry = WorldTransitions.For(1);
            Assert.IsNotNull(entry, "World 2 must author a WorldTransitions entry into World 3.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            PlayerController exitPlayer = SpawnPlayer(out _exitPlayerGo);
            _exitSequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _exitSequence.Initialize(fromCfg, fromMap, entry, fromWorldIndex: 1, exitPlayer, () => { });

            Transform segA = _exitSequence.CorridorSegmentRoot('A');
            Transform segB = _exitSequence.CorridorSegmentRoot('B');
            Transform segC = _exitSequence.CorridorSegmentRoot('C');
            Assert.IsNotNull(segA, "segment A must have been built");
            Assert.IsNotNull(segB, "segment B must have been built");
            Assert.IsNotNull(segC, "segment C must have been built");

            // ---- build the arrival side (World 3's own stub) ----
            WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(toCfg, "world3_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(toCfg, out MapData toMap, out string toReason), toReason);

            PlayerController arrivalPlayer = SpawnPlayer(out _arrivalPlayerGo);
            _arrivalSequence = new GameObject("WorldJoinSequence (Arrival)").AddComponent<WorldJoinSequence>();
            _arrivalSequence.InitializeArrival(toCfg, toMap, entry, fromWorldIndex: 1, arrivalPlayer, () => { });

            Transform arrivalRoot = _arrivalSequence.ArrivalRoot;
            Assert.IsNotNull(arrivalRoot, "the arrival shell must have been built");

            // ---- AC1a: every one of the ten 3 m slices of the exit corridor holds a dressing renderer
            // outside the 2.0 m centre lane. World 2's exit wall is E, so "along" is world X past the
            // door line and "across" is world Z off the door's own centreline. ----
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
                    float along = c.x - doorMouth.x;
                    float across = c.z - doorMouth.y;
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
                    float dMin = r.bounds.min.x - doorMouth.x;
                    float dMax = r.bounds.max.x - doorMouth.x;
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
