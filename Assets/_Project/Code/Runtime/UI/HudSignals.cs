using System;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Fire-and-forget event hub the HUD (YT-30) listens on for combat overlays, so
    /// gameplay can announce hits/kills without depending on any HUD type. Emitters
    /// (e.g. <c>RobotEnemy</c>) call the <c>Emit*</c> helpers; the <c>HudController</c>
    /// subscribes while enabled and unsubscribes on teardown. All events are null-safe
    /// with no subscribers (so headless tests that damage enemies stay silent).
    /// </summary>
    public static class HudSignals
    {
        /// <summary>A damageable took a hit. (worldPos, amount, crit)</summary>
        public static event Action<Vector3, float, bool> DamageDealt;

        /// <summary>A pickup/reward dropped. (worldPos, label, colour)</summary>
        public static event Action<Vector3, string, Color> Pickup;

        /// <summary>An enemy died — HUD converts to XP + a SPARKS pickup. (worldPos)</summary>
        public static event Action<Vector3> EnemyKilled;

        /// <summary>Max levelled up (YT-67). (newLevel, worldPos) — gameplay listens to get
        /// stronger, the feel layer listens to make it land.</summary>
        public static event Action<int, Vector3> LevelUp;

        /// <summary>A real factory came online — HUD stops driving the arena tracker off kills
        /// and waits for <see cref="FactoryDestroyed"/> instead (YT-37).</summary>
        public static event Action FactoryRegistered;

        /// <summary>A factory was destroyed — HUD advances the arena tracker for real. (worldPos)</summary>
        public static event Action<Vector3> FactoryDestroyed;

        /// <summary>A real boss exists — HUD stops driving the boss bar off the kill stand-in (YT-27).</summary>
        public static event Action BossRegistered;

        /// <summary>The boss engaged — show the bar + name card. (name, phases)</summary>
        public static event Action<string, int> BossEngaged;

        /// <summary>The boss's HP changed. (normalized 0..1)</summary>
        public static event Action<float> BossHealthChanged;

        /// <summary>The boss was defeated — hide the bar.</summary>
        public static event Action BossDefeated;

        /// <summary>The boss-death payoff has run its course (YT-152) — Max walked out through the exit
        /// gate, or the sequence timed out. This is the cue to finally show the results card, decoupled
        /// from <see cref="BossDefeated"/> so the blow-up, the flung parts and the walk-out can play
        /// first instead of the run cutting straight to results.</summary>
        public static event Action BossPayoffFinished;

        public static void EmitDamage(Vector3 worldPos, float amount, bool crit = false)
            => DamageDealt?.Invoke(worldPos, amount, crit);

        public static void EmitPickup(Vector3 worldPos, string label, Color color)
            => Pickup?.Invoke(worldPos, label, color);

        public static void EmitEnemyKilled(Vector3 worldPos)
            => EnemyKilled?.Invoke(worldPos);

        public static void EmitLevelUp(int level, Vector3 worldPos)
            => LevelUp?.Invoke(level, worldPos);

        public static void EmitFactoryRegistered()
            => FactoryRegistered?.Invoke();

        public static void EmitFactoryDestroyed(Vector3 worldPos)
            => FactoryDestroyed?.Invoke(worldPos);

        public static void EmitBossRegistered()
            => BossRegistered?.Invoke();

        public static void EmitBossEngaged(string name, int phases)
            => BossEngaged?.Invoke(name, phases);

        public static void EmitBossHealth(float normalized)
            => BossHealthChanged?.Invoke(normalized);

        public static void EmitBossDefeated()
            => BossDefeated?.Invoke();

        public static void EmitBossPayoffFinished()
            => BossPayoffFinished?.Invoke();
    }
}
