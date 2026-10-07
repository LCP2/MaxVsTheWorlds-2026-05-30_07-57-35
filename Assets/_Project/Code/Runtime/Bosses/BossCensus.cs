using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// How many boss instances a fight has, and whether they are all down (MV-542). Mirrors
    /// <c>FactoryCensus</c>'s own reasoning one level up the same problem: a single-boss fight worked
    /// because "the boss" and "the last boss" were the same event, and everything downstream — the HUD
    /// boss bar, <c>BossVictoryPayoff</c>, the exit door, results — could listen to one boss's own
    /// signals directly. With 2+ bosses those are different questions, so one place has to answer them:
    /// the HUD bar shows the COMBINED health of every living boss, and anything keyed on "the boss is
    /// dead" waits for the LAST one, not the first.
    ///
    /// Keyed on <see cref="MonoBehaviour"/>, not a concrete boss class (MV-696): every method here only
    /// ever uses the boss reference as a dictionary key and to pass back to the caller, so any boss type
    /// (<c>BigBermudaBoss</c>, <c>SludgequeenBoss</c>, …) can share this one census and the one HUD bar
    /// it drives, without this class needing to know about a second concrete type.
    ///
    /// Deliberately does not read a boss's health off the instance itself — <see cref="ReportHealth"/>
    /// is pushed in by the boss whenever its own <c>DestructibleHealth</c> changes, the same shape as
    /// every other signal this class emits, and the one that lets this be tested without needing a
    /// boss to be mid-fight (its own Intro/Fight phase machine only advances on Update, which EditMode
    /// tests cannot tick).
    /// </summary>
    public static class BossCensus
    {
        private static readonly List<MonoBehaviour> Living = new List<MonoBehaviour>(4);
        private static readonly Dictionary<MonoBehaviour, float> CurrentByBoss = new Dictionary<MonoBehaviour, float>(4);
        private static readonly Dictionary<MonoBehaviour, float> MaxByBoss = new Dictionary<MonoBehaviour, float>(4);
        private static readonly Dictionary<MonoBehaviour, int> AreaByBoss = new Dictionary<MonoBehaviour, int>(4);
        private static readonly Dictionary<MonoBehaviour, int> SpawnLevelByBoss = new Dictionary<MonoBehaviour, int>(4);
        private static readonly Dictionary<MonoBehaviour, float> SpawnProgressByBoss = new Dictionary<MonoBehaviour, float>(4);
        private static bool _engaged;

        /// <summary>MV-995: every area index whose boss(es) have ALL been defeated at some point this
        /// scene's lifetime — populated in <see cref="ReportDefeated"/> alongside
        /// <see cref="LastDefeatedAreaIndex"/>, and (on a RESUME) seeded directly from a checkpoint's own
        /// <c>SaveSlotData.CheckpointDefeatedBossAreas</c> by <see cref="ApplyCheckpointDefeatedAreas"/> —
        /// so a boss that hasn't even <see cref="Register"/>-ed yet (a cold-boot RESUME's freshly-built
        /// boss stands Dormant until Max walks into its wake area, well after the checkpoint restore
        /// runs) still has somewhere to ask "was my area already beaten" the instant it tries to
        /// <see cref="Register"/> itself. Unlike <c>AreaByBoss</c>, entries here are never removed — a
        /// defeat is permanent for the rest of the scene's life, cleared only by <see cref="Reset"/>.</summary>
        private static readonly HashSet<int> DefeatedAreas = new HashSet<int>(4);

        /// <summary>MV-721: sum of <c>Max</c> for every boss that has died so far in the CURRENT fight
        /// (i.e. since the engage latch last re-armed), with an implicit <c>Current</c> of 0 for each.
        /// <see cref="EmitCombinedHealth"/> folds this into its denominator so a death only ever removes
        /// that boss's CURRENT from the numerator — its MAX stays counted against the bar for the rest
        /// of the fight, instead of both vanishing together and instantly recomputing a higher fraction
        /// from whoever is left standing. Reset alongside <see cref="_engaged"/> (both are scoped to
        /// "the current fight", and — same as <see cref="_engaged"/> — only one fight is ever live at
        /// once, so this needs no per-area keying).</summary>
        private static float _deadMaxThisFight;

        public static int LivingCount => Living.Count;

        /// <summary>The area index <see cref="ReportDefeated"/> most recently cleared (MV-698) — set
        /// just before <see cref="HudSignals.EmitBossDefeated"/> fires, so a synchronous subscriber
        /// (<c>BossVictoryPayoff</c>) can read which area's last boss just fell without the scene-wide
        /// signal itself needing to carry a payload (see <c>BigBermudaRig.OnDefeated</c>'s own doc
        /// comment on why <see cref="HudSignals.BossDefeated"/> stays identity-free).</summary>
        public static int LastDefeatedAreaIndex { get; private set; }

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
            _deadMaxThisFight = 0f;
            LastDefeatedAreaIndex = 0;
            DefeatedAreas.Clear();
        }

        /// <summary>A boss has woken and joined the fight. The FIRST one engages the HUD boss bar;
        /// later ones (a 2+ boss fight) just add to the combined total — engaging a second time would
        /// snap the bar back to full and re-show the name card mid-fight.</summary>
        public static void Register(MonoBehaviour boss, string name, int phases, float current, float max,
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
        public static void ReportHealth(MonoBehaviour boss, float current, float max)
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
        public static void ReportSpawnLevel(MonoBehaviour boss, int level, float progress01)
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
        /// for its own last one.
        ///
        /// MV-721: EVERY death — not just the area's last one — raises <see cref="HudSignals.BossKilled"/>
        /// with the position captured HERE, synchronously, before the caller's own OnDeath deactivates
        /// the GameObject. That is this ticket's answer to "the spectacle must read the boss's position
        /// before it disappears": hand the position to the signal itself rather than have
        /// <c>BossSpectacle</c>/<c>BossDebris</c> go looking for the (about to vanish, and in a 2+ boss
        /// fight, ambiguous) instance afterward.</summary>
        public static void ReportDefeated(MonoBehaviour boss)
        {
            if (boss == null || !Living.Remove(boss)) return;
            Vector3 diedAt = boss.transform.position;
            _deadMaxThisFight += MaxByBoss[boss];
            CurrentByBoss.Remove(boss);
            MaxByBoss.Remove(boss);
            SpawnLevelByBoss.Remove(boss);
            SpawnProgressByBoss.Remove(boss);
            int areaIndex = AreaByBoss.TryGetValue(boss, out int a) ? a : 0;
            AreaByBoss.Remove(boss);

            HudSignals.EmitBossKilled(diedAt);

            if (!AnyLivingIn(areaIndex))
            {
                LastDefeatedAreaIndex = areaIndex;
                DefeatedAreas.Add(areaIndex);
                HudSignals.EmitBossHealth(0f);
                HudSignals.EmitBossDefeated();
            }
            else
            {
                EmitCombinedHealth();
                EmitCombinedSpawnLevel();
            }

            // MV-661: _engaged was a scene-lifetime latch, cleared only by Reset() (once per map
            // build) — so a12's boss engaging the bar once and later dying left it set for the rest
            // of the scene, and a20's/a30's bosses (registering later in the SAME scene) never got
            // their own BossEngaged: neither bar ever appeared for their fights. Engagement must be
            // per-fight instead: once literally no boss anywhere is left standing, the next Register
            // (whichever area it's in) is a fresh fight and must re-engage the bar.
            //
            // MV-721: _deadMaxThisFight rides alongside the SAME latch, for the same reason — it is
            // scoped to "the fight that just fully ended", and must not bleed its dead bosses' Max into
            // the NEXT area's fresh combined bar.
            if (Living.Count == 0) { _engaged = false; _deadMaxThisFight = 0f; }
        }

        /// <summary>This boss's GameObject went away without dying properly (a scene torn down, a
        /// test fixture cleaned up). Not a kill; nothing is raised here. Same shape as
        /// <c>FactoryCensus.Forget</c> — belt-and-braces against a boss outliving its level as a dead
        /// reference in a static list.</summary>
        public static void Forget(MonoBehaviour boss)
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
            foreach (KeyValuePair<MonoBehaviour, int> kv in AreaByBoss)
                if (kv.Value == areaIndex && Living.Contains(kv.Key)) return true;
            return false;
        }

        /// <summary>MV-1113: every living boss's current world position — what the Sentinel arrival
        /// search reads to keep a deploy point clear of a boss, the same way it already keeps clear of
        /// every awake <c>RobotEnemy</c>. Area-unfiltered, unlike <see cref="AnyLivingIn"/> — a boss
        /// fight occupies its whole arena, so every living boss counts regardless of which area it is
        /// keyed to.</summary>
        public static IEnumerable<Vector3> LivingPositions()
        {
            foreach (MonoBehaviour boss in Living)
                if (boss != null) yield return boss.transform.position;
        }

        /// <summary>Has <paramref name="areaIndex"/>'s boss already been beaten (MV-995) — either earlier
        /// THIS fight (<see cref="ReportDefeated"/>) or on a prior run, restored via
        /// <see cref="ApplyCheckpointDefeatedAreas"/>? This is what a boss's own <c>Wake()</c> must check
        /// BEFORE calling <see cref="Register"/>, since a cold-boot RESUME rebuilds every authored boss
        /// fresh and Dormant — the checkpoint restore that knows an area was already cleared runs well
        /// before Max ever walks into that boss's wake area and triggers this check for real.</summary>
        public static bool IsAreaDefeated(int areaIndex) => DefeatedAreas.Contains(areaIndex);

        /// <summary>The area indices <see cref="DefeatedAreas"/> holds right now, sorted (MV-995) — what
        /// <see cref="MaxWorlds.Save.SaveSystem.CaptureCheckpoint"/> persists into
        /// <c>SaveSlotData.CheckpointDefeatedBossAreas</c> so a later RESUME can restore the same set via
        /// <see cref="ApplyCheckpointDefeatedAreas"/>.</summary>
        public static int[] DefeatedAreaIndices()
        {
            var result = new int[DefeatedAreas.Count];
            DefeatedAreas.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        /// <summary>Re-applies a checkpoint's already-recorded boss defeats (MV-995), called once by
        /// <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME. Unconditionally seeds
        /// <see cref="IsAreaDefeated"/> for every area in <paramref name="areaIndices"/> — the seed is
        /// what actually stops a fresh, still-Dormant boss from ever engaging once it wakes for real,
        /// since at restore time it normally hasn't <see cref="Register"/>-ed itself yet. Also silently
        /// removes any boss that IS already Living in one of these areas (belt-and-braces for the rare
        /// case one has already woken) — no <see cref="HudSignals"/>, no <c>BossVictoryPayoff</c>, no
        /// drops: this is restoring history, not scoring a fresh kill. A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointDefeatedAreas(IReadOnlyList<int> areaIndices)
        {
            if (areaIndices == null || areaIndices.Count == 0) return;

            foreach (int areaIndex in areaIndices)
            {
                DefeatedAreas.Add(areaIndex);

                var toRemove = new List<MonoBehaviour>();
                foreach (KeyValuePair<MonoBehaviour, int> kv in AreaByBoss)
                    if (kv.Value == areaIndex) toRemove.Add(kv.Key);

                foreach (MonoBehaviour boss in toRemove)
                {
                    Living.Remove(boss);
                    CurrentByBoss.Remove(boss);
                    MaxByBoss.Remove(boss);
                    AreaByBoss.Remove(boss);
                    SpawnLevelByBoss.Remove(boss);
                    SpawnProgressByBoss.Remove(boss);
                    if (boss != null) UnityEngine.Object.DestroyImmediate(boss.gameObject);
                }
            }
        }

        private static void EmitCombinedHealth()
        {
            float current = 0f, max = _deadMaxThisFight; // MV-721: a dead boss keeps its Max, at Current 0
            foreach (MonoBehaviour b in Living)
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
            foreach (MonoBehaviour b in Living)
            {
                int l = SpawnLevelByBoss.TryGetValue(b, out int lv) ? lv : 1;
                float p = SpawnProgressByBoss.TryGetValue(b, out float pr) ? pr : 0f;
                if (l > level || (l == level && p > progress)) { level = l; progress = p; }
            }
            HudSignals.EmitBossSpawnLevel(level, progress);
        }
    }
}
