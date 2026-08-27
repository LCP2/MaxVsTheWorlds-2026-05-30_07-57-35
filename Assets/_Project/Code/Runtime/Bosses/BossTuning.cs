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
        /// duel; nothing else has to move.
        ///
        /// Recalibrated by MV-287 (was 4000): removing the per-run level/power ramp means Max's DPS
        /// on the way to the boss is now permanently the un-ramped base output — about the same as the
        /// old level-1 floor — so this is scaled down by the same ~2.32x the old level-7 ramp used to
        /// assume, preserving the original ~2 minute target rather than silently tripling the fight.</summary>
        public const float Health = 1725f;

        /// <summary>Below this fraction it enrages: faster, and it starts raining blades.</summary>
        public const float EnrageThreshold = 0.5f;

        // ---------------------------------------------------------------- movement

        /// <summary>MV-410: quartered from 3.6 — Lee's live-build report was "let's make it 1/4 the
        /// speed".</summary>
        public const float MoveSpeed = 0.9f;

        /// <summary>MV-588: the ram/charge is gone — the boss just walks at Max and stops this far
        /// out. Replaces the old <c>DesiredRange</c> circling.</summary>
        public const float Standoff = 3f;

        public const float EnrageMoveScale = 1.2f;   // was 1.4

        // ---------------------------------------------------------------- the fight escalates on its own clock
        //
        // MV-588: the charge is gone entirely — "kill it before its army outgrows you" replaces it. The
        // brood volley's composition escalates purely with time alive since Wake, never with anything
        // the player does (see BigBermudaBrain.SpawnLevel / BroodSpawnLevels).

        /// <summary>Seconds alive before the spawn level steps up. Level = 1 + floor(aliveSeconds /
        /// this), capped at <see cref="MaxSpawnLevel"/>.</summary>
        public const float SpawnLevelInterval = 30f;

        /// <summary>The ceiling the spawn level climbs to — L4 draws from the whole roster.</summary>
        public const int MaxSpawnLevel = 4;

        // ---------------------------------------------------------------- the blade rain (enrage) —
        // untouched by MV-588: the charge that USED to drop grass along its path is gone, but the
        // zone mechanics and their tuning are not this ticket's to move.

        /// <summary>Clippings the OLD charge used to drop along its path. Left authored, untouched
        /// (MV-588), even though nothing spawns one any more now the charge itself is gone.</summary>
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
        // Big Bermuda's SIGNATURE attack: it opens the side hatches and flings a volley of robots out
        // onto the lawn, so the fight is "kill it before its army outgrows you" (MV-588 removed the ram
        // entirely — this is now the boss's ONLY attack, not an addition to one). Every number here is a
        // feel call and lives in one place, exposed on the Settings panel's BOSS tab (YT-138) so it can
        // be swept live.

        /// <summary>Seconds between volleys before it enrages. The breather between waves — long enough
        /// that a volley is an event you brace for, not a constant drizzle.
        ///
        /// MV-410: halved from 7 to 3.5 — Lee's live-build report was "let's make it spawn robots much
        /// fast[er]". Enraged interval is this times <see cref="VolleyEnrageScale"/>, so 4.2s -> 2.1s.</summary>
        public const float VolleyInterval = 3.5f;

        /// <summary>Interval multiplier once enraged: the waves come ~40% faster as it reddens, so phase
        /// two is the boss leaning on the swarm harder — a &lt;1 "faster when angry" shape.</summary>
        public const float VolleyEnrageScale = 0.6f;

        /// <summary>The spawn TELL: how long the hatches crack and the cavity floods BEFORE the robots
        /// are flung — the player's window to reposition, long enough to see and act on.</summary>
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
