using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1081 (the one new test, per CC_AUTONOMY's testing policy): a real World 2 garrison survives
    /// both cutscenes that used to stomp it -- the <see cref="WorldJoinSequence"/> arrival walk and
    /// <see cref="WorldFinaleGate"/>'s weapon beat. On base commit d30d293,
    /// <c>SuspendGameplay</c>/<c>SuspendGameplayForBeat</c> set <c>RobotEnemy.enabled = false</c> directly
    /// on every active robot (and <c>RestoreGameplay</c>/<c>RestoreGameplayForBeat</c> set it back to
    /// <c>true</c>, which in a real build fires <c>OnEnable</c>'s own <c>ResetState()</c> and stomps a
    /// Dormant garrison robot, a Submerged Lurker, a converted robot and a mid-health Chase robot all
    /// back to a fresh full-health Chase). This EditMode run cannot see that OnEnable/ResetState half of
    /// it directly -- confirmed by direct probe that Unity defers OnEnable/OnDisable for a plain
    /// `.enabled` toggle outside Play mode here, same documented quirk
    /// MV865World2RouteOrderTests/MV828ReplicatorAreaIndexTests already name for AddComponent/SetActive --
    /// but the `.enabled = false` assignment ITSELF is synchronous and needs no callback to observe, so
    /// this test's fail-first signal is reading `RobotEnemy.enabled` back immediately after
    /// <c>InitializeArrival</c>/the Core collect, before any tick. Base-commit failure (quoted in the fix
    /// comment): that read comes back <c>false</c> for all four tracked robots.
    /// </summary>
    public sealed class MV1081CutsceneFreezeTests
    {
        private string _dir;
        private GameObject _hostGo;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1081-tests");
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

            _hostGo = new GameObject("MV-1081 Host");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<WorldFinaleGate>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BossVictoryPayoff>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_hostGo != null) Object.DestroyImmediate(_hostGo);

            // Seal()/the beats build real scratch GameObjects (RunTracker's Result Screen, Beat A/B's
            // own VFX/UI) untracked by this test's own handles -- same cleanup idiom MV1078/Mv915 use.
            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

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
            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
            RenderSettings.fog = false;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV1078FinaleWeaponAndCleanupTests/Mv915WorldOneFinaleSequenceTests carry) -- drive it directly
        // so every listener actually subscribes to HudSignals the way it does for real. Plumbing only --
        // none of the freeze mechanism under test is reached through reflection anywhere in this file.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static readonly FieldInfo RobotActiveListField =
            typeof(RobotEnemy).GetField("_active", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>AreaAccumulationDirector's garrison placement builds/activates a robot directly
        /// (GameObject.CreatePrimitive + AddComponent&lt;RobotEnemy&gt;, then SetActive(true) on an
        /// already-active instance) -- Unity never fires OnEnable for either step outside Play mode, so
        /// RobotEnemy.Active (only OnEnable populates it) silently stays empty for every real garrison
        /// spawn in EditMode. Same documented workaround MV828ReplicatorAreaIndexTests/
        /// MV865World2RouteOrderTests/MV531DissolveSnapshotTests already use: add the static-list entry
        /// directly rather than call OnEnable, which would also re-run ResetState and stamp a placed
        /// Dormant/Submerged robot back to a fresh full-health Chase -- exactly the corruption this
        /// ticket's freeze fix exists to stop, so this helper must never be the thing that causes it.
        /// Plumbing only: SetCutsceneFrozen, Current, HealthCurrent, IsConverted, Team, IsDamageable and
        /// Renderer.enabled are all read through public, engine-resolved surface everywhere else in this
        /// file.</summary>
        private static void RegisterAllUnregisteredRobots()
        {
            var list = (System.Collections.Generic.List<RobotEnemy>)RobotActiveListField.GetValue(null);
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (!list.Contains(r)) list.Add(r);
        }

        private static Pickup[] LivePickups() =>
            Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        /// <summary>Every resolved value a cutscene freeze must leave untouched, read only through
        /// public, engine-resolved surface -- no private field, no authored constant.</summary>
        private readonly struct Snapshot
        {
            public readonly RobotEnemy.State State;
            public readonly float Health;
            public readonly bool IsConverted;
            public readonly Team Team;
            public readonly bool IsDamageable;
            public readonly bool AnyBodyRendererEnabled;

            public Snapshot(RobotEnemy r)
            {
                State = r.Current;
                Health = r.HealthCurrent;
                IsConverted = r.IsConverted;
                Team = r.Team;
                IsDamageable = r.IsDamageable;
                AnyBodyRendererEnabled = r.GetComponentsInChildren<Renderer>(true).Any(rend => rend.enabled);
            }
        }

        private static void AssertUnchanged(string label, RobotEnemy r, Snapshot before)
        {
            var after = new Snapshot(r);
            Assert.AreEqual(before.State, after.State, $"MV-1081 {label}: state must survive the cutscene freeze unchanged");
            Assert.AreEqual(before.Health, after.Health, 0.01f, $"MV-1081 {label}: health must survive the cutscene freeze unchanged");
            Assert.AreEqual(before.IsConverted, after.IsConverted, $"MV-1081 {label}: IsConverted must survive the cutscene freeze unchanged");
            Assert.AreEqual(before.Team, after.Team, $"MV-1081 {label}: Team must survive the cutscene freeze unchanged");
            Assert.AreEqual(before.IsDamageable, after.IsDamageable, $"MV-1081 {label}: IsDamageable must survive the cutscene freeze unchanged");
            Assert.AreEqual(before.AnyBodyRendererEnabled, after.AnyBodyRendererEnabled, $"MV-1081 {label}: body-renderer visibility must survive the cutscene freeze unchanged");
        }

        [Test]
        public void World2Garrison_SurvivesTheArrivalWalkAndTheFinaleWeaponBeat_Unchanged()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "world2_config failed to load -- see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapBuild build = MapRuntime.Build(map, _hostGo.transform);

            var areaDirector = _hostGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg, worldIndex: 1);
            areaDirector.Configure(map, build.Cover);
            areaDirector.EnterArea(2);   // a2 "Grate Hall" -- rushers, lurkers and sludgers, all garrison-seeded

            // MV-514's own head-start/park dance may leave a pre-placed garrison member inactive until
            // its own area is actually reached -- force every a2 member active so this fixture's lookups
            // below see all of them, exactly as a real arrival at a2 would. SetParked(false) is the real
            // production un-park path (sets _skipResetOnNextEnable itself); never touches state/health.
            foreach (RobotEnemy parked in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (parked.AreaIndex == 2 && !parked.gameObject.activeSelf) parked.SetParked(false);

            // See RegisterAllUnregisteredRobots's own doc comment: real garrison placement never fires
            // OnEnable outside Play mode, so WorldJoinSequence/WorldFinaleGate's SuspendGameplay(ForBeat)
            // -- which both freeze off RobotEnemy.Active -- would otherwise see an empty list and freeze
            // nothing at all, silently passing this test for the wrong reason.
            RegisterAllUnregisteredRobots();

            RobotEnemy[] a2 = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                .Where(r => r.AreaIndex == 2).ToArray();
            RobotEnemy[] rushers = a2.Where(r => r.Kind == EnemyKind.Rusher).ToArray();
            RobotEnemy[] lurkers = a2.Where(r => r.Kind == EnemyKind.Lurker).ToArray();
            Assert.GreaterOrEqual(rushers.Length, 3, "a2's own authored garrison must place at least 3 rushers");
            Assert.GreaterOrEqual(lurkers.Length, 1, "a2's own authored garrison must place at least 1 lurker");

            // (a) a Dormant garrison robot, damaged to 40% -- left alone, never activated.
            RobotEnemy dormant = rushers[0];
            Assert.AreEqual(RobotEnemy.State.Dormant, dormant.Current, "setup: a2's rushers must deploy Dormant");
            dormant.SetHealthFraction(0.4f);

            // (b) a Submerged Lurker -- deployed Submerged by the real garrison path (BeginDormant routes
            // a Lurker straight into BeginSubmerged); never touched further.
            RobotEnemy submerged = lurkers[0];
            Assert.AreEqual(RobotEnemy.State.Submerged, submerged.Current, "setup: a2's lurkers must deploy Submerged");

            // (c) a captured robot -- TryConvert(requirePortExposed: false) is TRAP's own real
            // conversion entry point, independent of HP.
            RobotEnemy converted = rushers[1];
            Assert.IsTrue(converted.TryConvert(requirePortExposed: false),
                "setup: TryConvert must succeed on a live, not-yet-converted robot");

            // (d) an awake Chase robot at 55% -- Activate() + Tick() is the real Dormant -> Alert ->
            // Chase wake path (the same one a hit, or DormantWakeScheduler's own sight check, drives).
            RobotEnemy chasing = rushers[2];
            chasing.Activate();
            for (int i = 0; i < 400 && chasing.Current != RobotEnemy.State.Chase; i++) chasing.Tick(0.05f);
            Assert.AreEqual(RobotEnemy.State.Chase, chasing.Current, "setup: Activate()+Tick must reach Chase");
            chasing.SetHealthFraction(0.55f);

            var beforeArrival = new[]
            {
                ("Dormant garrison robot", dormant, new Snapshot(dormant)),
                ("Submerged Lurker", submerged, new Snapshot(submerged)),
                ("captured robot", converted, new Snapshot(converted)),
                ("awake Chase robot", chasing, new Snapshot(chasing)),
            };

            // ---- WorldJoinSequence: the real World 2 arrival walk, off the real World 1 -> World 2
            // WorldTransitionEntry (MV1077ArrivalVisibilityTests' own setup). ----
            _playerGo = new GameObject("Max") { tag = "Player" };
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");

            bool arrivalFinished = false;
            var sequence = new GameObject("WorldJoinSequence (Arrival)").AddComponent<WorldJoinSequence>();
            sequence.InitializeArrival(cfg, map, entry, fromWorldIndex: 0, player, () => arrivalFinished = true);

            // MV-1081's own fail-first signal. SuspendGameplay() has already run synchronously inside
            // InitializeArrival, above -- observable immediately, with no OnEnable/OnDisable callback
            // needed (Unity defers those outside Play mode, same documented quirk
            // MV865World2RouteOrderTests/MV828ReplicatorAreaIndexTests name for AddComponent/SetActive;
            // confirmed here to extend to a plain `.enabled` toggle too, which is why the rest of this
            // test's assertions below can't by themselves tell base from fixed in EditMode). On base
            // commit, SuspendGameplay sets RobotEnemy.enabled = false directly on every one of these --
            // no callback required to observe THAT part. SetCutsceneFrozen never touches `enabled`.
            foreach (var (label, robot, _) in beforeArrival)
                Assert.IsTrue(robot.enabled,
                    $"MV-1081 {label}: a cutscene must never disable the RobotEnemy component to pause it (SetCutsceneFrozen, not enabled = false)");

            for (int i = 0; i < 3000 && !arrivalFinished; i++) sequence.Tick(0.02f);
            Assert.IsTrue(arrivalFinished, "the arrival sequence must reach its own end within 60s of simulated walking.");

            foreach (var (label, robot, before) in beforeArrival)
                AssertUnchanged($"after the arrival walk ({label})", robot, before);

            // ---- WorldFinaleGate: World 2's own final boss (a21, dials.areaCount == 21) dies, drops the
            // Weapon Core, and collecting it starts Beat A (WEAPON TAKEN) -- the same chain
            // MV1078FinaleWeaponAndCleanupTests drives, against World 2's real boss instead of a hand-
            // rolled fixture. MapRuntime.BuildBoss always builds a BigBermudaBoss body regardless of the
            // authored boss id ("sludgequeen" here) -- SludgequeenBoss is not yet wired into the loader
            // (see its own class doc comment) -- so this is the real component the real loader built. ----
            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            var gate = new GameObject("WorldFinaleGate Test").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(payoff);
            InvokeOnEnable(gate);

            BigBermudaBoss boss = build.Bosses.Single();
            BossCensus.Register(boss, "SLUDGEQUEEN", 1, current: 100f, max: 100f, areaIndex: 21);

            var pickupDirector = PickupDirector.EnsureInstalled();
            InvokeOnDeath(boss);
            Assert.IsFalse(gate.IsOpen, "boss death alone must not open the exit.");

            Pickup core = LivePickups().Single(p => p.Kind == PickupKind.WeaponCore);
            InvokeCollect(pickupDirector, core);

            var beforeBeat = new[]
            {
                ("Dormant garrison robot", dormant, new Snapshot(dormant)),
                ("Submerged Lurker", submerged, new Snapshot(submerged)),
                ("captured robot", converted, new Snapshot(converted)),
                ("awake Chase robot", chasing, new Snapshot(chasing)),
            };

            // InvokeCollect, above, synchronously ran BeginWeaponBeat -> SuspendGameplayForBeat -- same
            // fail-first signal as the arrival walk's own check.
            foreach (var (label, robot, _) in beforeBeat)
                Assert.IsTrue(robot.enabled,
                    $"MV-1081 {label}: the finale weapon beat must never disable the RobotEnemy component either");

            gate.TickWeaponBeat(2.5f);   // WeaponBeatDuration -- runs Beat A to its own end in one call

            foreach (var (label, robot, before) in beforeBeat)
                AssertUnchanged($"after the finale weapon beat ({label})", robot, before);

            Object.DestroyImmediate(payoff.gameObject);
            Object.DestroyImmediate(gate.gameObject);
        }
    }
}
