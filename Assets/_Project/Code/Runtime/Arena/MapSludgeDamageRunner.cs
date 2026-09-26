using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-838: ticks <see cref="MapSludgeDamageTicker"/> against Max alone (never a robot — MV-795's
    /// own lesson) once a frame. Self-installing, same pattern as <see cref="StormdrainFloodRunner"/>:
    /// no scene wiring, so it exists in every scene — including a bare test fixture — with zero setup,
    /// and deliberately does NOT gate on <see cref="StormdrainFlood.FloodEnabled"/>: sludge damage must
    /// keep working with the flood switched off (MV-836).
    /// </summary>
    [MaxWorlds.Core.PerfSection("sludge")]
    public sealed class MapSludgeDamageRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MapSludgeDamageRunner>() != null) return;
            new GameObject("MapSludgeDamageRunner").AddComponent<MapSludgeDamageRunner>();
        }

        private Transform _target;
        private IDamageable _targetDamageable;
        private PlayerAbilities _targetAbilities;
        private MapSludgeDamageTicker _ticker;

        private void Update() => TickSludgeDamage(Time.deltaTime);

        /// <summary>The runner's whole per-frame job, pulled out so a test can drive it directly at a
        /// cadence it controls — same "a caller decides the cadence, a test drives it directly" idiom
        /// <see cref="StormdrainFloodRunner.TickFlood"/> already uses.</summary>
        public void TickSludgeDamage(float dt)
        {
            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null)
                {
                    _target = p.transform;
                    _targetDamageable = p.GetComponent<IDamageable>();
                    _targetAbilities = p.GetComponent<PlayerAbilities>();
                }
            }
            if (_target == null) return;

            // MV-838 DECISION (Lee): while the Force Field bubble is up, sludge ticks are skipped
            // entirely — no HP loss, no PlayerAbilities.AbsorbForceFieldDamage call, no hit VFX. Reporting
            // "not in sludge" for this one evaluation is what gets all three for free: the ticker below
            // never calls TakeDamage at all while this reads true, and its own accumulator resets exactly
            // as it would on stepping out of the sludge — the same "no partial tick banked" guarantee.
            bool forceFieldActive = _targetAbilities != null && _targetAbilities.ForceFieldActive;
            bool inSludge = !forceFieldActive && MapSludgeDamage.IsInFloorSludge(EnemyNavigation.Map, _target.position);
            _ticker.Tick(dt, inSludge, _targetDamageable, _target.position);
        }
    }
}
