using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1309 (the one new test, per CC_AUTONOMY's testing policy): the City's real a30 (Station
    /// Forecourt) now authors GRIDLOCK and her four feeding pylons. While any of them is alive she
    /// takes no damage at all; once all four are destroyed a hit lands in full; her death drops the
    /// standard Weapon Core at her own position, through the same finale chain every other world's
    /// boss already uses — and the City's own former bossless path is gone outright, not merely
    /// unreachable behind a guard.
    ///
    /// Fails on ada8696 (the commit before this ticket): <c>city_config.json</c>'s a30 authors no
    /// "boss" and no "pylons" at all (role "exit" only) — <c>built.Actors.TryGetValue("gridlock", ...)</c>
    /// and the pylon count assertion both fail before the shield can even be exercised. See the fix
    /// comment for the captured failure output.
    ///
    /// Real entry points throughout: the actual <c>city_config.json</c>, loaded through the real
    /// <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> pipeline — same construction idiom
    /// <c>MV959World2FinaleChainTests</c> uses for World 2's real a31. <see cref="BossCensus.Register"/>
    /// is called directly with the area's own authored index (30), same technique that test uses for
    /// its own boss, since <see cref="GridlockBoss"/>'s real <c>Wake()</c> resolves its area index off
    /// <c>EnemyNavigation.Map</c>, which is null with no <c>BackyardPath</c> built in this fixture.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value —
    /// a health delta, <see cref="Pylon.LiveFeedersOf"/>'s own live count, a pickup's resolved
    /// position, a reflected method lookup — never an authored constant, never a rendered pixel.
    /// // Guards MV-1309
    /// </summary>
    public sealed class MV1309GridlockArenaFinaleTests
    {
        private static readonly Type PhaseType =
            typeof(GridlockBoss).GetNestedType("Phase", BindingFlags.NonPublic);

        private static void InvokePrivate(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static DestructibleHealth GetHealth(GridlockBoss boss) =>
            (DestructibleHealth)typeof(GridlockBoss)
                .GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(boss);

        /// <summary>Enters Fight phase directly, bypassing the real <c>Wake()</c> — see the class doc
        /// for why: this fixture builds no <see cref="MaxWorlds.Arena.BackyardPath"/>, so <c>Wake()</c>'s
        /// own <c>ResolveAreaIndex</c> would read area 0, not a30's real 30.</summary>
        private static void SetPhaseFight(GridlockBoss boss) =>
            typeof(GridlockBoss).GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(boss, Enum.Parse(PhaseType, "Fight"));

        private static void AdvanceWeaponCoreSpawn(BossVictoryPayoff payoff) =>
            typeof(BossVictoryPayoff).GetMethod("TickPendingWeaponCore", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(payoff, new object[] { BossDeathSequence.FinaleTime + 0.01f });

        private static Pickup[] LivePickups() =>
            UnityEngine.Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        private GameObject _root;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            _root = new GameObject("MV-1309 Root");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            EnemyNavigation.Reset();
            DevTuning.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
        }

        [Test]
        public void GridlockShieldBlocksUntilPylonsFall_AndHerDeathDropsTheStandardCore_NoBosslessPathLeft()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, $"'{WorldLibrary.City}' failed to load — see the error log above.");
            Assert.AreEqual(30, cfg.dials.areaCount, "a30 must still be the City's authored final area");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            MapBuild built = MapRuntime.Build(map, _root.transform);

            // --- setup: the real boss + her real feeding pylons, both built by city_config.json itself ---
            Assert.IsTrue(built.Actors.TryGetValue("gridlock", out GameObject bossGo) && bossGo != null,
                "city_config.json's a30 did not build a 'gridlock' boss");
            var boss = bossGo.GetComponent<GridlockBoss>();
            Assert.IsNotNull(boss, "a30's boss must build as a GridlockBoss");
            InvokePrivate(boss, "Awake");

            var pylons = built.Pylons.Where(p => p.Feeds == "gridlock").ToList();
            Assert.AreEqual(4, pylons.Count, "setup failure: a30 must author exactly 4 pylons feeding 'gridlock'");
            foreach (Pylon p in pylons) p.Build();
            foreach (Pylon p in pylons) Assert.IsTrue(p.IsAlive, "setup failure: a freshly built pylon must start alive");
            Assert.AreEqual(4, Pylon.LiveFeedersOf("gridlock"), "setup failure: all 4 live pylons must be counted");

            SetPhaseFight(boss);
            DestructibleHealth health = GetHealth(boss);
            BossCensus.Register(boss, "GRIDLOCK", phases: 1, current: health.Current, max: health.Max, areaIndex: 30);

            // === AC: GRIDLOCK takes 0 damage with all 4 pylons standing ===
            float beforeShield = health.Current;
            boss.TakeDamage(new DamageInfo(100f, bossGo.transform.position, Vector3.down, Team.Player));
            Assert.AreEqual(0f, beforeShield - health.Current, 1e-3f,
                "MV-1309 AC1: GRIDLOCK must take 0 damage while any pylon feeding her is alive");

            // === AC: once all 4 pylons are destroyed, the same hit lands in full ===
            foreach (Pylon p in pylons) p.TakeDamage(new DamageInfo(120f, p.transform.position, Vector3.down, Team.Player));
            foreach (Pylon p in pylons) Assert.IsFalse(p.IsAlive, "setup failure: 120 damage must destroy a 120 HP pylon");
            Assert.AreEqual(0, Pylon.LiveFeedersOf("gridlock"), "setup failure: destroying every feeding pylon must drop the live count to 0");

            float beforeOpen = health.Current;
            boss.TakeDamage(new DamageInfo(100f, bossGo.transform.position, Vector3.down, Team.Player));
            Assert.AreEqual(100f, beforeOpen - health.Current, 1e-3f,
                "MV-1309 AC1: once every feeding pylon is dead the same hit must land in full");

            // === AC: on her death, a WeaponCore pickup exists at her own position (the standard
            // === finale every other world's boss already uses) ===
            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg, WorldCatalog.IndexOf(WorldIds.City));

            var payoff = _root.AddComponent<BossVictoryPayoff>();
            InvokePrivate(payoff, "OnEnable");

            Vector3 deathPos = bossGo.transform.position;
            InvokePrivate(boss, "OnDeath");
            AdvanceWeaponCoreSpawn(payoff);

            Pickup core = LivePickups().SingleOrDefault(p => p.Kind == PickupKind.WeaponCore);
            Assert.IsNotNull(core, "MV-1309 AC1: GRIDLOCK's death must drop a WeaponCore pickup");
            float dist = Vector2.Distance(
                new Vector2(core.transform.position.x, core.transform.position.z),
                new Vector2(deathPos.x, deathPos.z));
            Assert.LessOrEqual(dist, 1f, "MV-1309 AC1: the Weapon Core must land at GRIDLOCK's own death position");

            // === AC: the City's old bossless finale path is gone outright, not merely unreachable ===
            MethodInfo bosslessMethod = typeof(WorldFinaleGate).GetMethod("ResolveBosslessFinaleIfReady",
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNull(bosslessMethod,
                "MV-1309 AC1: WorldFinaleGate.ResolveBosslessFinaleIfReady must no longer exist — a30 now " +
                "authors a real boss, so the City uses the standard boss finale every other world uses");
        }
    }
}
