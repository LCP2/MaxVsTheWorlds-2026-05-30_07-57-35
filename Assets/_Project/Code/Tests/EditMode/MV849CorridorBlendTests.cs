using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-849: the World 1 -> World 2 joining corridor blends its floor/wall colours and the
    /// scene's lighting from Backyard toward Stormdrain as Max crosses it. Fails on base commit
    /// c8faa0c, which has no such blend — <c>WorldJoinSequence</c> exposes no
    /// <c>CorridorFloorRenderer</c>, and <c>TickWalkCorridor</c> never touches
    /// <see cref="BiomePalette"/>/<see cref="BackyardLook"/> at all, so the corridor's floor keeps
    /// its flat World 1 colour and <c>RenderSettings.fogColor</c> never moves off whatever the
    /// active <see cref="BackyardLighting"/> last set.
    /// </summary>
    public sealed class MV849CorridorBlendTests
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
        }

        [Test]
        public void AtTheCorridorMidpointTheFloorAndFogAreHalfwayToStormdrain()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            WorldArea a30 = cfg.Area("a30");
            Assert.IsNotNull(a30, "World 1 must author 'a30' — the boss arena this sequence exits from.");
            float doorZ = a30.WallSpan(Wall.E).Mid;

            _playerGo = new GameObject("Max");
            _playerGo.transform.position = new Vector3(a30.CenterXz.x, 0f, a30.CenterXz.y);
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            var go = new GameObject("WorldJoinSequence");
            _sequence = go.AddComponent<WorldJoinSequence>();
            _sequence.Initialize(cfg, map, player, () => { });

            // Drive the walk-to-door leg past its own timeout in one step, so the sequence lands in
            // the corridor without depending on this test re-deriving StepToward's own arithmetic.
            _sequence.Tick(100f);
            Assert.IsTrue(_sequence.IsPlaying, "the sequence finished before ever reaching the corridor.");

            // MV-849: t = clamp01((x - 340) / 22) — x = 351 is exactly the ticket's own halfway point.
            _playerGo.transform.position = new Vector3(351f, _playerGo.transform.position.y, doorZ);
            _sequence.Tick(0f);

            Renderer floor = _sequence.CorridorFloorRenderer;
            Assert.IsNotNull(floor, "the corridor's floor was never built.");

            var mpb = new MaterialPropertyBlock();
            floor.GetPropertyBlock(mpb);
            Color resolved = mpb.GetColor("_BaseColor");

            Color expectedFloor = Color.Lerp(
                BiomePalette.Backyard.ColorFor(SurfaceKind.Ground),
                BiomePalette.Stormdrain.ColorFor(SurfaceKind.Ground), 0.5f);

            Assert.AreEqual(expectedFloor.r, resolved.r, 0.01f, "floor red channel is not halfway blended.");
            Assert.AreEqual(expectedFloor.g, resolved.g, 0.01f, "floor green channel is not halfway blended.");
            Assert.AreEqual(expectedFloor.b, resolved.b, 0.01f, "floor blue channel is not halfway blended.");

            Color expectedFog = Color.Lerp(BackyardLook.Default.FogColor, BackyardLook.Stormdrain.FogColor, 0.5f);
            Assert.AreEqual(expectedFog.r, RenderSettings.fogColor.r, 0.01f, "fog red channel is not halfway blended.");
            Assert.AreEqual(expectedFog.g, RenderSettings.fogColor.g, 0.01f, "fog green channel is not halfway blended.");
            Assert.AreEqual(expectedFog.b, RenderSettings.fogColor.b, 0.01f, "fog blue channel is not halfway blended.");
        }
    }
}
