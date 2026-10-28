using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1194 (the one new test, per CC_AUTONOMY's testing policy): Lee, device, 15 Oct (City, World 3)
    /// walked through an open exit door and was dumped back inside the previous area against a door that
    /// then read locked, stuck. Root cause: <see cref="WorldJoinSequence.BeginCorridorWalk"/> recloses the
    /// exit door and lets Max walk the corridor under his own control (MV-1123), but the corridor is built
    /// outside <see cref="MapData.Bounds"/> — <see cref="FallSafetyNet.IsOutOfPlay"/> read every step of
    /// it as fallen out of the world, and <see cref="FallRecoveryState"/> teleported him back after half a
    /// second with the door already shut behind him and the sequence stuck mid-corridor.
    ///
    /// Fails on base commit 270ccbb: <see cref="FallSafetyNet"/> carries no
    /// <c>RegisterInPlayArea</c>/<c>UnregisterInPlayArea</c> (CS1061, so this test cannot even compile
    /// against it, let alone pass) and a position 5 m into the corridor reads out of play, so
    /// <see cref="FallRecoveryState.Tick"/> fires and moves Max off the fixture's seeded position.
    /// </summary>
    public sealed class MV1194CorridorFallRecoveryTests
    {
        private GameObject _playerGo;
        private GameObject _gateGo;
        private WorldJoinSequence _sequence;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sequence != null) Object.DestroyImmediate(_sequence.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_gateGo != null)
            {
                var gate = _gateGo.GetComponent<AreaGate>();
                if (gate != null) InvokeOnDestroy(gate);
                Object.DestroyImmediate(_gateGo);
            }
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        /// <summary>MV-1317: DestroyImmediate does not reliably fire OnDestroy synchronously under
        /// -batchmode -runTests for a component whose Awake only ran via reflection (same quirk
        /// AreaGateTests' own InvokeOnDestroy note names) — without this, AreaGate.OnDestroy's cleanup
        /// of its SIBLING threshold collider never runs. This test's exit gate sits on a root-level
        /// GameObject.CreatePrimitive cube, so the leaked threshold would sit at the gate's own world
        /// position for a later test in the same EditMode run to collide with.</summary>
        private static void InvokeOnDestroy(Object component) =>
            component.GetType().GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        /// <summary>Same wall-outward math <see cref="WorldJoinSequence"/> itself uses internally, kept
        /// deliberately separate here rather than exposed from production code (MV849CorridorBlendTests'
        /// own copy of this helper).</summary>
        private static Vector3 PointAlong(Vector2 doorMouth, Wall wall, float along) => wall switch
        {
            Wall.N => new Vector3(doorMouth.x, 0f, doorMouth.y + along),
            Wall.S => new Vector3(doorMouth.x, 0f, doorMouth.y - along),
            Wall.E => new Vector3(doorMouth.x + along, 0f, doorMouth.y),
            Wall.W => new Vector3(doorMouth.x - along, 0f, doorMouth.y),
            _ => new Vector3(doorMouth.x, 0f, doorMouth.y),
        };

        [Test]
        public void FallRecoveryNeverFiresInCorridor_AndRecoveryBehindDoorReopensIt()
        {
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(fromCfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            // A real, damageable exit gate (same idiom MV997WorldExitDoorTests/MV1123WorldJoinCorridorTests
            // use — AddComponent never fires Awake as a side effect inside this project's synchronous
            // EditMode harness, so _health stays null and ForceOpen()/Reclose() silently no-op without it).
            _gateGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _gateGo.name = "MV1194 Exit Gate";
            AreaGate exitGate = _gateGo.AddComponent<AreaGate>();
            InvokeAwake(exitGate);

            _sequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _sequence.Initialize(fromCfg, map, entry, fromWorldIndex: 0, player, () => { }, exitGate);
            Assert.IsTrue(_sequence.IsInCorridorWalk, "Initialize must land the sequence already walking the corridor");

            // 5 m into the corridor -- outside World 1's own zone bounds, which never reach past its exit
            // wall (the ticket's own fixture: "places Max 5 m into the corridor, outside MapData.Bounds()").
            Vector3 corridorPos = PointAlong(doorMouth, entry.ExitWall, 5f);
            Assert.IsFalse(map.Bounds().Contains(new Vector2(corridorPos.x, corridorPos.z)),
                "fixture assumption: 5 m into the corridor must sit outside the map's own zone bounds");

            // Seeded with the door mouth itself -- a real "last safe position" distinct from corridorPos,
            // so a wrongly-firing recovery actually moves Max somewhere this test can detect.
            var fallRecovery = new FallRecoveryState(new Vector3(doorMouth.x, 0f, doorMouth.y));
            player.transform.position = corridorPos;

            Vector3? recovered = null;
            const float dt = 0.25f;
            for (float elapsed = 0f; elapsed < 2f; elapsed += dt)
            {
                recovered = fallRecovery.Tick(player.transform.position, map, grounded: true, dt);
                if (recovered.HasValue) player.transform.position = recovered.Value;
            }

            Assert.IsNull(recovered, "fall recovery must never fire for a position inside the corridor's registered in-play footprint");
            Assert.AreEqual(corridorPos, player.transform.position, "Max must not have been moved out of the corridor");
            Assert.IsTrue(_sequence.IsInCorridorWalk, "the sequence must still be walking the corridor");

            // Belt and braces (AC2): Max somehow ends up back on the from-side of the exit door while the
            // sequence still thinks he's mid-corridor -- one tick must reopen the door and hand the
            // sequence back to AwaitingCrossing so he can simply walk through again.
            player.transform.position = PointAlong(doorMouth, entry.ExitWall, -1f);
            _sequence.Tick(0.01f);

            Assert.IsTrue(exitGate.IsOpen, "the exit door must reopen once Max is back on the from-side of it");
            Assert.IsTrue(_sequence.IsAwaitingCrossing, "the sequence must return to AwaitingCrossing so Max can simply walk through again");
        }
    }
}
