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

        /// <summary>Max's ring. Cyan — the Water Blaster's own colour (<see cref="MaxWorlds.Rendering.Element.Water"/>),
        /// and nothing else in the game is cyan, so it can only mean "you".
        ///
        /// Deliberately NOT his body red (<c>CharacterSkin.PlayerBody</c>). Red and amber are spoken
        /// for: they are the danger telegraph (YT-53). A red ring permanently under the player would
        /// be saying "danger here" about the one spot that is always safe to be.</summary>
        public static readonly Color PlayerRing = new Color(0.31f, 0.76f, 0.97f, 0.65f);

        /// <summary>Every hostile actor's ring — robots and the boss alike.
        ///
        /// Orange against Max's cyan is the strongest figure-ground pair available here: it is
        /// near-complementary (so the two never merge at a glance), it separates cleanly from the
        /// green lawn, and blue-vs-orange is the one high-contrast pair that survives the common
        /// forms of colour blindness — red-vs-green, the obvious choice for "them vs us", is the one
        /// that doesn't.
        ///
        /// It sits in the same warm family as the wind-up telegraph on purpose, and is quieter than
        /// it in every dimension — hollow where the telegraph is filled, half its alpha, and drawn
        /// underneath it. So a robot winding up doesn't swap one mark for a different one; its
        /// quiet ring FILLS and flares. The alarm grows out of the thing you were already tracking,
        /// which is what makes it read as escalation rather than as a new object appearing.</summary>
        public static readonly Color EnemyRing = new Color(0.98f, 0.46f, 0.16f, 0.42f);

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
