using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-716 — Hijacking, both ways: a Splicer (a role on the existing Gunner kind) channels one of
    /// Max's Sentinels over to the enemy team for a timed window (AC1-3), and a critically-damaged,
    /// port-exposed robot converts to Max's side for a timed window, capped at three at once, ending in
    /// a robots-only burnout AoE (AC5-7). One consolidated test method covers both halves (MV-465 Rule 1:
    /// at most one new test per ticket) — every assertion reads RESOLVED state (a Sentinel's/robot's
    /// actual <see cref="Team"/>, <see cref="Sentinel.IsHijacked"/>, <see cref="RobotEnemy.IsConverted"/>,
    /// <see cref="RobotEnemy.HealthCurrent"/>), never an authored constant.
    ///
    /// MV-1034 removed the distance-gated <c>CavitationImplosion.Apply</c> entry point this test's own
    /// AC4 used to drive conversion through (Lee: the charge/cavitation shot "does not work as a
    /// design"); <see cref="RobotEnemy.TryConvert()"/> itself is unchanged and reused by the new trap
    /// ability (MV-1035), so this now calls it directly — the distance-to-impact half of the old trigger
    /// no longer exists to test.
    ///
    /// MV-1035 also removed the 20s expiry/burnout blast this test's own AC6 used to drive through
    /// <c>TickConversion</c> (deleted — a converted robot now stays converted until it is killed) and
    /// replaced the fixed <c>MaxConvertedRobots</c>=3 cap with the settable <see cref="RobotEnemy.ConversionCap"/>,
    /// which this test now sets explicitly rather than relying on a hardcoded default. AC6's old burnout
    /// assertions are gone with the mechanic; AC7's census is asserted directly against the still-converted
    /// robots from AC5 instead.
    ///
    /// Fails on 5d7e4e0 (MV-715, the commit before this ticket): none of
    /// <see cref="RobotEnemy.TryBeginSpliceChannel"/>, <see cref="RobotEnemy.TickSpliceChannel"/>,
    /// <see cref="Sentinel.BeginHijack"/>, <see cref="Sentinel.TickHijack"/>,
    /// <see cref="RobotEnemy.TryConvert()"/> or <see cref="RobotPopulation"/> exist on that commit, so
    /// this test does not compile there.
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
            RobotEnemy.ConversionCap = 1; // MV-1035: static, mutable — never leak one test's cap into another
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
        public void SplicerChannelFlipsAndReturnsASentinel_AndOverrideConvertsARobot_MV716()
        {
            GameObject sentinelGo = null, splicerGo = null, secondSplicerGo = null;
            RobotEnemy nearRobot = null, healthyRobot = null, secondConverted = null,
                thirdConverted = null, fourthCandidate = null;
            try
            {
                RobotEnemy.ConversionCap = 3; // MV-1035: replaces the old fixed MaxConvertedRobots=3
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

                // === MV-1034: conversion is driven directly through RobotEnemy.TryConvert's own health
                // gate now (the old distance-to-implosion half of the trigger no longer exists — see
                // class doc). healthyRobot survives, unconverted, to be counted in AC7's census below —
                // placed opposite nearRobot (4m away) so AC6's burnout below (3m AoE, centred on
                // nearRobot) can't kill it before that census runs.
                Vector3 impact = new Vector3(0f, 0f, 100f); // far from everything above
                nearRobot = NewGunner("Near20pct", impact + new Vector3(1.9f, 0f, 0f));
                healthyRobot = NewGunner("Healthy30pct", impact + new Vector3(-2.1f, 0f, 0f));
                nearRobot.SetHealthFraction(0.20f);
                healthyRobot.SetHealthFraction(0.30f);

                Assert.IsTrue(nearRobot.TryConvert(), "a robot below the 25% override threshold must convert");
                Assert.AreEqual(Team.Player, nearRobot.Team);
                Assert.IsFalse(healthyRobot.TryConvert(), "a robot at 30% HP must not convert");

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

                // === AC7: converted robots are excluded from the live-robot cap and included in the
                // area's clear condition. MV-1035 removed the 20s expiry/burnout this used to reach via
                // TickConversion (deleted — a converted robot now stays converted until it is killed),
                // so this asserts directly against the still-converted robots from AC5 above.
                var mixedCensus = new[] { nearRobot, healthyRobot };
                Assert.AreEqual(1, RobotPopulation.LiveCapCount(mixedCensus),
                    "the live-robot cap must count only the non-converted, engageable robot (healthyRobot)");
                Assert.IsFalse(RobotPopulation.IsAreaClear(mixedCensus),
                    "an area with a still-converted robot must not read as clear");

                var onlyConvertedCensus = new[] { nearRobot, secondConverted, thirdConverted };
                Assert.AreEqual(0, RobotPopulation.LiveCapCount(onlyConvertedCensus),
                    "converted robots alone must contribute nothing to the live-robot cap");
                Assert.IsFalse(RobotPopulation.IsAreaClear(onlyConvertedCensus),
                    "converted robots must still block the area's clear condition — MV-1035: permanently, not just until a 20s timer");
            }
            finally
            {
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                if (splicerGo != null) Object.DestroyImmediate(splicerGo);
                if (secondSplicerGo != null) Object.DestroyImmediate(secondSplicerGo);
                if (nearRobot != null) Object.DestroyImmediate(nearRobot.gameObject);
                if (healthyRobot != null) Object.DestroyImmediate(healthyRobot.gameObject);
                if (secondConverted != null) Object.DestroyImmediate(secondConverted.gameObject);
                if (thirdConverted != null) Object.DestroyImmediate(thirdConverted.gameObject);
                if (fourthCandidate != null) Object.DestroyImmediate(fourthCandidate.gameObject);
            }
        }
    }
}
