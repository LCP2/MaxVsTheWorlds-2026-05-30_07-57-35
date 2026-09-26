using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-849: the World 1 -> World 2 joining corridor reads as World 1 at its start, World 2 at its
    /// end, and the scene's lighting/fog follow Max continuously along it.
    ///
    /// MV-964 replaced the old live, per-frame property-block colour tween with three fixed, STATICALLY
    /// coloured segments (colour now changes by PLACE, not by how far Max has personally walked) — this
    /// test now reads segment B's actual built material (a resolved value: the real renderer the engine
    /// built, not a re-typed literal) instead of a property block sampled mid-tween. The
    /// lighting/fog-follows-Max half of the original assertion is unchanged in spirit and kept.
    /// </summary>
    public sealed class MV849CorridorBlendTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
        }

        [Test]
        public void TheCorridorReadsFromWorldToToWorldInPlace_AndLightingFollowsMaxAlongIt()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");
            Vector2 doorMouth = entry.ExitDoorMouth(cfg);

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            var go = new GameObject("WorldJoinSequence");
            _sequence = go.AddComponent<WorldJoinSequence>();
            _sequence.Initialize(cfg, map, entry, fromWorldIndex: 0, player, () => { });

            // --- MV-964 §5: segment B (the mid-length stretch) is a static 50/50 lerp of the two
            // worlds' own palettes, resolved once when the corridor is built, not sampled off a live
            // tween. ---
            Transform segB = _sequence.CorridorSegmentRoot('B');
            Assert.IsNotNull(segB, "segment B must have been built");
            Transform floorGo = segB.Find("Segment B Floor");
            Assert.IsNotNull(floorGo, "segment B must build its own floor");
            Renderer floorRenderer = floorGo.GetComponent<Renderer>();

            Color expectedB = Color.Lerp(
                BiomePalette.ForWorld(0).ColorFor(SurfaceKind.Ground),
                BiomePalette.ForWorld(1).ColorFor(SurfaceKind.Ground), 0.5f);
            Color resolvedB = floorRenderer.sharedMaterial.GetColor(BaseColorId);

            Assert.AreEqual(expectedB.r, resolvedB.r, 0.01f, "segment B's floor red channel is not the 50/50 lerp.");
            Assert.AreEqual(expectedB.g, resolvedB.g, 0.01f, "segment B's floor green channel is not the 50/50 lerp.");
            Assert.AreEqual(expectedB.b, resolvedB.b, 0.01f, "segment B's floor blue channel is not the 50/50 lerp.");

            // --- MV-964 §4.4: fog/lighting still follow Max continuously, reaching World 2's look at
            // 73% of the corridor's own length (unchanged from MV-849). World 1's exit wall is N, so
            // "along" is simply how far north of the door line Max has walked. ---
            float midAlong = 0.365f * entry.CorridorLength;   // t = along / (0.73 * length) = 0.5
            _playerGo.transform.position = new Vector3(doorMouth.x, 0f, doorMouth.y + midAlong);
            _sequence.Tick(0f);

            Color expectedFog = Color.Lerp(BackyardLook.ForWorld(0).FogColor, BackyardLook.ForWorld(1).FogColor, 0.5f);
            Assert.AreEqual(expectedFog.r, RenderSettings.fogColor.r, 0.01f, "fog red channel is not halfway blended.");
            Assert.AreEqual(expectedFog.g, RenderSettings.fogColor.g, 0.01f, "fog green channel is not halfway blended.");
            Assert.AreEqual(expectedFog.b, RenderSettings.fogColor.b, 0.01f, "fog blue channel is not halfway blended.");
        }
    }
}
