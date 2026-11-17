using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1379 — a melee robot chasing Max whose only path is a destructible cover piece hits that piece
    /// with its normal attack until it breaks, then resumes the chase.
    ///
    /// Scene: a dead-end corridor (indestructible Cover-layer walls either side, a floor) with the
    /// Salvage Crab's kind (<see cref="EnemyKind.Bruiser"/>; the Reef skin only adds a sideways bias to
    /// its approach) at one end, a 150 HP <see cref="DestructibleCover"/> across the corridor, and Max
    /// behind it. No other route exists. The fight is driven entirely by <see cref="RobotEnemy.Tick"/>.
    ///
    /// Tier 2 (resolved state): the piece's own <see cref="DestructibleCover.IsAlive"/> /
    /// <see cref="DestructibleCover.IsBroken"/> / collider.enabled, and the Crab's resolved planar
    /// distance to Max against the archetype's contact radius — no authored constant is asserted.
    /// // Guards MV-1379
    /// </summary>
    public sealed class MV1379BlockedMeleeBreaksCoverTests
    {
        private const float OriginX = 71423f;
        private const float OriginZ = 66208f;
        private const float Dt = 0.05f;
        private const int MaxSteps = 2400; // 120 simulated seconds — far longer than 150 HP at 1 hit/s needs

        private sealed class MaxReceiver : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static void ResetWorldState()
        {
            DifficultyDirector.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            LungeTokenPool.Reset();
            DevTuning.Reset();
            DestructibleCover.ResetRegistry();
        }

        [SetUp]
        public void SetUp() => ResetWorldState();

        [TearDown]
        public void TearDown() => ResetWorldState();

        private static GameObject Block(string name, Vector3 center, Vector3 size, bool cover)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = center;
            go.transform.localScale = size;
            if (cover) CoverLayer.Assign(go);
            return go;
        }

        [Test]
        public void SalvageCrabBlockedByDestructiblePiece_BreaksItAndReachesMax()
        {
            Assert.IsTrue(CoverLayer.Exists, "test setup: the Cover layer must exist");

            var made = new List<GameObject>();
            RobotEnemy crab = null;
            try
            {
                Vector3 o = new Vector3(OriginX, 0f, OriginZ);
                made.Add(Block("MV1379 Floor", o + new Vector3(0f, -0.5f, 3f), new Vector3(20f, 1f, 20f), cover: false));
                made.Add(Block("MV1379 Wall L", o + new Vector3(-2.25f, 1f, 3f), new Vector3(0.5f, 2f, 12f), cover: true));
                made.Add(Block("MV1379 Wall R", o + new Vector3(2.25f, 1f, 3f), new Vector3(0.5f, 2f, 12f), cover: true));
                made.Add(Block("MV1379 Wall Back", o + new Vector3(0f, 1f, -3.25f), new Vector3(4f, 2f, 0.5f), cover: true));
                made.Add(Block("MV1379 Wall End", o + new Vector3(0f, 1f, 9.25f), new Vector3(4f, 2f, 0.5f), cover: true));

                GameObject pieceGo = Block("MV1379 Piece", o + new Vector3(0f, 1f, 3f), new Vector3(4f, 2f, 1f), cover: true);
                made.Add(pieceGo);
                var piece = pieceGo.AddComponent<DestructibleCover>();
                piece.Configure(null, 150f);
                piece.Build();

                Vector3 crabPos = o + new Vector3(0f, 0f, 0f);
                Vector3 maxPos = o + new Vector3(0f, 1f, 5.5f);

                GameObject maxGo = new GameObject("MV1379 Max", typeof(CharacterController)) { tag = "Player" };
                maxGo.transform.position = maxPos;
                maxGo.AddComponent<MaxReceiver>();
                made.Add(maxGo);

                GameObject spawnerGo = new GameObject("MV1379 Spawner");
                spawnerGo.transform.position = maxPos;
                made.Add(spawnerGo);
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                List<RobotEnemy> spawned = spawner.SpawnExact(EnemyKind.Bruiser, 1);
                Assert.AreEqual(1, spawned.Count, "test setup: SpawnExact didn't spawn the Crab");
                crab = spawned[0];
                crab.transform.position = new Vector3(crabPos.x, EnemyArchetype.Bruiser.SpawnHeight, crabPos.z);
                crab.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

                Assert.IsTrue(piece.IsAlive, "test setup: the piece must start intact");

                float touch = EnemyArchetype.Bruiser.ContactRadius;
                float finalDist = float.MaxValue;
                for (int i = 0; i < MaxSteps; i++)
                {
                    crab.Tick(Dt);
                    Vector3 to = maxPos - crab.transform.position; to.y = 0f;
                    finalDist = to.magnitude;
                    if (piece.IsBroken && finalDist <= touch) break;
                }

                Assert.IsFalse(piece.IsAlive, "MV-1379: the blocked Crab must hit the piece until its HP reaches 0");
                Assert.IsTrue(piece.IsBroken, "MV-1379: a piece at 0 HP is broken");
                Assert.IsFalse(pieceGo.GetComponent<Collider>().enabled, "MV-1379: a broken piece must no longer block");
                Assert.LessOrEqual(finalDist, touch,
                    $"MV-1379: once through, the Crab must resume the chase and reach touch range of Max (dist {finalDist:F2} m, contact {touch:F2} m)");
            }
            finally
            {
                if (crab != null) Object.DestroyImmediate(crab.gameObject);
                foreach (GameObject g in made) if (g != null) Object.DestroyImmediate(g);
                // the rubble decal Break() parents under the piece's parent (none here) — sweep it
                GameObject rubble = GameObject.Find("Rubble");
                if (rubble != null) Object.DestroyImmediate(rubble);
                ResetWorldState();
            }
        }
    }
}
