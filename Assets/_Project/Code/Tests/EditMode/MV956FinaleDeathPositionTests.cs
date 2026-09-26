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

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-956 (the one new test, per CC_AUTONOMY's testing policy): Lee's own World 1 save (2026-09-26)
    /// killed a30's south boss (a30_boss2) before its north one (a30_boss1) -- the Weapon Core landed
    /// where the FIRST boss died, not the one that actually emptied the area, and the exit wall
    /// (<see cref="WorldFinaleGate"/>) never opened because it waited on <see cref="HudSignals.RunComplete"/>
    /// (every robot in a30 dead), which never landed with other a30 robots still alive.
    ///
    /// Drives the REAL, edited <c>world1_config.json</c> (a30_boss1 moved to (316,127); 15 top-left
    /// garrison entries removed) through the actual map build, so this test is also AC2's evidence --
    /// asserted on the built scene, not the JSON. AC1's two kill orders then run against fresh boss pairs
    /// at those exact authored positions, with a30's OTHER (still-alive, still-built) garrison and area
    /// 1's own ambient population left untouched throughout -- this test never fires
    /// <see cref="HudSignals.RunComplete"/> at all, and Victory still seals.
    /// </summary>
    public sealed class MV956FinaleDeathPositionTests
    {
        private string _dir;
        private GameObject _root;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv956-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            MaxWorlds.Weapons.WeaponSystemState.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-956 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            Object.DestroyImmediate(_root);

            // Seal() -> ShowResults() builds a real "Result Screen" GameObject (RunTracker's own idiom,
            // untracked by this test's own handles) -- clean it up so it doesn't leak into later tests.
            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            MaxWorlds.Weapons.WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV625CrossAreaBossDeathTests/Mv915WorldOneFinaleSequenceTests carry) -- drive it directly so
        // every listener actually subscribes to HudSignals the way it does for real.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        // Same "no stray collider beside the required CharacterController" construction
        // Mv915WorldOneFinaleSequenceTests/MV698WeaponCoreFinaleDropTests use.
        private static BigBermudaBoss NewBoss(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            return go.AddComponent<BigBermudaBoss>();
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

        [Test]
        public void A30FinaleOrb_FollowsTheLastBossOwnDeathSpot_AndTheGateOpensWithoutClearingAnyRobots()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            // --- AC2: the built scene, not the JSON. ---
            MapBuild built = MapRuntime.Build(map, _root.transform);

            Assert.IsTrue(built.Actors.TryGetValue("a30_boss1", out GameObject boss1Go) && boss1Go != null,
                "world1_config.json's a30_boss1 was not built");
            Assert.IsTrue(built.Actors.TryGetValue("a30_boss2", out GameObject boss2Go) && boss2Go != null,
                "world1_config.json's a30_boss2 was not built");

            Vector3 builtBoss1Pos = boss1Go.transform.position;
            Assert.AreEqual(316f, builtBoss1Pos.x, 0.5f,
                "AC2: a30_boss1's BUILT position must resolve to the authored move (x=316)");
            Assert.AreEqual(127f, builtBoss1Pos.z, 0.5f,
                "AC2: a30_boss1's BUILT position must resolve to the authored move (z=127)");

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, System.Array.Empty<CoverPiece>());   // fills area 1
            areaDirector.EnterArea(30);                                      // fills a30 directly

            (float x, float z)[] removedGarrisonCoords =
            {
                (297.5f, 130.5f), (299.5f, 130.5f), (301.5f, 130.5f),
                (297.5f, 128.5f), (299.5f, 128.5f), (301.5f, 128.5f),
                (304.5f, 126.5f), (306.5f, 126.5f), (309.5f, 126.5f), (311.5f, 126.5f),
                (304.5f, 124.5f), (306.5f, 124.5f), (308.5f, 124.5f),
                (297.5f, 117.5f), (299.5f, 117.5f),
            };
            RobotEnemy[] a30Enemies = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                .Where(r => r.AreaIndex == 30).ToArray();
            Assert.AreEqual(30, a30Enemies.Length,
                "AC2: a30 must build exactly its remaining 30 garrison robots (45 authored - 15 removed)");
            foreach ((float rx, float rz) in removedGarrisonCoords)
            {
                bool stillBuilt = a30Enemies.Any(r =>
                    Mathf.Abs(r.transform.position.x - rx) < 0.01f && Mathf.Abs(r.transform.position.z - rz) < 0.01f);
                Assert.IsFalse(stillBuilt,
                    $"AC2: a removed garrison entry ({rx},{rz}) was still built as a live RobotEnemy in a30");
            }

            // --- AC1: kill a30_boss2 (south) first, THEN a30_boss1 (north) -- Lee's own real save. a30's
            // other 30 garrison robots and area 1's own ambient population (both built above, both never
            // touched below) are the "at least one ordinary robot still alive" this ticket requires --
            // this test never fires HudSignals.RunComplete at all, and Victory still seals at the end. ---
            var pickupDirector = PickupDirector.EnsureInstalled();

            // RunTracker must exist BEFORE either phase's boss deaths -- HudSignals.WeaponCoreDropped
            // fires synchronously off the finale death itself, and RunTracker only latches
            // _weaponCoreAwaited (its seal condition, see TrySeal) if it was already subscribed when
            // that happens.
            var tracker = new GameObject("RunTracker Test").AddComponent<RunTracker>();
            InvokeOnEnable(tracker);

            var payoff1 = new GameObject("BossVictoryPayoff Test 1").AddComponent<BossVictoryPayoff>();
            var gate1 = new GameObject("WorldFinaleGate Test 1").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(payoff1);
            InvokeOnEnable(gate1);

            var boss1 = boss1Go.GetComponent<BigBermudaBoss>();
            var boss2 = boss2Go.GetComponent<BigBermudaBoss>();
            BossCensus.Register(boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);

            InvokeOnDeath(boss2);   // south dies first
            Assert.IsFalse(gate1.IsOpen, "boss1 (a30's other boss) is still alive -- the gate must stay shut");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "no orb may be granted while boss1 is still alive");

            Vector3 boss1DeathPos = boss1Go.transform.position;
            InvokeOnDeath(boss1);   // a30_boss1 is the one that actually empties a30 this time

            Assert.IsTrue(gate1.IsOpen, "MV-956: the wall must open the instant a30's last boss dies");

            Pickup core1 = LivePickups().Single(p => p.Kind == PickupKind.WeaponCore);
            float dist1 = Vector2.Distance(
                new Vector2(core1.transform.position.x, core1.transform.position.z),
                new Vector2(boss1DeathPos.x, boss1DeathPos.z));
            Assert.LessOrEqual(dist1, 1f,
                "MV-956: the orb must land within 1m (XZ) of a30_boss1's OWN death spot -- the boss that " +
                "actually died last, not a scene-wide lookup that could resolve to a30_boss2 instead");

            Object.DestroyImmediate(core1.gameObject);
            Object.DestroyImmediate(payoff1.gameObject);
            Object.DestroyImmediate(gate1.gameObject);
            BossCensus.Reset();

            // --- AC1 reversed: a fresh pair at the SAME authored positions, killed in the OPPOSITE
            // order -- a30_boss1 (north) first this time, so the orb must follow a30_boss2 (south). ---
            BigBermudaBoss boss1b = NewBoss("a30_boss1 (phase 2)");
            boss1b.transform.position = new Vector3(316f, 1.5f, 127f);
            BigBermudaBoss boss2b = NewBoss("a30_boss2 (phase 2)");
            boss2b.transform.position = new Vector3(320f, 1.5f, 86f);

            var payoff2 = new GameObject("BossVictoryPayoff Test 2").AddComponent<BossVictoryPayoff>();
            var gate2 = new GameObject("WorldFinaleGate Test 2").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(payoff2);
            InvokeOnEnable(gate2);

            BossCensus.Register(boss1b, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            BossCensus.Register(boss2b, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);

            InvokeOnDeath(boss1b);   // north dies first this time
            Assert.IsFalse(gate2.IsOpen, "boss2 is still alive -- the gate must stay shut");

            Vector3 boss2DeathPos = boss2b.transform.position;
            InvokeOnDeath(boss2b);   // a30_boss2 is the one that empties a30 this time

            Assert.IsTrue(gate2.IsOpen,
                "MV-956 (reversed order): the wall must open the instant a30's last boss dies");

            Pickup core2 = LivePickups().Single(p => p.Kind == PickupKind.WeaponCore);
            float dist2 = Vector2.Distance(
                new Vector2(core2.transform.position.x, core2.transform.position.z),
                new Vector2(boss2DeathPos.x, boss2DeathPos.z));
            Assert.LessOrEqual(dist2, 1f,
                "MV-956 (reversed order): the orb must land within 1m (XZ) of a30_boss2's OWN death spot");

            Assert.Greater(areaDirector.ActiveCount, 0,
                "MV-956: a30's other garrison robots (and area 1's ambient ones) must still be alive at " +
                "this point -- proof neither the orb, the gate, nor Victory below depend on the area/world " +
                "being cleared");

            // --- Victory seals despite those robots still alive: Max collects the core and crosses the
            // now-open finale gate. ---
            InvokeCollect(pickupDirector, core2);
            HudSignals.EmitBossPayoffFinished();
            Assert.AreEqual(0, SaveSystem.Load(0).WorldIndex,
                "Victory must not seal before Max actually crosses the open gate");

            HudSignals.EmitFinaleGateCrossed();
            Assert.AreEqual(1, SaveSystem.Load(0).WorldIndex,
                "MV-956: Victory must seal and advance WorldIndex even though a30/area 1 robots are still " +
                "alive, and even though HudSignals.RunComplete was never fired in this whole test");

            Object.DestroyImmediate(payoff2.gameObject);
            Object.DestroyImmediate(gate2.gameObject);
            Object.DestroyImmediate(tracker.gameObject);
        }
    }
}
