using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The always-on ground-anchor layer (YT-85). Its whole job is to be READ, so what's pinned here
    /// is the things that decide whether it can be: that the two sides don't look alike, that the
    /// anchor can't be mistaken for the danger telegraph, and that the marks stack in the right
    /// order on the ground.
    /// </summary>
    public sealed class GroundAnchorTests
    {
        // --- Figure-ground: you must be able to find yourself ------------------------------------

        [Test]
        public void YouAndTheEnemyAreNotTheSameColour_AndNotByAWhisker()
        {
            var you = GroundAnchorTuning.PlayerRing;
            var them = GroundAnchorTuning.EnemyRing;

            float delta = Mathf.Abs(you.r - them.r) + Mathf.Abs(you.g - them.g) + Mathf.Abs(you.b - them.b);
            Assert.Greater(delta, 1.0f,
                "the two rings are too close to tell apart in a crowd, which is the one thing they exist to do");
        }

        [Test]
        public void YouAreCool_AndTheyAreWarm()
        {
            // Blue-vs-orange, not red-vs-green: it's the high-contrast pair that survives the common
            // forms of colour blindness. Getting this backwards would look fine to most people and
            // be unplayable for some.
            Assert.Greater(GroundAnchorTuning.PlayerRing.b, GroundAnchorTuning.PlayerRing.r,
                "Max's ring has gone warm — it now reads as a threat marker");
            Assert.Greater(GroundAnchorTuning.EnemyRing.r, GroundAnchorTuning.EnemyRing.b,
                "the enemy ring has gone cool — it now reads as friendly");
        }

        [Test]
        public void BothRingsStandOutFromTheLawn()
        {
            // A ring that matches the grass is a ring nobody sees. The lawn is Grass green.
            var grass = MaxWorlds.Rendering.ElementPalette.ColorOf(MaxWorlds.Rendering.Element.Grass);
            foreach (var (name, ring) in new[]
                     { ("player", GroundAnchorTuning.PlayerRing), ("enemy", GroundAnchorTuning.EnemyRing) })
            {
                float delta = Mathf.Abs(grass.r - ring.r) + Mathf.Abs(grass.g - ring.g) + Mathf.Abs(grass.b - ring.b);
                Assert.Greater(delta, 0.6f, $"the {name} ring disappears into the grass");
            }
        }

        [Test]
        public void TheAnchorsAreQuieterThanTheDangerTelegraph()
        {
            // The anchor is on all the time; the telegraph means MOVE. If the permanent decoration
            // shouted as loudly as the alarm, the alarm would stop working within a minute of play.
            // TelegraphVfx drives its alpha up to 0.95 as the strike lands.
            Assert.Less(GroundAnchorTuning.EnemyRing.a, 0.6f,
                "the always-on enemy ring is as loud as an incoming attack");
            Assert.Less(GroundAnchorTuning.ContactShadow.a, 0.6f,
                "the contact shadow is a hole in the lawn, not a shadow");
        }

        [Test]
        public void TheContactShadowIsDark_ButNotBlack()
        {
            var s = GroundAnchorTuning.ContactShadow;
            Assert.Less(s.r + s.g + s.b, 0.45f, "too bright to read as a shadow");
            Assert.Greater(s.r + s.g + s.b, 0.05f,
                "pure black reads as a hole punched in the world, not as shade on grass");
        }

        // --- What stacks on top of what ----------------------------------------------------------

        [Test]
        public void TheDangerTelegraphAlwaysDrawsOverTheAlwaysOnAnchors()
        {
            // These are coplanar quads on a flat lawn. Height IS the draw order, and an always-on
            // decoration that could cover the one mark you must react to would be actively harmful.
            Assert.Less(GroundAnchorTuning.ShadowLift, GroundAnchorTuning.RingLift,
                "the contact shadow would z-fight with, or cover, its own ring");
            Assert.Less(GroundAnchorTuning.RingLift, GroundRing.GroundLift,
                "an anchor ring can now cover an attack telegraph — the player would lose the tell");
        }

        [Test]
        public void EveryGroundMarkIsClearOfTheLawn()
        {
            // Coplanar with y=0 and they z-fight into a shimmering mess.
            Assert.Greater(GroundAnchorTuning.ShadowLift, 0f);
            Assert.Greater(GroundAnchorTuning.RingLift, 0f);
        }

        // --- Size ---------------------------------------------------------------------------------

        [Test]
        public void TheRingHalosTheActor_AndTheShadowHugsIt()
        {
            Assert.Greater(GroundAnchorTuning.RingRadiusScale, 1f,
                "a ring inside the body's own outline is a belt, not a halo — it won't separate " +
                "the actor from the ground");
            Assert.Less(GroundAnchorTuning.ShadowRadiusScale, GroundAnchorTuning.RingRadiusScale,
                "the shadow has spilled outside the ring; it reads as a puddle, not as contact");
        }

        [Test]
        public void TheFootprintIsTakenFromTheColliderThatActuallyDefinesIt()
        {
            // A CharacterController silently multiplies its radius by the transform's scale, and a
            // robot's root IS its scaled body (a rusher is 0.8). Read cc.radius at face value and
            // every rusher wears a ring 20% too small — the same class of bug as YT-74.
            var go = new GameObject("Footprint Test");
            try
            {
                var cc = go.AddComponent<CharacterController>();
                cc.radius = 0.5f;

                go.transform.localScale = Vector3.one;
                Assert.AreEqual(0.5f, GroundAnchorTuning.FootprintRadius(cc), 1e-3);

                go.transform.localScale = new Vector3(0.8f, 0.7f, 0.8f);   // a rusher
                Assert.AreEqual(0.4f, GroundAnchorTuning.FootprintRadius(cc), 1e-3,
                    "the ring ignored the body's scale");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AnActorWithNoControllerDoesNotCrashTheLayer()
        {
            Assert.Greater(GroundAnchorTuning.FootprintRadius(null), 0f);
        }

        // --- The textures -------------------------------------------------------------------------

        [Test]
        public void TheAnchorRingIsHollow_SoItDoesNotPaintTheGroundUnderTheActor()
        {
            // This is what keeps it from being confusable with the FILLED danger disc, and what lets
            // it sit under an actor without tinting the lawn it's standing on.
            var tex = VfxMaterials.Annulus(128);

            float centre = tex.GetPixel(64, 64).a;
            float rim = tex.GetPixel(64, 64 + 51).a;   // ~0.80 of the radius — where the rim lives
            float outside = tex.GetPixel(2, 2).a;

            Assert.Greater(rim, 0.5f, "the anchor ring has no rim — there is nothing to see");
            Assert.Less(centre, 0.2f, "the anchor ring is filled in; it now reads as a danger disc");
            Assert.Greater(rim, centre * 3f, "the rim must dominate the fill, or it isn't a ring");
            Assert.Less(outside, 0.02f, "the ring bleeds past its own edge");
        }

        [Test]
        public void TheAnchorRingIsHollowerThanTheDangerDisc()
        {
            // The two must not be able to be mistaken for one another. Shape carries that, not just
            // colour — so it survives a colour-blind player and a busy background alike.
            float anchorCentre = VfxMaterials.Annulus(128).GetPixel(64, 64).a;
            float dangerCentre = VfxMaterials.Ring(128).GetPixel(64, 64).a;

            Assert.Less(anchorCentre, dangerCentre,
                "the anchor is now as filled as the danger telegraph — the two will read as the same mark");
        }

        [Test]
        public void TheContactShadowIsSoftAllTheWayOut_WithNoEdge()
        {
            // A blob with a hard edge reads as a decal someone stamped on the grass. Contact shadows
            // have no boundary; that's what makes them read as shade rather than as paint.
            var tex = VfxMaterials.Glow(64);

            float centre = tex.GetPixel(32, 32).a;
            float mid = tex.GetPixel(32, 32 + 12).a;
            float edge = tex.GetPixel(32, 32 + 30).a;

            Assert.Greater(centre, 0.9f, "the shadow has no core");
            Assert.Greater(centre, mid, "the shadow does not fall off");
            Assert.Greater(mid, edge, "the shadow has a hard edge — it reads as a stamped decal");
            Assert.Less(edge, 0.1f, "the shadow never actually reaches zero; it will read as a disc");
        }
    }
}
