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
    /// MV-915 (the one new test, per CC_AUTONOMY's testing policy): World 1's finale (a30, two Big
    /// Bermudas) end to end. Fails on base commit 77ac727, in two different ways:
    ///
    ///  1. The BigBermudaRig-related assertions compile unchanged against base commit and fail at
    ///     runtime: <c>BigBermudaRig</c> subscribed to <see cref="HudSignals.BossDefeated"/>, which only
    ///     fires once EVERY boss in an area is down (MV-591) — so a30's FIRST boss dying never started
    ///     its own rig's death at all; <c>rig1.Running</c> stays true until boss2 also dies.
    ///
    ///  2. The exit-fence assertions reference <c>WorldFinaleGate</c> and
    ///     <see cref="HudSignals.FinaleGateCrossed"/>, which do not exist on base commit at all — that
    ///     part of the regression is a missing feature, not a wrong branch, so the honest "fails on base
    ///     commit" evidence for it is a compiler error (CS0246 — the type could not be found), not an
    ///     assertion mismatch. Quoted alongside the runtime failure in the fix comment.
    ///
    /// a12/a20 (mid-run, single-boss areas) are asserted unaffected by both fixes: BossKilled and
    /// BossDefeated already fire together for a single-boss area, so switching BigBermudaRig's
    /// subscription changes nothing observable there, and WorldFinaleGate only ever exists for the
    /// world's actual last boss area.
    /// </summary>
    public sealed class Mv915WorldOneFinaleSequenceTests
    {
        private string _dir;
        private GameObject _root;
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv915-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-915 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
            Object.DestroyImmediate(_root);

            // Seal() -> ShowResults() builds a real "Result Screen" GameObject (RunTracker's own idiom,
            // untracked by this test's own handles) -- clean it up so it doesn't leak into later tests.
            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);

            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV625CrossAreaBossDeathTests/MV698WeaponCoreFinaleDropTests carry) -- drive it directly so
        // every listener actually subscribes to HudSignals the way it does for real.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private BigBermudaBoss NewBoss(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var boss = go.AddComponent<BigBermudaBoss>();
            _spawned.Add(go);
            return boss;
        }

        private BigBermudaRig NewRig(BigBermudaBoss boss)
        {
            BigBermudaRig rig = BigBermudaRig.CreateFor(boss);
            InvokeOnEnable(rig);
            _spawned.Add(rig.gameObject);
            return rig;
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

        private static float LastHealth(List<string> trace)
        {
            for (int i = trace.Count - 1; i >= 0; i--)
            {
                const string prefix = "BossHealthChanged(";
                if (!trace[i].StartsWith(prefix)) continue;
                string inner = trace[i].Substring(prefix.Length).TrimEnd(')');
                return float.Parse(inner);
            }
            Assert.Fail("no BossHealthChanged event was captured");
            return -1f;
        }

        [Test]
        public void A30TwoBossFinale_RunsTheFiveStepSequence_WithoutAffectingA12OrA20()
        {
            var cfg = new WorldConfig
            {
                dials = new WorldDials { areaCount = 30 },
                areas = new[]
                {
                    new WorldArea { id = "a12", index = 12, role = "boss",
                        origin = new WorldAreaOrigin(), size = new WorldAreaSize() },
                    new WorldArea { id = "a20", index = 20, role = "boss",
                        origin = new WorldAreaOrigin(), size = new WorldAreaSize() },
                    new WorldArea { id = "a30", index = 30, role = "boss",
                        origin = new WorldAreaOrigin(), size = new WorldAreaSize() },
                },
            };

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);

            var pickupDirector = _root.AddComponent<PickupDirector>();

            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            InvokeOnEnable(payoff);
            _spawned.Add(payoff.gameObject);

            var tracker = new GameObject("RunTracker Test").AddComponent<RunTracker>();
            InvokeOnEnable(tracker);
            _spawned.Add(tracker.gameObject);

            var gate = new GameObject("WorldFinaleGate Test").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(gate);
            _spawned.Add(gate.gameObject);

            var trace = new List<string>();
            HudSignals.BossKilled += _ => trace.Add("BossKilled");
            HudSignals.BossHealthChanged += n => trace.Add($"BossHealthChanged({n:0.##})");
            HudSignals.BossDefeated += () => trace.Add("BossDefeated");
            HudSignals.WeaponCoreDropped += () => trace.Add("WeaponCoreDropped");
            HudSignals.WeaponCoreCollected += () => trace.Add("WeaponCoreCollected");
            HudSignals.RunComplete += () => trace.Add("RunComplete");
            HudSignals.FinaleGateCrossed += () => trace.Add("FinaleGateCrossed");

            // --- a12: a single-boss mid-run area, killed FIRST -- exactly what a real run does. This is
            // BossVictoryPayoff's own single scene-wide instance arming itself off THIS death, unchanged
            // by this ticket. ---
            BigBermudaBoss a12Boss = NewBoss("a12_boss1");
            BigBermudaRig a12Rig = NewRig(a12Boss);
            BossCensus.Register(a12Boss, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 12);
            trace.Add("--- a12 boss dies ---");
            InvokeOnDeath(a12Boss);
            Assert.IsFalse(a12Rig.Running,
                "AC6 baseline: a12's own (only) boss dying must start its own rig dying -- BossKilled " +
                "and BossDefeated already fire together for a single-boss area, so this is unchanged by " +
                "the MV-915 fix");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "a12 is a mid-run area, never the finale -- it must never drop a Weapon Core");

            // --- a30: TWO bosses, registered after a12 fully clears (BossCensus's own per-fight
            // engagement latch has just reset, matching a real run). ---
            BigBermudaBoss boss1 = NewBoss("a30_boss1");
            BigBermudaBoss boss2 = NewBoss("a30_boss2");
            BigBermudaRig rig1 = NewRig(boss1);
            BigBermudaRig rig2 = NewRig(boss2);
            BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            BossCensus.Register(boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);

            // AC2: the first of a30's two bosses dies.
            trace.Add("--- a30 boss1 dies ---");
            InvokeOnDeath(boss1);

            Assert.IsFalse(rig1.Running,
                "AC2: a death spectacle (the rig's own death sequence) must fire for boss1 the instant " +
                "IT dies, not wait for boss2 -- this is the MV-915 root cause for symptom 1");
            Assert.IsTrue(rig2.Running, "boss2 is still alive -- its own rig must still be Running");
            Assert.AreEqual(0.5f, LastHealth(trace), 0.001f,
                "AC2: the combined boss health bar must resolve to 50% after the first of two bosses dies");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "AC2: no orb/Weapon Core may be granted on the first boss's death -- boss2 is still up");
            Assert.IsFalse(gate.IsOpen,
                "AC2: no gate-open signal may be raised before a30's robots are even reached");

            // AC3: the second (last) of a30's two bosses dies.
            trace.Add("--- a30 boss2 dies ---");
            InvokeOnDeath(boss2);

            Assert.IsFalse(rig2.Running, "AC3: a death spectacle must fire for boss2 too");
            Assert.AreEqual(0f, LastHealth(trace), 0.001f,
                "AC3: the combined boss health bar must resolve to 0 once both bosses are down");

            // MV-956: the fence now opens on THIS death (a30's last boss falling), never on RunComplete --
            // asserted here, before anything reports the area/world as empty, since nothing in this test
            // ever will (no RunComplete is fired at all below, and Victory still seals -- MV-956's whole
            // point is that no robot-clearing is required anywhere in the finale).
            Assert.IsTrue(gate.IsOpen,
                "MV-956: WorldFinaleGate must open the instant a30's last boss dies, not wait for RunComplete");

            List<Pickup> core = LivePickups().Where(p => p.Kind == PickupKind.WeaponCore).ToList();
            Assert.AreEqual(1, core.Count,
                "AC3: exactly one half-orb (Weapon Core) must be granted on the area's last boss falling");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.Device),
                "the World 1 finale drop must never be a Device");

            InvokeCollect(pickupDirector, core[0]);
            WeaponSystemState.OpenWeaponCoreMorphIfPending(worldIndex: 0);
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "AC3: once the collected core's morph applies (THE RIG's own open ceremony), the active " +
                "primary weapon must resolve to the laser (LPPE)");

            // AC4: the boss payoff beat (a12's own walk-out, unrelated to a30) finishing is RunTracker's
            // other seal condition. In the real game its Update() loop would already have fired
            // BossPayoffFinished (either Max satisfying IsAtDoor near a12, or its resultsTimeout fallback)
            // well before a30 is even reached; this test doesn't tick Update(), so drive that half of
            // RunTracker's seal condition directly, the same "exercise the domain event" idiom MV698's
            // own test uses. MV-956: no HudSignals.EmitRunComplete() anywhere in this test -- Victory
            // must seal without it.
            trace.Add("--- boss payoff walk-out beat finishes (a12's own, unrelated to a30) ---");
            HudSignals.EmitBossPayoffFinished();

            Assert.AreEqual(0, SaveSystem.Load(0).WorldIndex,
                "Victory must not seal before Max actually crosses the open gate, even though every " +
                "other seal condition has already landed");

            // AC5: walking through the open gateway. WorldFinaleGate.IsBeyondFence is the pure geometry
            // check the real Update() loop drives off Max's live position.
            Assert.IsTrue(WorldFinaleGate.IsBeyondFence(new Vector3(0f, 0f, 10f), fenceZ: 9f, centerX: 0f, halfWidth: 3f),
                "AC5 geometry: standing past the fence line, within its half-width, must read as beyond it");
            Assert.IsFalse(WorldFinaleGate.IsBeyondFence(new Vector3(10f, 0f, 10f), fenceZ: 9f, centerX: 0f, halfWidth: 3f),
                "AC5 geometry: standing outside the doorway's half-width must not read as beyond it");

            trace.Add("--- Max walks through the open fence ---");
            HudSignals.EmitFinaleGateCrossed();

            Assert.AreEqual(1, SaveSystem.Load(0).WorldIndex,
                "AC5: crossing the open gateway must advance the save's WorldIndex into World 2");

            // AC6: a20's mid-run boss (never touched above) is completely unaffected by any of this.
            BigBermudaBoss a20Boss = NewBoss("a20_boss1");
            BigBermudaRig a20Rig = NewRig(a20Boss);
            BossCensus.Register(a20Boss, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 20);
            trace.Add("--- a20 boss dies (after World 1 already sealed) ---");
            InvokeOnDeath(a20Boss);
            Assert.IsFalse(a20Rig.Running,
                "AC6: a20's own boss dying must still start its own rig dying, unaffected by a30's fix");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "AC6: a20 is never the final area (index 20 != dials.areaCount) -- it must not grant a " +
                "second Weapon Core (the first was already collected and destroyed above)");

            TestContext.WriteLine("MV-915 event trace:\n" + string.Join("\n", trace));
        }
    }
}
