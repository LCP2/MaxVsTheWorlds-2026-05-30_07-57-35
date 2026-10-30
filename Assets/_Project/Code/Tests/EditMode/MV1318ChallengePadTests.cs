using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1318 (ticket AC1): "build an area with a pad and a composition; place Max on the pad; wave 1
    /// spawns 4 robots; kill all waves (18 robots) with no hit -> 2 Supercells on the pad and the pad is
    /// used; a second run of the same scenario with one recorded hit -> 1 Supercell." Must fail to COMPILE
    /// on the pre-fix base commit: no <see cref="ChallengePad"/> type, no <see cref="WorldArea.challenges"/>
    /// field and no <see cref="WorldChallenge"/> type exist there — the strongest form of Rule 1's "fails
    /// first" proof, same as <c>MV1296ExplosiveCanisterBlastTests</c>. See the fix comment for the
    /// captured failure output.
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> (same construction idiom as
    /// <c>MV1294PylonLiveFeedersTests.BuildWorld</c>/<c>MV1296ExplosiveCanisterBlastTests.BuildWorld</c>)
    /// carrying one area with a composition and a challenge pad, loaded through the real
    /// <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> pipeline, driving the real
    /// <see cref="ChallengePad.Tick"/>/<see cref="RobotEnemy.TakeDamage"/>/<see cref="HudSignals.EmitPlayerHit"/>
    /// path — no direct reward-math call anywhere in this file.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value — a
    /// pad's own <see cref="ChallengePad.State"/>, <see cref="ChallengePad.LiveWaveRobots"/> and
    /// <see cref="ChallengePad.SupercellsAwarded"/> — never an authored constant, never a rendered pixel.
    /// Reads those two pad-owned readouts rather than <c>RobotEnemy.Active</c>/<c>Pickup.Active</c>: both
    /// registries are populated from their own type's <c>OnEnable</c>, which does not reliably fire off a
    /// plain <c>SetActive(true)</c> toggle under this project's <c>-batchmode -nographics</c> EditMode
    /// run even though the robot/pickup is otherwise genuinely alive/placed (see each readout's own doc
    /// comment) — a pre-existing engine behaviour this ticket did not introduce and is out of scope to fix.
    /// // Guards MV-1318
    /// </summary>
    public sealed class MV1318ChallengePadTests
    {
        // Same "distinctive far-off origin" idiom MV1294PylonLiveFeedersTests/MV1296ExplosiveCanisterBlastTests
        // use — EditMode tests share one physics scene for the whole cc-verify run with no per-test reset,
        // and this test spawns/kills three dozen robots and places live pickups, both a real correctness
        // risk (not just cosmetic) if a stray object from another test sat nearby. Must stay well under
        // CharacterControllerSafety.MaxAbsCoordinate (100000) — unlike MV1296's manually-constructed
        // RobotEnemy, this test drives the real EnemySpawner.SpawnExact pipeline, which refuses a spawn
        // past that ceiling (silently, no exception — it just never lands in RobotEnemy.Active).
        private const float OriginX = 48209f;
        private const float OriginZ = 57641f;

        // Pads A and B sit far enough apart (and each far outside the other's 15 m fail distance and
        // 6-9 m beam ring) that running one pad's full attempt to completion before starting the other's
        // can never cross-contaminate Pickup.Active/RobotEnemy.Active counts.
        private const float PadBOffsetX = 40f;

        private GameObject _root;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            // First VfxMaterials/health-bar init in this test domain logs a plain Debug.Log once, not a
            // failure — same idiom MV1296ExplosiveCanisterBlastTests uses for the same reason.
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            ChallengePad.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            ChallengePad.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        /// <summary>Stub (entry, empty) -> "a1" (one Rusher-only composition, two challenge pads 200 m
        /// apart) — same two-area skeleton <c>MV1294PylonLiveFeedersTests.BuildWorld</c> uses.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1318 Test World",
            dials = new WorldDials
            {
                areaCount = 1, baseThreat = 0f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f }, powerupCadence = 99,
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = OriginX - 2f, z = OriginZ - 6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    // Centred on the same X as "stub" (OriginX), same as MV1296ExplosiveCanisterBlastTests'
                    // own a1 — the gate validator requires the shared wall's midpoint to line up between
                    // the two areas it joins, and widening this area to hold two far-apart pads without
                    // keeping it centred on stub's own X spills the gate past the shared wall.
                    id = "a1", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = OriginX - (PadBOffsetX + 20f), z = OriginZ },
                    size = new WorldAreaSize { w = (PadBOffsetX + 20f) * 2f, d = 20f },
                    composition = new WorldComposition { rusher = 1 },
                    challenges = new[]
                    {
                        new WorldChallenge { id = "a1_challenge1", x = OriginX, z = OriginZ + 10f },
                        new WorldChallenge { id = "a1_challenge2", x = OriginX + PadBOffsetX, z = OriginZ + 10f },
                    },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void ClearingAllThreeWaves_Awards2SupercellsWithNoHitAnd1WithAHit_AndMarksThePadUsed()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _root = new GameObject("MV-1318 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            Assert.AreEqual(2, built.Challenges.Count, "setup failure: fixture must build exactly two challenge pads");

            // AddComponent's own Awake never runs outside Play mode (ChallengePad.Build's own doc comment,
            // same as Canister.Build/Pylon.Build) — build every actor explicitly, same idiom
            // MV1296ExplosiveCanisterBlastTests already uses.
            ChallengePad padA = built.Challenges[0];
            ChallengePad padB = built.Challenges[1];
            padA.Build();
            padB.Build();

            Assert.AreEqual(ChallengePad.ChallengeState.Ready, padA.State, "setup failure: a freshly built pad must start READY");
            Assert.AreEqual(ChallengePad.ChallengeState.Ready, padB.State, "setup failure: a freshly built pad must start READY");

            _playerGo = new GameObject("MV-1318 Max") { tag = "Player" };

            // === Run 1: no hit recorded across the whole attempt -> AC1's "2 Supercells" ===
            RunChallengeToWin(padA, _playerGo.transform, recordOneHit: false, assertWave1SpawnsFour: true);
            Assert.AreEqual(ChallengePad.ChallengeState.Won, padA.State, "MV-1318 AC1: clearing all three waves must WIN the pad (the pad is used)");
            Assert.AreEqual(2, padA.SupercellsAwarded, "MV-1318 AC1: a no-hit clear must place 2 Supercells on the pad");

            // === Run 2: one hit recorded during the attempt -> AC1's "1 Supercell" ===
            RunChallengeToWin(padB, _playerGo.transform, recordOneHit: true, assertWave1SpawnsFour: false);
            Assert.AreEqual(ChallengePad.ChallengeState.Won, padB.State, "MV-1318 AC1: clearing all three waves must WIN the pad (the pad is used)");
            Assert.AreEqual(1, padB.SupercellsAwarded, "MV-1318 AC1: a clear with one recorded hit must place only 1 Supercell");
        }

        /// <summary>Drives one pad's full 3-wave attempt (18 robots across waves of 4/6/8) to WON — moves
        /// Max onto the pad to start it, ticks in small steps, and kills every robot the pad has put on
        /// the field the instant it appears so each wave clears as fast as the pad's own beam-in/wave-gap
        /// timing allows. <paramref name="assertWave1SpawnsFour"/> checks AC1's own "wave 1 spawns 4
        /// robots" before any kill — only needed once, on the first run.</summary>
        private static void RunChallengeToWin(ChallengePad pad, Transform player, bool recordOneHit,
            bool assertWave1SpawnsFour)
        {
            const float dt = 0.05f;
            const int wave1Size = 4;
            player.position = pad.transform.position;

            // Phase 1: let wave 1 beam all the way in WITHOUT killing anything, so all 4 robots are ever
            // alive at once and AC1's own "wave 1 spawns 4 robots" is a real, checkable moment — killing
            // on sight (phase 2's strategy) would never let the count reach more than 1 at a time. Reads
            // pad.LiveWaveRobots, not RobotEnemy.Active/ActiveCount — see that property's own doc comment
            // for why the static registry can't be trusted here.
            int phase1Step = 0;
            while (phase1Step < 400 && pad.LiveWaveRobots.Count < wave1Size)
            {
                pad.Tick(dt);
                phase1Step++;
            }

            if (assertWave1SpawnsFour)
                Assert.AreEqual(wave1Size, pad.LiveWaveRobots.Count, "MV-1318 AC1: wave 1 must spawn exactly 4 robots");

            // Phase 2: kill everything the pad has put on the field RIGHT NOW, every tick, so each
            // subsequent wave clears as soon as it finishes beaming in rather than idling out the
            // simulated clock. One hit (for the "with a hit" run) is recorded partway through.
            bool hitRecorded = false;
            for (int step = 0; step < 2000 &&
                 (pad.State == ChallengePad.ChallengeState.Ready || pad.State == ChallengePad.ChallengeState.Running);
                 step++)
            {
                var alive = new List<RobotEnemy>(pad.LiveWaveRobots);
                foreach (RobotEnemy e in alive)
                {
                    if (e == null) continue;
                    e.TakeDamage(new DamageInfo(99999f, e.transform.position, Vector3.up, Team.Player));
                }

                if (recordOneHit && !hitRecorded && pad.State == ChallengePad.ChallengeState.Running)
                {
                    HudSignals.EmitPlayerHit(player.position, Vector3.forward, true);
                    hitRecorded = true;
                }

                pad.Tick(dt);
            }

            Assert.AreEqual(ChallengePad.ChallengeState.Won, pad.State,
                "setup failure: the simulated attempt must reach WON within the step budget");
        }
    }
}
