using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1113: retires the Sentinel placement joystick in favour of a SENTINEL button whose deploy
    /// point the game itself picks. One test (MV-465 Rule 1) through the REAL deploy path
    /// (<see cref="PlayerAbilities.TryDeploySentinelNearMax"/>) carrying every AC1 assertion as a
    /// sub-check:
    ///  - three seeded layouts (open floor; Max ringed by six awake robots at 2m; Max on a deck strip)
    ///    each deploy a sentinel whose chosen point satisfies every item-3 predicate, asserted
    ///    individually against the REAL registries/map this test seeds — never a hardcoded point;
    ///  - a fourth, fully-blocked layout proves the NO ROOM outcome: wallet and cooldown untouched;
    ///  - a second tap within the 10s cooldown deploys nothing;
    ///  - from tap to 2.99s the sentinel takes no damage and fires no shot; once the arrival is over
    ///    it fires at a robot in range.
    ///
    /// Fails to even compile on base commit d30d293 — <c>PlayerAbilities</c> has no
    /// <c>TryDeploySentinelNearMax</c>/<c>SentinelDeployOutcome</c> member at all; see the fix comment
    /// for the exact compiler error quoted against that commit.
    /// </summary>
    public sealed class MV1113SentinelButtonDeployTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo BackyardPathMapField = typeof(BackyardPath).GetField("_map", NonPublicInstance);
        private static readonly FieldInfo RobotCcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private List<GameObject> _spawned;

        [SetUp]
        public void SetUp()
        {
            _spawned = new List<GameObject>();
            ResetState();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ResetState();
        }

        private static void ResetState()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset(); // also resets RigState — the category unlock below must come after this
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            DevTuning.Reset();
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private PlayerAbilities NewMax(Vector3 position)
        {
            var go = Track(new GameObject("MV1113-Max"));
            go.transform.position = position;
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCellSecondary(100);
            return go.AddComponent<PlayerAbilities>();
        }

        private void InstallMap(MapData map)
        {
            var pathGo = Track(new GameObject("MV1113-backyard-path"));
            var path = pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);
        }

        /// <summary>A live, awake, damageable robot — same construction idiom
        /// <c>MV1006SentinelFiresThroughBarriersTests.NewRobot</c> uses (Awake/OnEnable don't run
        /// reliably for a plain AddComponent outside Play mode, so OnEnable is invoked directly to seed
        /// <see cref="RobotEnemy.Active"/> and reset its combat state).</summary>
        private RobotEnemy NewAwakeRobot(Vector3 position)
        {
            var go = Track(new GameObject("MV1113-Robot"));
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            RobotCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            RobotOnEnableMethod.Invoke(robot, null);
            go.transform.position = position;
            return robot;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Re-checks every ticket item-3 predicate against the REAL point
        /// <see cref="PlayerAbilities.TryDeploySentinelNearMax"/> actually chose, reading the SAME real
        /// registries the production search reads — never a precomputed expected coordinate.</summary>
        private static void AssertArrivalPredicates(Vector3 point, Vector3 maxPos, string label)
        {
            float distToMax = FlatDistance(point, maxPos);
            Assert.That(distToMax, Is.InRange(
                    PlayerAbilities.SentinelArrivalMinDistance - 1e-2f, PlayerAbilities.SentinelArrivalMaxDistance + 1e-2f),
                $"{label}: distance from Max must sit in the [2.5, 4.5] band — was {distToMax}");

            foreach (RobotEnemy robot in RobotEnemy.Active)
            {
                if (robot == null || !robot.IsAwake) continue;
                float d = FlatDistance(robot.transform.position, point);
                Assert.That(d, Is.GreaterThanOrEqualTo(PlayerAbilities.SentinelArrivalEnemyClearanceFloor - 1e-2f),
                    $"{label}: must never land closer than the relaxed floor to an awake robot — was {d}");
            }

            foreach (Sentinel s in Sentinel.Active)
            {
                if (s == null || FlatDistance(s.transform.position, point) < 1e-3f) continue; // this IS the sentinel just placed
                float d = FlatDistance(s.transform.position, point);
                Assert.That(d, Is.GreaterThanOrEqualTo(PlayerAbilities.SentinelPlacementClearance - 1e-2f),
                    $"{label}: must stay clear of other sentinels — was {d}");
            }

            Assert.That(LineOfSight.Clear(point, maxPos), Is.True, $"{label}: must have clear line of sight to Max");
        }

        [Test]
        public void TheRealSentinelButtonDeployPathSatisfiesEveryArrivalRuleAndTimingAcrossSeededLayouts()
        {
            // ---------------------------------------------------------------- Layout A: open floor
            var originA = new Vector3(81234f, 0f, 51234f);
            // deckHeight must sit well above Max's own y=0 here — MapData.ZoneAt/IsWalkable's "onDeck"
            // test is fromPosition.y >= deckHeight - 0.5f, and a default deckHeight of 0 would read y=0
            // as "on deck" with no deck entity underneath, which degrades IsWalkable to an unconditional
            // true (never actually exercising zone.Contains against the floor zone below).
            InstallMap(new MapData { deckHeight = 2.5f, zones = new[]
            {
                new MapZone { id = "a1", x = originA.x, z = originA.z, width = 40f, depth = 40f, level = 0 },
            }});
            PlayerAbilities abilitiesA = NewMax(originA);

            var outcomeA = abilitiesA.TryDeploySentinelNearMax();
            Assert.That(outcomeA, Is.EqualTo(PlayerAbilities.SentinelDeployOutcome.Deployed),
                "open floor: a deploy must succeed with nothing in the way");
            Assert.That(Sentinel.Active.Count, Is.EqualTo(1));
            Vector3 pointA = Sentinel.Active[0].transform.position;
            AssertArrivalPredicates(pointA, originA, "open floor");

            // AC1: a second tap within 10s deploys nothing.
            int cellsBeforeSecondTap = PickupWallet.PowerCellsSecondary;
            var secondTap = abilitiesA.TryDeploySentinelNearMax();
            Assert.That(secondTap, Is.EqualTo(PlayerAbilities.SentinelDeployOutcome.NotReady),
                "a second tap within 10s must deploy nothing");
            Assert.That(Sentinel.Active.Count, Is.EqualTo(1));
            Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(cellsBeforeSecondTap));

            // AC1: from tap to 2.99s no damage taken and no shot fired; once live, it fires.
            Sentinel sentinelA = Sentinel.Active[0];
            RobotEnemy targetRobot = NewAwakeRobot(pointA + new Vector3(2f, 0f, 0f));

            sentinelA.TakeDamage(new DamageInfo(1000f, pointA, Vector3.forward, Team.Enemy));
            Assert.That(sentinelA.IsAlive, Is.True, "mid-arrival: a Sentinel must take no damage at all");
            Assert.That(sentinelA.HealthCurrent, Is.EqualTo(sentinelA.HealthMax).Within(1e-2f));

            sentinelA.TickSentinel(2.99f);
            Assert.That(sentinelA.IsArriving, Is.True, "at 2.99s the arrival must not have finished yet");
            float robotHpBeforeLive = targetRobot.HealthCurrent;
            Assert.That(targetRobot.HealthCurrent, Is.EqualTo(robotHpBeforeLive), "mid-arrival: a Sentinel must fire no shot");

            sentinelA.TickSentinel(0.02f); // 2.99 -> 3.01s: the arrival finishes on this tick
            Assert.That(sentinelA.IsArriving, Is.False, "at 3.01s the arrival must be over");

            sentinelA.TickSentinel(0.05f); // first tick as a LIVE sentinel — fires immediately (fresh _fireCooldown)
            Assert.That(targetRobot.HealthCurrent, Is.LessThan(robotHpBeforeLive),
                "once live, the Sentinel must fire at a robot in range");

            // ---------------------------------------------------------------- Layout B: Max ringed by six robots at 2m
            ResetState();
            var originB = new Vector3(82234f, 0f, 52234f);
            InstallMap(new MapData { deckHeight = 2.5f, zones = new[]
            {
                new MapZone { id = "b1", x = originB.x, z = originB.z, width = 40f, depth = 40f, level = 0 },
            }});
            PlayerAbilities abilitiesB = NewMax(originB);
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                NewAwakeRobot(originB + new Vector3(Mathf.Sin(angle) * 2f, 0f, Mathf.Cos(angle) * 2f));
            }

            var outcomeB = abilitiesB.TryDeploySentinelNearMax();
            Assert.That(outcomeB, Is.EqualTo(PlayerAbilities.SentinelDeployOutcome.Deployed),
                "ringed by six robots at 2m: a relaxed-clearance point must still be found");
            Vector3 pointB = Sentinel.Active[0].transform.position;
            AssertArrivalPredicates(pointB, originB, "ringed by six robots");

            // ---------------------------------------------------------------- Layout C: Max on a deck strip
            ResetState();
            var originC = new Vector3(83234f, 0f, 53234f);
            const float deckHeight = 2.5f;
            InstallMap(new MapData
            {
                deckHeight = deckHeight,
                zones = new[]
                {
                    new MapZone { id = "c-floor", x = originC.x, z = originC.z, width = 30f, depth = 30f, level = 0 },
                    new MapZone { id = "c-deck", x = originC.x, z = originC.z, width = 30f, depth = 30f, level = 1 },
                },
                entities = new[]
                {
                    new MapEntity { id = "c-deck-strip", kind = "deck", x = originC.x, z = originC.z, width = 2f, depth = 12f },
                },
            });
            PlayerAbilities abilitiesC = NewMax(originC + new Vector3(0f, deckHeight, 0f));

            var outcomeC = abilitiesC.TryDeploySentinelNearMax();
            Assert.That(outcomeC, Is.EqualTo(PlayerAbilities.SentinelDeployOutcome.Deployed),
                "deck strip: a point along the strip's own long axis must be found");
            Vector3 pointC = Sentinel.Active[0].transform.position;
            AssertArrivalPredicates(pointC, abilitiesC.transform.position, "deck strip");
            Assert.That(pointC.y, Is.EqualTo(deckHeight).Within(0.05f), "deck strip: the sentinel must rest at the deck's own height");
            Assert.That(Mathf.Abs(pointC.x - originC.x), Is.LessThanOrEqualTo(1f + 1e-2f),
                "deck strip: the sentinel must land inside the strip's own 2m-wide rect, not beyond its edge");

            // ---------------------------------------------------------------- Layout D: NO ROOM
            ResetState();
            var originD = new Vector3(84234f, 0f, 54234f);
            // A 1x1m zone can never contain a point 2.5m+ away from its own centre — every candidate
            // the search samples fails "on walkable ground inside Max's current area" outright, at
            // every relaxation step, guaranteeing NO ROOM deterministically.
            InstallMap(new MapData { deckHeight = 2.5f, zones = new[]
            {
                new MapZone { id = "d1", x = originD.x, z = originD.z, width = 1f, depth = 1f, level = 0 },
            }});
            PlayerAbilities abilitiesD = NewMax(originD);
            int cellsBeforeNoRoom = PickupWallet.PowerCellsSecondary;
            float cooldownBeforeNoRoom = abilitiesD.SentinelCooldownRemaining;

            var outcomeD = abilitiesD.TryDeploySentinelNearMax();
            Assert.That(outcomeD, Is.EqualTo(PlayerAbilities.SentinelDeployOutcome.NoRoom),
                "a wholly blocked layout must report NO ROOM");
            Assert.That(Sentinel.Active.Count, Is.EqualTo(0), "NO ROOM: nothing may be placed");
            Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(cellsBeforeNoRoom), "NO ROOM: nothing may be spent");
            Assert.That(abilitiesD.SentinelCooldownRemaining, Is.EqualTo(cooldownBeforeNoRoom), "NO ROOM: no cooldown may start");
        }
    }
}
