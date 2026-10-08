using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1111 (the one new test, per CC_AUTONOMY's testing policy): a world's own finale exit door
    /// (<see cref="MaxWorlds.Arena.MapRuntime"/>'s own <c>BuildExitDoorGate</c>) shipped with
    /// <see cref="AreaGate.Locked"/> left false — an ordinary, shootable <see cref="AreaGate"/> — so it
    /// took sustained primary-weapon fire and broke open exactly like any other area gate, letting Max
    /// walk a world's finale before its own conditions (final boss dead, orb taken, final area clear)
    /// were ever met. Lee's device playthrough (TestFlight v0.11.7, 2026-10-06): "Final World 2 area —
    /// was able to shoot the gate, open it and walk through before the orb was picked up."
    ///
    /// Loads every shipped world through the real production pipeline (<c>WorldLibrary.Load</c> -&gt;
    /// <c>WorldMapLoader.TryLoad</c> -&gt; <c>WorldTransitions.ApplyExitDoorway</c> -&gt;
    /// <c>MapRuntime.Build</c>, <see cref="MV997WorldExitDoorTests"/>'s own idiom) and, for each, fires
    /// that world's own real primary weapon at the real built exit gate through its own real per-tick hit
    /// path — <see cref="WaterBlaster.FireTick"/> for World 1's RCDA, a 100000-damage
    /// <see cref="SeekerPulse"/> for World 2's LPPE, <see cref="Undertow"/>'s own lance for World 3 (the
    /// exact per-weapon idioms <see cref="WaterBlasterGateDamageTests"/>/<see cref="MV1044UndertowOpensGateTests"/>/
    /// <see cref="SeekerPulseWorldTargetTests"/> already establish) — applying 100000 damage against each
    /// gate's own single-digit-hundreds <see cref="AreaGate.MaxHp"/>. Then drives that world's real
    /// finale (final boss death, Core collection, clean-up, <see cref="WorldFinaleGate"/>'s own weapon/
    /// exit beats, <see cref="MV997WorldExitDoorTests"/>'s own idiom again) through to the SAME real gate
    /// actually opening.
    ///
    /// Fails on base commit d30d293 for World 2: `BuildExitDoorGate` never sets <c>Locked</c>, so the
    /// LPPE pulse's 100000 damage sails straight through <c>AreaGate.TakeDamage</c> and
    /// <c>WorldFinaleGate.IsOpen</c> never even needs to flip for Max to already be standing beyond the
    /// doorway.
    /// </summary>
    public sealed class MV1111ExitGateNeverDamageableTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo WaterBlasterAwake = typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterFireTick = typeof(WaterBlaster).GetMethod("FireTick", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake = typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick = typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly FieldInfo BarVisualsField = typeof(WorldHealthBar).GetField("_barVisuals", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            // Same defensive sweep MV997WorldExitDoorTests/MV845/MV849/MV965/967 already make: a stray
            // component left by an earlier fixture in this EditMode batch would make a guard (e.g.
            // WorldJoinSequence.OpenExitDoor's own "already running" check) bail silently.
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            DevMode.Reset();
            DevTuning.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            // MV-1079: Beat A/Beat B's own scratch VFX/UI are never torn down by OnDisable here -- same
            // "OnDisable isn't reliably invoked for AddComponent outside Play mode" note MV997/MV1013
            // already carry.
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            DevMode.Reset();
            DevTuning.Reset();
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", NonPublicInstance).Invoke(component, null);

        private static void InvokeOnEnable(Object component) =>
            component.GetType().GetMethod("OnEnable", NonPublicInstance).Invoke(component, null);

        private static void InvokeOnDeath(Component boss) =>
            boss.GetType().GetMethod("OnDeath", NonPublicInstance).Invoke(boss, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, NonPublicInstance).SetValue(target, value);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", NonPublicInstance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", NonPublicInstance)
                .Invoke(director, new object[] { index, pickup });
        }

        /// <summary>World 1's own real primary (RCDA): loops <see cref="WaterBlaster.FireTick"/> — its
        /// own real per-tick hit path, GateHitResolver included — enough times for its own authored
        /// per-tick damage to sum past 100000.</summary>
        private static void FireWaterBlasterAt(Vector3 origin, Quaternion aim)
        {
            var go = new GameObject("MV1111 WaterBlaster");
            try
            {
                go.transform.SetPositionAndRotation(origin, aim);
                var blaster = go.AddComponent<WaterBlaster>();
                WaterBlasterAwake.Invoke(blaster, null);
                Physics.SyncTransforms();

                float perTick = blaster.DamagePerSecond * blaster.FireInterval;
                Assert.Greater(perTick, 0f, "precondition: WaterBlaster must deal real per-tick damage");
                int ticks = Mathf.CeilToInt(100000f / perTick);
                for (int i = 0; i < ticks; i++) WaterBlasterFireTick.Invoke(blaster, null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>World 3's own real primary (UNDERTOW): same loop idiom as
        /// <see cref="FireWaterBlasterAt"/>, through <see cref="Undertow"/>'s own real
        /// <c>Tick(dt)</c> — the same GateHitResolver path MV-1044 gave it.</summary>
        private static void FireUndertowAt(Vector3 origin, Quaternion aim)
        {
            var go = new GameObject("MV1111 Undertow");
            try
            {
                go.transform.SetPositionAndRotation(origin, aim);
                var undertow = go.AddComponent<Undertow>();
                UndertowAwake.Invoke(undertow, null);
                undertow.SetFiring(true);
                Physics.SyncTransforms();
                // The tank must never gate this -- InfiniteEnergy is DevMode's own default state (see
                // DevMode.Reset), just needs Enabled to actually apply (MV1044's own idiom).
                DevMode.Enabled = true;

                float perTick = undertow.DamagePerSecond * undertow.FireInterval;
                Assert.Greater(perTick, 0f, "precondition: Undertow must deal real per-tick damage");
                int ticks = Mathf.CeilToInt(100000f / perTick);
                for (int i = 0; i < ticks; i++) UndertowTick.Invoke(undertow, new object[] { undertow.FireInterval });
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>World 2's own real primary (LPPE): a single <see cref="SeekerPulse"/> carrying the
        /// full 100000 damage, driven through its own real flight <c>Tick</c> to a genuine hit
        /// (<c>SeekerPulse.TryHitWorldDamageable</c>, MV-749's own world-geometry path) rather than many
        /// small pulses — LPPE's own per-shot damage is an explicit <c>Fire(...)</c> argument, not a
        /// fixed authored tick rate the way RCDA/UNDERTOW's own per-tick damage is.</summary>
        private static void FireLppePulseAt(Vector3 origin, Vector3 dir)
        {
            const float step = 1f / 60f;
            const float cap = 3f;
            SeekerPulse pulse = SeekerPulse.Fire(origin, dir, speed: 18f, turnRateDegPerSec: 360f,
                lifetime: cap, damage: 100000f, lockRange: 14f, lockHalfAngleDeg: 35f);

            float elapsed = 0f;
            while (!pulse.IsSpent && elapsed < cap)
            {
                pulse.Tick(step);
                elapsed += step;
            }
            Assert.IsTrue(pulse.IsSpent, "precondition: the LPPE pulse must actually reach and resolve against the gate");
        }

        private readonly struct Row
        {
            public readonly string WorldKey;
            public readonly int WorldIndex;
            public readonly string BossId;

            public Row(string worldKey, int worldIndex, string bossId)
            {
                WorldKey = worldKey; WorldIndex = worldIndex; BossId = bossId;
            }
        }

        [Test]
        public void ExitGateResistsItsWorldsOwnPrimaryWeapon_AndOnlyOpensThroughTheRealFinale()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            var rows = new[]
            {
                new Row(WorldLibrary.World1, worldIndex: 0, bossId: "a30_boss1"),
                new Row(WorldLibrary.World2, worldIndex: 1, bossId: "sludgequeen"),
                new Row(WorldLibrary.World3, worldIndex: 2, bossId: "anchorhead"),
            };

            foreach (Row row in rows)
            {
                GameObject root = null, pathGo = null, gateGo = null, payoffGo = null, playerGo = null, sequenceGo = null;
                try
                {
                    WorldConfig cfg = WorldLibrary.Load(row.WorldKey);
                    Assert.IsNotNull(cfg, $"{row.WorldKey} failed to load — see the error log above.");
                    Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                    // --- build that world's REAL map through the normal MapRuntime path. ---
                    WorldTransitions.ApplyExitDoorway(map, cfg, row.WorldIndex);
                    root = new GameObject($"MV1111 Root {row.WorldKey}");
                    MapBuild built = MapRuntime.Build(map, root.transform);
                    Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                    AreaGate gate = built.ExitGate;
                    Assert.IsNotNull(gate, $"world {row.WorldIndex} must build a real exit AreaGate");
                    // AddComponent never fires Awake in this project's synchronous EditMode harness
                    // (confirmed empirically for AreaGate -- see AreaGateTests' own MV-386 note).
                    InvokeAwake(gate);

                    Assert.IsTrue(gate.IsAlive && !gate.IsOpen,
                        $"world {row.WorldIndex}: precondition -- the exit gate must start closed and intact");
                    float maxHp = gate.MaxHp;
                    Assert.Greater(maxHp, 0f, "precondition: the gate must carry real HP to lose if it were unprotected");

                    // --- AC1/AC2: sealed before the finale -- no damage, no "GATE" plate, through the
                    // REAL per-world primary weapon's own hit path, applying 100000 damage. ---
                    WeaponSystemState.ApplyWorldLoadout(row.WorldIndex);

                    Vector3 gateCentre = gate.transform.position;
                    Vector3 approach = gate.AwayFromPlayerDirection.sqrMagnitude > 1e-4f
                        ? gate.AwayFromPlayerDirection.normalized
                        : Vector3.back;
                    Vector3 origin = gateCentre - approach * 5f;
                    Quaternion aim = Quaternion.LookRotation(approach, Vector3.up);

                    switch (row.WorldIndex)
                    {
                        case 0: FireWaterBlasterAt(origin, aim); break;
                        case 1: FireLppePulseAt(origin, approach); break;
                        case 2: FireUndertowAt(origin, aim); break;
                        default: Assert.Fail($"no primary-weapon fixture wired for world index {row.WorldIndex}"); break;
                    }

                    Assert.IsTrue(gate.IsAlive,
                        $"world {row.WorldIndex}: the exit gate must survive its own world's primary fire");
                    Assert.AreEqual(maxHp, gate.HealthCurrent, 0.01f,
                        $"world {row.WorldIndex}: the exit gate must take NO damage at all from 100000 of its own " +
                        "world's primary fire before the finale (MV-1111's own regression)");
                    Assert.IsFalse(gate.IsOpen,
                        $"world {row.WorldIndex}: the exit gate must stay closed under 100000 damage before the finale");
                    Assert.AreNotEqual("GATE", gate.ReadoutName,
                        $"world {row.WorldIndex}: a sealed finale gate must read as the existing locked-door " +
                        "treatment, not a breakable GATE plate");

                    var bar = gate.GetComponent<WorldHealthBar>();
                    Assert.IsNotNull(bar, $"world {row.WorldIndex}: the exit gate must build its own WorldHealthBar");
                    var barVisuals = (RectTransform)BarVisualsField.GetValue(bar);
                    Assert.IsNotNull(barVisuals, $"world {row.WorldIndex}: WorldHealthBar must build its own bar visuals");
                    Assert.IsFalse(barVisuals.gameObject.activeSelf,
                        $"world {row.WorldIndex}: a sealed finale gate's health-bar fill must not be visible");

                    // --- AC3/AC4: drive that world's real finale -- final boss death, Core collection,
                    // clean-up, the weapon/exit beats -- and prove the SAME real gate opens. ---
                    pathGo = new GameObject($"MV1111 BackyardPath {row.WorldKey}");
                    var path = pathGo.AddComponent<BackyardPath>();
                    SetPrivateField(path, "_cfg", cfg);
                    SetPrivateField(path, "_map", map);
                    SetPrivateField(path, "_build", built);

                    var areaDirector = pathGo.AddComponent<AreaAccumulationDirector>();
                    areaDirector.ConfigureWorld(cfg, row.WorldIndex);

                    playerGo = new GameObject("MV1111 Max");
                    playerGo.AddComponent<CharacterController>();
                    playerGo.AddComponent<PlayerController>();

                    gateGo = new GameObject("WorldFinaleGate Test");
                    var finale = gateGo.AddComponent<WorldFinaleGate>();
                    InvokeAwake(finale);
                    InvokeOnEnable(finale);

                    payoffGo = new GameObject("BossVictoryPayoff Test");
                    var payoff = payoffGo.AddComponent<BossVictoryPayoff>();
                    InvokeOnEnable(payoff);

                    Assert.IsTrue(built.Actors.TryGetValue(row.BossId, out GameObject bossGo) && bossGo != null,
                        $"{row.WorldKey}'s a{cfg.dials.areaCount} boss ('{row.BossId}') was not built");
                    Component boss = (Component)bossGo.GetComponent<BigBermudaBoss>() ?? bossGo.GetComponent<SludgequeenBoss>();
                    Assert.IsNotNull(boss, $"world {row.WorldIndex}'s final boss must build as a boss component");

                    List<MonoBehaviour> finalBosses = built.Bosses
                        .Where(b => b != null && map.ZoneAt(b.transform.position.x, b.transform.position.z)?.AreaIndex == cfg.dials.areaCount)
                        .ToList();
                    Assert.IsNotEmpty(finalBosses, $"world {row.WorldIndex} built no boss inside its own final area");

                    foreach (MonoBehaviour b in finalBosses)
                        BossCensus.Register(b, "TEST BOSS", phases: 1, current: 100f, max: 100f, areaIndex: cfg.dials.areaCount);

                    Assert.IsFalse(finale.IsOpen, $"world {row.WorldIndex}'s gate must stay shut before its own boss dies");

                    foreach (MonoBehaviour b in finalBosses) InvokeOnDeath(b);

                    Assert.IsFalse(finale.IsOpen,
                        $"world {row.WorldIndex}: boss death alone must not open the exit -- the Core must be collected");

                    var pickupDirector = PickupDirector.EnsureInstalled();
                    Pickup core = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)
                        .Single(p => p.Kind == PickupKind.WeaponCore);
                    InvokeCollect(pickupDirector, core);

                    // MV-1079: collecting the Core starts Beat A (2.5s); this director's own per-area
                    // robot count is 0, so clean-up finds zero left and moves straight to Beat B, 1.0s of
                    // which is what actually calls WorldFinaleGate.Open().
                    finale.TickWeaponBeat(2.5f);
                    finale.TickExitBeat(1.0f);

                    Assert.IsTrue(finale.IsOpen,
                        $"world {row.WorldIndex}'s real finale must open the gate once the Core is collected");
                    Assert.IsTrue(built.ExitGate.IsOpen,
                        $"world {row.WorldIndex}: the SAME real map gate MapRuntime built must be the one that opened");

                    var sequence = Object.FindFirstObjectByType<WorldJoinSequence>();
                    if (sequence != null) sequenceGo = sequence.gameObject; // null for World 3 (no corridor to build)
                }
                finally
                {
                    if (sequenceGo != null) Object.DestroyImmediate(sequenceGo);
                    if (gateGo != null) Object.DestroyImmediate(gateGo);
                    if (payoffGo != null) Object.DestroyImmediate(payoffGo);
                    if (playerGo != null) Object.DestroyImmediate(playerGo);
                    if (pathGo != null) Object.DestroyImmediate(pathGo);
                    if (root != null) Object.DestroyImmediate(root);
                    foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                        Object.DestroyImmediate(stray.gameObject);
                    WorldJoinDressing.Clear();
                    StormdrainKit.Clear();
                    MaterialLibrary.Clear();
                    BossCensus.Reset();
                }
            }
        }
    }
}
