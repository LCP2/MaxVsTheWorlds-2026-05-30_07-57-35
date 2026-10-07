using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1122 (the one new test, per CC_AUTONOMY's testing policy): the final area's clean-up phase,
    /// end to end, against World 1's real a30 -- five cases, each a real <see cref="WorldFinaleGate"/>/
    /// <see cref="AreaAccumulationDirector"/> driven through its real signals and a <c>TickCleanup(dt)</c>
    /// the test can drive deterministically (same "a Tick(dt) the test can drive" contract
    /// <see cref="WorldFinaleGate.TickWeaponBeat"/>/<see cref="WorldFinaleGate.TickExitBeat"/> already give
    /// their own beats).
    ///
    /// Fails on base commit 431d543 (the tip before this ticket): <c>BigBermudaBoss.LaunchVolley</c> never
    /// stamps a flung add's <c>AreaIndex</c>, and <c>BigBermudaBoss.OnDeath</c> never lands one still mid-
    /// flight -- Case 1's <c>bossThrown.AreaIndex</c> assertion and Case 3 (AC1h) are the direct proof;
    /// Case 3's own failure output is quoted in the fix comment.
    /// </summary>
    public sealed class MV1122ClearTheAreaTests
    {
        private string _dir;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1122-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            SweepStrayActors();
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        /// <summary>Catch-all teardown: this test's single method runs five independent cases, each with
        /// its own fixture, inside one try-less block -- an assertion failure partway through a case
        /// throws straight out of the whole test method, skipping every later case's own TeardownFixture
        /// call (including that case's own). Without this, a stray HudController/WorldFinaleGate left
        /// subscribed to static events (HudSignals, WeaponSystemState) corrupts every OTHER test that
        /// runs afterward in the same suite -- not a hypothetical: this is exactly what happened the
        /// first time this test's own Case 1 threw (see fix comment), cascading into 40+ unrelated
        /// failures elsewhere in the run. [TearDown] always runs regardless of where the test aborted.</summary>
        private static void SweepStrayActors()
        {
            foreach (var gate in Object.FindObjectsByType<WorldFinaleGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { InvokeLifecycle(gate, "OnDisable"); Object.DestroyImmediate(gate.gameObject); }
            foreach (var payoff in Object.FindObjectsByType<BossVictoryPayoff>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { InvokeLifecycle(payoff, "OnDisable"); Object.DestroyImmediate(payoff.gameObject); }
            foreach (var hud in Object.FindObjectsByType<HudController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { InvokeLifecycle(hud, "OnDisable"); Object.DestroyImmediate(hud.gameObject); }
            HudController.SkipTouchControlsForTests = false;

            GameObject probeRoot = GameObject.Find("MV-1122 Probe Root");
            if (probeRoot != null) Object.DestroyImmediate(probeRoot);
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);

            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);
            foreach (var arrow in Object.FindObjectsByType<EdgeArrow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(arrow.gameObject);
            // Every robot this test built, by any path (ambient/garrison under the BackyardPath root
            // above, a boss's own flung add under its OWN unparented "Brood Adds" root -- BigBermudaBoss
            // .OnDestroy would tear that one down, but OnDestroy is exactly as unreliable outside Play
            // mode as OnEnable/OnDisable already are, so it leaks across test boundaries without this.
            foreach (var r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(r.gameObject);
            GameObject broodAdds = GameObject.Find("Brood Adds");
            if (broodAdds != null) Object.DestroyImmediate(broodAdds);
            // A Weapon Core (or anything else) PickupDirector.EnsureInstalled() dropped -- not parented
            // under the probe root either, and Pickup.ResetRegistry() only clears the static registry,
            // not the live GameObjects, so a stray one survives to confuse the NEXT case's own
            // LivePickups().Single(...) with "more than one WeaponCore" otherwise. The director itself
            // goes too -- EnsureInstalled() finds-or-creates ONE scene singleton, so if it survives
            // across cases its own internal _live list keeps a dangling reference to whatever Pickup
            // this sweep just destroyed out from under it (MissingReferenceException on the next
            // Collect). Destroying it forces a fresh, empty one next EnsureInstalled() call.
            foreach (var pd in Object.FindObjectsByType<PickupDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(pd.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam.name.StartsWith("MV-1122")) Object.DestroyImmediate(cam.gameObject);
        }

        // Lifecycle plumbing only (MV1078/MV1131's own established idiom) -- Unity does not reliably run
        // Awake/OnEnable for AddComponent outside Play mode. LaunchVolley/AdvanceAdds/OnDeath are the same
        // "no public equivalent exists" exception MV1078's own InvokeOnDeath/InvokeCollect already carry --
        // never used to read or set the state an assertion below depends on.
        private static void InvokeLifecycle(Component c, string method) =>
            c.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(c, null);

        private static void InvokeLaunchVolley(BigBermudaBoss boss, int count) =>
            typeof(BigBermudaBoss).GetMethod("LaunchVolley", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, new object[] { count });

        private static void InvokeAdvanceAdds(BigBermudaBoss boss, float dt) =>
            typeof(BigBermudaBoss).GetMethod("AdvanceAdds", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, new object[] { dt });

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static Pickup[] LivePickups() => Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        private static void Kill(RobotEnemy r) =>
            r.TakeDamage(new DamageInfo(999999f, r.transform.position, Vector3.forward, Team.Player));

        /// <summary>A bare, real <see cref="RobotEnemy"/> stamped area30 -- the test device Case 5's
        /// second half uses to simulate "something keeps entering the area", proving the hard time limit
        /// fires independently of the no-progress thinner. Built active throughout (never the deactivate/
        /// reactivate dance production spawn paths use for CharacterController-creation safety), so
        /// Awake/OnEnable fire reliably even outside Play mode.</summary>
        private static RobotEnemy SpawnArea30Robot(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "MV-1122 Injected Robot";
            var e = go.AddComponent<RobotEnemy>();
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            e.transform.position = position;
            e.SetAreaIndex(30);
            return e;
        }

        private readonly struct Fixture
        {
            public readonly GameObject Root;
            public readonly MapData Map;
            public readonly GameObject HudGo;
            public readonly HudController Hud;
            public readonly WorldFinaleGate Gate;
            public readonly BigBermudaBoss Boss1;
            public readonly BigBermudaBoss Boss2;
            public readonly GameObject PayoffGo;

            public Fixture(GameObject root, MapData map, GameObject hudGo, HudController hud, WorldFinaleGate gate,
                BigBermudaBoss boss1, BigBermudaBoss boss2, GameObject payoffGo)
            {
                Root = root; Map = map; HudGo = hudGo; Hud = hud; Gate = gate;
                Boss1 = boss1; Boss2 = boss2; PayoffGo = payoffGo;
            }
        }

        /// <summary>Builds World 1's real map/area/finale-gate/HUD/payoff fixture fresh, filled through
        /// a30 (the final boss area) -- the shared arrange step every case below starts from. Goes
        /// through a real <see cref="BackyardPath"/> (not a hand-rolled <c>MapRuntime.Build</c> +
        /// <c>AreaAccumulationDirector</c> pair, MV1078/MV1131's own shortcut) because <see cref="WorldFinaleGate.Awake"/>
        /// hard-requires finding one -- without it, this gate's own <c>_map</c> stays null, and this
        /// ticket's own footprint check (Change 2) needs <c>_map</c> to mean anything at all.</summary>
        private static Fixture BuildFixture()
        {
            var root = new GameObject("MV-1122 Probe Root");
            var path = root.AddComponent<BackyardPath>();
            InvokeLifecycle(path, "Awake");
            Assert.IsNotNull(path.Map, "fixture: BackyardPath must have loaded World 1's real map");

            AreaAccumulationDirector areaDirector = path.AreaDirector;
            Assert.IsNotNull(areaDirector, "fixture: BackyardPath must have built its own AreaAccumulationDirector");
            areaDirector.EnterArea(30);

            HudController.SkipTouchControlsForTests = true;
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");

            var gateGo = new GameObject("MV-1122 WorldFinaleGate Test");
            var gate = gateGo.AddComponent<WorldFinaleGate>();
            InvokeLifecycle(gate, "Awake");
            InvokeLifecycle(gate, "OnEnable");
            Assert.IsTrue(gate.enabled,
                "fixture: WorldFinaleGate.Awake must have found the real BackyardPath above and stayed enabled");

            var payoffGo = new GameObject("MV-1122 BossVictoryPayoff Test");
            var payoff = payoffGo.AddComponent<BossVictoryPayoff>();
            InvokeLifecycle(payoff, "OnEnable");

            Assert.IsTrue(path.Actors.TryGetValue("a30_boss1", out GameObject boss1Go) && boss1Go != null,
                "world1_config.json's a30_boss1 was not built");
            Assert.IsTrue(path.Actors.TryGetValue("a30_boss2", out GameObject boss2Go) && boss2Go != null,
                "world1_config.json's a30_boss2 was not built");
            var boss1 = boss1Go.GetComponent<BigBermudaBoss>();
            var boss2 = boss2Go.GetComponent<BigBermudaBoss>();

            return new Fixture(root, path.Map, hudGo, hud, gate, boss1, boss2, payoffGo);
        }

        private static void TeardownFixture(Fixture f)
        {
            // SweepStrayActors (below) finds and tears down the gate/payoff/HUD/root by scanning the
            // scene, not these specific handles -- same end state, robust even if a case above already
            // destroyed one of them itself.
            SweepStrayActors();
            RobotEnemy.ResetRegistry();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
        }

        /// <summary>Kills every a30 robot except the ones in <paramref name="keep"/> (by reference).</summary>
        private static void KillExcept(IEnumerable<RobotEnemy> a30Enemies, params RobotEnemy[] keep)
        {
            var keepSet = new HashSet<RobotEnemy>(keep);
            foreach (RobotEnemy r in a30Enemies)
                if (!keepSet.Contains(r)) Kill(r);
        }

        private static Pickup CollectCoreAndRunBeatA(Fixture f)
        {
            Pickup core = LivePickups().Single(p => p.Kind == PickupKind.WeaponCore);
            var pickupDirector = PickupDirector.EnsureInstalled();
            InvokeCollect(pickupDirector, core);
            f.Gate.TickWeaponBeat(2.5f); // whole Beat A in one call -- BeginCleanup runs synchronously at the end
            return core;
        }

        [Test]
        public void FinalAreaCleanup_CountRingsArrowsAndSafetyNets_MatchTheTicket()
        {
            // ======================================================== Case 1 (AC1 a-f): the 3-counted flow
            {
                Fixture f = BuildFixture();

                RobotEnemy[] a30Enemies = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Where(r => r.AreaIndex == 30).ToArray();
                Assert.Greater(a30Enemies.Length, 3,
                    "fixture: a30 must build more ambient robots than this case needs, or the setup is wrong");

                RobotEnemy garrisonA = a30Enemies[0];
                RobotEnemy garrisonB = a30Enemies[1];
                RobotEnemy footprintViolator = a30Enemies[2];
                KillExcept(a30Enemies, garrisonA, garrisonB, footprintViolator);
                Assert.IsTrue(garrisonA.IsAlive && garrisonB.IsAlive && footprintViolator.IsAlive,
                    "setup: exactly 3 of a30's ambient robots must still be alive before the boss dies");

                MapZone a30Zone = f.Map.Zone("area30");
                Assert.IsNotNull(a30Zone, "fixture: world1_config.json must author an area30 zone");
                footprintViolator.transform.position =
                    new Vector3(a30Zone.XMax + 20f, footprintViolator.transform.position.y, a30Zone.ZMin);

                BossCensus.Register(f.Boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                // AddComponent doesn't reliably fire Awake for a boss MapRuntime.Build creates inactive-then-
                // activates outside Play mode (same note every sibling finale test carries for OnEnable) --
                // Awake is what constructs _health, which the real Wake() below reads.
                InvokeLifecycle(f.Boss2, "Awake");
                InvokeLifecycle(f.Boss2, "Wake"); // real Wake() -- not a reflected field -- stamps _areaIndex
                Assert.IsFalse(f.Boss2.IsAlive, "fixture: Wake() with no damage taken must not itself kill the boss");

                // One robot "thrown by the boss inside the area" -- the real LaunchVolley/AdvanceAdds path,
                // proving MV-1122's own area-stamping hypothesis for a boss add.
                var beforeLaunch = new HashSet<RobotEnemy>(Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None));
                InvokeLaunchVolley(f.Boss2, 1);
                InvokeAdvanceAdds(f.Boss2, 10f); // one big dt -- guaranteed past the arc's own landing instant
                RobotEnemy bossThrown = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Except(beforeLaunch).SingleOrDefault();
                Assert.IsNotNull(bossThrown, "fixture: LaunchVolley must have produced exactly one new robot");
                Assert.AreEqual(30, bossThrown.AreaIndex,
                    "MV-1122 hypothesis: a boss-flung add must carry its boss's own AreaIndex");
                Assert.IsTrue(bossThrown.enabled, "fixture: the thrown add must have landed before clean-up begins");

                InvokeOnDeath(f.Boss1);
                InvokeOnDeath(f.Boss2); // a30's last boss
                CollectCoreAndRunBeatA(f);

                // ---------- AC1a ----------
                Assert.AreEqual("CLEAR THE AREA   ROBOTS LEFT 3", f.Hud.ObjectiveText, "AC1a: the strip's own text");

                // ---------- AC1b: the footprint violator is dead within one tick ----------
                Assert.IsFalse(footprintViolator.IsAlive, "AC1b: the robot outside the footprint must be dead within one tick");

                // ---------- AC1c: three following rings at 0.9m (+-0.05), each within 0.1m (XZ) of its robot ----------
                Assert.AreEqual(3, f.Gate.CleanupRings.Count, "AC1c: three ring objects must exist");
                foreach (var kv in f.Gate.CleanupRings)
                {
                    float radius = kv.Value.transform.localScale.x * 0.5f;
                    Assert.That(radius, Is.EqualTo(0.9f).Within(0.05f), $"AC1c: ring radius was {radius}m");
                    Vector3 a = kv.Value.transform.position, b = kv.Key.transform.position;
                    float xz = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                    Assert.That(xz, Is.LessThanOrEqualTo(0.1f), $"AC1c: ring was {xz}m from its own robot (XZ)");
                }

                // ---------- AC1d: exactly one edge arrow when the camera can't see one of the three ----------
                RobotEnemy[] counted = { garrisonA, garrisonB, bossThrown };
                // Anchor counted[0] at the zone's own centre rather than wherever it happened to be
                // authored: FindObjectsByType's traversal order (and so which ambient robot lands at
                // a30Enemies[0]) depends on Unity's own instance-ID layout, which shifts with how many
                // objects earlier tests in the same run created -- a robot authored near a30's edge
                // would leave no room for the "clamped far candidate" below to clear visibleHalf, and
                // which robot that is was never something this fixture controlled. The ring/AC1c checks
                // above already ran against its original authored spot, so moving it now is safe.
                Vector3 c0 = new Vector3(
                    (a30Zone.XMin + a30Zone.XMax) * 0.5f,
                    counted[0].transform.position.y,
                    (a30Zone.ZMin + a30Zone.ZMax) * 0.5f);
                counted[0].transform.position = c0;
                float visibleHalf = Mathf.Max(2f, Mathf.Min(a30Zone.XMax - a30Zone.XMin, a30Zone.ZMax - a30Zone.ZMin) * 0.15f);
                counted[1].transform.position = c0 + new Vector3(visibleHalf * 0.2f, 0f, 0f);
                Vector3 farCandidate = c0 + new Vector3(visibleHalf * 4f, 0f, visibleHalf * 4f);
                Vector3 clamped = new Vector3(
                    Mathf.Clamp(farCandidate.x, a30Zone.XMin + 1f, a30Zone.XMax - 1f),
                    counted[2].transform.position.y,
                    Mathf.Clamp(farCandidate.z, a30Zone.ZMin + 1f, a30Zone.ZMax - 1f));
                counted[2].transform.position = clamped;
                Assert.That(Vector2.Distance(new Vector2(clamped.x, clamped.z), new Vector2(c0.x, c0.z)),
                    Is.GreaterThan(visibleHalf),
                    "fixture: the clamped far robot must still land outside the camera's own visible radius");

                // A real BackyardPath build (unlike MV1078/MV1131's own map-only fixture) brings up its
                // own gameplay camera rig, tagged MainCamera, well after this test's own [SetUp] already
                // suppressed whatever ambient one the editor's loaded scene carried -- so Camera.main
                // would resolve to THAT one, not this probe, without suppressing it too.
                CameraTestUtil.SuppressAmbientMainCameras();
                var camGo = new GameObject("MV-1122 Camera Probe", typeof(Camera)) { tag = "MainCamera" };
                camGo.transform.position = c0 + Vector3.up * 50f;
                camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = visibleHalf;
                cam.aspect = 1f;

                f.Gate.TickCleanup(0.01f);
                Assert.AreEqual(1, f.Gate.ActiveEdgeArrowCount, "AC1d: exactly one edge arrow must be active");

                // ---------- AC1e: killing one robot updates the strip ----------
                Kill(counted[0]);
                f.Gate.TickCleanup(0.01f);
                Assert.AreEqual("CLEAR THE AREA   ROBOTS LEFT 2", f.Hud.ObjectiveText, "AC1e: the strip after one kill");

                // ---------- AC1f: 10.5s with no further kills thins the rest to 0; exit beat begins ----------
                float t = 0f;
                while (t < 10.5f) { f.Gate.TickCleanup(0.05f); t += 0.05f; }
                Assert.IsTrue(f.Gate.ExitBeatActive, "AC1f: the exit beat must have begun by 10.5s of no-progress thinning");

                Object.DestroyImmediate(camGo);
                TeardownFixture(f);
            }

            // ======================================================== Case 2 (AC1g): an already-empty area
            {
                Fixture f = BuildFixture();

                RobotEnemy[] a30Enemies = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Where(r => r.AreaIndex == 30).ToArray();
                foreach (RobotEnemy r in a30Enemies) Kill(r);

                BossCensus.Register(f.Boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                BossCensus.Register(f.Boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                InvokeOnDeath(f.Boss1);
                InvokeOnDeath(f.Boss2);
                CollectCoreAndRunBeatA(f);

                Assert.IsFalse(f.Hud.ObjectiveVisible, "AC1g: the strip must never show ROBOTS LEFT for an already-empty area");
                Assert.IsTrue(f.Gate.ExitBeatActive, "AC1g: the exit beat must begin on the same tick clean-up would have started");

                f.Gate.TickExitBeat(1.0f);
                Assert.IsTrue(f.Gate.IsOpen, "AC1g: the exit must actually open");

                TeardownFixture(f);
            }

            // ======================================================== Case 3 (AC1h): boss dies mid-throw
            {
                Fixture f = BuildFixture();

                InvokeLifecycle(f.Boss1, "Awake");
                InvokeLifecycle(f.Boss1, "Wake");
                var before = new HashSet<RobotEnemy>(Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None));
                InvokeLaunchVolley(f.Boss1, 1);
                InvokeAdvanceAdds(f.Boss1, 0.01f); // nowhere near a full arc -- still mid-flight
                RobotEnemy add = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Except(before).SingleOrDefault();
                Assert.IsNotNull(add, "fixture: LaunchVolley must have produced exactly one new robot");
                Assert.IsFalse(add.enabled, "fixture: the add must still be mid-flight (brain off) before the boss dies");

                BossCensus.Register(f.Boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                InvokeOnDeath(f.Boss1);

                Assert.IsTrue(add.enabled,
                    "AC1h: within one tick of the boss's own death, an add still in flight must have its RobotEnemy enabled");
                float floorY = f.Map.SurfaceHeightAt(add.transform.position);
                Assert.That(Mathf.Abs(add.transform.position.y - floorY), Is.LessThanOrEqualTo(1f),
                    $"AC1h: within one tick of the boss's own death, that add must be at floor height " +
                    $"(was y={add.transform.position.y}, floor={floorY})");

                TeardownFixture(f);
            }

            // ======================================================== Case 4 (AC1i): held 3m above the floor
            {
                Fixture f = BuildFixture();

                RobotEnemy[] a30Enemies = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Where(r => r.AreaIndex == 30).ToArray();
                RobotEnemy held = a30Enemies[0];
                KillExcept(a30Enemies, held);
                held.transform.position += Vector3.up * 3f;

                BossCensus.Register(f.Boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                BossCensus.Register(f.Boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                InvokeOnDeath(f.Boss1);
                InvokeOnDeath(f.Boss2);
                CollectCoreAndRunBeatA(f);
                Assert.IsTrue(held.IsAlive, "setup: the held robot must still be alive once clean-up begins");

                float t = 0f;
                while (t < 2.5f && held.IsAlive) { f.Gate.TickCleanup(0.1f); t += 0.1f; }
                Assert.IsFalse(held.IsAlive, $"AC1i: the held robot must be gone within 2.5s of clean-up beginning (t={t})");
                Assert.IsTrue(f.Gate.ExitBeatActive, "AC1i: the exit beat must follow");

                TeardownFixture(f);
            }

            // ======================================================== Case 5 (AC1j): no-progress, then hard limit
            {
                Fixture f = BuildFixture();

                RobotEnemy[] a30Enemies = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Where(r => r.AreaIndex == 30).ToArray();
                RobotEnemy kept = a30Enemies[0];
                KillExcept(a30Enemies, kept);

                BossCensus.Register(f.Boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                BossCensus.Register(f.Boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                InvokeOnDeath(f.Boss1);
                InvokeOnDeath(f.Boss2);
                CollectCoreAndRunBeatA(f);

                float t = 0f;
                while (t < 11.5f && !f.Gate.ExitBeatActive) { f.Gate.TickCleanup(0.1f); t += 0.1f; }
                Assert.IsTrue(f.Gate.ExitBeatActive,
                    $"AC1j: one robot never damaged, never unreachable, must still be cleared by the no-progress " +
                    $"thinner alone, and the exit beat must have begun by 11.5s (t={t})");

                TeardownFixture(f);
            }
            {
                Fixture f = BuildFixture();

                RobotEnemy[] a30Enemies = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Where(r => r.AreaIndex == 30).ToArray();
                foreach (RobotEnemy r in a30Enemies) Kill(r); // start from a clean area; inject our own below

                BossCensus.Register(f.Boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
                BossCensus.Register(f.Boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);

                MapZone a30Zone = f.Map.Zone("area30");
                Vector3 spot = new Vector3((a30Zone.XMin + a30Zone.XMax) * 0.5f, 0f, (a30Zone.ZMin + a30Zone.ZMax) * 0.5f);
                SpawnArea30Robot(spot);

                InvokeOnDeath(f.Boss1);
                InvokeOnDeath(f.Boss2);
                CollectCoreAndRunBeatA(f);

                float t = 0f;
                float nextAddAt = 5f;
                while (t < 45.5f && !f.Gate.ExitBeatActive)
                {
                    f.Gate.TickCleanup(0.1f);
                    t += 0.1f;
                    if (t >= nextAddAt) { SpawnArea30Robot(spot); nextAddAt += 5f; }
                }
                Assert.IsTrue(f.Gate.ExitBeatActive,
                    $"AC1j: with the no-progress rule defeated by a fresh robot every 5s, the HARD LIMIT must " +
                    $"still open the exit by 45.5s regardless (t={t})");

                TeardownFixture(f);
            }
        }
    }
}
