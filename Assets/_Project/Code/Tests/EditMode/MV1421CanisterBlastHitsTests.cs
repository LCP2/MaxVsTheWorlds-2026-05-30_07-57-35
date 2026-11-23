using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Audio;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Player;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1421: a canister's blast must actually land. Lee, on the live City build: "the blast does no
    /// damage", certainly none to Max. The sweep read a 32-slot buffer with mask ~0, so floor, walls and
    /// cover could fill every slot before Max or a robot was reached; it also looked only at the
    /// collider's own GameObject. Now: 6 m radius, 120 damage out to 3 m falling linearly to 60 at 6 m,
    /// landing as the ring of fire reaches each target, and an explosion cue on CanisterDetonated.
    ///
    /// One test, Tier 2 (Rule 2): every assertion reads a resolved runtime value - Max's own
    /// <see cref="PlayerHealth.Current"/>, a robot's own <see cref="RobotEnemy.HealthCurrent"/>, the
    /// director's own voice count - never an authored constant. Max is built the way MV1071 builds him
    /// (a CharacterController with PlayerController + PlayerHealth), not a bare PlayerHealth on a cube.
    /// 60 static box colliders sit within 3 m to cover the buffer-overflow case.
    ///
    /// Fails on the base commit a551d15: it does not compile (no SfxCueLibrary.Cue.CanisterExplosion).
    /// </summary>
    public sealed class MV1421CanisterBlastHitsTests
    {
        // Distinctive far-off origin: EditMode tests share one physics scene with no per-test reset.
        private static readonly Vector3 Origin = new Vector3(86421f, 0f, 53187f);

        private static readonly MethodInfo RobotOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Camera[] _suppressedAmbientCameras;
        private SfxDirector _director;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            Canister.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_director != null)
            {
                Invoke(_director, "OnDisable");
                Object.DestroyImmediate(_director.gameObject);
            }
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            Canister.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);

        private RobotEnemy SpawnRobot(string name, EnemyArchetype archetype, Vector3 offset)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            var robot = go.AddComponent<RobotEnemy>();
            robot.Apply(archetype);
            RobotOnEnable.Invoke(robot, null);
            go.transform.position = Origin + offset;
            return robot;
        }

        [Test]
        public void CanisterBlast_ReachesMaxAndRobotsThroughACrowdOfColliders_WithAFalloffAndItsOwnSound()
        {
            var canisterGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _spawned.Add(canisterGo);
            canisterGo.transform.position = Origin;
            var canister = canisterGo.AddComponent<Canister>();
            canister.Build();

            // Max, built the way the game builds him, 2 m away, no Force Field.
            var playerGo = new GameObject("Player-MV1421", typeof(CharacterController)) { tag = "Player" };
            _spawned.Add(playerGo);
            Invoke(playerGo.AddComponent<PlayerController>(), "Awake");
            var max = playerGo.AddComponent<PlayerHealth>();
            max.Initialize();
            playerGo.transform.position = Origin + new Vector3(0f, 0f, -2f);

            RobotEnemy bruiser2 = SpawnRobot("Bruiser2", EnemyArchetype.Bruiser, new Vector3(2f, 0f, 0f));
            RobotEnemy heavy2 = SpawnRobot("Heavy2", EnemyArchetype.Heavy, new Vector3(-2f, 0f, 0f));
            RobotEnemy bruiser5 = SpawnRobot("Bruiser5", EnemyArchetype.Bruiser, new Vector3(0f, 0f, 5f));
            RobotEnemy rusher7 = SpawnRobot("Rusher7", EnemyArchetype.Rusher, new Vector3(7f, 0f, 3f));

            // 60 static colliders within 3 m: the floor/wall/cover crowd that filled the old 32-slot buffer.
            for (int i = 0; i < 60; i++)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _spawned.Add(box);
                float ang = i / 60f * Mathf.PI * 2f;
                float r = 1f + (i % 4) * 0.5f;
                box.transform.position = Origin + new Vector3(Mathf.Cos(ang) * r, 0.5f, Mathf.Sin(ang) * r);
                box.transform.localScale = Vector3.one * 0.2f;
            }

            float maxBefore = max.Current;
            float heavyBefore = heavy2.HealthCurrent;
            float bruiser5Before = bruiser5.HealthCurrent;
            float rusher7Before = rusher7.HealthCurrent;

            // Sound: a director listening on the bus requests the new cue when a canister detonates.
            _director = new GameObject("SfxDirector-MV1421").AddComponent<SfxDirector>();
            Invoke(_director, "Awake");
            Invoke(_director, "OnEnable");
            HudSignals.EmitCanisterDetonated(Origin + new Vector3(500f, 0f, 500f));
            Assert.AreEqual(1, _director.VoiceStartsThisWindow(SfxCueLibrary.Cue.CanisterExplosion),
                "HudSignals.CanisterDetonated must make SfxDirector request CanisterExplosion");
            CollectionAssert.Contains(SfxCueLibrary.AllCues, SfxCueLibrary.Cue.CanisterExplosion);
            Assert.AreEqual("Canister explosion", SfxCueLibrary.DisplayNames[SfxCueLibrary.Cue.CanisterExplosion]);

            // Drive the canister to 0 HP, then tick past the 1.0 s fuse and the 0.4 s the ring takes to reach 6 m.
            canister.TakeDamage(new DamageInfo(80f, Origin, Vector3.down, Team.Player));
            Assert.IsTrue(canister.IsFusing, "setup failure: lethal damage must start the fuse");
            for (int i = 0; i < 20; i++) canister.Tick(0.1f);

            Assert.AreEqual(120f, maxBefore - max.Current, 0.01f, "Max, 2 m away, must take the full 120");
            Assert.LessOrEqual(bruiser2.HealthCurrent, 0f, "a bruiser 2 m away must be dead");
            Assert.AreEqual(120f, heavyBefore - heavy2.HealthCurrent, 0.01f,
                "a heavy 2 m away must take 120 and survive");
            Assert.Greater(heavy2.HealthCurrent, 0f, "a heavy (260 HP) must survive a 120 blast");
            Assert.AreEqual(80f, bruiser5Before - bruiser5.HealthCurrent, 1f,
                "a bruiser 5 m away takes the linear fall-off: 80");
            Assert.AreEqual(0f, rusher7Before - rusher7.HealthCurrent, 0.001f,
                "a robot beyond 6 m must take no damage");
        }
    }
}
