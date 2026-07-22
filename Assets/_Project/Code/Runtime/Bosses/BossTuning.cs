namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Everything about how hard Big Bermuda hits, in one place (YT-94).
    ///
    /// These are deliberately NOT <c>[SerializeField]</c>s on <see cref="BigBermudaBoss"/>. They used
    /// to be, and the scene silently won: <c>Backyard_Slice.unity</c> carries a serialized copy of
    /// every one of them, so the boss the code described was not the boss anyone fought, and editing
    /// the C# default did nothing at all. The last person to change the boss's HP had to RENAME the
    /// field to make the new value take — that is the workaround this file exists to retire. Same
    /// reasoning, and the same story, as <see cref="MaxWorlds.Combat.BlasterTuning"/>.
    ///
    /// "Boss tuning values are easy to adjust" is an acceptance criterion of this ticket, so: they are
    /// here, they are named after what the player feels, and nothing can shadow them.
    ///
    /// WHY THE FIGHT WAS UNFAIR, in the two numbers that mattered:
    ///
    ///   * The wind-up was 0.75 s, and the brain scaled EVERY phase by 0.65 when enraged — so the tell
    ///     before an enraged charge lasted 0.49 s. In that time the boss crossed the gap at 22 m/s.
    ///     Human reaction is about a quarter of a second and Max moves at 6, so there was no window:
    ///     the charge was not dodged, it was survived or not.
    ///   * A blade zone TICKS. 12 damage every 0.4 s for 1.2 s of life is 36 damage from ONE blade,
    ///     three of them landed around the player every 1.4 s, on top of the charges.
    ///
    /// Both are fixed here, and <see cref="BossFight"/> is the arithmetic that says so.
    /// </summary>
    public static class BossTuning
    {
        // ---------------------------------------------------------------- the fight's length

        /// <summary>Boss HP. THIS is the fight-length knob — the only one. At the DPS a player
        /// actually brings to the boss (see <see cref="BossFight"/>) it buys a fight of about two
        /// minutes, which is the YT-27 target this ticket asks to return to. Halve it for a one-minute
        /// duel; nothing else has to move.</summary>
        public const float Health = 4000f;

        /// <summary>Below this fraction it enrages: faster, and it starts raining blades.</summary>
        public const float EnrageThreshold = 0.5f;

        /// <summary>How much the enrage speeds its cycle up. It used to be 0.65 — which sped up the
        /// TELL as well as the attack, and a tell that shortens as the fight gets harder is the
        /// definition of unfair. It still winds up faster; it no longer winds up faster than you can
        /// read.</summary>
        public const float EnrageTimeScale = 0.85f;

        // ---------------------------------------------------------------- movement

        public const float MoveSpeed = 3.6f;
        public const float DesiredRange = 6f;
        public const float EnrageMoveScale = 1.2f;   // was 1.4

        /// <summary>How fast it crosses the arena at you. Was 16 (22.4 enraged), which at a 2.4 m
        /// contact radius is a hit you cannot step out of.</summary>
        public const float ChargeSpeed = 12f;

        // ---------------------------------------------------------------- the charge

        /// <summary>The dodge window: how long the tell burns before it commits. The single most
        /// important number in the fight, and it was the smallest.</summary>
        public const float ChargeWindup = 1.15f;     // was 0.75

        public const float Reposition = 1.3f;
        public const float ChargeTime = 0.9f;
        public const float Recover = 1.0f;

        public const float ChargeContactDamage = 13f;   // was 18
        public const float ChargeContactRadius = 2.4f;

        /// <summary>Clippings dropped along the charge. A trail to walk out of, not a second attack:
        /// it ticks, so its LIFE is as much of its damage as its damage is.</summary>
        public const float GrassDamage = 4f;            // was 6
        public const float GrassInterval = 0.18f;
        public const float GrassRadius = 1.7f;
        public const float GrassLife = 1.2f;            // was 1.8 — one tick fewer to stand in
        public const float GrassArm = 0.2f;

        // ---------------------------------------------------------------- the blade rain (enrage)

        public const float BladeInterval = 2.6f;        // was 1.4
        public const int BladeCount = 2;                // was 3
        public const float BladeDamage = 7f;            // was 12
        public const float BladeRadius = 1.5f;
        public const float BladeSpread = 5f;
        public const float BladeLife = 0.8f;            // was 1.2 — 36 damage a blade became 14
        public const float BladeArm = 0.85f;            // was 0.55 — long enough to walk out of

        // ---------------------------------------------------------------- the brood volley (YT-157)
        //
        // Big Bermuda's SIGNATURE second attack: it opens the side hatches and flings a volley of robots
        // out onto the lawn, so the fight becomes "the boss AND the swarm it keeps launching", not just
        // dodge-the-charge. Ramming stays; this is additive. Every number here is a feel call and lives
        // in one place, exposed on the Settings panel's BOSS tab (YT-138) so it can be swept live.

        /// <summary>Seconds between volleys before it enrages. The breather between waves — long enough
        /// that a volley is an event you brace for, not a constant drizzle.</summary>
        public const float VolleyInterval = 7f;

        /// <summary>Interval multiplier once enraged: the waves come ~40% faster as it reddens, so phase
        /// two is the boss leaning on the swarm harder. Same &lt;1 "faster when angry" shape as
        /// <see cref="EnrageTimeScale"/>.</summary>
        public const float VolleyEnrageScale = 0.6f;

        /// <summary>The spawn TELL: how long the hatches crack and the cavity floods BEFORE the robots
        /// are flung. This is the read — the player's window to reposition — so it is deliberately close
        /// to the charge wind-up (<see cref="ChargeWindup"/>), long enough to see and act on.</summary>
        public const float VolleyWindup = 1.2f;

        /// <summary>How long the hatches stay gaping after the fling, as the swarm spills — the "it is
        /// emptying" beat before the shell closes.</summary>
        public const float VolleyOpenHold = 0.7f;

        /// <summary>Robots flung per volley before enrage.</summary>
        public const int RobotsPerVolley = 2;

        /// <summary>Robots flung per volley once enraged — a bigger wave on top of the faster cadence.</summary>
        public const int RobotsPerVolleyEnraged = 3;

        /// <summary>The ceiling on adds alive at once. The boss fight is the ONE time nothing else caps
        /// the robot count (every factory is dead by now), so this is the whole "kiteable, not a wall of
        /// bodies" guarantee (YT-63/74/80) — a volley that would breach it throws fewer, or none.</summary>
        public const int MaxConcurrentAdds = 6;

        /// <summary>Whether it flings adds in phase one, or only once enraged. True = a second threat
        /// from the opening; false = the swarm is the phase-two escalation. Ships true so the signature
        /// attack is taught early; flip for an enrage-only wave.</summary>
        public const bool VolleyFiresBeforeEnrage = true;

        // The throw itself — the arc a flung robot travels from a hatch to the lawn.

        /// <summary>How high, in metres, an add arcs above the straight line to its landing spot — the
        /// lift that reads as "thrown" at the 72° camera.</summary>
        public const float VolleyArcApex = 4f;

        /// <summary>Seconds an add spends in the air before it lands and turns into a normal robot.</summary>
        public const float VolleyArcTime = 0.8f;

        /// <summary>How far off the boss's flank a hatch mouth sits (metres) — where the throw starts.
        /// Matches the rig's hatch height so the robot leaves from the open shell, not the floor.</summary>
        public const float HatchMuzzleSide = 2.2f;
        public const float HatchMuzzleHeight = 1.7f;

        /// <summary>How far out to the flank an add lands, and how much further each successive robot of
        /// the same volley is thrown, so a two-or-three wave fans out instead of stacking on one spot.</summary>
        public const float VolleyLandingSide = 6f;
        public const float VolleyLandingSpread = 2.2f;
        public const float VolleyLandingForward = 1.5f;

        // ---------------------------------------------------------------- derived

        /// <summary>The tell before an ENRAGED charge — the number that made the fight unfair.</summary>
        public static float EnragedChargeWindup => ChargeWindup * EnrageTimeScale;

        public static float EnragedChargeSpeed => ChargeSpeed * EnrageMoveScale;

        /// <summary>Worst case a single blade can do: it ticks for its whole life.</summary>
        public static float BladeWorstCase => BladeDamage * TicksIn(BladeLife);

        /// <summary>Worst case one patch of clippings can do.</summary>
        public static float GrassWorstCase => GrassDamage * TicksIn(GrassLife);

        /// <summary>How many times a zone bites if you stand in it for <paramref name="life"/>
        /// seconds. <see cref="DamageZone"/> ticks on a 0.4 s beat, and the first bite lands when it
        /// arms — which is what turned a 12-damage blade into a 36-damage one.</summary>
        public static int TicksIn(float life) => 1 + (int)(life / 0.4f);
    }
}
