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
        private static readonly Dictionary<BigBermudaBoss, int> AreaByBoss = new Dictionary<BigBermudaBoss, int>(4);
        private static readonly Dictionary<BigBermudaBoss, int> SpawnLevelByBoss = new Dictionary<BigBermudaBoss, int>(4);
        private static readonly Dictionary<BigBermudaBoss, float> SpawnProgressByBoss = new Dictionary<BigBermudaBoss, float>(4);
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
            AreaByBoss.Clear();
            SpawnLevelByBoss.Clear();
            SpawnProgressByBoss.Clear();
            _engaged = false;
        }

        /// <summary>A boss has woken and joined the fight. The FIRST one engages the HUD boss bar;
        /// later ones (a 2+ boss fight) just add to the combined total — engaging a second time would
        /// snap the bar back to full and re-show the name card mid-fight.</summary>
        public static void Register(BigBermudaBoss boss, string name, int phases, float current, float max,
                                    int areaIndex)
        {
            if (boss == null || Living.Contains(boss)) return;
            Living.Add(boss);
            CurrentByBoss[boss] = current;
            MaxByBoss[boss] = max;
            AreaByBoss[boss] = areaIndex;
            SpawnLevelByBoss[boss] = 1;
            SpawnProgressByBoss[boss] = 0f;

            if (!_engaged)
            {
                _engaged = true;
                HudSignals.EmitBossEngaged(name, phases);
            }
            EmitCombinedHealth();
            EmitCombinedSpawnLevel();
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

        /// <summary>This boss's spawn level (MV-588 — how far its brood volley composition has
        /// escalated) changed. Pushes the HIGHEST level among every living boss, and that leader's own
        /// progress, to the HUD's spawn-level bar — same "combine, don't last-write-wins" reasoning as
        /// <see cref="ReportHealth"/>.</summary>
        public static void ReportSpawnLevel(BigBermudaBoss boss, int level, float progress01)
        {
            if (boss == null || !Living.Contains(boss)) return;
            SpawnLevelByBoss[boss] = level;
            SpawnProgressByBoss[boss] = progress01;
            EmitCombinedSpawnLevel();
        }

        /// <summary>This boss died. Victory/death payoffs (<c>BossVictoryPayoff</c>, the exit door,
        /// results) must wait for the LAST one IN ITS OWN AREA — not the last one scene-wide (MV-591).
        /// Reading it scene-wide made a12's single boss the last boss in the game, which fired the
        /// whole victory chain 18 areas early. a20 authors two and a30 three; each area's payoff waits
        /// for its own last one.</summary>
        public static void ReportDefeated(BigBermudaBoss boss)
        {
            if (boss == null || !Living.Remove(boss)) return;
            CurrentByBoss.Remove(boss);
            MaxByBoss.Remove(boss);
            SpawnLevelByBoss.Remove(boss);
            SpawnProgressByBoss.Remove(boss);
            int areaIndex = AreaByBoss.TryGetValue(boss, out int a) ? a : 0;
            AreaByBoss.Remove(boss);

            if (!AnyLivingIn(areaIndex))
            {
                HudSignals.EmitBossHealth(0f);
                HudSignals.EmitBossDefeated();
            }
            else
            {
                EmitCombinedHealth();
                EmitCombinedSpawnLevel();
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
            AreaByBoss.Remove(boss);
            SpawnLevelByBoss.Remove(boss);
            SpawnProgressByBoss.Remove(boss);
        }

        /// <summary>Is any boss belonging to <paramref name="areaIndex"/> still alive? (MV-591) —
        /// what <see cref="MaxWorlds.Arena.WorldRunner"/> checks before raising
        /// <see cref="HudSignals.EmitRunComplete"/> for the final area.</summary>
        public static bool AnyLivingIn(int areaIndex)
        {
            foreach (KeyValuePair<BigBermudaBoss, int> kv in AreaByBoss)
                if (kv.Value == areaIndex && Living.Contains(kv.Key)) return true;
            return false;
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

        /// <summary>The HUD's spawn-level bar shows the HIGHEST level among every living boss (MV-588) —
        /// same "don't let the wrong one win" reasoning as the combined health bar, but max rather than
        /// sum: a level is a milestone, not a quantity to add up across bosses.</summary>
        private static void EmitCombinedSpawnLevel()
        {
            int level = 1;
            float progress = 0f;
            foreach (BigBermudaBoss b in Living)
            {
                int l = SpawnLevelByBoss.TryGetValue(b, out int lv) ? lv : 1;
                float p = SpawnProgressByBoss.TryGetValue(b, out float pr) ? pr : 0f;
                if (l > level || (l == level && p > progress)) { level = l; progress = p; }
            }
            HudSignals.EmitBossSpawnLevel(level, progress);
        }
    }
}
