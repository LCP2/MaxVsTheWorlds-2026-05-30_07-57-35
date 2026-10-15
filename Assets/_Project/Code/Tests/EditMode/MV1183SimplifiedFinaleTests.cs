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
    /// MV-1183 (the one new test, per CC_AUTONOMY's testing policy): Lee's device playtests (12-14 Oct)
    /// found World 1 a30's two-boss finale "totally broken" -- killing the first Big Bermuda dropped the
    /// orb while the second sat Dormant, never approached, because <c>BossCensus.AnyLivingIn</c> only
    /// ever sees a REGISTERED boss. This test reproduces exactly that: boss2 is deliberately left
    /// unregistered (never woken) while boss1 dies, then is registered and killed only afterward, the
    /// same "wake on first hit, then die" order a real Dormant companion boss follows.
    ///
    /// Also covers Lee's full simplified rule in one pass: the orb only appears once BOTH authored
    /// bosses are down; taking it destroys every robot still alive in the final area (awake and
    /// Dormant alike) and opens the exit, in the same frame, with no clean-up count/ring/arrow left
    /// behind; and a resume afterward leaves the door open.
    ///
    /// Fails on base commit d31842c: boss1 dying alone already reads a30 as "empty" (<c>BossCensus.AnyLivingIn</c>
    /// has nothing else registered), so <c>BossVictoryPayoff</c> drops the orb immediately -- the
    /// assertion "no orb exists after boss1 dies" fails there. Collecting the orb then only starts the
    /// MV-1122 clean-up phase (no robots are destroyed and the door does not open in the same call) --
    /// the later assertions fail too.
    /// </summary>
    public sealed class MV1183SimplifiedFinaleTests
    {
        private string _dir;
        private GameObject _root;
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1183-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-1183 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
            Object.DestroyImmediate(_root);

            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable/Awake aren't reliably invoked for a plain AddComponent outside Play mode -- drive
        // them directly (same idiom Mv915WorldOneFinaleSequenceTests/MV698WeaponCoreFinaleDropTests use).
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        // MV-1190: the Weapon Core no longer drops synchronously on death -- it drops at
        // BossDeathSequence.FinaleTime (1.6s) into the dying boss's own death choreography. A single
        // big tick past that threshold fires the deferred spawn immediately, same as a real 1.6s wait
        // would, with no real clock needed.
        private static void AdvanceWeaponCoreSpawn(BossVictoryPayoff payoff) =>
            typeof(BossVictoryPayoff).GetMethod("TickPendingWeaponCore", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(payoff, new object[] { BossDeathSequence.FinaleTime + 0.01f });

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private BigBermudaBoss NewBoss(string name, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var boss = go.AddComponent<BigBermudaBoss>();
            _spawned.Add(go);
            return boss;
        }

        private RobotEnemy NewAliveRobot(string name, int areaIndex)
        {
            var go = new GameObject(name);
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            InvokeOnEnable(e); // runs ResetState(); SetAreaIndex must come after, or it gets clobbered
            e.SetAreaIndex(areaIndex);
            _spawned.Add(go);
            return e;
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
        public void TwoBossFinale_OrbWaitsForBoth_ThenPickupClearsTheAreaAndOpensTheDoor()
        {
            var cfg = new WorldConfig
            {
                dials = new WorldDials { areaCount = 30 },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "a30", index = 30, role = "boss",
                        origin = new WorldAreaOrigin(), size = new WorldAreaSize(),
                        // The authored total MV-1183 gates on -- two bosses, same shape as World 1's
                        // real a30 (world1_config.json).
                        bosses = new[] { new WorldBoss(), new WorldBoss() },
                    },
                },
            };

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);

            var pickupDirector = _root.AddComponent<PickupDirector>();

            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            InvokeOnEnable(payoff);
            _spawned.Add(payoff.gameObject);

            var gate = new GameObject("WorldFinaleGate Test").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(gate);
            _spawned.Add(gate.gameObject);

            var objectiveTexts = new List<string>();
            HudSignals.Objective += (text, _) => { if (text != null) objectiveTexts.Add(text); };

            var boss1Pos = new Vector3(10f, 0f, 0f);
            var boss2Pos = new Vector3(20f, 0f, 0f);
            BigBermudaBoss boss1 = NewBoss("a30_boss1", boss1Pos);
            BigBermudaBoss boss2 = NewBoss("a30_boss2", boss2Pos);

            // Four ordinary robots in the final area -- three awake, one Dormant (placed as authored
            // garrison, Lee's own device bug: "a30 (two Big Bermudas)... the second just sits dormant").
            RobotEnemy r1 = NewAliveRobot("a30_robot1", 30);
            RobotEnemy r2 = NewAliveRobot("a30_robot2", 30);
            RobotEnemy r3 = NewAliveRobot("a30_robot3", 30);
            RobotEnemy r4 = NewAliveRobot("a30_robot4_dormant", 30);
            r4.BeginDormant(authoredSlot: true);
            Assert.IsTrue(r4.IsDormant, "precondition: the fourth robot must actually be Dormant");

            // boss1 wakes (Register mirrors a real boss's first-hit Wake()) and dies -- boss2 is left
            // UNREGISTERED, exactly like a real Dormant companion boss nobody has approached yet.
            BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            InvokeOnDeath(boss1);

            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "killing the first of a30's two bosses must not drop the orb -- the second is still alive, " +
                "just Dormant and never yet approached");
            Assert.IsFalse(gate.IsOpen, "the exit door must stay shut while a boss is still alive");

            // Now boss2 is found and killed too -- the LAST boss in the area.
            BossCensus.Register(boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            InvokeOnDeath(boss2);
            AdvanceWeaponCoreSpawn(payoff);

            Pickup[] cores = LivePickups().Where(p => p.Kind == PickupKind.WeaponCore).ToArray();
            Assert.AreEqual(1, cores.Length, "killing the LAST boss in the area must drop exactly one orb");
            Assert.LessOrEqual(Vector3.Distance(cores[0].transform.position, boss2Pos), 1.5f,
                "the orb must appear where the boss that actually died LAST fell");
            Assert.Greater(Vector3.Distance(cores[0].transform.position, boss1Pos), 5f,
                "the orb must not land at the FIRST boss's own death spot");
            Assert.IsFalse(gate.IsOpen, "the exit door must still be shut -- the orb hasn't been taken yet");

            // Pick up the orb. No frames advance beyond this single call.
            InvokeCollect(pickupDirector, cores[0]);

            Assert.IsTrue(gate.IsOpen,
                "taking the orb must open the exit door immediately, in the same call");
            Assert.IsFalse(r1.IsAlive, "every robot in the final area must be destroyed when the orb is taken");
            Assert.IsFalse(r2.IsAlive, "every robot in the final area must be destroyed when the orb is taken");
            Assert.IsFalse(r3.IsAlive, "every robot in the final area must be destroyed when the orb is taken");
            Assert.IsFalse(r4.IsAlive,
                "a Dormant robot must be destroyed too, not skipped because it was never chasing anyone");

            Assert.IsFalse(objectiveTexts.Any(t => t.Contains("CLEAR THE AREA") || t.Contains("ROBOTS LEFT")),
                "MV-1183: the retired clean-up phase's objective strip must never be shown");
            // The clean-up count/ring/arrow objects themselves are no longer even buildable: this ticket
            // deleted the methods that created them (TickCleanup/UpdateCleanupVisuals) and the two VFX
            // classes (EdgeArrow, GroundChevron) that existed only to serve them and the retired MV-1125
            // trail -- nothing left to assert a count of zero against.

            // A resume afterward must never re-lock the door.
            SetPrivateField(gate, "_cfg", cfg);
            Assert.IsTrue(BossCensus.IsAreaDefeated(30), "precondition: the final area must already read defeated");
            gate.ResumeFromCheckpoint(weaponGranted: true, exitOpen: true);

            Assert.IsTrue(gate.IsOpen, "MV-1183: the exit must never be locked again after a RESUME");
        }
    }
}
