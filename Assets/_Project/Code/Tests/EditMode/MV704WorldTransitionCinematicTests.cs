using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-704: the World 1 -> World 2 transition cinematic. Fails on base commit 5ea895b, which has
    /// neither <c>BeatSequencer</c> nor <c>WorldTransitionCinematic</c> to build against.
    ///
    /// Built via <see cref="WorldTransitionCinematic.Initialize"/> rather than <c>AddComponent</c> + a
    /// frame wait, same reason as <see cref="IntroCinematic"/>'s own tests: a MonoBehaviour without
    /// <c>[ExecuteAlways]</c> only receives <c>Awake</c> once Unity is actually in Play Mode, which this
    /// EditMode suite never enters (MV-299/311/330).
    /// </summary>
    public sealed class MV704WorldTransitionCinematicTests
    {
        private GameObject _camGo;
        private WorldTransitionCinematic _cinematic;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldTransitionCinematic>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_cinematic != null) Object.DestroyImmediate(_cinematic.gameObject);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        [Test]
        public void SequencerResolvesFourBeats_SkipLandsOnLandAtZero_AndOnFinishedHandsOffToTheEntryStub()
        {
            bool finished = false;
            var go = new GameObject("WorldTransitionCinematic");
            _cinematic = go.AddComponent<WorldTransitionCinematic>();
            _cinematic.Initialize(() => finished = true);

            // AC1a: the resolved beat list has 4 beats totalling <= 10.5 s.
            Assert.AreEqual(4, _cinematic.BeatCount, "the transition must resolve exactly WRECK/DROP/SHAFT/LAND.");
            Assert.LessOrEqual(_cinematic.TotalDuration, 10.5f,
                "the transition must stay under the ticket's 10.5 s budget (20 s minus room for the rest of the scene).");

            // AC1b: Skip() at any beat resolves to LAND with elapsed 0 — driven from the very start,
            // before a single Tick, which is the earliest "any beat" a caller could skip from.
            _cinematic.Skip();
            Assert.AreEqual(WorldTransitionCinematic.Land, _cinematic.BeatName,
                "Skip must jump straight to the LAND beat, not run the timeline out.");
            Assert.AreEqual(0f, _cinematic.BeatElapsed,
                "Skip must land on LAND at its own start (0 s elapsed within the beat), not partway through it.");
            Assert.IsFalse(finished, "landing on LAND is not itself finished — the beat still has to play out.");

            // AC1c: OnFinished starts the run at the stub — ticking LAND out fires the hand-off exactly
            // once, and the entry stub it hands off to is the same "spawn" entity WorldMapLoader
            // synthesises for World 2 at the entry-role area's centre (proven independently below, since
            // the real hand-off is a scene reload no EditMode test can drive).
            _cinematic.Tick(2.5f);   // LAND's full duration
            Assert.IsTrue(finished, "OnFinished must fire once LAND has played out.");
            Assert.IsFalse(_cinematic.IsPlaying, "the cinematic must be done once OnFinished has fired.");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            Assert.IsNotNull(spawn, "WorldMapLoader must synthesise a playerSpawn entity for World 2.");

            WorldArea stub = cfg.Area("stub");
            Assert.IsNotNull(stub, "World 2 must author an entry-role area named 'stub'.");
            Assert.IsTrue(stub.IsEntryRole, "'stub' must carry the entry role — it is what OnFinished's reload lands Max in.");
            Assert.AreEqual(stub.CenterXz, spawn.CenterXz,
                "the synthesised spawn point must resolve to the entry stub's centre, not a1 or anywhere else.");
        }
    }
}
