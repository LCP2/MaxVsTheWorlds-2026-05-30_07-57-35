using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The aim reticle (YT-84). The one property that actually matters is TRUTHFULNESS: the shape on
    /// the ground has to be the weapon, not a picture of it. A reticle that disagrees with the hit
    /// test is worse than no reticle at all — it is a lie the player has been taught to trust, and
    /// they will walk into range they don't have.
    /// </summary>
    public sealed class AimReticleTests
    {
        private static Mesh Build(float range, float halfAngle) =>
            AimReticleMesh.Build(range, halfAngle);

        private static void Free(Mesh m) => Object.DestroyImmediate(m);

        [Test]
        public void ItReachesExactlyAsFarAsTheWeaponDoes()
        {
            var m = Build(6f, 35f);
            try
            {
                // The drawn wedge runs a hair past the range so the bright boundary has room to
                // feather out — but only a hair, and DrawnReach says so out loud.
                float reach = m.bounds.max.z;
                Assert.AreEqual(AimReticleMesh.DrawnReach(6f), reach, 0.05f,
                    "the reticle promises a different reach than the blaster has");
                Assert.Less(reach, 6f * 1.1f, "the reticle overstates the weapon's range");
            }
            finally { Free(m); }
        }

        [Test]
        public void ALongerWeaponDrawsALongerReticle()
        {
            var near = Build(6f, 35f);
            var far = Build(12f, 35f);
            try
            {
                Assert.Greater(far.bounds.max.z, near.bounds.max.z * 1.8f,
                    "range doubled and the reticle didn't move — it is a hardcoded shape, which is " +
                    "the exact thing this ticket exists to prevent");
            }
            finally { Free(near); Free(far); }
        }

        [Test]
        public void AWiderSprayDrawsAWiderReticle()
        {
            var narrow = Build(6f, 10f);
            var wide = Build(6f, 60f);
            try
            {
                Assert.Greater(wide.bounds.size.x, narrow.bounds.size.x * 2f,
                    "the spread changed and the arc didn't — the indicator isn't reading the stats");
            }
            finally { Free(narrow); Free(wide); }
        }

        [Test]
        public void TheArcSpansTheConeTheHitTestActuallyUses()
        {
            // halfAngle is HALF the total spread — the same convention SprayHit.InCone uses. Draw the
            // full angle where the code meant the half and the reticle promises twice the coverage
            // the weapon has, which is the most dangerous possible way to be wrong.
            const float halfAngle = 30f;
            var m = Build(10f, halfAngle);
            try
            {
                float widest = 0f;
                foreach (var v in m.vertices)
                    if (v.magnitude > 0.01f)
                        widest = Mathf.Max(widest, Mathf.Abs(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg));

                Assert.AreEqual(halfAngle, widest, 0.5f,
                    "the drawn arc is not the cone the blaster hits in");
            }
            finally { Free(m); }
        }

        [Test]
        public void ThePointyEndPointsForward()
        {
            var m = Build(6f, 35f);
            try
            {
                Assert.Greater(m.bounds.max.z, 0f, "the reticle has no forward extent at all");
                Assert.GreaterOrEqual(m.bounds.min.z, -0.01f,
                    "the reticle extends BEHIND Max — he would be told he can spray backwards");
            }
            finally { Free(m); }
        }

        [Test]
        public void TheBoundaryIsTheLoudestPartOfIt()
        {
            // The fill is decoration. The one fact the player needs is where their reach ENDS, so the
            // edge has to be the thing the eye lands on.
            var m = Build(6f, 35f);
            try
            {
                var verts = m.vertices;
                var cols = m.colors;

                float centreAlpha = cols[0].a;
                float edgeAlpha = 0f, midAlpha = 0f;
                for (int i = 0; i < verts.Length; i++)
                {
                    float r = verts[i].magnitude;
                    if (Mathf.Abs(r - 6f) < 0.05f) edgeAlpha = Mathf.Max(edgeAlpha, cols[i].a);
                    if (Mathf.Abs(r - 6f * 0.82f) < 0.05f) midAlpha = Mathf.Max(midAlpha, cols[i].a);
                }

                Assert.Greater(edgeAlpha, midAlpha * 2f, "the range boundary doesn't stand out");
                Assert.Greater(edgeAlpha, centreAlpha, "the reticle is brightest under Max's own feet");
            }
            finally { Free(m); }
        }

        [Test]
        public void ItFadesToNothingAtItsOuterRim_SoTheEdgeDoesNotAlias()
        {
            var m = Build(6f, 35f);
            try
            {
                var verts = m.vertices;
                var cols = m.colors;
                float outermost = 0f, alphaThere = 1f;

                for (int i = 0; i < verts.Length; i++)
                {
                    float r = verts[i].magnitude;
                    if (r > outermost) { outermost = r; alphaThere = cols[i].a; }
                }

                Assert.AreEqual(0f, alphaThere, 1e-3,
                    "the wedge stops dead at its outer rim — at this camera a hard bright line " +
                    "aliases into a dashed one");
            }
            finally { Free(m); }
        }

        [Test]
        public void ItIsQuietWhileIdle_AndComesUpWhenYouAim()
        {
            // Always legible, never shouting. The Craft Bible is explicit that juice must not obscure
            // a threat, and this is the biggest mark on the field.
            Assert.Less(AimReticle.IdleAlpha, 0.25f,
                "the idle reticle is loud enough to be permanently painting the lawn");
            Assert.Greater(AimReticle.IdleAlpha, 0.05f,
                "the idle reticle is invisible — range stops being something you just know");
            Assert.Greater(AimReticle.AimingAlpha, AimReticle.IdleAlpha * 1.5f,
                "aiming barely changes it; the player gets no feedback that they're committing");
            Assert.Less(AimReticle.AimingAlpha, 0.6f,
                "an aiming reticle this solid will hide the robots standing in it");
        }

        [Test]
        public void EveryGroundMarkThatMattersDrawsOverTheReticle()
        {
            // It's the biggest thing on the lawn, so it goes at the very bottom of the stack. An
            // enemy's anchor ring and — above all — its attack telegraph must both punch straight
            // through it. Juice never obscures a threat.
            Assert.Less(AimReticle.GroundLift, GroundAnchorTuning.ShadowLift,
                "the reticle can cover an actor's contact shadow");
            Assert.Less(AimReticle.GroundLift, GroundAnchorTuning.RingLift,
                "the reticle can cover an enemy's anchor ring — you would lose them inside your own aim");
            Assert.Less(AimReticle.GroundLift, GroundRing.GroundLift,
                "the reticle can cover an attack telegraph — the player would lose the one mark " +
                "they have to react to, inside the mark showing where they're shooting");
            Assert.Greater(AimReticle.GroundLift, 0f, "coplanar with the lawn — it will z-fight");
        }

        [Test]
        public void TheReticleIsDrawnInMaxsOwnColour_WhateverThatCurrentlyIs()
        {
            // Not "the reticle is cyan". It WAS cyan, and cyan was right for about four hours: YT-85
            // gave the ground rings one team axis and YT-86 gave the bodies the opposite one, and
            // YT-87 had to unpick every actor being haloed in the other side's colour. A reticle
            // that names its own colour is one more thing to forget when the palette moves.
            //
            // So the assertion is a relationship, not a value: Max's reach is drawn in Max's colour.
            Assert.AreEqual(GroundAnchorTuning.PlayerRing.r, AimReticle.Tint.r, 1e-3);
            Assert.AreEqual(GroundAnchorTuning.PlayerRing.g, AimReticle.Tint.g, 1e-3);
            Assert.AreEqual(GroundAnchorTuning.PlayerRing.b, AimReticle.Tint.b, 1e-3);
        }

        [Test]
        public void TheReticleCanNeverBeMistakenForTheENEMYsColour()
        {
            // The failure YT-87 actually shipped: Max's mark wearing the swarm's hue. If this ever
            // fails, the player is being told that the cone they are aiming is a robot.
            var mine = AimReticle.Tint;
            var theirs = GroundAnchorTuning.EnemyRing;

            float delta = Mathf.Abs(mine.r - theirs.r)
                        + Mathf.Abs(mine.g - theirs.g)
                        + Mathf.Abs(mine.b - theirs.b);

            Assert.Greater(delta, 1.0f,
                "Max's aim cone is drawn in (or near) the enemy colour — this is YT-87 all over " +
                "again, and it is worse than drawing no reticle at all");
        }

        [Test]
        public void ADegenerateWeaponDoesNotProduceABrokenMesh()
        {
            foreach (var (range, angle) in new[] { (0f, 0f), (-5f, -20f), (1e6f, 400f) })
            {
                var m = Build(range, angle);
                try
                {
                    Assert.Greater(m.vertexCount, 0);
                    foreach (var v in m.vertices)
                        Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.z),
                            $"range {range} / angle {angle} produced NaN geometry");
                }
                finally { Free(m); }
            }
        }
    }
}
