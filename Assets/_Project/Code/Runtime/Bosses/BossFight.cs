using UnityEngine;
using MaxWorlds.Combat;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// How long does the Big Bermuda fight last? (YT-94, re-scoped MV-588)
    ///
    /// MV-588 removed the ram/charge entirely — the old dodge-window model this class used to carry
    /// (how long the tell burns, whether a player can walk clear of the contact radius) described an
    /// attack that no longer exists, so it went with it. What is left is the fight-length half of the
    /// model: what the blaster actually does to the boss's HP, cut by how much of the fight the player
    /// spends not pointing it at anything.
    ///
    /// THE ASSUMPTIONS ARE THE WHOLE MODEL, so they are named, not buried. It is a MODEL, not a proof —
    /// it cannot say the fight is fun, only that it lands in a length worth calling a boss fight rather
    /// than a chore.
    /// </summary>
    public static class BossFight
    {
        /// <summary>Fraction of the fight the player is actually inside the blaster's 6 m range AND
        /// pointing it at the boss. The rest is spent repositioning, dodging the brood volley's adds,
        /// and chasing the boss's own slow walk-and-standoff.</summary>
        public const float Engagement = 0.45f;

        /// <summary>How long a human takes to see a tell and start moving. A quarter of a second is the
        /// textbook figure for a simple visual reaction and it is generous on a phone, held in one hand,
        /// at the bus stop. Untouched by MV-588: still what the blade rain's own warning is measured
        /// against.</summary>
        public const float ReactionSeconds = 0.25f;

        // ---------------------------------------------------------------- how long does it last?

        /// <summary>What the blaster actually does to the boss, per second: the gun's raw output, cut
        /// by the tank running dry, and cut again by all the time he spends not pointing it at
        /// anything. MV-287 removed the per-run level/power ramp — Max's DPS is now permanently the
        /// weapon's base output (no automatic scaling) until a chosen upgrade changes it, so there is
        /// no level input here any more.</summary>
        public static float PlayerDps(float engagement)
        {
            const float RawDps = 40f;   // 4 damage a tick, a tick every 0.1 s (WaterBlaster)

            return RawDps
                 * BlasterTuning.WorstCaseUptime
                 * Mathf.Clamp01(engagement);
        }

        public static float PlayerDps() => PlayerDps(Engagement);

        /// <summary>Seconds to put the boss down.</summary>
        public static float SecondsToKill(float engagement) =>
            BossTuning.Health / Mathf.Max(0.01f, PlayerDps(engagement));

        public static float SecondsToKill() => SecondsToKill(Engagement);
    }
}
