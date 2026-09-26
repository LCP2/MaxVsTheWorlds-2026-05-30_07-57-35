using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-980 — Lee's own Observation: World 1's a5 area alone can hold dozens of dormant garrison
    /// robots within Max's reach at once (MV-966 parks only the ones OUTSIDE it; World 1's whole
    /// composition IS garrison, unlike World 3's budget-queued 24). Every one of them used to pay a
    /// full <c>CharacterController.Move</c> (gravity), a <c>LineOfSight</c> raycast
    /// (<c>Perception.Tick</c>, run unconditionally every frame regardless of MV-936's own throttled
    /// wake CHECK) and a full <see cref="RobotRig"/>.LateUpdate body, every single frame, forever.
    ///
    /// Reproduces the SHAPE of World 1's a5 (a population of dozens of authored garrison entries in one
    /// area, matching the ticket's own "up to 60 per area" number) on a synthetic <see cref="WorldConfig"/>
    /// rather than the real shipped World 1 asset — same "authored, exact garrison placements" mechanism
    /// (<see cref="WorldArea.garrison"/>, MV-559) <c>MV559AuthoredGarrisonPositionsTests</c> already
    /// drives the same way, just with 30 entries instead of 1. Loading the real World 1 config and
    /// walking it gate-by-gate to a5 (as <c>MV966ParkByReachTests</c> does for its own, differently-
    /// scoped assertion) costs whole minutes of real map/pathing generation across every intervening
    /// area — far past this repo's own ~150s cc-verify budget for what this ticket only needs the
    /// POPULATION SCALE for, not the real level's geometry.
    ///
    /// Must fail on the commit before this ticket: without <see cref="RobotEnemy"/>'s own
    /// <c>Tick(float)</c> returning immediately for <c>State.Dormant</c>, ticking a dormant robot climbs
    /// <see cref="CharacterControllerMotion.CallCount"/> and <see cref="LineOfSight.CallCount"/> exactly
    /// like ticking an awake one, and <c>RobotRig.LateUpdate</c> has no Dormant-unconditional early-out.
    ///
    /// Tier 2 (resolved values) throughout — every assertion reads a call counter built for exactly
    /// this proof, never an authored constant or a rendered pixel.
    /// </summary>
    public sealed class MV980DormantZeroCostTests
    {
        private static readonly MethodInfo TickMethod =
            typeof(RobotEnemy).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo EnsureRigBuiltMethod =
            typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RigLateUpdateMethod =
            typeof(RobotRig).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _directorGo;
        private readonly List<GameObject> _scratchGos = new List<GameObject>();
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            DormantWakeScheduler.ResetForTests();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            foreach (GameObject go in _scratchGos) if (go != null) Object.DestroyImmediate(go);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DormantWakeScheduler.ResetForTests();
            DevTuning.Reset();
        }

        private static void InvokeTick(RobotEnemy e, float dt) => TickMethod.Invoke(e, new object[] { dt });

        private RobotEnemy NewAwakeChaser(int i)
        {
            var go = new GameObject($"MV980-chaser-{i}");
            _scratchGos.Add(go);
            go.transform.position = new Vector3(i * 2f, 0f, 0f);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.ResetState(); // lands in State.Chase
            return e;
        }

        /// <summary>A one-area world (stub -> a1 -> boss, same three-area shape
        /// MV559AuthoredGarrisonPositionsTests/MV656GarrisonStaysDormantOnGateBreakTests already use)
        /// whose a1 authors <paramref name="garrisonCount"/> exact garrison entries on a grid safely
        /// inside its 20x20 bounds — every one of them placed regardless of SeedCount
        /// (<c>Garrison.SeedSlotsFor</c>'s own "every authored entry is placed, even past SeedCount").</summary>
        private static WorldConfig GarrisonWorld(int garrisonCount)
        {
            var garrison = new WorldGarrisonEntry[garrisonCount];
            int cols = Mathf.CeilToInt(Mathf.Sqrt(garrisonCount));
            for (int i = 0; i < garrisonCount; i++)
            {
                int gx = i % cols, gz = i / cols;
                garrison[i] = new WorldGarrisonEntry { kind = "rusher", x = -8f + gx * 2f, z = 2f + gz * 2f };
            }

            return new WorldConfig
            {
                dials = new WorldDials { areaCount = 1, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f } },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", index = 0, role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal", garrisonDensity = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = garrisonCount },
                        garrison = garrison,
                    },
                    new WorldArea
                    {
                        id = "boss", index = 2, role = "boss+exit",
                        origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
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
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void DormantGarrison_CostsNothingPerFrame_UntilWoken_ByDamageOrByTheScheduler()
        {
            // ---- a 30-strong authored garrison in one area, all Dormant, matching the ticket's own
            // "up to 60 per area" population scale (see GarrisonWorld's own doc comment for why this is
            // a synthetic config rather than the real, whole-minutes-to-load World 1 asset). ------------
            const int garrisonCount = 30;
            WorldConfig cfg = GarrisonWorld(garrisonCount);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("MV980 Area Director");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>()); // seeds area 1's garrison

            var dormant = new List<RobotEnemy>();
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (r.IsDormant) dormant.Add(r);
            Assert.AreEqual(garrisonCount, dormant.Count,
                "setup failure: every authored garrison entry must have placed, dormant, for this test to mean anything");

            // ---- plus 5 awake chasers, sharing the same field-wide registries -----------------------
            var chasers = new RobotEnemy[5];
            for (int i = 0; i < chasers.Length; i++) chasers[i] = NewAwakeChaser(i);

            // ---- Part 1: tick 120 frames (2 simulated seconds @ 60 fps) — dormant robots ticked
            // directly, one call per robot per frame, exactly like a live Update() would. -------------
            const float dt = 1f / 60f;
            int ccBefore = CharacterControllerMotion.CallCount;
            int losBefore = LineOfSight.CallCount;

            for (int frame = 0; frame < 120; frame++)
                foreach (RobotEnemy r in dormant)
                    InvokeTick(r, dt);

            Assert.AreEqual(ccBefore, CharacterControllerMotion.CallCount,
                "MV-980: a Dormant robot must execute zero CharacterController.Move over 120 frames");
            Assert.AreEqual(losBefore, LineOfSight.CallCount,
                "MV-980: a Dormant robot's own per-frame Tick must never touch LineOfSight");
            foreach (RobotEnemy r in dormant)
                Assert.AreEqual(RobotEnemy.State.Dormant, r.Current,
                    "a plain tick loop alone must never wake a dormant garrison member");

            // ---- sanity: an awake chaser still genuinely moves when ticked -------------------------
            int ccBeforeChasers = CharacterControllerMotion.CallCount;
            for (int frame = 0; frame < 10; frame++)
                foreach (RobotEnemy c in chasers)
                    InvokeTick(c, dt);
            Assert.Greater(CharacterControllerMotion.CallCount, ccBeforeChasers,
                "setup sanity: the Dormant gate must not have swallowed movement for every robot");

            // ---- Part 2: the central scheduler's own pass-rate is bounded to <=10 Hz ----------------
            for (int frame = 0; frame < 120; frame++) DormantWakeScheduler.Tick(dt);
            Assert.Greater(DormantWakeScheduler.PassCount, 0,
                "setup failure: the scheduler never ran a single pass across 2 simulated seconds");
            Assert.LessOrEqual(DormantWakeScheduler.PassCount, 20,
                "MV-980: the central scheduler must never run more than 10 passes per simulated second");

            // ---- Part 3: RobotRig.LateUpdate never runs its body for a Dormant robot ----------------
            RobotRig dormantRig = dormant[0].GetComponent<RobotRig>();
            Assert.IsNotNull(dormantRig, "setup failure: a real placed garrison member must already carry its own RobotRig");
            EnsureRigBuiltMethod.Invoke(dormantRig, null);
            for (int i = 0; i < 5; i++) RigLateUpdateMethod.Invoke(dormantRig, null);
            Assert.AreEqual(0, dormantRig.LateUpdateBodyRunCount,
                "MV-980: a Dormant robot's RobotRig.LateUpdate must never run its body");

            RobotRig chaserRig = chasers[0].gameObject.AddComponent<RobotRig>();
            EnsureRigBuiltMethod.Invoke(chaserRig, null);
            for (int i = 0; i < 5; i++) RigLateUpdateMethod.Invoke(chaserRig, null);
            Assert.AreEqual(5, chaserRig.LateUpdateBodyRunCount,
                "setup sanity: an awake robot's RobotRig.LateUpdate must still run its body every call");

            // ---- Part 4: damage wakes a dormant robot immediately, same frame ----------------------
            RobotEnemy shot = dormant[1];
            Assert.AreEqual(RobotEnemy.State.Dormant, shot.Current, "setup failure: nothing must have already woken this one");
            shot.TakeDamage(new DamageInfo(1f, shot.transform.position, Vector3.up, Team.Player));
            Assert.AreNotEqual(RobotEnemy.State.Dormant, shot.Current,
                "MV-980: taking damage must wake a dormant robot immediately, not wait on the scheduler's own pass");

            // ---- Part 5: an on-screen, sight-clear robot wakes within one scheduler interval (0.1s) -
            var playerGo = new GameObject("MV980-lone-player") { tag = "Player" };
            _scratchGos.Add(playerGo);
            playerGo.transform.position = Vector3.zero;

            var loneGo = new GameObject("MV980-lone-dormant");
            _scratchGos.Add(loneGo);
            loneGo.transform.position = Vector3.forward * 5f; // clear line, no obstruction
            var loneCc = loneGo.AddComponent<CharacterController>();
            var lone = loneGo.AddComponent<RobotEnemy>();
            CcField.SetValue(lone, loneCc);
            lone.ResetState(); // finds the tagged player via AcquireTarget
            lone.BeginDormant();

            // No camera in this scene (suppressed in SetUp) — MV-478's fail-open rule (AC8) means
            // "on screen" reads true, same idiom MV363DormantRobotTests uses, so a clear sight-line
            // alone is enough to prove the latency bound below.
            lone.CentralWakeCheck(DormantWakeScheduler.TickInterval);
            Assert.AreNotEqual(RobotEnemy.State.Dormant, lone.Current,
                "MV-980: an on-screen, sight-clear dormant robot must wake within one scheduler pass (0.1s)");
        }
    }
}
