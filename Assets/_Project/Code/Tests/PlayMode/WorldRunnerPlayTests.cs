using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The origination wiring MV-270 adds on top of the pure engine classes (MV-268/269):
    /// <see cref="SupplyLineNetworkTests"/> proves the network's own logic in isolation; this proves a
    /// BUILT boss <see cref="AreaGate"/> actually stays locked until a real, built shed
    /// <see cref="MowerHutch"/> dies, and opens itself the moment it does — the "all-sheds-destroyed"
    /// condition <see cref="MapValidation.WorldBossGate"/> requires every boss gate to carry, made real.
    /// </summary>
    public sealed class WorldRunnerPlayTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevTuning.Reset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            DevTuning.Reset();
            yield return null;
        }

        /// <summary>One shed area behind the entry stub, one boss area behind it — the smallest world
        /// that exercises the whole chain: shed dies -> supply line halts -> AllShedsDestroyed -> boss
        /// gate unlocks and force-opens.</summary>
        private static WorldConfig OneShedWorld() => new WorldConfig
        {
            world = "Test World",
            dials = new WorldDials
            {
                areaCount = 1, baseThreat = 1f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f }, toughnessCurve = new WorldToughnessCurve(), powerupCadence = 1,
                band = new WorldBand(),
            },
            enemyTypes = new WorldEnemyTypes
            {
                small = new WorldEnemyTypeEntry { thv = 1f }, large = new WorldEnemyTypeEntry { thv = 1f },
                heavy = new WorldEnemyTypeEntry { thv = 1f }, brute = new WorldEnemyTypeEntry { thv = 1f },
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "shed", hasShed = true, garrisonDensity = "none",
                    shed = new WorldShed { x = 0f, z = 10f },
                    origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
                new WorldArea
                {
                    id = "boss", index = 2, role = "boss+exit",
                    origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                },
            },
        };

        private static DamageInfo Hit(float amount, DamageSource source, Team attacker = Team.Player) =>
            new DamageInfo(amount, Vector3.zero, Vector3.forward, attacker, source: source);

        private MapBuild _built;

        private IEnumerator Build(WorldConfig cfg = null)
        {
            cfg ??= OneShedWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _root = new GameObject("WorldRunner Test Root");
            _built = MapRuntime.Build(map, _root.transform);

            var runner = _root.AddComponent<WorldRunner>();
            runner.Configure(cfg, _built);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BossGate_StartsLockedAndRejectsPrimaryFireEvenPastItsOwnMaxHp()
        {
            yield return Build();
            var bossGate = _built.Actors["bg"].GetComponent<AreaGate>();

            Assert.IsTrue(bossGate.Locked, "the boss gate must start locked — nothing has destroyed a shed yet");

            bossGate.TakeDamage(Hit(bossGate.MaxHp + 999f, DamageSource.PrimaryWeapon));
            yield return null;

            Assert.IsTrue(bossGate.IsAlive, "sustained primary fire alone broke the locked boss gate");
            Assert.IsFalse(bossGate.IsOpen, "the boss gate opened without any shed being destroyed");
        }

        [UnityTest]
        public IEnumerator DestroyingTheOnlyShed_UnlocksAndForceOpensTheBossGate()
        {
            yield return Build();
            var bossGate = _built.Actors["bg"].GetComponent<AreaGate>();
            var hutch = _built.Actors["a1_shed"].GetComponent<MowerHutch>();
            Assert.IsNotNull(hutch, "the shed area built no MowerHutch factory");

            hutch.TakeDamage(Hit(hutch.AuthoredMax + 999f, DamageSource.PrimaryWeapon));
            yield return null; // WorldRunner.Update polls IsAlive once per frame

            Assert.IsFalse(bossGate.Locked, "the boss gate stayed locked after the only shed was destroyed");
            Assert.IsTrue(bossGate.IsOpen, "the boss gate never opened once all sheds were destroyed");
        }

        // ---------------------------------------------------------------- gate orientation (MV-271)

        /// <summary>Same entry stub as <see cref="OneShedWorld"/>, but 'a1' and 'a2' sit side by side
        /// (an E/W-wall doorway between them) instead of stacked — the regression shape for MV-271:
        /// g1/g3/g5/g7 in world1_config are exactly this pattern, and used to render with the N/S
        /// gate's box (wide along X, thin along Z), leaving the opening effectively unblocked.</summary>
        private static WorldConfig EastWestWorld() => new WorldConfig
        {
            world = "Orientation Test World",
            dials = new WorldDials
            {
                areaCount = 2, baseThreat = 1f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f, 1f }, toughnessCurve = new WorldToughnessCurve(), powerupCadence = 1,
                band = new WorldBand(),
            },
            enemyTypes = new WorldEnemyTypes
            {
                small = new WorldEnemyTypeEntry { thv = 1f }, large = new WorldEnemyTypeEntry { thv = 1f },
                heavy = new WorldEnemyTypeEntry { thv = 1f }, brute = new WorldEnemyTypeEntry { thv = 1f },
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
                new WorldArea
                {
                    id = "a2", index = 2, role = "normal",
                    origin = new WorldAreaOrigin { x = 10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "g1", width = 3f, opensWith = "primary",
                    from = new WorldGateEndpoint { area = "a1", wall = "E", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a2", wall = "W", pos = 0.5f },
                },
            },
        };

        [UnityTest]
        public IEnumerator AnEastWestGate_RendersVertical_NotWithTheNorthSouthGatesBox()
        {
            WorldConfig cfg = EastWestWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            yield return Build(cfg);

            GameObject nsGate = _built.Actors["g0"];
            GameObject ewGate = _built.Actors["g1"];

            Bounds nsBounds = nsGate.GetComponent<Collider>().bounds;
            Bounds ewBounds = ewGate.GetComponent<Collider>().bounds;

            Assert.Greater(nsBounds.size.x, nsBounds.size.z,
                "'g0' (an N/S-wall gate) should stay wide along X, thin along Z");
            Assert.Greater(ewBounds.size.z, ewBounds.size.x,
                "'g1' (an E/W-wall gate) rendered wide along X, thin along Z — it is horizontal, so " +
                "the doorway it should seal is effectively left open (MV-271)");

            // The rotation must not have shrunk or grown the seal — only turned it.
            Assert.AreEqual(MapRuntime.SealWidth(map, map.Entity("g1")), ewBounds.size.z, 0.05f,
                "the E/W gate's seal width changed when it was turned to face the right way");
        }
    }
}
