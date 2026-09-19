using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-845: the World 1 -> World 2 joining sequence. Fails on base commit 10fce02, which has no
    /// <c>WorldJoinSequence</c> to build against.
    ///
    /// Built via <see cref="WorldJoinSequence.Initialize"/> rather than <c>AddComponent</c> + a frame
    /// wait, same reason as <see cref="MV704WorldTransitionCinematicTests"/>: a MonoBehaviour without
    /// <c>[ExecuteAlways]</c> only receives <c>Awake</c> once Unity is actually in Play Mode, which this
    /// EditMode suite never enters (MV-299/311/330).
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

        [Test]
        public void MaxReachesTheCorridorBeforeTheSequenceRequestsTheWorldLoad()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            WorldArea a30 = cfg.Area("a30");
            Assert.IsNotNull(a30, "World 1 must author 'a30' — the boss arena this sequence exits from.");

            // AC1: "Max anywhere inside a30" — its centre is as good a stand-in as any.
            _playerGo = new GameObject("Max");
            _playerGo.transform.position = new Vector3(a30.CenterXz.x, 0f, a30.CenterXz.y);
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            bool finished = false;
            var go = new GameObject("WorldJoinSequence");
            _sequence = go.AddComponent<WorldJoinSequence>();
            _sequence.Initialize(cfg, map, player, () => finished = true);

            float doorX = a30.XMax;
            float doorZ = a30.WallSpan(Wall.E).Mid;
            float zAtCrossing = float.NaN;

            // AC1: "x > 340" for World 1's a30 (XMax 338) — walked in fixed 0.05 s steps well past any
            // plausible completion time (100 s of simulated motion) so a regression reads as a real
            // failure, not a test that just didn't tick long enough.
            for (int i = 0; i < 2000 && !finished; i++)
            {
                _sequence.Tick(0.05f);
                if (float.IsNaN(zAtCrossing) && player.transform.position.x > doorX + 2f)
                    zAtCrossing = player.transform.position.z;
            }

            Assert.IsFalse(float.IsNaN(zAtCrossing),
                "Max must reach x > a30.XMax + 2 (inside the corridor) before the sequence finishes.");
            Assert.LessOrEqual(Mathf.Abs(zAtCrossing - doorZ), 1.5f,
                "Max must be inside the corridor's Z bounds (|z - doorZ| <= 1.5) when he crosses into it.");
        }
    }
}
