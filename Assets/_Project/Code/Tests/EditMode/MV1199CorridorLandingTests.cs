using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1199 (the one new test, testing policy Rule 1): Lee, TestFlight, 16 Oct — the World 2 -> City
    /// corridor "ends in mid air and I just fall off and keep bouncing back up to the end of it", so the
    /// run can never reach the City. Root cause: the old fade trigger
    /// (<c>alongFromExit &gt;= CorridorLength + FarDoorCrossClearance</c>, 2 m PAST the far door) asked
    /// Max to stand somewhere segment C's own floor (<see cref="WorldJoinSequence"/>'s
    /// <c>BuildExitCorridor</c>) never reached — it stopped dead at <c>CorridorLength</c>, exactly at the
    /// door. He fell, <see cref="MaxWorlds.Arena.FallRecoveryState"/> put him back at the corridor's end
    /// after half a second, and the fade never started.
    ///
    /// Built through the real <see cref="WorldJoinSequence.Initialize"/> entry point (the same idiom
    /// MV965/ClimbCorridorTests already establish) off World 2's real <see cref="WorldTransitionEntry"/>
    /// row (the climb corridor into the City).
    ///
    /// Fails on base commit f12b316: (a) a downward raycast at d 31.0/31.5/32.0/32.5 — all past
    /// <c>CorridorLength</c> (30) but within the new 3 m landing — hits nothing, since segment C's floor
    /// never extended past the door; (b) with Max placed at <c>CorridorLength - 0.4</c> (29.6, short of
    /// the old trigger's 32), one tick leaves the sequence still in <see cref="WorldJoinSequence.IsInCorridorWalk"/>
    /// instead of having started the fade.
    /// </summary>
    public sealed class MV1199CorridorLandingTests
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

            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();

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

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        /// <summary>Same wall-outward math <see cref="WorldJoinSequence"/> itself uses internally, kept
        /// deliberately separate here rather than exposed from production code (MV1194CorridorFallRecoveryTests'
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
        public void CorridorFloorReachesPastTheFarDoor_AndTheFadeStartsBeforeItInstead()
        {
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);

            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(fromCfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData fromMap, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(stormdrainIndex);
            Assert.IsNotNull(entry, "World 2 must author a WorldTransitions entry into the City.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            _sequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _sequence.Initialize(fromCfg, fromMap, entry, fromWorldIndex: stormdrainIndex, player, () => { });
            Assert.IsTrue(_sequence.IsInCorridorWalk, "Initialize must land the sequence already walking the corridor");

            // Open the far gate (it only opens on its own once Max is within 6 m, via Tick) and disable
            // its own collider for this measurement -- AreaGate deliberately keeps the swinging leaf
            // solid (MV-386) rather than disabling it, so without this a downward raycast at the door
            // line would hit whatever pose the leaf's swing (never ticked in this synchronous EditMode
            // harness) left it in, not the floor this test is actually measuring.
            AreaGate farGate = _sequence.FarGate;
            Assert.IsNotNull(farGate, "the exit corridor must build its own far gate");
            farGate.ForceOpen();
            Collider farGateCollider = farGate.GetComponent<Collider>();
            if (farGateCollider != null) farGateCollider.enabled = false;

            // Max himself stands on the centre line near the corridor's start (Initialize lands him there,
            // mid-CorridorWalk), and his own CharacterController is a capsule collider that a downward
            // raycast hits before it ever reaches the floor underneath. Disable it for the AC1a sweep --
            // AC1b below only reads/writes transform.position and never relies on this collider.
            CharacterController playerCapsule = player.GetComponent<CharacterController>();
            playerCapsule.enabled = false;

            // ---- AC1a: every 0.5 m step from 0 to CorridorLength + 2.5, a downward raycast on the
            // centre line must hit the real floor within 0.2 m of the corridor's own floor height (0,
            // confirmed at y 0 within 1 cm the whole corridor long by ClimbCorridorTests — the dressing
            // that draws the climb carries no collider of its own, MV-1178). ----
            Physics.SyncTransforms();
            float endD = entry.CorridorLength + 2.5f;
            int steps = Mathf.RoundToInt(endD / 0.5f);
            for (int i = 0; i <= steps; i++)
            {
                float d = i * 0.5f;
                Vector3 origin = PointAlong(doorMouth, entry.ExitWall, d) + Vector3.up * 5f;
                Assert.IsTrue(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f),
                    $"a downward raycast at d {d:F1} on the centre line hit nothing — the corridor floor doesn't reach here");
                Assert.AreEqual(0f, hit.point.y, 0.2f,
                    $"a downward raycast at d {d:F1} hit '{hit.transform.name}' at y {hit.point.y:F3}, not the expected corridor floor height");
            }

            // ---- AC1b: placed just short of the far door (CorridorLength - 0.4), one tick must have
            // started the fade (left Phase.CorridorWalk) — not require walking on past the door first. ----
            player.transform.position = PointAlong(doorMouth, entry.ExitWall, entry.CorridorLength - 0.4f);
            _sequence.Tick(0.016f);

            Assert.IsFalse(_sequence.IsInCorridorWalk,
                "one tick with Max short of the far door must have started the fade, not left him walking the corridor");
        }
    }
}
