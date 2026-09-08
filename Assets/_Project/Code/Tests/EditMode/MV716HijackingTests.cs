using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-716 — Hijacking, both ways: a Splicer (a role on the existing Gunner kind) channels one of
    /// Max's Sentinels over to the enemy team for a timed window (AC1-3), and a cavitation implosion
    /// converts a critically-damaged, port-exposed robot to Max's side for a timed window, capped at
    /// three at once, ending in a robots-only burnout AoE (AC4-7). One consolidated test method covers
    /// both halves (MV-465 Rule 1: at most one new test per ticket) — every assertion reads RESOLVED
    /// state (a Sentinel's/robot's actual <see cref="Team"/>, <see cref="Sentinel.IsHijacked"/>,
    /// <see cref="RobotEnemy.IsConverted"/>, <see cref="RobotEnemy.HealthCurrent"/>), never an authored
    /// constant.
    ///
    /// Fails on 5d7e4e0 (MV-715, the commit before this ticket): none of
    /// <see cref="RobotEnemy.TryBeginSpliceChannel"/>, <see cref="RobotEnemy.TickSpliceChannel"/>,
    /// <see cref="Sentinel.BeginHijack"/>, <see cref="Sentinel.TickHijack"/>,
    /// <see cref="RobotEnemy.TryConvert"/>, <see cref="RobotEnemy.TickConversion"/> or
    /// <see cref="RobotPopulation"/> exist on that commit, so this test does not compile there.
    /// </summary>
    public sealed class MV716HijackingTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
        }

        private static RobotEnemy NewGunner(string name, Vector3 position)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc); // Awake (which normally wires this) doesn't run outside Play mode
            e.Apply(EnemyArchetype.Gunner);
            go.transform.position = position;
            return e;
        }

        private static Sentinel NewSentinel(Vector3 position, float maxHp = 80f)
        {
            var go = new GameObject("Sentinel");
            var s = go.AddComponent<Sentinel>();
            s.Init(position, maxHp: maxHp, range: 7f, fireInterval: 0.6f, moveSpeed: 0f,
                standoffDistance: 2.5f, followTarget: null);
            return s;
        }

        [Test]
        public void SplicerChannelFlipsAndReturnsASentinel_AndOverrideConvertsAndBurnsOutARobot_MV716()
        {
            GameObject maxGo = null, sentinelGo = null, splicerGo = null, secondSplicerGo = null, bystanderGo = null;
            RobotEnemy nearRobot = null, farRobot = null, healthyRobot = null, secondConverted = null,
                thirdConverted = null, fourthCandidate = null;
            try
            {
                // === AC1 (part 1) + AC3: a Splicer in range and in line of sight completes a 2.5s
                // channel, flipping the Sentinel's team, then the Sentinel returns to Max's team exactly
                // 12s later at whatever health it then has.
                Sentinel sentinel = NewSentinel(Vector3.zero);
                sentinelGo = sentinel.gameObject;
                RobotEnemy splicer = NewGunner("Splicer", new Vector3(5f, 0f, 0f));
                splicerGo = splicer.gameObject;
                splicer.MarkAsSplicer();

                Assert.AreEqual(Team.Player, sentinel.Team, "test precondition: a fresh Sentinel starts on Max's team");
                Assert.IsTrue(splicer.TryBeginSpliceChannel(sentinel, distance: 5f, hasLineOfSight: true),
                    "a Splicer within range and line of sight must be able to begin a channel");

                splicer.TickSpliceChannel(1.5f, hasLineOfSight: true);
                Assert.AreEqual(Team.Player, sentinel.Team, "the Sentinel must not flip before the channel completes");

                splicer.TickSpliceChannel(1.0f, hasLineOfSight: true); // elapsed 2.5s exactly
                Assert.AreEqual(Team.Enemy, sentinel.Team, "the Sentinel's team must flip the instant the 2.5s channel completes");
                Assert.IsTrue(sentinel.IsHijacked);

                sentinel.TakeDamage(new DamageInfo(10f, sentinel.transform.position, Vector3.forward, Team.Player));
                float healthAtFlip = sentinel.HealthCurrent;
                sentinel.TickHijack(11f);
                Assert.AreEqual(Team.Enemy, sentinel.Team, "must still be hijacked before the 12s mark");
                sentinel.TickHijack(1f); // elapsed 12s exactly (11f + 1f, both exact in float — avoids boundary flake)
                Assert.AreEqual(Team.Player, sentinel.Team, "the Sentinel must revert to Max's team exactly 12s after the flip");
                Assert.IsFalse(sentinel.IsHijacked);
                Assert.AreEqual(healthAtFlip, sentinel.HealthCurrent, 0.01f,
                    "the Sentinel must return at the health it then has, not full health");

                // === AC2: a second Splicer cannot begin a channel while one Sentinel is spliced. Proven
                // against a SECOND sentinel while the first is re-locked mid-channel, so the refusal is
                // provably the world-wide lock, not merely "the same sentinel is already engaged".
                Sentinel secondSentinel = NewSentinel(new Vector3(0f, 0f, 20f));
                RobotEnemy otherSplicer = NewGunner("Splicer2", new Vector3(5f, 0f, 20f));
                secondSplicerGo = otherSplicer.gameObject;
                otherSplicer.MarkAsSplicer();

                Assert.IsTrue(splicer.TryBeginSpliceChannel(sentinel, distance: 5f, hasLineOfSight: true),
                    "test setup: the lock must be free again after the first hijack ended");
                Assert.IsFalse(otherSplicer.TryBeginSpliceChannel(secondSentinel, distance: 5f, hasLineOfSight: true),
                    "a second Splicer must not be able to begin a channel while one Sentinel is spliced (AC2)");
                Object.DestroyImmediate(secondSentinel.gameObject);
                splicer.CancelSpliceChannel(); // release the lock the AC2 setup above claimed
                Assert.AreEqual(Team.Player, sentinel.Team);

                // === AC1 (part 2): killing the Splicer at 2.0s into a fresh channel leaves the Sentinel
                // on Max's team.
                RobotEnemy killableSplicer = NewGunner("KillableSplicer", new Vector3(5f, 0f, 0f));
                killableSplicer.MarkAsSplicer();
                Assert.IsTrue(killableSplicer.TryBeginSpliceChannel(sentinel, distance: 5f, hasLineOfSight: true));
                killableSplicer.TickSpliceChannel(2.0f, hasLineOfSight: true);
                killableSplicer.TakeDamage(new DamageInfo(999f, killableSplicer.transform.position, Vector3.forward, Team.Player));
                Assert.IsFalse(killableSplicer.IsAlive, "test setup: the Splicer must actually be dead");
                killableSplicer.TickSpliceChannel(1.0f, hasLineOfSight: true); // would complete at 3.0s if not cancelled
                Assert.AreEqual(Team.Player, sentinel.Team, "killing the Splicer mid-channel must leave the Sentinel on Max's team");
                Object.DestroyImmediate(killableSplicer.gameObject);

                // === AC4: a cavitation implosion 1.9m from a robot at 20% HP converts it; at 2.1m it
                // does not; at 30% HP within range it does not.
                Vector3 impact = new Vector3(0f, 0f, 100f); // far from everything above
                nearRobot = NewGunner("Near20pct", impact + new Vector3(1.9f, 0f, 0f));
                // Opposite side of the impact point, not +2.1 alongside nearRobot: still exactly 2.1m
                // from the impact (AC4 only cares about distance-to-impact), but 4m from nearRobot —
                // outside nearRobot's eventual 3m burnout AoE (AC6), so farRobot survives to be counted
                // in AC7's census below.
                farRobot = NewGunner("Far20pct", impact + new Vector3(-2.1f, 0f, 0f));
                healthyRobot = NewGunner("Healthy30pct", impact + new Vector3(1.5f, 0f, 0f));
                nearRobot.SetHealthFraction(0.20f);
                farRobot.SetHealthFraction(0.20f);
                healthyRobot.SetHealthFraction(0.30f);
                Physics.SyncTransforms();

                CavitationImplosion.Apply(impact, damage: 1f, pullRadius: 4f, pullDistance: 0f, staggerSeconds: 0f);

                Assert.IsTrue(nearRobot.IsConverted, "a robot 1.9m from the implosion at 20% HP must convert");
                Assert.AreEqual(Team.Player, nearRobot.Team);
                Assert.IsFalse(farRobot.IsConverted, "a robot 2.1m from the implosion must not convert, even at 20% HP");
                Assert.IsFalse(healthyRobot.IsConverted, "a robot at 30% HP must not convert, even within range");

                // === AC5: a fourth conversion while three are converted is refused; the existing three
                // are unaffected.
                secondConverted = NewGunner("Second", impact + new Vector3(0f, 0f, 5f));
                thirdConverted = NewGunner("Third", impact + new Vector3(0f, 0f, 10f));
                fourthCandidate = NewGunner("Fourth", impact + new Vector3(0f, 0f, 15f));
                secondConverted.SetHealthFraction(0.1f);
                thirdConverted.SetHealthFraction(0.1f);
                fourthCandidate.SetHealthFraction(0.1f);
                Assert.IsTrue(secondConverted.TryConvert());
                Assert.IsTrue(thirdConverted.TryConvert());
                Assert.AreEqual(3, RobotEnemy.Converted.Count, "test setup: three robots (nearRobot + these two) must be converted");

                Assert.IsFalse(fourthCandidate.TryConvert(), "a fourth conversion while three are already converted must be refused");
                Assert.AreEqual(3, RobotEnemy.Converted.Count, "the existing three converted robots must be unaffected by the refused fourth");
                Assert.IsTrue(nearRobot.IsConverted, "the oldest converted robot must remain converted");

                // === AC6: a converted robot's burnout deals damage to a robot 2m away and none to Max at
                // the same distance.
                maxGo = new GameObject("MV-716 test Max", typeof(CharacterController));
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();
                maxGo.transform.position = nearRobot.transform.position + new Vector3(2f, 0f, 0f);

                RobotEnemy bystander = NewGunner("Bystander", nearRobot.transform.position + new Vector3(0f, 0f, 2f));
                bystanderGo = bystander.gameObject;
                float bystanderHealthBefore = bystander.HealthCurrent;
                float maxHealthBefore = playerHealth.Current;
                Physics.SyncTransforms();

                nearRobot.TickConversion(19f);
                Assert.IsTrue(nearRobot.IsConverted, "must still be converted before the 20s mark");
                nearRobot.TickConversion(1f); // elapsed 20s exactly (19f + 1f, both exact in float — avoids boundary flake)

                Assert.Less(bystander.HealthCurrent, bystanderHealthBefore, "burnout must damage a robot within its 3m AoE");
                Assert.AreEqual(maxHealthBefore, playerHealth.Current, 1e-3f, "burnout must never damage Max");
                Assert.IsFalse(nearRobot.IsConverted, "a burned-out robot is no longer converted");
                bool nearRobotStillInConvertedRegistry = false;
                foreach (RobotEnemy r in RobotEnemy.Converted)
                {
                    if (r == nearRobot) { nearRobotStillInConvertedRegistry = true; break; }
                }
                Assert.IsFalse(nearRobotStillInConvertedRegistry, "a burned-out robot must free its conversion slot");

                // === AC7: converted robots are excluded from the live-robot cap and included in the
                // area's clear condition.
                var mixedCensus = new[] { secondConverted, thirdConverted, farRobot };
                Assert.AreEqual(1, RobotPopulation.LiveCapCount(mixedCensus),
                    "the live-robot cap must count only the non-converted, engageable robot (farRobot)");
                Assert.IsFalse(RobotPopulation.IsAreaClear(mixedCensus),
                    "an area with a still-converted (not yet burned-out) robot must not read as clear");

                var onlyConvertedCensus = new[] { secondConverted, thirdConverted };
                Assert.AreEqual(0, RobotPopulation.LiveCapCount(onlyConvertedCensus),
                    "converted robots alone must contribute nothing to the live-robot cap");
                Assert.IsFalse(RobotPopulation.IsAreaClear(onlyConvertedCensus),
                    "converted-but-not-yet-burned-out robots must still block the area's clear condition");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                if (splicerGo != null) Object.DestroyImmediate(splicerGo);
                if (secondSplicerGo != null) Object.DestroyImmediate(secondSplicerGo);
                if (bystanderGo != null) Object.DestroyImmediate(bystanderGo);
                if (nearRobot != null) Object.DestroyImmediate(nearRobot.gameObject);
                if (farRobot != null) Object.DestroyImmediate(farRobot.gameObject);
                if (healthyRobot != null) Object.DestroyImmediate(healthyRobot.gameObject);
                if (secondConverted != null) Object.DestroyImmediate(secondConverted.gameObject);
                if (thirdConverted != null) Object.DestroyImmediate(thirdConverted.gameObject);
                if (fourthCandidate != null) Object.DestroyImmediate(fourthCandidate.gameObject);
            }
        }
    }
}
