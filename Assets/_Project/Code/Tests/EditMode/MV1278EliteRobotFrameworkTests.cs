using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1278 AC1: a world whose <see cref="WorldDials.eliteChancePct"/> is 100 must spawn an elite
    /// through the FACTORY path (<see cref="EnemySpawner"/>) with resolved max health 2.0x a same-kind
    /// robot spawned with eliteChancePct 0, and must stop at <see cref="WorldDials.eliteMaxPerArea"/>
    /// live elites per area.
    ///
    /// <see cref="EnemySpawner.SpawnOne"/> (reflected, same idiom <see cref="EnemySpawnerTests"/> already
    /// uses) is what actually calls the private <c>SpawnKind</c> this ticket wires the elite roll into.
    /// A bare <see cref="BackyardPath"/> stamped with a hand-built <see cref="MapData"/> (one zone, id
    /// "area1") seeds <see cref="EnemyNavigation.Map"/> so <c>EnemySpawner.ResolveAreaIndex</c> resolves
    /// a real, non-zero area for the roll's per-area cap to mean anything — same reflection-seeding idiom
    /// <c>MV898GroundRingSurfaceHeightTests</c> already uses for the identical "EnemySpawner needs a
    /// live map" problem. <see cref="RobotEnemy.HealthCurrent"/> doubles as "resolved max health" here
    /// because nothing has damaged either robot — <c>ResetState</c> always sets current health to max.
    ///
    /// Fails to compile on the base commit (before this ticket): <c>WorldDials.eliteChancePct</c>/
    /// <c>eliteMaxPerArea</c>, <c>RobotEnemy.IsElite</c> and the <c>MaxWorlds.Enemies.EliteAffix</c>/
    /// <c>EliteRoll</c> types do not exist yet — there is no elite system for a factory spawn to roll
    /// into.
    /// </summary>
    public sealed class MV1278EliteRobotFrameworkTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo SpawnOneMethod =
            typeof(EnemySpawner).GetMethod("SpawnOne", BindingFlags.NonPublic | BindingFlags.Instance);

        // MV-1021/MV-828: AddComponent's own Awake/OnEnable never run outside Play mode, so
        // RobotEnemy._active (what RobotEnemy.Active, and so EliteRoll's own per-area live-elite count,
        // reads) never gets a factory-spawned robot added the way the real game does. Found the new
        // instance off EnemySpawner's own _live list instead (SpawnKind appends to it as plain code,
        // unconditional on any Unity lifecycle callback), then registered into RobotEnemy._active by
        // hand — the same RegisterAllUnregisteredRobots idiom MV828ReplicatorAreaIndexTests already uses
        // for this identical EditMode-only gap.
        private static readonly FieldInfo SpawnerLiveField =
            typeof(EnemySpawner).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo RobotActiveListField =
            typeof(RobotEnemy).GetField("_active", BindingFlags.NonPublic | BindingFlags.Static);

        // Distinctive far-off origin (same idiom MV828ReplicatorAreaIndexTests/MV820ReplicatorQueueingTests
        // use) — EditMode tests share one physics scene for the whole cc-verify run with no per-test
        // reset, so near-origin coordinates risk colliding with another test's own leftover geometry.
        private const float OriginX = 83000f;
        private const float OriginZ = 46000f;

        private GameObject _pathGo;
        private GameObject _hutchGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hutchGo != null) Object.DestroyImmediate(_hutchGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        /// <summary>Seeds <see cref="EnemyNavigation.Map"/> with one 40x40 m zone ("area1") centred on
        /// the test origin, then builds an <see cref="EnemySpawner"/> (via <see cref="MowerHutch.Build"/>,
        /// same construction <see cref="EnemySpawnerTests"/> uses) inside it, configured with
        /// <paramref name="cfg"/> and a Rusher-only composition so every roll is for a known kind.</summary>
        private EnemySpawner BuildSpawnerInArea1(WorldConfig cfg)
        {
            var map = new MapData
            {
                zones = new[]
                {
                    new MapZone { id = "area1", x = OriginX, z = OriginZ, width = 40f, depth = 40f },
                },
            };

            _pathGo = new GameObject("MV1278-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);
            EnemyNavigation.Reset(); // discard any cached BackyardPath lookup from an earlier test/call

            _hutchGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutchGo.name = "MV1278 Mower Hutch";
            _hutchGo.transform.position = new Vector3(OriginX, 1f, OriginZ);
            _hutchGo.AddComponent<MowerHutch>().Build();
            var spawner = _hutchGo.GetComponent<EnemySpawner>();
            spawner.ConfigureWorldConfig(cfg);
            spawner.ConfigureAreaComposition(new WorldComposition { rusher = 1 });
            return spawner;
        }

        private static WorldConfig BuildWorldConfig(float eliteChancePct, int eliteMaxPerArea) => new WorldConfig
        {
            dials = new WorldDials { eliteChancePct = eliteChancePct, eliteMaxPerArea = eliteMaxPerArea },
        };

        /// <summary>Drives one factory release and returns the robot it produced — the newest entry in
        /// the spawner's own private <c>_live</c> list, then registered into <c>RobotEnemy._active</c>
        /// by hand (see the field's own doc comment) so a following spawn's elite-cap roll — which reads
        /// <see cref="RobotEnemy.LiveEliteCountInArea"/>, itself a walk of <c>_active</c> — actually sees
        /// it as live.</summary>
        private static RobotEnemy SpawnOneRobot(EnemySpawner spawner)
        {
            SpawnOneMethod.Invoke(spawner, null);

            var live = (System.Collections.Generic.List<RobotEnemy>)SpawnerLiveField.GetValue(spawner);
            RobotEnemy robot = live[live.Count - 1];

            var active = (System.Collections.Generic.List<RobotEnemy>)RobotActiveListField.GetValue(null);
            if (!active.Contains(robot)) active.Add(robot);

            return robot;
        }

        [Test]
        public void FactorySpawn_RollsElite100Pct_DoublesResolvedMaxHealth_AndCapsPerArea_MV1278()
        {
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            Assert.IsNotNull(SpawnOneMethod, "EnemySpawner.SpawnOne went missing");

            // === baseline: eliteChancePct 0 never rolls elite ===
            EnemySpawner baselineSpawner = BuildSpawnerInArea1(BuildWorldConfig(eliteChancePct: 0f, eliteMaxPerArea: 3));
            RobotEnemy baseline = SpawnOneRobot(baselineSpawner);
            Assert.IsFalse(baseline.IsElite, "eliteChancePct 0 must never roll an elite");

            Object.DestroyImmediate(_hutchGo); _hutchGo = null;
            Object.DestroyImmediate(_pathGo); _pathGo = null;

            // === eliteChancePct 100: the very next factory spawn must be elite, at double health ===
            EnemySpawner eliteSpawner = BuildSpawnerInArea1(BuildWorldConfig(eliteChancePct: 100f, eliteMaxPerArea: 1));
            RobotEnemy elite = SpawnOneRobot(eliteSpawner);

            Assert.IsTrue(elite.IsElite, "eliteChancePct 100 must roll the very next factory spawn elite");
            Assert.AreEqual(baseline.HealthCurrent * 2.0f, elite.HealthCurrent, 0.001f,
                "an elite's resolved max health must be exactly 2.0x the same-kind non-elite's");

            // === eliteMaxPerArea 1: a second spawn in the SAME area must not roll a second elite ===
            RobotEnemy second = SpawnOneRobot(eliteSpawner);
            Assert.IsFalse(second.IsElite,
                "with eliteMaxPerArea 1 and one live elite already in the area, a second spawn must not be elite");
        }
    }
}
