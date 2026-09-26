using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-845: the World 1 -> World 2 joining sequence. MV-964 generalised it off a
    /// <see cref="WorldTransitionEntry"/> instead of a hardcoded World 1 door/world-x table, and moved
    /// the "walk to the door" leg out of this class entirely (it's ordinary, un-scripted gameplay now —
    /// <see cref="WorldJoinSequence.Initialize"/> represents the moment Max has already crossed the
    /// threshold). This test now drives straight from that moment to the sequence's own end.
    ///
    /// Built via <see cref="WorldJoinSequence.Initialize"/> rather than <c>AddComponent</c> + a frame
    /// wait: a MonoBehaviour without <c>[ExecuteAlways]</c> only receives <c>Awake</c> once Unity is
    /// actually in Play Mode, which this EditMode suite never enters (MV-299/311/330).
    /// </summary>
    public sealed class MV845WorldJoinSequenceTests
    {
        private GameObject _camGo;
        private GameObject _playerGo;
        private WorldJoinSequence _sequence;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
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
        }

        /// <summary>Same wall-outward math <see cref="WorldJoinSequence"/> itself uses internally, kept
        /// deliberately separate here rather than exposed from production code — a30's exit wall is N,
        /// so this resolves to "how far north of the door line".</summary>
        private static float AlongDistance(Vector3 pos, Vector2 doorMouth, Wall wall) => wall switch
        {
            Wall.N => pos.z - doorMouth.y,
            Wall.S => doorMouth.y - pos.z,
            Wall.E => pos.x - doorMouth.x,
            Wall.W => doorMouth.x - pos.x,
            _ => 0f,
        };

        [Test]
        public void MaxReachesTheCorridorEndBeforeTheSequenceFinishes()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");

            WorldArea a30 = entry.ExitArea(cfg);
            Assert.IsNotNull(a30, "World 1 must author 'a30' — the boss arena this sequence exits from.");

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            bool finished = false;
            var go = new GameObject("WorldJoinSequence");
            _sequence = go.AddComponent<WorldJoinSequence>();
            _sequence.Initialize(cfg, map, entry, fromWorldIndex: 0, player, () => finished = true);

            Vector2 doorMouth = entry.ExitDoorMouth(cfg);
            float finalAlong = float.NaN;

            // Walked in fixed 0.05 s steps well past any plausible completion time (100 s of simulated
            // motion) so a regression reads as a real failure, not a test that just didn't tick long
            // enough.
            for (int i = 0; i < 2000 && !finished; i++)
            {
                _sequence.Tick(0.05f);
                finalAlong = AlongDistance(player.transform.position, doorMouth, entry.ExitWall);
            }

            Assert.IsTrue(finished, "the sequence must reach its own end within 100 s of simulated walking.");
            Assert.AreEqual(entry.CorridorLength - 3f, finalAlong, 0.5f,
                "Max must be resting 'corridor length - 3 m' outward from the door when the sequence ends.");
        }
    }
}
