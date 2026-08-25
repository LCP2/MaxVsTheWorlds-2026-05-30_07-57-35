using System.Collections.Generic;
using MaxWorlds.UI;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// How many <see cref="BigBermudaBoss"/> instances a fight has, and whether they are all down
    /// (MV-542). Mirrors <c>FactoryCensus</c>'s own reasoning one level up the same problem: a
    /// single-boss fight worked because "the boss" and "the last boss" were the same event, and
    /// everything downstream — the HUD boss bar, <c>BossVictoryPayoff</c>, the exit door, results —
    /// could listen to one boss's own signals directly. With 2+ bosses those are different
    /// questions, so one place has to answer them: the HUD bar shows the COMBINED health of every
    /// living boss, and anything keyed on "the boss is dead" waits for the LAST one, not the first.
    ///
    /// Deliberately does not read a boss's health off the instance itself — <see cref="ReportHealth"/>
    /// is pushed in by the boss whenever its own <c>DestructibleHealth</c> changes, the same shape as
    /// every other signal this class emits, and the one that lets this be tested without needing a
    /// boss to be mid-fight (its own Intro/Fight phase machine only advances on Update, which EditMode
    /// tests cannot tick).
    /// </summary>
    public static class BossCensus
    {
        private static readonly List<BigBermudaBoss> Living = new List<BigBermudaBoss>(4);
        private static readonly Dictionary<BigBermudaBoss, float> CurrentByBoss = new Dictionary<BigBermudaBoss, float>(4);
        private static readonly Dictionary<BigBermudaBoss, float> MaxByBoss = new Dictionary<BigBermudaBoss, float>(4);
        private static bool _engaged;

        public static int LivingCount => Living.Count;

        /// <summary>Wipe the census. Called when a level starts building (the map engine), so a scene
        /// loaded a second time — in the game or in a test run — counts its own bosses and not the
        /// previous level's ghosts. Same reasoning as <c>FactoryCensus.Reset</c>.</summary>
        public static void Reset()
        {
            Living.Clear();
            CurrentByBoss.Clear();
            MaxByBoss.Clear();
            _engaged = false;
        }

        /// <summary>A boss has woken and joined the fight. The FIRST one engages the HUD boss bar;
        /// later ones (a 2+ boss fight) just add to the combined total — engaging a second time would
        /// snap the bar back to full and re-show the name card mid-fight.</summary>
        public static void Register(BigBermudaBoss boss, string name, int phases, float current, float max)
        {
            if (boss == null || Living.Contains(boss)) return;
            Living.Add(boss);
            CurrentByBoss[boss] = current;
            MaxByBoss[boss] = max;

            if (!_engaged)
            {
                _engaged = true;
                HudSignals.EmitBossEngaged(name, phases);
            }
            EmitCombinedHealth();
        }

        /// <summary>This boss's own HP changed (damage, or a live Retune from the Settings slider).
        /// Pushes the recombined (sum current / sum max) fraction to the HUD boss bar.</summary>
        public static void ReportHealth(BigBermudaBoss boss, float current, float max)
        {
            if (boss == null || !Living.Contains(boss)) return;
            CurrentByBoss[boss] = current;
            MaxByBoss[boss] = max;
            EmitCombinedHealth();
        }

        /// <summary>This boss died. Victory/death payoffs (<c>BossVictoryPayoff</c>, the exit door,
        /// results) must wait for the LAST one — only defeat the HUD bar once none remain.</summary>
        public static void ReportDefeated(BigBermudaBoss boss)
        {
            if (boss == null || !Living.Remove(boss)) return;
            CurrentByBoss.Remove(boss);
            MaxByBoss.Remove(boss);

            if (Living.Count == 0)
            {
                HudSignals.EmitBossHealth(0f);
                HudSignals.EmitBossDefeated();
            }
            else
            {
                EmitCombinedHealth();
            }
        }

        /// <summary>This boss's GameObject went away without dying properly (a scene torn down, a
        /// test fixture cleaned up). Not a kill; nothing is raised here. Same shape as
        /// <c>FactoryCensus.Forget</c> — belt-and-braces against a boss outliving its level as a dead
        /// reference in a static list.</summary>
        public static void Forget(BigBermudaBoss boss)
        {
            Living.Remove(boss);
            CurrentByBoss.Remove(boss);
            MaxByBoss.Remove(boss);
        }

        private static void EmitCombinedHealth()
        {
            float current = 0f, max = 0f;
            foreach (BigBermudaBoss b in Living)
            {
                current += CurrentByBoss[b];
                max += MaxByBoss[b];
            }
            HudSignals.EmitBossHealth(max > 0f ? current / max : 0f);
        }
    }
}
