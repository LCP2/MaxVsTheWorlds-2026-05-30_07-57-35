using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1331 (the one new test, Rule 1): a scene reload (RunFlow quit-to-menu, cross-world RESUME)
    /// destroys a pooled RicochetDisc/LightMissileBall's GameObject without ever calling its own Retire()
    /// first — neither is DontDestroyOnLoad. Proves, on RESOLVED values (MV-465 Tier 2): ClearAll() does
    /// not throw on the resulting stale pool entry (AC1/AC2's first half), and the very next Spawn() still
    /// returns a live, active instance rather than getting stuck behind the destroyed one (AC1/AC2's
    /// second half).
    ///
    /// Fails on base commit 7677383 (main HEAD before this ticket): both ClearAll() and Spawn() index
    /// their pool via a plain `_active` bool read with no Unity `== null` guard, so touching the stale
    /// entry throws MissingReferenceException.
    /// </summary>
    public sealed class MV1331StaticProjectilePoolReloadTests
    {
        [TearDown]
        public void TearDown()
        {
            RicochetDisc.DestroyPoolForTests();
            LightMissileBall.DestroyPoolForTests();
        }

        [Test]
        public void RicochetDiscClearAllSurvivesADestroyedPoolEntryAndSpawnStillReturnsALiveDisc()
        {
            RicochetDisc disc = RicochetDisc.Spawn(Vector3.zero, Vector3.forward, speed: 20f, bounces: 2,
                powerLevel: 1, aimLevel: 0, damage: 12f, bankBonusPerBounce: 0.2f, turnRateDegPerSec: 0f,
                lookaheadMetres: 0f, radius: 0.25f, lifetime: 5f);
            Assert.IsNotNull(disc, "test precondition: the pool must have room for one disc");

            // Simulate a scene reload: the disc's GameObject is destroyed without Retire() ever running
            // (RicochetDisc carries no DontDestroyOnLoad -- exactly the reported defect).
            Object.DestroyImmediate(disc.gameObject);

            Assert.DoesNotThrow(() => RicochetDisc.ClearAll(),
                "ClearAll must drop a destroyed pool entry rather than touch it");

            RicochetDisc respawned = RicochetDisc.Spawn(Vector3.zero, Vector3.forward, speed: 20f, bounces: 2,
                powerLevel: 1, aimLevel: 0, damage: 12f, bankBonusPerBounce: 0.2f, turnRateDegPerSec: 0f,
                lookaheadMetres: 0f, radius: 0.25f, lifetime: 5f);

            Assert.IsNotNull(respawned, "Spawn must still return a disc after a destroyed entry, not stay stuck behind it");
            Assert.IsTrue(respawned.IsActiveDisc, "the returned disc must be live/active");
        }

        [Test]
        public void LightMissileBallClearAllSurvivesADestroyedPoolEntryAndSpawnStillReturnsALiveBall()
        {
            LightMissileBall ball = LightMissileBall.Spawn(Vector3.zero, Vector3.forward, speed: 9f,
                damage: 30f, trackingBase: 170f, areaIndex: 0);
            Assert.IsNotNull(ball, "test precondition: the pool must have room for one ball");

            // Simulate a scene reload: same stale-entry hazard as RicochetDisc, see that test's comment.
            Object.DestroyImmediate(ball.gameObject);

            Assert.DoesNotThrow(() => LightMissileBall.ClearAll(),
                "ClearAll must drop a destroyed pool entry rather than touch it");

            LightMissileBall respawned = LightMissileBall.Spawn(Vector3.zero, Vector3.forward, speed: 9f,
                damage: 30f, trackingBase: 170f, areaIndex: 0);

            Assert.IsNotNull(respawned, "Spawn must still return a ball after a destroyed entry, not stay stuck behind it");
            Assert.IsTrue(respawned.IsActiveBall, "the returned ball must be live/active");
        }
    }
}
