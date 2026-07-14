using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Player;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Is the Big Bermuda fight fair, and how long does it last? (YT-94)
    ///
    /// "Too difficult" is a feeling, and a feeling is not something you can tune against — you tune
    /// against it and then wait a day to find out. So the feeling is turned into arithmetic here: how
    /// long the player has to react to a charge, how much damage lands on someone who dodges most of
    /// them, and how long the boss takes to fall. Then the tests assert the numbers, and the fight can
    /// be re-tuned without a playtest to tell you whether you have made it worse.
    ///
    /// THE ASSUMPTIONS ARE THE WHOLE MODEL, so they are named, not buried. They are stated below and
    /// every one of them is arguable — which is the point. If Lee says "a competent player dodges more
    /// than that", the number to change is <see cref="CompetentDodge"/>, and the tests will say what it
    /// costs. A model whose assumptions you cannot see is a vibe with a decimal point.
    ///
    /// It is a MODEL, not a proof. It cannot tell you the fight is fun. What it can do is stop the
    /// obviously unfair fight — the one where the tell is shorter than a human reaction — from ever
    /// shipping again, which is the one we shipped.
    /// </summary>
    public static class BossFight
    {
        // ---------------------------------------------------------------- the player we assume

        /// <summary>How long a human takes to see the tell and start moving. A quarter of a second is
        /// the textbook figure for a simple visual reaction and it is generous on a phone, held in one
        /// hand, at the bus stop.</summary>
        public const float ReactionSeconds = 0.25f;

        /// <summary>Max's radius, near enough. He has to clear the charge's contact radius by his own
        /// width or the "dodge" clips him anyway.</summary>
        public const float PlayerRadius = 0.5f;

        /// <summary>How fast Max runs (PlayerController). A dodge is only as good as the legs, and a
        /// tell you cannot outrun is not a tell.</summary>
        public const float PlayerSpeed = 6f;

        /// <summary>The level Max tends to reach by the time he gets to the boss: two factories' worth
        /// of robots is roughly forty-odd kills, and the XP track puts that at level 7. It matters
        /// because the power ramp more than doubles his DPS on the way (YT-67) — tune the boss's HP
        /// against a level-1 blaster and you have tuned it against a gun nobody brings.</summary>
        public const int LevelAtTheBoss = 7;

        /// <summary>Fraction of the fight the player is actually inside the blaster's 6 m range AND
        /// pointing it at the boss. The rest is spent repositioning, dodging, and closing the gap the
        /// boss keeps opening — it circles at exactly the blaster's reach.</summary>
        public const float Engagement = 0.45f;

        /// <summary>How much of a clearly-telegraphed attack a COMPETENT player avoids. Not a perfect
        /// player: this is someone who reads the tell, moves, and still gets clipped once in six.</summary>
        public const float CompetentDodge = 0.85f;

        /// <summary>And someone who is not paying attention. The fight has to be able to kill him, or
        /// it is not a fight.</summary>
        public const float CarelessDodge = 0.55f;

        /// <summary>Fraction of a boss fight spent out of contact long enough for the out-of-combat
        /// regen to actually start (YT-80 makes you earn it: five clean seconds). In a boss fight you
        /// earn it rarely.</summary>
        public const float CleanFraction = 0.2f;

        // ---------------------------------------------------------------- can you dodge the charge?

        /// <summary>
        /// How long the player has, from the tell lighting up to the boss arriving: the wind-up, plus
        /// the time it takes to cross the gap it opened before charging.
        /// </summary>
        public static float DodgeWindow(bool enraged)
        {
            float windup = enraged ? BossTuning.EnragedChargeWindup : BossTuning.ChargeWindup;
            float speed = enraged ? BossTuning.EnragedChargeSpeed : BossTuning.ChargeSpeed;

            return windup + BossTuning.DesiredRange / Mathf.Max(0.01f, speed);
        }

        /// <summary>How long it actually takes to get out of the way: see it, then walk your own width
        /// clear of the thing's contact radius.</summary>
        public static float TimeToDodge =>
            ReactionSeconds + (BossTuning.ChargeContactRadius + PlayerRadius) / PlayerSpeed;

        /// <summary>The margin a player has on a charge. Negative means the attack cannot be dodged,
        /// only absorbed — which is what "too difficult" turned out to mean.</summary>
        public static float DodgeMargin(bool enraged) => DodgeWindow(enraged) - TimeToDodge;

        // ---------------------------------------------------------------- how long does it last?

        /// <summary>What the blaster actually does to the boss, per second, in the hands of the player
        /// who gets there: the gun's raw output, ramped by his level, cut by the tank running dry, and
        /// cut again by all the time he spends not pointing it at anything.</summary>
        public static float PlayerDps(int level)
        {
            const float RawDps = 40f;   // 4 damage a tick, a tick every 0.1 s (WaterBlaster)

            return RawDps
                 * PowerRamp.DpsMultiplier(level)
                 * BlasterTuning.WorstCaseUptime
                 * Engagement;
        }

        /// <summary>Seconds to put the boss down.</summary>
        public static float SecondsToKill(int level) =>
            BossTuning.Health / Mathf.Max(0.01f, PlayerDps(level));

        // ---------------------------------------------------------------- can you survive it?

        /// <summary>One full attack cycle of the boss, in seconds.</summary>
        public static float CycleSeconds(bool enraged)
        {
            float cycle = BossTuning.Reposition + BossTuning.ChargeWindup
                        + BossTuning.ChargeTime + BossTuning.Recover;

            return enraged ? cycle * BossTuning.EnrageTimeScale : cycle;
        }

        /// <summary>
        /// Damage the boss lands per second on a player who dodges <paramref name="dodge"/> of it.
        ///
        /// A dodged zone is not a zone you never touch — it is one you get out of after a single tick,
        /// which is why the zones' LIFE was as much of the problem as their damage. What lands is one
        /// bite; what used to land, if you were slow, was three.
        /// </summary>
        public static float IncomingDps(float dodge, bool enraged)
        {
            float hit = Mathf.Clamp01(1f - dodge);
            float cycle = CycleSeconds(enraged);

            float charge = BossTuning.ChargeContactDamage * hit / cycle;
            float grass = BossTuning.GrassDamage * hit / cycle;

            float blades = enraged
                ? BossTuning.BladeDamage * BossTuning.BladeCount * hit / BossTuning.BladeInterval
                : 0f;

            return charge + grass + blades;
        }

        /// <summary>Damage taken over a whole fight — half of it enraged, because the enrage is at
        /// half health and the player's damage is roughly even across the fight.</summary>
        public static float DamageOverTheFight(float dodge, int level)
        {
            float seconds = SecondsToKill(level);

            return (IncomingDps(dodge, enraged: false) + IncomingDps(dodge, enraged: true))
                 * 0.5f * seconds;
        }

        /// <summary>What the slow trickle of out-of-combat regen gives back across a fight in which you
        /// are rarely out of combat.</summary>
        public static float RegenOverTheFight(int level) =>
            PlayerTuning.RegenPerSec * CleanFraction * SecondsToKill(level);

        /// <summary>Health left at the end, for a player of this skill. At or below zero, he died.</summary>
        public static float HealthLeft(float dodge, int level, float playerHealth = 100f) =>
            playerHealth + RegenOverTheFight(level) - DamageOverTheFight(dodge, level);

        public static bool Survives(float dodge, int level, float playerHealth = 100f) =>
            HealthLeft(dodge, level, playerHealth) > 0f;
    }
}
