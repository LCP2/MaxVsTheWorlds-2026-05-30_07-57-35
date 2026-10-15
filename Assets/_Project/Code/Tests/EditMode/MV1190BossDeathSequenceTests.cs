using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1190 (the one new test, per CC_AUTONOMY's testing policy): Lee, closing MV-152, "it kind of
    /// just sinks into the ground" — Big Bermuda's old <c>BigBermudaRig.TickDeath</c> toppled and sank
    /// the body into the lawn over 0.55s, and the Weapon Core orb appeared the instant the boss died
    /// rather than at any kind of climax. This proves the replacement: a shared, timed 2.2s choreography
    /// (<see cref="BossDeathSequence"/>) that never lets the body's Y drop below where it died standing,
    /// fires exactly four chain-reaction blasts 0.25s apart and one finale blast at 1.6s, and defers the
    /// Weapon Core (<see cref="BossVictoryPayoff"/>) to that same 1.6s moment instead of spawning it
    /// synchronously on death.
    ///
    /// Fails on base commit (pre-MV-1190): neither <c>BigBermudaRig.TickDeath(float)</c> nor
    /// <c>BossDeathSequence</c> nor <c>BossVictoryPayoff.TickPendingWeaponCore</c> exist yet — the old
    /// rig sank the body (<c>transform.position += Vector3.down * (t*t*0.8f)</c>) and the old
    /// <c>BossVictoryPayoff.MaybeDropWeaponCore</c> spawned the orb synchronously inside
    /// <c>OnDefeated</c>, before this test's very first elapsed-time sample.
    /// </summary>
    public sealed class MV1190BossDeathSequenceTests
    {
        private string _dir;
        private GameObject _root;
        private readonly System.Collections.Generic.List<Object> _spawned = new System.Collections.Generic.List<Object>();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1190-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            BossCensus.Reset();
            Pickup.ResetRegistry();
            Time.timeScale = 1f;

            _root = new GameObject("MV-1190 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
            Object.DestroyImmediate(_root);

            BossCensus.Reset();
            Pickup.ResetRegistry();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // Same reflection idioms Mv915WorldOneFinaleSequenceTests/MV698WeaponCoreFinaleDropTests/
        // MV613BossRigScaleTests already use: Awake/OnEnable aren't reliably invoked for a plain
        // AddComponent outside Play mode, and the dt-driven tick methods are private on purpose (the
        // same "pulled out so a test can drive it at a controlled rate" seam SludgequeenRig.Tick uses).
        private static void InvokeAwake(Object c) =>
            c.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);

        private static void InvokeOnEnable(Object c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static void InvokeTickDeath(BigBermudaRig rig, float dt) =>
            typeof(BigBermudaRig).GetMethod("TickDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, new object[] { dt });

        private static void InvokeTickPendingWeaponCore(BossVictoryPayoff payoff, float dt) =>
            typeof(BossVictoryPayoff).GetMethod("TickPendingWeaponCore", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(payoff, new object[] { dt });

        private static Pickup[] LivePickups() =>
            Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        [Test]
        public void BigBermudaDeath_NeverSinks_FiresExactlyFourChainBlastsAndOneFinale_AndTimesTheOrbToIt()
        {
            // A one-boss final area, same shape MV1183SimplifiedFinaleTests/MV956FinaleDeathPositionTests
            // use — killing this one boss both empties its area and is the world's own final boss.
            var cfg = new WorldConfig
            {
                dials = new WorldDials { areaCount = 5 },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "a5", index = 5, role = "boss",
                        origin = new WorldAreaOrigin(), size = new WorldAreaSize(),
                        bosses = new[] { new WorldBoss() },
                    },
                },
            };

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);

            _root.AddComponent<PickupDirector>();

            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            InvokeOnEnable(payoff);
            _spawned.Add(payoff.gameObject);

            GameObject bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = bossGo.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            bossGo.transform.position = new Vector3(42f, 0f, 17f);
            BigBermudaBoss boss = bossGo.AddComponent<BigBermudaBoss>();
            InvokeAwake(boss);
            BigBermudaRig rig = BigBermudaRig.CreateFor(boss);
            // OnEnable isn't reliably invoked for AddComponent outside Play mode either (same note as
            // InvokeAwake above) -- without this, the rig never subscribes to HudSignals.BossKilled and
            // never starts its own death sequence below.
            InvokeOnEnable(rig);
            _spawned.Add(bossGo);
            _spawned.Add(rig.gameObject);

            BossCensus.Register(boss, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 5);

            float preDeathY = rig.transform.position.y;

            InvokeOnDeath(boss);   // the area's one and only, final boss dies

            Assert.IsNotNull(rig.DeathSequence, "killing the boss must start its rig's own death sequence");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "AC1c: the orb must not exist the instant the boss dies");

            const float dt = 1f / 60f;
            bool orbSeenBeforeFinale = false;

            for (int i = 0; i < 160; i++)   // 160/60 ~= 2.67s -- comfortably past the 2.2s sequence
            {
                InvokeTickDeath(rig, dt);
                InvokeTickPendingWeaponCore(payoff, dt);

                Assert.GreaterOrEqual(rig.transform.position.y, preDeathY - 0.001f,
                    $"AC1a: the body sank below its pre-death Y at t={rig.DeathSequence.Elapsed:0.000}s");

                if (rig.DeathSequence.Elapsed < BossDeathSequence.FinaleTime - 0.05f
                    && LivePickups().Any(p => p.Kind == PickupKind.WeaponCore))
                {
                    orbSeenBeforeFinale = true;
                }
            }

            Assert.IsFalse(orbSeenBeforeFinale, "AC1c: the orb must not appear before the 1.6s finale blast");
            Assert.AreEqual(1, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "AC1c: the orb must exist once the sequence has passed the finale");

            Assert.AreEqual(BossDeathSequence.ChainCount, rig.DeathSequence.ChainExplosionsFired,
                "AC1b: exactly four chain explosions must fire");
            Assert.IsTrue(rig.DeathSequence.BigBlastFired, "AC1b: the finale blast must fire");

            for (int i = 0; i < BossDeathSequence.ChainCount; i++)
            {
                float expected = BossDeathSequence.ThroesEnd + i * BossDeathSequence.ChainInterval;
                Assert.AreEqual(expected, rig.DeathSequence.ChainFireTimes[i], 0.05f,
                    $"AC1b: chain explosion {i} must fire at {expected:0.00}s +-0.05s");
            }
            Assert.AreEqual(BossDeathSequence.FinaleTime, rig.DeathSequence.FinaleFireTime.Value, 0.05f,
                "AC1b: the finale blast must fire at 1.6s +-0.05s");
        }
    }
}
