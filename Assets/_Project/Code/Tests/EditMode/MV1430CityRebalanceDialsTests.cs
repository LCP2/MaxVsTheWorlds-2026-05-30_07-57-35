using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1430 — the World 3 (City) rebalance, ONE test (MV-465 Rule 1), Tier 2 resolved values only:
    /// the LOADED City has no challenge pad, no vault, no carriesKey and no projector/jammer/welder in any
    /// garrison or composition; a City bruiser resolves to 200 max HP while World 2's is still 100; a real
    /// City Launcher's missile resolves to 1.5x the damage of a World 2 Launcher's; a City relay with 24
    /// robots alive in its area emits nothing for 10 s and emits again once one dies; and a City canister
    /// blast does 240 to a Heavy and kills a Bruiser 2 m away while Max, 2 m away, still takes 120.
    ///
    /// Fails on the base commit a85d8e8: it does not compile (no <c>WorldDials.rangedDamageMultiplier</c>,
    /// <c>RobotEnemy.SetRangedDamageMultiplier</c>, <c>HomingMissile.Damage</c>, <c>Canister.SetRobotDamageMultiplier</c>).
    /// </summary>
    public sealed class MV1430CityRebalanceDialsTests
    {
        private sealed class StubMax : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 0.02f;
        private static readonly Vector3 LauncherOrigin = new Vector3(-61234f, 0f, 48765f);
        private static readonly Vector3 RelayOrigin = new Vector3(71234f, 0f, -38765f);
        private static readonly Vector3 CanisterOrigin = new Vector3(81234f, 0f, 58765f);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            Canister.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DifficultyDirector.Reset();
            CharacterControllerMotion.HasWarnedOversizedMove = true;
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<HomingMissile>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(m.gameObject);
            foreach (GameObject go in _spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            Canister.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            EnemyNavigation.Reset();
            DifficultyDirector.Reset();
            DevTuning.Reset();
        }

        private RobotEnemy NewRobot(EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject("MV1430-robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(archetype);
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        /// <summary>A real Launcher of <paramref name="cfg"/>'s world, Max 6 m east, ticked until its first
        /// missile exists; returns that missile's resolved damage.</summary>
        private float LauncherMissileDamage(WorldConfig cfg)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(ground);
            ground.transform.position = new Vector3(LauncherOrigin.x, -1f, LauncherOrigin.z);
            ground.transform.localScale = new Vector3(200f, 2f, 200f);
            var max = new GameObject("MV1430 Max") { tag = "Player" };
            _spawned.Add(max);
            max.AddComponent<StubMax>();
            max.transform.position = LauncherOrigin + new Vector3(6f, 0f, 0f);
            var spawnerGo = new GameObject("MV1430 Spawner");
            _spawned.Add(spawnerGo);
            spawnerGo.transform.position = LauncherOrigin;
            var sp = spawnerGo.AddComponent<EnemySpawner>();
            sp.ConfigureWorldConfig(cfg);

            RobotEnemy r = sp.SpawnExact(EnemyKind.Launcher, 1)[0];
            r.transform.position = LauncherOrigin + Vector3.up * EnemyArchetype.Of(EnemyKind.Launcher).SpawnHeight;
            OnEnableMethod.Invoke(r, null);
            r.SetAreaIndex(1);
            r.SetRangedDamageMultiplier(cfg.dials.rangedDamageMultiplier);
            r.BeginDormant(true);
            Physics.SyncTransforms();
            r.TakeDamage(new DamageInfo(1f, r.transform.position, Vector3.right, Team.Player));

            HomingMissile[] found = Array.Empty<HomingMissile>();
            for (int i = 0; i < 1000 && found.Length == 0; i++)
            {
                r.Tick(Dt);
                found = UnityEngine.Object.FindObjectsByType<HomingMissile>(FindObjectsSortMode.None);
            }
            Assert.AreEqual(1, found.Length, "setup failure: the Launcher must fire a missile");
            float damage = found[0].Damage;
            UnityEngine.Object.DestroyImmediate(found[0].gameObject);
            return damage;
        }

        /// <summary>Total damage a canister at <see cref="CanisterOrigin"/> deals to a Heavy and a Bruiser 2 m
        /// away and to Max 2 m away, with <paramref name="robotMult"/> applied to robots.</summary>
        private void Blast(WorldConfig cfg, out float heavyTaken, out bool bruiserDead, out float maxTaken)
        {
            var canisterGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _spawned.Add(canisterGo);
            canisterGo.transform.position = CanisterOrigin;
            var canister = canisterGo.AddComponent<Canister>();
            canister.Build();
            canister.SetRobotDamageMultiplier(cfg.dials.hazardRobotDamageMultiplier);

            var playerGo = new GameObject("Player-MV1430", typeof(CharacterController)) { tag = "Player" };
            _spawned.Add(playerGo);
            typeof(PlayerController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(playerGo.AddComponent<PlayerController>(), null);
            var maxHealth = playerGo.AddComponent<PlayerHealth>();
            maxHealth.Initialize();
            playerGo.transform.position = CanisterOrigin + new Vector3(0f, 0f, -2f);

            RobotEnemy heavy = NewRobot(EnemyArchetype.For(EnemyKind.Heavy, cfg), CanisterOrigin + new Vector3(-2f, 0f, 0f));
            RobotEnemy bruiser = NewRobot(EnemyArchetype.For(EnemyKind.Bruiser, cfg), CanisterOrigin + new Vector3(2f, 0f, 0f));

            float heavyBefore = heavy.HealthCurrent;
            float maxBefore = maxHealth.Current;
            canister.TakeDamage(new DamageInfo(80f, CanisterOrigin, Vector3.down, Team.Player));
            for (int i = 0; i < 20; i++) canister.Tick(0.1f);

            heavyTaken = heavyBefore - heavy.HealthCurrent;
            bruiserDead = bruiser.HealthCurrent <= 0f;
            maxTaken = maxBefore - maxHealth.Current;
        }

        [Test]
        public void CityRebalance_NoVaultPadsOrCutRobots_DoubleHp_RangedDamage_AliveCap_AndRobotBlast()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            WorldConfig world2 = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(city, "city_config must load");
            Assert.IsNotNull(world2, "world2_config must load");

            // ---- 1. The loaded City: nothing of the cut features is left anywhere ----
            foreach (WorldArea a in city.areas)
            {
                Assert.AreEqual(0, (a.challenges ?? Array.Empty<WorldChallenge>()).Length, $"{a.id}: challenge pads must be gone");
                Assert.AreEqual(0, (a.vaults ?? Array.Empty<WorldVault>()).Length, $"{a.id}: vaults must be gone");
                foreach (WorldGarrisonEntry g in a.garrison ?? Array.Empty<WorldGarrisonEntry>())
                {
                    Assert.IsFalse(g.carriesKey, $"{a.id}: no garrison entry may carry a key");
                    string kind = (g.kind ?? "").ToLowerInvariant();
                    Assert.IsFalse(kind == "projector" || kind == "jammer" || kind == "welder", $"{a.id}: garrison kind '{kind}' is cut");
                }
                DifficultyEngine.Composition solved = city.SolveComposition(a.index);
                Assert.AreEqual(0, solved.Projector + solved.Jammer + solved.Welder, $"{a.id}: solved composition must hold no projector, jammer or welder");
            }

            // ---- 2. Max HP: a City bruiser 200, a World 2 bruiser still 100 ----
            Assert.AreEqual(200f, NewRobot(EnemyArchetype.For(EnemyKind.Bruiser, city), RelayOrigin + new Vector3(0f, 0f, 500f)).MaxHealth, 0.01f, "City bruiser max HP");
            Assert.AreEqual(100f, NewRobot(EnemyArchetype.For(EnemyKind.Bruiser, world2), RelayOrigin + new Vector3(0f, 0f, 520f)).MaxHealth, 0.01f, "World 2 bruiser max HP");

            // ---- 3. A City Launcher's missile hits 1.5x a World 2 Launcher's ----
            float w2Damage = LauncherMissileDamage(world2);
            float cityDamage = LauncherMissileDamage(city);
            Assert.Greater(w2Damage, 0f, "setup failure: World 2's missile must deal damage");
            Assert.AreEqual(w2Damage * 1.5f, cityDamage, 0.001f, "City Launcher missile damage must be 1.5x World 2's");

            // ---- 4. Relay alive cap: 24 alive in the area -> nothing for 10 s; one dies -> it emits again ----
            var relayGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(relayGo);
            relayGo.transform.position = RelayOrigin;
            relayGo.transform.localScale = new Vector3(2f, 1.5f, 2f);
            var relay = relayGo.AddComponent<Replicator>();
            relay.Build();
            relay.Configure(2);
            relay.SetAreaIndex(3);
            EnemySpawner relaySpawner = relay.GetComponent<EnemySpawner>();
            relaySpawner.ConfigureWorldConfig(city);

            var robots = new List<RobotEnemy>();
            for (int i = 0; i < 24; i++)
            {
                RobotEnemy rb = NewRobot(EnemyArchetype.Rusher, RelayOrigin + new Vector3(5f + i * 0.01f, 0f, 0f));
                rb.SetAreaIndex(3);
                robots.Add(rb);
            }
            relay.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robots[0].Current, "setup failure: the nearest robot must be lured to the relay");
            robots[0].transform.position = robots[0].ReplicatorSeekTarget;
            Assert.AreEqual(24, relay.AliveInArea(), "setup failure: 24 robots alive in the relay's area");

            for (int i = 0; i < 500; i++) relay.TickConsumption(Dt);   // 10 s
            Assert.AreEqual(0, relaySpawner.Emitted, "24 alive = the City aliveCap: the relay must emit nothing in 10 s");
            Assert.AreEqual(24, relay.AliveInArea(), "and it must not have consumed anything either");

            robots[5].Despawn();   // one dies: 23 alive
            relay.OnAreaEntered(3);   // MV-1488: a held relay keeps no queue, so Max (in the area) is what lures it again
            for (int i = 0; i < 500 && relaySpawner.Emitted == 0; i++) relay.TickConsumption(Dt);
            Assert.Greater(relaySpawner.Emitted, 0, "with 23 alive the relay must emit again");

            // ---- 5. Canister blast: 240 to a Heavy, kills a Bruiser at 2 m; Max, 2 m away, still takes 120 ----
            Blast(city, out float heavyTaken, out bool bruiserDead, out float maxTaken);
            Assert.AreEqual(240f, heavyTaken, 0.01f, "a City heavy 2 m from the blast takes 240");
            Assert.IsTrue(bruiserDead, "a City bruiser (200 HP) 2 m from the blast must die to 240");
            Assert.AreEqual(120f, maxTaken, 0.01f, "Max, 2 m from the blast, still takes 120");
        }
    }
}
