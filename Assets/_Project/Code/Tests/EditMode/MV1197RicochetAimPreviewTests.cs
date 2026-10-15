using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1197 (the one new test, Rule 1): the RICOCHET aim preview must draw the disc's REAL first two
    /// bounce legs, using the SAME hit/bounce code the disc itself uses -- not a separate approximation.
    /// In a fixture with two walls angled so a 45-degree shot bounces off each in turn, this computes the
    /// aim preview's own predicted bounce points (<see cref="RicochetDisc.ComputePreviewLegs"/>), then
    /// fires a REAL disc along the identical aim and ticks it to its own two actual bounce contacts
    /// (MV-465 Tier 2: resolved values, never an authored constant). Asserts the two sets of points match
    /// within 0.05 m.
    ///
    /// Fails on base commit 778fec4 (main HEAD before this ticket) -- it cannot compile:
    /// <see cref="RicochetDisc.ComputePreviewLegs"/>, <see cref="RicochetDisc.FirstBouncePoint"/> and
    /// <see cref="RicochetDisc.SecondBouncePoint"/> don't exist there at all.
    /// </summary>
    public sealed class MV1197RicochetAimPreviewTests
    {
        private GameObject _wallA;
        private GameObject _wallB;

        [TearDown]
        public void TearDown()
        {
            RicochetDisc.DestroyPoolForTests();
            if (_wallA != null) Object.DestroyImmediate(_wallA);
            if (_wallB != null) Object.DestroyImmediate(_wallB);
        }

        [Test]
        public void AimPreviewBouncePointsMatchTheRealDiscsOwnFirstTwoBounces()
        {
            // --- the fixture: "two walls" per the ticket -- a vertical wall the 45-degree shot meets
            // first, and a horizontal wall its reflected second leg meets next, so both legs end on a
            // real, distinct contact.
            _wallA = BuildWall(new Vector3(3f, 1f, 0f), new Vector3(0.2f, 2f, 30f));
            _wallB = BuildWall(new Vector3(0f, 1f, 10f), new Vector3(30f, 2f, 0.2f));
            // Colliders built at CreatePrimitive's own default origin before being moved can still read
            // stale bounds on the very first SphereCast otherwise -- same gotcha MV1195's own fixture
            // comment documents.
            Physics.SyncTransforms();

            Vector3 origin = Vector3.zero;
            Vector3 direction = new Vector3(1f, 0f, 1f).normalized;
            const float radius = Ricochet.DefaultRadius;
            const float maxLegDistance = Ricochet.PreviewMaxLegDistance;

            var previewPoints = new Vector3[3];
            int pointCount = RicochetDisc.ComputePreviewLegs(origin, direction, radius, maxLegDistance,
                previewPoints, out int bounceCount);

            Assert.AreEqual(3, pointCount, "test precondition: the fixture's two walls must produce two preview legs");
            Assert.AreEqual(2, bounceCount, "test precondition: both preview legs must end on a real contact");

            // VfxMaterials.ParticleShader logs an informational line the FIRST time any test in the whole
            // run resolves it (a one-time, process-wide cache fill) -- harmless, but Unity's test
            // framework fails a test on any log it didn't expect. Same ignoreFailingMessages idiom
            // MV1195RicochetBounceDamageMorphTests already uses around RicochetDisc.Spawn.
            LogAssert.ignoreFailingMessages = true;
            RicochetDisc disc;
            try
            {
                disc = RicochetDisc.Spawn(origin, direction, Ricochet.DefaultSpeed, bounces: 2,
                    powerLevel: 1, aimLevel: 0,
                    damage: 12f, bankBonusPerBounce: 0.2f, turnRateDegPerSec: 0f, lookaheadMetres: 0f,
                    radius, Ricochet.DefaultLifetimeSeconds);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsNotNull(disc, "test precondition: the pool must have room for one disc");

            const float step = 1f / 60f;
            const float budgetSeconds = 3f;
            int maxSteps = Mathf.CeilToInt(budgetSeconds / step);
            for (int i = 0; i < maxSteps && disc.BouncesTaken < 2; i++) disc.Tick(step);

            Assert.AreEqual(2, disc.BouncesTaken, "test precondition: the real disc must actually take both bounces");

            Assert.That(Vector3.Distance(previewPoints[1], disc.FirstBouncePoint), Is.LessThanOrEqualTo(0.05f),
                "the preview's first bounce point must match the real disc's own first bounce within 0.05m");
            Assert.That(Vector3.Distance(previewPoints[2], disc.SecondBouncePoint), Is.LessThanOrEqualTo(0.05f),
                "the preview's second bounce point must match the real disc's own second bounce within 0.05m");
        }

        private static GameObject BuildWall(Vector3 center, Vector3 size)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "MV1197 Wall";
            wall.transform.position = center;
            wall.transform.localScale = size;
            CoverLayer.Assign(wall);
            return wall;
        }
    }
}
