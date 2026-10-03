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
    /// MV-1076: Lee, v0.11.5 on his phone, walking the World 1 -> World 2 corridor — from about halfway
    /// along, everything outside the corridor walls was featureless black/void, because
    /// <see cref="WorldJoinSequence.BuildExitCorridor"/>'s three segments were only ever a floor and two
    /// walls, with nothing built beside or past them.
    ///
    /// Fail-first (base b04b39b, current HEAD still matches that diagnosis): a probe point 5 m either
    /// side of the corridor's own centre line, at its far end, found no renderer at all — see the fix
    /// comment for the quoted failure output.
    /// </summary>
    public sealed class MV1076GroundApronTests
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

        /// <summary>Same wall-outward math <see cref="WorldJoinSequence"/> itself uses internally —
        /// kept deliberately separate here rather than exposed from production code, same idiom every
        /// other corridor test suite already uses.</summary>
        private static Vector3 OutwardDir(Wall wall) => wall switch
        {
            Wall.N => Vector3.forward,
            Wall.S => Vector3.back,
            Wall.E => Vector3.right,
            Wall.W => Vector3.left,
            _ => Vector3.forward,
        };

        private static Vector3 AcrossDir(Wall wall)
        {
            Vector3 d = OutwardDir(wall);
            return new Vector3(-d.z, 0f, d.x);
        }

        [Test]
        public void GroundApronCoversBothSidesOfTheCorridorAtItsFarEnd()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            var go = new GameObject("WorldJoinSequence");
            _sequence = go.AddComponent<WorldJoinSequence>();
            _sequence.Initialize(cfg, map, entry, fromWorldIndex: 0, player, null);

            Vector2 doorMouth = entry.ExitDoorMouth(cfg);
            Vector3 farCentre = new Vector3(doorMouth.x, -0.02f, doorMouth.y)
                                + OutwardDir(entry.ExitWall) * entry.CorridorLength;
            Vector3 across = AcrossDir(entry.ExitWall);

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 probe = farCentre + across * (5f * side);
                bool covered = renderers.Any(r => r.enabled && r.bounds.Contains(probe));
                Assert.IsTrue(covered,
                    $"no ground-apron renderer found at {probe} (5 m {(side > 0 ? "east/north" : "west/south")} " +
                    "of the corridor's centre line at its far end) — the corridor's own walls stop here and " +
                    "there must be ground to stand on beside them, not void.");
            }
        }
    }
}
