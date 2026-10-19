using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 5's Orbitals secondary (MV-1236) — gold orbs that circle Max at a fixed height, always on
    /// once owned, with nothing to press (the ticket's own "Always on once owned; nothing to press").
    /// Self-attaches from <see cref="MaxWorlds.Player.PlayerController.Awake"/> the same way
    /// <see cref="ShoulderRack"/> does, and is a total no-op while
    /// <see cref="WeaponSystemState.SecondaryKind"/> doesn't read <see cref="SecondaryKind.Orbitals"/>.
    ///
    /// The look (trailing arcs, gold material, growth by POWER) is MV-1241 — this ticket is the
    /// gameplay orbit/hit math and THE RIG board only, so no mesh/VFX is built here; a later ticket
    /// reads <see cref="OrbPosition"/>/<see cref="OrbCount"/> to place its own visuals.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class Orbitals : MonoBehaviour
    {
        /// <summary>Rule: orbs circle at this height over Max's own root — "over cover".</summary>
        public const float OrbitHeight = 1.2f;

        /// <summary>Rule: the 2D (XZ) contact radius against a Team.Enemy IDamageable.</summary>
        public const float ContactRadius = 0.35f;

        /// <summary>Rule: the same orb cannot hit the same target again this soon.</summary>
        public const float PerTargetHitCooldownSeconds = 0.8f;

        /// <summary>Rule: at most 5 orbs, whatever ORBS's own level resolves to.</summary>
        public const int MaxOrbs = 5;

        private static readonly Collider[] s_hitColliders = new Collider[16];

        // One cooldown table per orbit SLOT (not per target globally) — "the SAME orb cannot hit the
        // same robot again for 0.8s" is a per-slot rule: a different orb may still hit that same robot
        // on its own separate pass.
        private readonly Dictionary<IDamageable, float>[] _slotCooldowns = BuildSlotCooldowns();

        private float _spinAngleDeg;

        private static Dictionary<IDamageable, float>[] BuildSlotCooldowns()
        {
            var slots = new Dictionary<IDamageable, float>[MaxOrbs];
            for (int i = 0; i < slots.Length; i++) slots[i] = new Dictionary<IDamageable, float>();
            return slots;
        }

        /// <summary>How many orbs are live right now — ORBS's (<c>s_orb</c>) current level, clamped to
        /// <see cref="MaxOrbs"/>.</summary>
        public int OrbCount => Mathf.Clamp(
            AbilityTuning.OrbitalsOrbCount(
                WeaponSystemState.OrbitalsTrackLevel(OrbitalsTrackKind.OrbCount),
                AbilityTuning.DefaultOrbitalsBaseOrbCount, AbilityTuning.DefaultOrbitalsOrbCountPerLevel),
            0, MaxOrbs);

        /// <summary>The orbit radius right now — REACH's (<c>s_rad</c>) current level.</summary>
        public float Reach => AbilityTuning.OrbitalsReachMetres(
            WeaponSystemState.OrbitalsTrackLevel(OrbitalsTrackKind.Reach),
            AbilityTuning.DefaultOrbitalsBaseReachMetres, AbilityTuning.DefaultOrbitalsReachPerLevel);

        /// <summary>Damage dealt per touch right now — POWER's (<c>s_odm</c>) current level.</summary>
        public float TouchDamage => AbilityTuning.OrbitalsTouchDamage(
            WeaponSystemState.OrbitalsTrackLevel(OrbitalsTrackKind.Power),
            AbilityTuning.DefaultOrbitalsBaseTouchDamage, AbilityTuning.DefaultOrbitalsTouchDamagePerLevel);

        /// <summary>Orbit speed right now, degrees/sec — SPIN's (<c>s_spd</c>) current level.</summary>
        public float SpinDegPerSec => AbilityTuning.OrbitalsSpinDegPerSec(
            WeaponSystemState.OrbitalsTrackLevel(OrbitalsTrackKind.Spin),
            AbilityTuning.DefaultOrbitalsBaseSpinDegPerSec, AbilityTuning.DefaultOrbitalsSpinPerLevel);

        /// <summary>Orb <paramref name="index"/>'s current world position, evenly spaced over
        /// <see cref="OrbCount"/> around Max at <see cref="Reach"/> and <see cref="OrbitHeight"/>.
        /// Public for MV-1241's own visuals to read, and for a test to assert without reflection.</summary>
        public Vector3 OrbPosition(int index)
        {
            int count = OrbCount;
            if (count <= 0) return transform.position + Vector3.up * OrbitHeight;

            float angleDeg = _spinAngleDeg + index * (360f / count);
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * Reach;
            return transform.position + Vector3.up * OrbitHeight + offset;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame orbit/hit step — public so a test can drive deterministic time
        /// without a live PlayerLoop, same seam every other weapon here carries.</summary>
        public void Tick(float dt)
        {
            if (WeaponSystemState.SecondaryKind != SecondaryKind.Orbitals) return;
            // MV-1248: SECONDARY stays locked until World 5's 2nd cradle destroyed banks and reveals it
            // (WeaponsScreen.OpenSecondaryFamilyReveal) — without this, the orbit/hit math ran from the
            // moment the world loaded regardless, since every s_orb/s_rad/s_odm/s_spd node here starts
            // owned at level 1 (OrbitalsTrackKind's own class doc) with no level-based gate of its own.
            if (!RigState.IsCategoryUnlocked("SECONDARY")) return;

            int count = OrbCount;
            if (count <= 0) return;

            _spinAngleDeg += SpinDegPerSec * dt;
            if (_spinAngleDeg >= 360f) _spinAngleDeg -= 360f;

            float damage = TouchDamage;
            MapData map = EnemyNavigation.Map;

            for (int slot = 0; slot < count; slot++)
            {
                Dictionary<IDamageable, float> cooldowns = _slotCooldowns[slot];
                TickCooldowns(cooldowns, dt);
                HitAtPosition(OrbPosition(slot), cooldowns, damage, map);
            }
        }

        private static readonly List<IDamageable> s_scratchKeys = new List<IDamageable>();

        /// <summary>Snapshots the keys first — writing back into <paramref name="cooldowns"/> while
        /// still enumerating it (even just updating an existing key's value) throws "Collection was
        /// modified" under Mono's Dictionary, unlike .NET Core's.</summary>
        private static void TickCooldowns(Dictionary<IDamageable, float> cooldowns, float dt)
        {
            if (cooldowns.Count == 0) return;

            s_scratchKeys.Clear();
            s_scratchKeys.AddRange(cooldowns.Keys);

            foreach (IDamageable target in s_scratchKeys)
            {
                float remaining = cooldowns[target] - dt;
                if (remaining <= 0f) cooldowns.Remove(target);
                else cooldowns[target] = remaining;
            }
        }

        /// <summary>Rule 2/7: a 2D (XZ) contact radius against a live, awake Team.Enemy IDamageable —
        /// an infinite vertical capsule at the orb's XZ position rather than a 3D sphere, same idiom
        /// <see cref="LightMissileBall"/> uses to hit ground-level geometry while flying above it.
        /// Cover-layer geometry is ignored (the orbit flies over cover, same as the ball). "Awake" only
        /// filters a <see cref="IDormant"/> receiver (a sleeping <see cref="MaxWorlds.Enemies.RobotEnemy"/>)
        /// — a machine or boss with no such concept is always eligible, matching the ticket's own "also
        /// hits machines and bosses". Never Max or a sentinel: both are Team.Player.</summary>
        private void HitAtPosition(Vector3 pos, Dictionary<IDamageable, float> cooldowns, float damage, MapData map)
        {
            Vector3 bottom = new Vector3(pos.x, -1000f, pos.z);
            Vector3 top = new Vector3(pos.x, 1000f, pos.z);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, ContactRadius, s_hitColliders, ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider c = s_hitColliders[i];
                if (CoverLayer.Exists && c.gameObject.layer == CoverLayer.Index) continue;
                if (!c.TryGetComponent(out IDamageable target)) continue;
                if (!target.IsAlive || target.Team != Team.Enemy) continue;
                if (target is IDormant dormant && dormant.IsDormant) continue;
                if (!CombatLevel.SameLevel(map, transform.position, c.transform.position)) continue;
                if (cooldowns.TryGetValue(target, out float remaining) && remaining > 0f) continue;

                target.TakeDamage(new DamageInfo(damage, pos, Vector3.down, Team.Player,
                    source: DamageSource.SecondaryWeapon));
                cooldowns[target] = PerTargetHitCooldownSeconds;
            }
        }
    }
}
