using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1484 (the one new test, Rule 1): a Parts Cache is solid to player shots, SEALED or UNSEALED. Ricochet disc and Undertow
    /// lance are each fired at a cache with a robot 3 m directly behind it (resolved values, Tier 2):
    ///   SEALED   - the robot and the cache take 0 damage, a shield deflect is registered, the disc comes back reflected.
    ///   UNSEALED - the robot takes 0 damage and the cache's HP drops.
    /// Fails on the base commit: the cache sat on the default layer and was IsAlive only while unsealed, so every shot flew through.
    /// </summary>
    public sealed class MV1484PartsCacheBlocksShotsTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo PlayerControllerAwake = typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake = typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick = typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnable = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo RobotCc = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo RobotHealth = typeof(RobotEnemy).GetField("_health", NonPublicInstance);

        private readonly List<GameObject> _made = new List<GameObject>();
        private int _deflects;

        private void OnDeflect(Vector3 p, Vector3 d) => _deflects++;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            PartsCache.ResetRegistry();
            _deflects = 0;
            HudSignals.ShieldDeflected += OnDeflect;
        }

        [TearDown]
        public void TearDown()
        {
            HudSignals.ShieldDeflected -= OnDeflect;
            foreach (GameObject g in _made) if (g != null) Object.DestroyImmediate(g);
            _made.Clear();
            RicochetDisc.DestroyPoolForTests();
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            PartsCache.ResetRegistry();
        }

        private PartsCache NewCache(Vector3 at, bool sealedState)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _made.Add(go);
            go.transform.position = at;
            go.transform.localScale = new Vector3(WorldMapLoader.PartsCacheFootprint, WorldMapLoader.PartsCacheHeight, WorldMapLoader.PartsCacheFootprint);
            var cache = go.AddComponent<PartsCache>();
            cache.Configure("mv1484", WorldCatalog.IndexOf(WorldIds.City), 1);
            cache.Build();
            if (!sealedState)
                typeof(PartsCache).GetProperty("State").SetValue(cache, PartsCache.CacheState.Unsealed);
            return cache;
        }

        private RobotEnemy NewRobot(Vector3 at)
        {
            var go = new GameObject("Behind");
            _made.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            RobotCc.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotOnEnable.Invoke(robot, null);
            go.transform.position = at;
            RobotHealth.SetValue(robot, 300f);
            return robot;
        }

        [Test]
        public void RicochetDiscAndUndertow_AreStoppedByASealedOrUnsealedPartsCache_AndNothingReachesTheRobotBehind_MV1484()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                foreach (bool sealedState in new[] { true, false })
                {
                    string state = sealedState ? "SEALED" : "UNSEALED";

                    // ---- Ricochet: disc along +Z, cache at z=4, robot at z=7 (3 m behind).
                    PartsCache cache = NewCache(new Vector3(0f, 0.9f, 4f), sealedState);
                    RobotEnemy robot = NewRobot(new Vector3(0f, 0.7f, 7f));
                    Physics.SyncTransforms();
                    float cacheHp0 = cache.Health, robotHp0 = robot.HealthCurrent;
                    _deflects = 0;

                    RicochetDisc disc = RicochetDisc.Spawn(new Vector3(0f, 0.9f, 0f), Vector3.forward, Ricochet.DefaultSpeed, 2,
                        1, 0, 10f, 0f, 0f, 0f, Ricochet.DefaultRadius, Ricochet.DefaultLifetimeSeconds);
                    Assert.IsNotNull(disc, "disc must spawn");
                    for (int i = 0; i < 90 && disc.IsActiveDisc && disc.BouncesTaken == 0; i++) disc.Tick(1f / 60f);

                    Assert.AreEqual(robotHp0, robot.HealthCurrent, $"{state}: the disc must not reach the robot behind the cache");
                    Assert.GreaterOrEqual(disc.BouncesTaken, 1, $"{state}: the disc must bounce off the cache like a wall");
                    Assert.Less(disc.transform.forward.z, 0f, $"{state}: the disc's resolved direction must be reflected (z = {disc.transform.forward.z:0.00})");
                    if (sealedState)
                    {
                        Assert.AreEqual(cacheHp0, cache.Health, "SEALED: the cache takes no damage");
                        Assert.GreaterOrEqual(_deflects, 1, "SEALED: a deflect must be registered");
                    }
                    else Assert.Less(cache.Health, cacheHp0, "UNSEALED: the cache's HP must drop");

                    Object.DestroyImmediate(cache.gameObject); Object.DestroyImmediate(robot.gameObject);
                    RicochetDisc.DestroyPoolForTests();
                    RobotEnemy.ResetRegistry(); PartsCache.ResetRegistry();

                    // ---- Undertow: Max at the origin facing +Z, cache 3 m ahead, robot 6 m ahead.
                    var player = new GameObject("MV1484 Player", typeof(CharacterController), typeof(PlayerController));
                    _made.Add(player);
                    PlayerControllerAwake.Invoke(player.GetComponent<PlayerController>(), null);
                    var undertow = player.GetComponent<Undertow>();
                    UndertowAwake.Invoke(undertow, null);
                    WeaponSystemState.ApplyWeaponCoreMorph(WorldCatalog.IndexOf(WorldIds.Reef));
                    DevMode.Enabled = true; DevMode.InfiniteEnergy = true; DevMode.AutoFire = true;

                    cache = NewCache(player.transform.position + player.transform.forward * 3f, sealedState);
                    robot = NewRobot(player.transform.position + player.transform.forward * 6f);
                    Physics.SyncTransforms();
                    cacheHp0 = cache.Health; robotHp0 = robot.HealthCurrent;
                    _deflects = 0;

                    for (int i = 0; i < 20; i++) UndertowTick.Invoke(undertow, new object[] { 0.1f });

                    Assert.AreEqual(robotHp0, robot.HealthCurrent, $"{state}: the lance must be cut at the cache, not reach the robot behind it");
                    if (sealedState)
                    {
                        Assert.AreEqual(cacheHp0, cache.Health, "SEALED: the lance does no damage to the cache");
                        Assert.GreaterOrEqual(_deflects, 1, "SEALED: the lance hitting the cache must register a deflect");
                    }
                    else Assert.Less(cache.Health, cacheHp0, "UNSEALED: the lance must damage the cache");

                    DevMode.AutoFire = false;
                    Object.DestroyImmediate(player); Object.DestroyImmediate(cache.gameObject); Object.DestroyImmediate(robot.gameObject);
                    RobotEnemy.ResetRegistry(); PartsCache.ResetRegistry(); WeaponSystemState.Reset(); DevMode.Reset();
                }
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }
    }
}
