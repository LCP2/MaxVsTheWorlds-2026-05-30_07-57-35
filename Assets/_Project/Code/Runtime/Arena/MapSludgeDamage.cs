using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-838: map-authored sludge (any <c>sludge[]</c> rect a world author places — the "S" cells) now
    /// hurts Max the same way <see cref="StormdrainFlood"/>'s parked flood was going to, without ever
    /// reading <see cref="StormdrainFlood.FloodEnabled"/> — sludge damage must keep working with the
    /// flood switched off (MV-836). Deliberately its own small static class + ticker, not a re-use of
    /// <see cref="FloodDamageTicker"/> itself, so this mechanic's own rate can never accidentally
    /// retune (or get parked) by a future change to the flood's.
    /// </summary>
    public static class MapSludgeDamage
    {
        /// <summary>Lee's own authored rate — 6 (2026-09-17 decision) retuned to 7.5 by MV-924
        /// (+25%, "standing in sludge should feel urgent to leave").</summary>
        public const float DamagePerSecond = 7.5f;

        /// <summary>Same fixed-cadence idiom as every other damage-over-time hazard in this project
        /// (<see cref="SludgePuddle"/>, <see cref="StormdrainFlood"/>'s own ticker): frame-rate
        /// independent and testable for any dt.</summary>
        public const float DamageTickInterval = 0.25f;

        /// <summary>True only inside a map-AUTHORED sludge rect, at floor level. Deliberately reuses
        /// <see cref="MapGeometry.SpeedMultiplierAt"/> rather than re-walking <c>map.entities</c> a
        /// second time: that method already loops <c>EntityKind.Sludge</c> only (never
        /// <see cref="MaxWorlds.Enemies.SludgePuddle"/>, which is a separate, runtime-spawned hazard
        /// out of this ticket's scope) and already applies MV-837's own floor-vs-deck Y gate
        /// (<c>map.deckHeight - 0.5</c>) — the exact two rules this ticket needs, so a mover reading
        /// less than full speed there is standing in floor-level map sludge and nothing else.</summary>
        public static bool IsInFloorSludge(MapData map, Vector3 worldPosition) =>
            MapGeometry.SpeedMultiplierAt(map, worldPosition.x, worldPosition.y, worldPosition.z) < 1f;
    }

    /// <summary>Per-receiver damage-over-time state for standing in floor-level map sludge (MV-838) —
    /// a struct, not shared global state, same reasoning as <see cref="FloodDamageTicker"/>: whichever
    /// single receiver this is wired to (Max only — MV-795's lesson against a per-tick DoT ever
    /// touching robots) carries its own independent 0.25 s clock.</summary>
    public struct MapSludgeDamageTicker
    {
        private float _accumulator;

        /// <summary>One evaluation. A no-op — and the clock resets — the instant
        /// <paramref name="isInSludge"/> reads false, so leaving the sludge (or the Force Field going
        /// up, which the caller reports as "not in sludge" for this purpose — MV-838's own "skipped
        /// entirely") never banks a partial tick against the next visit.</summary>
        public void Tick(float dt, bool isInSludge, IDamageable receiver, Vector3 position)
        {
            if (!isInSludge) { _accumulator = 0f; return; }
            if (dt <= 0f || receiver == null || !receiver.IsAlive) return;

            _accumulator += dt;
            while (_accumulator >= MapSludgeDamage.DamageTickInterval)
            {
                _accumulator -= MapSludgeDamage.DamageTickInterval;
                float amount = MapSludgeDamage.DamagePerSecond * MapSludgeDamage.DamageTickInterval;
                receiver.TakeDamage(new DamageInfo(amount, position, Vector3.up, Team.Neutral, source: DamageSource.Environment));
            }
        }
    }
}
