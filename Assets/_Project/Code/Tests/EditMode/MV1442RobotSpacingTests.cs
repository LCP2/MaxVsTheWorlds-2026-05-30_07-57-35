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
    /// MV-1442 - robots heaped into one pile because separation (MV-321) only ran in TickChase. ONE test
    /// (MV-465 Rule 1), Tier 2 resolved positions after real ticks. (1) A mixed pile - marksman Bolter, Arc
    /// Striker, posted Gunner, Launcher and 2 Bruisers all within 0.6 m of one point, Max 12 m away: after 3 s
    /// no two centres are closer than 1.5 m. (2) A relay luring 6 robots through 3 full cycles: after each
    /// cycle no two robots within 6 m of the relay are closer than 1.5 m, and queued robots' slots are at
    /// least 1.8 m apart.
    /// </summary>
    public sealed class MV1442RobotSpacingTests
    {
        private static readonly Vector3 PileOrigin = new Vector3(-81113f, 0f, 44221f);
        private static readonly Vector3 RelayOrigin = new Vector3(-41113f, 0f, 44221f);
        private const float Dt = 0.02f;

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        [TearDown]
        public void Clear()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            CharacterControllerMotion.HasWarnedOversizedMove = true;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static float MinPairwise(IList<RobotEnemy> rs, out string worst)
        {
            float min = float.MaxValue; worst = "";
            for (int i = 0; i < rs.Count; i++)
                for (int j = i + 1; j < rs.Count; j++)
                {
                    float d = Flat(rs[i].transform.position, rs[j].transform.position);
                    if (d < min) { min = d; worst = rs[i].Kind + "/" + rs[j].Kind; }
                }
            return min;
        }

        private RobotEnemy NewRobot(EnemyArchetype a, Vector3 at)
        {
            var go = new GameObject("MV1442-" + a.Kind);
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(a);
            OnEnableMethod.Invoke(e, null);
            e.transform.position = at + Vector3.up * a.SpawnHeight;
            return e;
        }

        [Test]
        public void MixedPileAndRelayQueue_NeverHeapRobotsCloserThanOnePointFiveMetres_MV1442()
        {
            LogAssert.ignoreFailingMessages = true;
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");

            // ---------------- (1) mixed pile ----------------
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(ground);
            ground.transform.position = new Vector3(PileOrigin.x, -1f, PileOrigin.z);
            ground.transform.localScale = new Vector3(200f, 2f, 200f);

            var maxGo = new GameObject("MV1442 Max", typeof(CharacterController)) { tag = "Player" };
            _spawned.Add(maxGo);
            maxGo.AddComponent<PlayerHealth>().Initialize();
            maxGo.transform.position = PileOrigin + new Vector3(12f, 0f, 0f);

            var kinds = new[] { EnemyKind.Bolter, EnemyKind.ArcStriker, EnemyKind.Gunner, EnemyKind.Launcher,
                                EnemyKind.Bruiser, EnemyKind.Bruiser };
            var pile = new List<RobotEnemy>();
            for (int i = 0; i < kinds.Length; i++)
            {
                float ang = i * Mathf.PI * 2f / kinds.Length;
                Vector3 at = PileOrigin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.3f;   // all within 0.6 m
                RobotEnemy r = NewRobot(EnemyArchetype.For(kinds[i], city), at);
                if (kinds[i] == EnemyKind.Gunner) r.SetSquad("pile", true, true);
                pile.Add(r);
            }
            Physics.SyncTransforms();
            for (int step = 0; step < 150; step++)   // 3 s
            {
                foreach (RobotEnemy r in pile) r.Tick(Dt);
                Physics.SyncTransforms();
            }
            float pileMin = MinPairwise(pile, out string pileWorst);
            Assert.GreaterOrEqual(pileMin, 1.5f,
                "mixed pile after 3 s: closest pair is " + pileWorst + " at " + pileMin.ToString("F2") + " m (must be >= 1.5 m)");

            foreach (RobotEnemy r in pile) Object.DestroyImmediate(r.gameObject);
            RobotEnemy.ResetRegistry();

            // ---------------- (2) relay queue, 3 full cycles ----------------
            DevTuning.GlobalRobotBudget = 100f;
            var boxGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(boxGo);
            boxGo.transform.position = RelayOrigin;
            var box = boxGo.AddComponent<Replicator>();
            box.Build();
            box.Configure(3);
            box.SetAreaIndex(1);
            EnemySpawner spawner = boxGo.GetComponent<EnemySpawner>();

            maxGo.transform.position = RelayOrigin + new Vector3(0f, 0f, 30f);
            var queue = new List<RobotEnemy>();
            for (int i = 0; i < 6; i++)
            {
                RobotEnemy r = NewRobot(EnemyArchetype.Rusher, RelayOrigin + new Vector3(6f + 0.3f * i, 0f, -8f + 0.3f * (i % 2)));
                r.SetAreaIndex(1);
                queue.Add(r);
            }
            box.OnAreaEntered(1);
            Assert.AreEqual(6, queue.FindAll(r => r.IsAssignedToReplicator).Count, "setup: all 6 robots are lured");

            for (int cycle = 1; cycle <= 3; cycle++)
            {
                RobotEnemy head = null;
                foreach (RobotEnemy r in RobotEnemy.Active)
                    if (r.IsAssignedToReplicator && Flat(r.ReplicatorSeekTarget, box.QueueSlotPosition(0)) < 0.01f) head = r;
                Assert.IsNotNull(head, "cycle " + cycle + ": a robot holds slot 0");
                head.transform.position = head.ReplicatorSeekTarget;   // arrives -> Intake
                Physics.SyncTransforms();

                int emittedBefore = spawner.Emitted;
                int guard = 0;
                while (spawner.Emitted < emittedBefore + 2 && guard++ < 3000)
                {
                    box.TickConsumption(Dt);
                    foreach (RobotEnemy r in new List<RobotEnemy>(RobotEnemy.Active)) r.Tick(Dt);
                    Physics.SyncTransforms();
                }
                for (int i = 0; i < 150; i++)   // settle 3 s
                {
                    box.TickConsumption(Dt);
                    foreach (RobotEnemy r in new List<RobotEnemy>(RobotEnemy.Active)) r.Tick(Dt);
                    Physics.SyncTransforms();
                }

                var near = new List<RobotEnemy>();
                var targets = new List<Vector3>();
                foreach (RobotEnemy r in RobotEnemy.Active)
                {
                    if (r.IsAlive && Flat(r.transform.position, RelayOrigin) <= 6f) near.Add(r);
                    if (r.IsAssignedToReplicator) targets.Add(r.ReplicatorSeekTarget);
                }
                float relayMin = MinPairwise(near, out string relayWorst);
                Assert.GreaterOrEqual(relayMin, 1.5f,
                    "relay cycle " + cycle + ": closest pair within 6 m is " + relayWorst + " at " + relayMin.ToString("F2") + " m (must be >= 1.5 m)");
                for (int i = 0; i < targets.Count; i++)
                    for (int j = i + 1; j < targets.Count; j++)
                        Assert.GreaterOrEqual(Flat(targets[i], targets[j]), 1.8f - 0.02f,   // 0.02 = float precision at coordinates near 4e4
                            "relay cycle " + cycle + ": queue slots " + i + " and " + j + " are closer than 1.8 m");
            }
        }

        /// <summary>MV-1466: the 1.5 m guarantee of the mixed pile must hold for EVERY tie-break id ordering,
        /// not only the instance ids a given test order happens to produce. Sweeps id bases 0-29 through the
        /// test-only RobotEnemy.DeterministicIds seam (same pile, same 3 s window, same 1.5 m threshold).</summary>
        [Test]
        public void MixedPile_ReachesOnePointFiveMetresWithin3s_OnEveryIdBase0To29_MV1466()
        {
            LogAssert.ignoreFailingMessages = true;
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(ground);
            ground.transform.position = new Vector3(PileOrigin.x, -1f, PileOrigin.z);
            ground.transform.localScale = new Vector3(200f, 2f, 200f);
            var maxGo = new GameObject("MV1466 Max", typeof(CharacterController)) { tag = "Player" };
            _spawned.Add(maxGo);
            maxGo.AddComponent<PlayerHealth>().Initialize();
            maxGo.transform.position = PileOrigin + new Vector3(12f, 0f, 0f);
            var kinds = new[] { EnemyKind.Bolter, EnemyKind.ArcStriker, EnemyKind.Gunner, EnemyKind.Launcher,
                                EnemyKind.Bruiser, EnemyKind.Bruiser };
            var failures = new List<string>();
            try
            {
                RobotEnemy.DeterministicIds = true;
                for (int idBase = 0; idBase < 30; idBase++)
                {
                    RobotEnemy.ResetRegistry();
                    RobotEnemy.DeterministicIdBase = idBase;
                    var pile = new List<RobotEnemy>();
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        float ang = i * Mathf.PI * 2f / kinds.Length;
                        Vector3 at = PileOrigin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.3f;
                        RobotEnemy r = NewRobot(EnemyArchetype.For(kinds[i], city), at);
                        if (kinds[i] == EnemyKind.Gunner) r.SetSquad("pile", true, true);
                        pile.Add(r);
                    }
                    Physics.SyncTransforms();
                    for (int step = 0; step < 150; step++)
                    {
                        foreach (RobotEnemy r in pile) r.Tick(Dt);
                        Physics.SyncTransforms();
                    }
                    float min = MinPairwise(pile, out string worst);
                    if (min < 1.5f) failures.Add("base " + idBase + ": " + worst + " " + min.ToString("F2") + " m");
                    foreach (RobotEnemy r in pile) Object.DestroyImmediate(r.gameObject);
                }
            }
            finally { RobotEnemy.DeterministicIds = false; RobotEnemy.DeterministicIdBase = 0; }
            Assert.IsEmpty(failures, "id bases whose 3 s mixed pile is under 1.5 m: " + string.Join("; ", failures));
        }
    }
}
