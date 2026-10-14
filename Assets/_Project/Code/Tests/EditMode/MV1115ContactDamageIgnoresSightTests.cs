using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1115 — Lee, device, World 1: "Only the right robot is doing damage" with two Brutes
    /// pressed against Max, one on each side.
    ///
    /// Root cause (confirmed by reading, not guessed): <c>RobotEnemy.TickChase</c>'s own give-up
    /// check — <c>PursuitStall.TickHunting</c>, gated only by <c>searchTime</c>/<c>minHuntTime</c>,
    /// neither of which knows about range — fires the instant a touch kind's SIGHT fails, even while
    /// it is standing IN CONTACT, and drops it to <c>State.Search</c>. <c>TickSearch</c> never calls
    /// <c>TickContactTouch</c>, so a robot that never stopped touching Max goes permanently silent
    /// the moment something breaks its sightline — exactly the hypothesis the ticket names first.
    /// The fix (see <c>RobotEnemy.TickChase</c>/<c>TickSearch</c>) skips that give-up decision
    /// entirely whenever a touch kind is within its own <c>contactRadius</c> of the REAL target
    /// position — contact needs no line of sight, whatever state the pursuit logic would otherwise
    /// pick.
    ///
    /// Real production entry points throughout, same discipline as <c>MV1105EveryKindDamagesASentinelTests</c>:
    /// <list type="bullet">
    /// <item>the real World 1 map, area 2, loaded via <c>WorldLibrary.Load</c> /
    /// <c>WorldMapLoader.TryLoad</c> -&gt; <c>MapRuntime.Build</c> (same idiom as
    /// <c>BossKindDispatchTests.BuildWorld</c>) — gives the real floor and area-2 bounds/cover;</item>
    /// <item>both Brutes come from <c>EnemySpawner.SpawnExact</c>, the same pool/factory path a
    /// Replicator's doubled twin uses — area 2 itself authors no Brutes (its composition is
    /// rusher/blinker only), so this is the only reflection-free way to put two of them there, and it
    /// is a real, shipped production entry point, not a test shortcut;</item>
    /// <item>Max is a real "Player"-tagged <c>GameObject</c> — found by <c>RobotEnemy.AcquireTarget</c>
    /// the same way the real game finds him, never a private field set directly;</item>
    /// <item>the fight is driven entirely by <c>RobotEnemy.Tick</c> (MV-1015's public per-frame seam),
    /// never a private Tick* method invoked by reflection.</item>
    /// </list>
    ///
    /// One honest deviation from the ticket's own worked example, recorded here rather than guessed
    /// past: area 2's five authored cover pieces are ALL dressed "hedge", and hedges are deliberately
    /// left off the Cover layer by design (MV-400 — <c>Sightlines.cs</c>'s own doc comment: "Lee
    /// wants robots to see and shoot through a plant row"). A literal area-2 hedge therefore cannot
    /// block a sightline, in this area or any other in World 1; the obstruction below instead uses
    /// the identical real mechanism <c>MapRuntime.BuildCover</c> itself uses to make a tree or
    /// planter block sight (<c>CoverLayer.Assign</c> on a real collider), parked squarely on one
    /// Brute's own line to Max — a genuine obstruction the production raycast
    /// (<c>LineOfSight.Between</c>) really cannot see through, not a private override of
    /// <c>Perception</c>'s own state.
    ///
    /// Attribution of which Brute landed which hit ("attribute damage per attacker" in the AC) reads
    /// <c>DamageInfo.Point</c> — <c>RobotEnemy.TickContactTouch</c> stamps every hit with the
    /// attacking robot's own <c>transform.position</c> — and classifies each hit by which of the two
    /// (stationary; MV-434 halts a touch kind once already in contact) Brutes it landed nearest to.
    /// <c>DamageInfo.Attacker</c> is only ever a <see cref="Team"/>, never a specific robot, so this is
    /// the one way to tell the two attackers' hits apart without reflecting into either robot.
    /// </summary>
    public sealed class MV1115ContactDamageIgnoresSightTests
    {
        private const float Dt = 0.05f;
        private const int Steps = 120; // 6.0 simulated seconds, the AC's own window

        private sealed class AttributingMax : MonoBehaviour, IDamageable
        {
            public float Health = 1000f; // generous — this test counts hits/attribution, not survival
            public readonly List<DamageInfo> Hits = new List<DamageInfo>();
            public bool IsAlive => Health > 0f;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info)
            {
                if (!DamageRules.Applies(info.Attacker, Team)) return;
                Hits.Add(info);
                Health -= info.Amount;
            }
        }

        private static void ResetWorldState()
        {
            DifficultyDirector.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            LungeTokenPool.Reset();
            DevTuning.Reset();
        }

        [SetUp]
        public void SetUp() => ResetWorldState();

        [TearDown]
        public void TearDown() => ResetWorldState();

        /// <summary>How far (world units) a candidate point sits from the nearest edge of
        /// <paramref name="area"/>'s own authored cover rectangles — the real, loaded area-2 cover
        /// list, never a hand-picked constant. Negative/zero means inside one.</summary>
        private static float ClearanceFromCover(WorldArea area, float x, float z)
        {
            float clearance = float.MaxValue;
            foreach (WorldCover c in area.cover)
            {
                float dx = Mathf.Max(Mathf.Abs(x - c.x) - c.width * 0.5f, 0f);
                float dz = Mathf.Max(Mathf.Abs(z - c.z) - c.depth * 0.5f, 0f);
                clearance = Mathf.Min(clearance, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return clearance;
        }

        /// <summary>The point inside <paramref name="area"/>'s real rect, inset by <paramref name="edgeMargin"/>
        /// from every wall/gate on its boundary, that sits farthest from the area's own authored cover —
        /// found by scanning the real loaded geometry, never a coordinate copied from anywhere else.</summary>
        private static Vector3 FindClearSpot(WorldArea area, float edgeMargin)
        {
            float best = float.NegativeInfinity;
            Vector3 bestPoint = default;
            for (float x = area.XMin + edgeMargin; x <= area.XMax - edgeMargin; x += 1f)
            {
                for (float z = area.ZMin + edgeMargin; z <= area.ZMax - edgeMargin; z += 1f)
                {
                    float clearance = ClearanceFromCover(area, x, z);
                    if (clearance > best) { best = clearance; bestPoint = new Vector3(x, 0f, z); }
                }
            }
            Assert.Greater(best, edgeMargin, $"world 1 area '{area.id}' has no spot clear of its own cover");
            return bestPoint;
        }

        /// <summary>A real Cover-layer obstruction — the exact mechanism <c>MapRuntime.BuildCover</c>
        /// uses for a tree/planter (MV-400's non-hedge case) — parked on the real line between the
        /// blocked robot's eye point and Max's, tall enough that <c>LineOfSight.Clear</c>'s raycast
        /// cannot miss it regardless of exactly which height convention either eye point used.
        /// Offset away from <paramref name="blockedRobotPos"/> by more than the Brute's own collider
        /// radius: centred on the segment's own midpoint, this box's near face sat INSIDE that collider
        /// (0.6 m radius) and the robot's own <c>CharacterController.Move</c> depenetrated against it
        /// on the very first tick, flinging the robot off the carefully-placed contact geometry before
        /// a single sight check ran — the actual reason an earlier version of this test still measured
        /// zero hits after the fix. Overlapping the stationary Max's own collider is harmless (nothing
        /// ever calls <c>Move()</c> on him here), so biasing well toward Max's end costs nothing.</summary>
        private static GameObject BuildSightBlocker(Vector3 blockedRobotPos, Vector3 targetPos, string name)
        {
            Vector3 toTarget = targetPos - blockedRobotPos;
            float distance = toTarget.magnitude;
            Vector3 dir = toTarget / distance;
            float clearOfRobot = EnemyArchetype.Brute.ColliderRadius + 0.4f;
            float along = Mathf.Min(clearOfRobot, distance * 0.9f); // never past the target itself
            Vector3 center = blockedRobotPos + dir * along;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = new Vector3(center.x, 1.25f, center.z);
            go.transform.localScale = new Vector3(0.6f, 2.5f, 0.6f);
            CoverLayer.Assign(go);
            return go;
        }

        private static int HitsAttributedTo(List<DamageInfo> hits, Vector3 self, Vector3 other) =>
            hits.Count(h => Vector3.Distance(h.Point, self) <= Vector3.Distance(h.Point, other));

        [Test]
        public void TwoBrutesInContact_BothDamageMax_EvenWhenOnesSightIsBlocked()
        {
            Assert.IsTrue(CoverLayer.Exists, "test setup: the Cover layer must exist for this to prove anything");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world 1 failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            WorldArea area2 = cfg.AreaByIndex(2);
            Assert.IsNotNull(area2, "world 1 has no area 2");

            GameObject mapRoot = null;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                mapRoot = new GameObject("MV1115 World1 Root");
                MapRuntime.Build(map, mapRoot.transform);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            try
            {
                Vector3 spot = FindClearSpot(area2, edgeMargin: 3f);
                float separation = (EnemyBodySeparation.MinDistance(
                        EnemyArchetype.Brute.ColliderRadius, EnemyArchetype.PlayerRadius)
                    + EnemyArchetype.Brute.ContactRadius) * 0.5f;
                // Comfortably inside contact radius, comfortably outside the body-separation floor —
                // resolved from the Brute's own archetype, never an invented number.
                Assert.Less(separation, EnemyArchetype.Brute.ContactRadius);
                Assert.Greater(separation,
                    EnemyBodySeparation.MinDistance(EnemyArchetype.Brute.ColliderRadius, EnemyArchetype.PlayerRadius));

                Vector3 maxPos = spot + new Vector3(0f, 1f, 0f);
                Vector3 leftPos = new Vector3(spot.x - separation, EnemyArchetype.Brute.SpawnHeight, spot.z);
                Vector3 rightPos = new Vector3(spot.x + separation, EnemyArchetype.Brute.SpawnHeight, spot.z);

                foreach (Vector3 p in new[] { maxPos, leftPos, rightPos })
                    Assert.Greater(ClearanceFromCover(area2, p.x, p.z), 0f,
                        "test setup: the chosen contact geometry must not sit inside area 2's own cover");

                // ---- Scenario A: both Brutes in contact, clear sight (area 2's own hedges never
                // block anyway) — the baseline the AC's own numbers describe.
                RunScenario(maxPos, leftPos, rightPos, false,
                    out int leftHitsA, out int rightHitsA, out float maxDamageA);

                Assert.GreaterOrEqual(leftHitsA, 4, "baseline: left Brute must land at least 4 hits in 6s");
                Assert.GreaterOrEqual(rightHitsA, 4, "baseline: right Brute must land at least 4 hits in 6s");
                Assert.GreaterOrEqual(maxDamageA, 8f * EnemyArchetype.Brute.TouchDamage,
                    "baseline: Max must lose at least 8 hits' worth over 6s");

                // ---- Scenario B: repeat, with the LEFT Brute's sight forcibly blocked for the whole
                // 6s by a real Cover-layer obstruction — this is the one the bug actually breaks.
                RunScenario(maxPos, leftPos, rightPos, true,
                    out int leftHitsB, out int rightHitsB, out float maxDamageB);

                Assert.GreaterOrEqual(leftHitsB, 4,
                    "MV-1115: a Brute standing IN CONTACT must keep landing hits even once its own " +
                    "sight is blocked — contact needs no line of sight");
                Assert.GreaterOrEqual(rightHitsB, 4,
                    "the unobstructed right Brute must be unaffected by its neighbour's blocked sight");
            }
            finally
            {
                if (mapRoot != null) Object.DestroyImmediate(mapRoot);
                ResetWorldState();
            }
        }

        private void RunScenario(Vector3 maxPos, Vector3 leftPos, Vector3 rightPos,
            bool blockLeftSight, out int leftHits, out int rightHits, out float maxDamage)
        {
            ResetWorldState();

            GameObject maxGo = null, spawnerGo = null, blockerGo = null;
            RobotEnemy left = null, right = null;
            try
            {
                maxGo = new GameObject("MV1115 Max", typeof(CharacterController)) { tag = "Player" };
                maxGo.transform.position = maxPos;
                var receiver = maxGo.AddComponent<AttributingMax>();

                spawnerGo = new GameObject("MV1115 Spawner");
                spawnerGo.transform.position = maxPos;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                List<RobotEnemy> spawned = spawner.SpawnExact(EnemyKind.Brute, 2);
                Assert.AreEqual(2, spawned.Count, "test setup: SpawnExact didn't spawn both Brutes");
                left = spawned[0];
                right = spawned[1];

                left.transform.position = leftPos;
                left.transform.rotation = Quaternion.LookRotation((maxPos - leftPos).normalized, Vector3.up);
                right.transform.position = rightPos;
                right.transform.rotation = Quaternion.LookRotation((maxPos - rightPos).normalized, Vector3.up);

                if (blockLeftSight)
                    blockerGo = BuildSightBlocker(leftPos, maxPos, "MV1115 Sight Blocker");

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

                for (int i = 0; i < Steps; i++)
                {
                    left.Tick(Dt);
                    right.Tick(Dt);
                }

                if (blockLeftSight)
                    Assert.IsFalse(left.Sight.HasSight,
                        "test setup: the blocker must have genuinely broken the left Brute's sight for this to prove anything");

                leftHits = HitsAttributedTo(receiver.Hits, leftPos, rightPos);
                rightHits = HitsAttributedTo(receiver.Hits, rightPos, leftPos);
                maxDamage = 1000f - receiver.Health;
            }
            finally
            {
                if (blockerGo != null) Object.DestroyImmediate(blockerGo);
                if (left != null) Object.DestroyImmediate(left.gameObject);
                if (right != null) Object.DestroyImmediate(right.gameObject);
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                ResetWorldState();
            }
        }
    }
}
