using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The always-on ground-anchor layer (YT-85): what colour each side's ring is, how big it is,
    /// and — the fiddly part — what height each ground mark draws at.
    ///
    /// Authored in code, not as [SerializeField]s on a scene object, for the reason YT-80 paid for
    /// the hard way: Unity bakes serialized fields into Backyard_Slice.unity and the scene then
    /// silently outranks the source.
    /// </summary>
    public static class GroundAnchorTuning
    {
        // --- Colour ------------------------------------------------------------------------------

        // A RING MUST AGREE WITH THE BODY STANDING IN IT. That is the whole rule, and it is the one
        // this file got wrong (YT-87).
        //
        // These two colours were originally the other way round — cyan for Max, orange for the swarm
        // — and on the day they were written they were right. Then YT-86 gave Max a hot orange body
        // and the robots cold turquoise/violet ones, and nothing failed, because nothing tested the
        // two systems against each other. The shipped result: Max's body sat 9 deg from the ENEMY
        // ring's hue and the rusher's body sat 11 deg from MAX's ring. Every actor on the field was
        // standing in the other team's colour.
        //
        // That is not a missing cue, it is a lying one — worse than having no rings at all. You hunt
        // for the orange blob that is you, and the lawn was full of orange haloes that were robots.
        // So the rings moved to the bodies, not the other way round: Max is orange in the spec
        // (YT-76), and warm-vs-cold is the axis the Craft Bible builds actor readability on.
        //
        // Blue-vs-orange — the pair that survives the common forms of colour blindness, where the
        // obvious red-vs-green does not — is kept. It is just put on the correct side now.

        /// <summary>Max's ring. Warm, to agree with his warm body — but a bright PEARL, not an amber.
        ///
        /// The saturation is doing the work here. Saturated red and gold on the ground layer are
        /// spoken for: they are the danger telegraph (YT-53, <see cref="TelegraphVfx"/>, which warns
        /// red and arms gold), and a saturated warm ring permanently under the player would be
        /// saying "danger here" about the one spot that is always safe to be. This is warm enough to
        /// belong to Max and desaturated enough that it can never be mistaken for an alarm — and it
        /// is separated from the telegraph three ways over, by saturation, by shape (hollow, not a
        /// filled disc) and by alpha.
        ///
        /// It is also the BRIGHTEST mark on the ground layer, which is deliberate: of all the things
        /// the eye can find first, "where am I" is the one worth spending that on.
        ///
        /// The alpha and the tint are set where they are because of what they land on. This is an
        /// alpha-blended mark over a DARK lawn (the grass composites out around value 0.20), so the
        /// ring's on-screen colour is not the colour written here — it is this colour dragged a third
        /// of the way toward green. The first cut of this was a softer cream at 0.62 alpha and it
        /// composited to a muddy khaki: still legible, but a washed-out halo is a washed-out answer
        /// to "where am I". Brighter and less saturated wins twice, because it also moves it further
        /// from the telegraph's gold.</summary>
        public static readonly Color PlayerRing = new Color(1f, 0.95f, 0.86f, 0.72f);

        /// <summary>Every hostile actor's ring — robots and the boss alike. Cold and deep, to agree
        /// with the cold bodies standing in it: the rusher's turquoise (188 deg) and the bruiser's
        /// violet (275 deg) both sit in the same half of the wheel as this indigo (232 deg), so the
        /// ring reinforces the body instead of arguing with it. It opposes Max's orange across the
        /// wheel, and it separates hard from the green lawn.
        ///
        /// WHAT THIS COST, stated plainly: the old warm ring sat in the same family as the wind-up
        /// telegraph on purpose, so a robot winding up didn't swap one mark for another — its quiet
        /// ring FILLED and flared, and the alarm grew out of the thing you were already tracking.
        /// That was a genuinely nice piece of escalation and it is gone; the wind-up now changes hue
        /// as well as filling. The Craft Bible's tie-break order is readability > game feel > visual
        /// richness, and knowing which blob is you outranks how gracefully the alarm arrives.</summary>
        public static readonly Color EnemyRing = new Color(0.25f, 0.34f, 0.95f, 0.5f);

        /// <summary>The contact shadow. Not black — a black blob on a sunlit lawn reads as a hole in
        /// the world. This is a deep, slightly blue-shifted shade, which is what a shadow on grass
        /// under a warm sun actually looks like.</summary>
        public static readonly Color ContactShadow = new Color(0.06f, 0.07f, 0.11f, 0.34f);

        // --- Size --------------------------------------------------------------------------------

        /// <summary>Ring radius, as a multiple of the actor's real collider footprint. Wider than the
        /// body, so the ring reads as a halo around the actor rather than as a belt on it.</summary>
        public const float RingRadiusScale = 1.7f;

        /// <summary>Contact-shadow radius, as a multiple of the footprint. Tighter than the ring and
        /// slightly tighter than the body: a shadow that reaches past an actor's own outline stops
        /// looking like contact and starts looking like a puddle.</summary>
        public const float ShadowRadiusScale = 1.15f;

        // --- Height ------------------------------------------------------------------------------
        //
        // Ground marks are coplanar quads on a y=0 lawn, so the ONLY thing keeping them from
        // z-fighting each other into a shimmering mess is that each one draws at its own height.
        // The order below is a priority order, and it is the whole reason these are constants in one
        // place rather than three magic numbers in three files:
        //
        //   0.012  contact shadow   — furthest down; it is under the actor, so it is under everything
        //   0.020  anchor ring      — above its own shadow, below anything that demands a reaction
        //   0.030  danger telegraph — GroundRing.GroundLift, unchanged (YT-53). Always on top: an
        //                             always-on decoration must never be able to cover the one mark
        //                             the player has to move away from.

        public const float ShadowLift = 0.012f;
        public const float RingLift = 0.020f;

        /// <summary>The footprint an actor actually occupies, in world metres — read off the
        /// CharacterController that defines it rather than from a table that can drift away from it.
        ///
        /// The lossyScale term is load-bearing: a CharacterController silently multiplies its radius
        /// by the transform's scale, and the robots ARE their own scaled primitive (a rusher's body
        /// is 0.8). Take cc.radius at face value and every rusher gets a ring 20% too small, which is
        /// the same class of bug as YT-74.</summary>
        public static float FootprintRadius(CharacterController cc)
        {
            if (cc == null) return 0.5f;
            var s = cc.transform.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }
    }
}
