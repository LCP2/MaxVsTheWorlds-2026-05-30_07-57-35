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

        /// <summary>A Supercell was collected (MV-519) — grants its cells instantly, no bank/cash-in
        /// step. (worldPos, cellsBefore, cellsAfter) — HudController drives its self-terminating burst +
        /// "+10" flyup + readout count-up event off this, since a plain floating toast (see
        /// <see cref="Pickup"/>) can't carry the readout's own before/after values.</summary>
        public static event Action<Vector3, int, int> SupercellCollected;

        /// <summary>An enemy died — HUD converts to a SPARKS pickup. (worldPos)</summary>
        public static event Action<Vector3> EnemyKilled;

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

        /// <summary>A Blinker just teleported (MV-330). (fromWorldPos, toWorldPos) — the reposition in
        /// <c>RobotEnemy.TickTeleport</c> is a same-frame snap, so this carries BOTH points rather than
        /// just one: unlike a death or a hit, the VFX has to land at two places, not one.</summary>
        public static event Action<Vector3, Vector3> BlinkerTeleported;

        /// <summary>Max himself teleported (MV-338). (fromWorldPos, toWorldPos) — same two-point shape
        /// as <see cref="BlinkerTeleported"/>, kept as a distinct event rather than reusing it: Max's own
        /// blink drives both a bigger VFX beat and a brief time-slow (<c>GameFeel</c>), neither of which
        /// should fire off an enemy's teleport.</summary>
        public static event Action<Vector3, Vector3> MaxTeleported;

        /// <summary>A homing missile detonated — a direct hit OR an out-of-fuel ground impact
        /// (MV-349). (worldPos, damage) — damage is 0 when the blast landed on empty ground, so
        /// listeners can tell a real hit from a miss without a second event.</summary>
        public static event Action<Vector3, float> MissileImpact;

        /// <summary>A homing missile just ran out of fuel and is sputtering before it drops
        /// (MV-349 AC3). (worldPos)</summary>
        public static event Action<Vector3> MissileSputtering;

        /// <summary>An out-of-fuel missile bounced off the ground (MV-349 AC3). (worldPos)</summary>
        public static event Action<Vector3> MissileBounced;

        /// <summary>Teleport's joystick started being aimed (MV-371) — (the ability's full blink
        /// distance at the current level, metres). The camera-zoom controller listens rather than
        /// taking a direct reference, so the joystick control doesn't have to know the camera zoom
        /// exists.</summary>
        public static event Action<float> TeleportAimStarted;

        /// <summary>Teleport's joystick aim ended — release (fired or aborted) or the control was
        /// disabled mid-aim.</summary>
        public static event Action TeleportAimEnded;

        public static void EmitDamage(Vector3 worldPos, float amount, bool crit = false)
            => DamageDealt?.Invoke(worldPos, amount, crit);

        public static void EmitPickup(Vector3 worldPos, string label, Color color)
            => Pickup?.Invoke(worldPos, label, color);

        public static void EmitSupercellCollected(Vector3 worldPos, int cellsBefore, int cellsAfter)
            => SupercellCollected?.Invoke(worldPos, cellsBefore, cellsAfter);

        public static void EmitEnemyKilled(Vector3 worldPos)
            => EnemyKilled?.Invoke(worldPos);

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

        public static void EmitBlinkerTeleported(Vector3 from, Vector3 to)
            => BlinkerTeleported?.Invoke(from, to);

        public static void EmitMaxTeleported(Vector3 from, Vector3 to)
            => MaxTeleported?.Invoke(from, to);

        public static void EmitMissileImpact(Vector3 worldPos, float damage)
            => MissileImpact?.Invoke(worldPos, damage);

        public static void EmitMissileSputtering(Vector3 worldPos)
            => MissileSputtering?.Invoke(worldPos);

        public static void EmitMissileBounced(Vector3 worldPos)
            => MissileBounced?.Invoke(worldPos);

        public static void EmitTeleportAimStarted(float maxRangeMetres)
            => TeleportAimStarted?.Invoke(maxRangeMetres);

        public static void EmitTeleportAimEnded()
            => TeleportAimEnded?.Invoke();
    }
}
