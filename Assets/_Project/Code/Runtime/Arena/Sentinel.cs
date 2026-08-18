using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Max's deployable Sentinel (MV-362, restructured MV-422: "Delete the Wall Sentinel entirely...
    /// one sentinel only — the Gunner, now just 'Sentinel'"). A low-HP turret — "a hose pipe on a
    /// stick", reusing the primary's own blue — that auto-fires at the nearest robot in range and,
    /// once its Move axis is leveled, follows Max at a standoff distance. Placed at an aimed point,
    /// with its own HP.
    ///
    /// Six RIG axes (<c>u_sen</c>'s children, MV-422) replace the old three tracks: Damage
    /// (<c>u_dmg</c>), Range (<c>u_rng</c>), Health (<c>u_hp</c>) — all direct children of
    /// <c>u_sen</c> — then Move (<c>u_mov</c>), Cost (<c>u_cst</c>), Slots (<c>u_slt</c>) behind
    /// Damage/Range/Health respectively. "Always weaker than Max's CURRENT primary" is enforced
    /// structurally, not by a cap that could drift out of date: every shot's damage is a FRACTION
    /// (&lt; 1.0, see <see cref="AbilityTuning.SentinelDamagePerShot"/>) of Max's own live RCDA
    /// Damage-track output, read fresh from <see cref="WeaponSystemState"/>/<see cref="WeaponCatalog"/>
    /// on every shot.
    ///
    /// Deployed sentinels are permanent until destroyed — no recall, no player-triggered repair
    /// (DECISION, Lee 15 Aug 2026) — so unlike <see cref="MaxWorlds.Enemies.RobotEnemy"/> it is never
    /// pooled; <see cref="Die"/> destroys the GameObject outright, the same one-shot lifecycle
    /// <see cref="MaxWorlds.Factories.MowerHutch"/> uses for its own death. MV-398 (same day)
    /// reversed only the "no repair" half: a damaged-but-alive sentinel now passively regens HP once
    /// left unhit for a while — see <see cref="Update"/> — but a destroyed one still never comes
    /// back, and there is still no manual repair action.
    ///
    /// <see cref="Team"/> is <see cref="Team.Player"/> — Max's own device. <see cref="DamageRules"/>'s
    /// same-team rejection means a robot (Team.Enemy) CAN hit it, and Max's own primary (Team.Player)
    /// CANNOT — <c>WaterBlaster.FireTick</c> already skips every <c>Team.Player</c> receiver, so
    /// nothing extra is needed to stop Max from shooting his own sentinel.
    /// </summary>
    public sealed class Sentinel : MonoBehaviour, IDamageable, IHealthReadout
    {
        private static readonly List<Sentinel> _active = new List<Sentinel>(8);

        /// <summary>Every sentinel deployed right now — what <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s
        /// retargeting reads to find the nearest one, and what
        /// <see cref="MaxWorlds.Weapons.PlayerAbilities"/> counts against the Slots (u_slt) cap.</summary>
        public static IReadOnlyList<Sentinel> Active => _active;

        /// <summary>Empties the registry ONLY — mirrors <see cref="MaxWorlds.Enemies.RobotEnemy.ResetRegistry"/>'s
        /// list-only contract for test isolation. Does not destroy any GameObject; see
        /// <see cref="DestroyAllActive"/> for the real teardown a fresh level or an area crossing needs.</summary>
        public static void ResetRegistry() => _active.Clear();

        /// <summary>Destroys every deployed sentinel and empties the registry. Sentinels aren't
        /// pooled (unlike robots), so a full reset has to tear the GameObjects down too, not just
        /// forget them. Two call sites: <see cref="MaxWorlds.Arena.Map.MapRuntime"/> on a fresh level
        /// build, and <see cref="MaxWorlds.Enemies.AreaAccumulationDirector.PlayerCrossedIntoArea"/>
        /// (MV-362 spec: "they do not travel between areas... passing a gate clears them and refunds
        /// the slots" — MV-396 fixed "passing" to mean Max has actually walked through, not merely that
        /// the gate broke) — the "refund" is automatic here, since the Slots cap is always checked
        /// live against <see cref="Active"/>.Count, never a separately-tracked balance.</summary>
        public static void DestroyAllActive()
        {
            if (_active.Count == 0) return;
            var snapshot = new List<Sentinel>(_active);
            _active.Clear();
            foreach (Sentinel s in snapshot)
            {
                if (s == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(s.gameObject);
                else UnityEngine.Object.DestroyImmediate(s.gameObject);
            }
        }

        private static readonly Color BodyColor = new Color(0.35f, 0.55f, 0.75f); // the primary's blue
        private static readonly Collider[] s_hits = new Collider[16];

        // MV-395: the shot itself was invisible — damage landed but nothing was ever drawn from the
        // turret to its target. A LineRenderer flash tracer, built the same way RobotRig builds the
        // enemy Gunner's beam (see RobotRig.BuildBeamLine), tinted with the primary's own blue
        // (BodyColor above) per MV-362's "reuses the primary weapon's visual language".
        private static readonly Color BeamColor = new Color(0.55f, 0.85f, 1f, 1f);
        private const float BeamHalfWidth = 0.06f;
        private const float MuzzleHeight = 1.1f; // near the top of the Body cylinder (see BuildBody)

        /// <summary>How long the tracer stays on screen per shot. Capped below the fire interval so a
        /// fast-firing turret's beam never runs into the next shot's own flash.</summary>
        private float BeamVisibleSeconds => Mathf.Min(0.12f, _fireInterval * 0.9f);

        private LineRenderer _beamLine;
        private float _beamTimer;

        public string ReadoutName => "SENTINEL";

        private float _range;
        private float _fireInterval;
        private float _fireCooldown;
        private float _moveSpeed;
        private float _standoffDistance;
        private Transform _followTarget;

        private DestructibleHealth _health;
        private float _timeSinceDamage;

        public bool IsAlive => _health != null && _health.IsAlive;

        public Team Team => Team.Player;

        public float Normalized => _health?.Normalized ?? 0f;
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;

        /// <summary>Fired once, the instant this sentinel is destroyed.</summary>
        public event Action<Sentinel> Died;

        /// <summary>Places and builds the turret. <paramref name="range"/>/<paramref name="moveSpeed"/>
        /// are the Range (u_rng)/Move (u_mov) axes' CURRENT values at deploy time — read fresh from
        /// <see cref="WeaponSystemState"/>-adjacent <c>RigState</c> lookups by the caller, not cached
        /// here beyond this one deploy (matching the old Gunner's own "read fresh each shot" rule for
        /// damage). <paramref name="followTarget"/>/<paramref name="standoffDistance"/> drive the
        /// Move axis's follow behaviour (MV-422) — null/0 speed leaves it stationary, the pre-MV-422
        /// behaviour.</summary>
        public void Init(Vector3 position, float maxHp, float range, float fireInterval,
            float moveSpeed, float standoffDistance, Transform followTarget)
        {
            transform.position = position;
            _range = range;
            _fireInterval = fireInterval;
            _moveSpeed = moveSpeed;
            _standoffDistance = standoffDistance;
            _followTarget = followTarget;
            InitHealth(maxHp);
            BuildBody();
            WorldHealthBar.Attach(gameObject, this, 1.9f, 1.2f, alwaysShow: true);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)
        }

        private void BuildBody()
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vis.name = "Body";
            vis.transform.SetParent(transform, worldPositionStays: false);
            vis.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            vis.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);

            var rend = vis.GetComponent<Renderer>();
            if (rend != null)
            {
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", BodyColor);
                rend.SetPropertyBlock(mpb);
            }

            var col = vis.GetComponent<Collider>();
            if (col != null) col.isTrigger = false; // solid — robots route around it like any wall
        }

        protected void InitHealth(float maxHp)
        {
            _health = new DestructibleHealth(maxHp);
            _health.Destroyed += Die;

            // Registered here, not left to OnEnable alone: a sentinel is only "deployed" once Init
            // has actually run, and this guarantees the registry sees it the instant deployment
            // completes regardless of Unity's own OnEnable timing for a freshly-scripted GameObject
            // (belt-and-braces — OnEnable below still covers the ordinary enable/disable case).
            if (!_active.Contains(this)) _active.Add(this);
        }

        private void OnEnable()
        {
            if (!_active.Contains(this)) _active.Add(this);
        }

        private void OnDisable() => _active.Remove(this);

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            if (info.Amount > 0f) _timeSinceDamage = 0f; // MV-398: (re)starts the regen delay below
            _health.TakeDamage(info.Amount);
        }

        /// <summary>HP after <paramref name="dt"/> seconds of passive regen (MV-398) — same
        /// delay-gated linear trickle as <see cref="PlayerHealth.Regenerate"/> (never revives a
        /// destroyed sentinel, never overfills past <paramref name="max"/>), reused rather than
        /// reinvented since the ticket's tuning is deliberately aliased to Max's own. Pure, so the
        /// trickle is unit-testable without a live sentinel.</summary>
        public static float Regenerate(float current, float max, float timeSinceDamage, float delay, float perSec, float dt) =>
            PlayerHealth.Regenerate(current, max, timeSinceDamage, delay, perSec, dt);

        private void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            _timeSinceDamage += dt;

            float next = Regenerate(_health.Current, _health.Max, _timeSinceDamage,
                AbilityTuning.DefaultSentinelRegenDelaySeconds, AbilityTuning.DefaultSentinelRegenPerSec, dt);
            float healAmount = next - _health.Current;
            if (healAmount > 0f) _health.Heal(healAmount);

            if (_followTarget != null && _moveSpeed > 0f)
            {
                Vector3 next3 = AbilityTuning.SentinelStandoffStep(
                    transform.position, _followTarget.position, _standoffDistance, _moveSpeed, dt);
                if (next3 != transform.position)
                {
                    transform.position = next3;
                    Physics.SyncTransforms();
                }
            }

            if (_beamTimer > 0f)
            {
                _beamTimer -= dt;
                if (_beamTimer <= 0f && _beamLine != null) _beamLine.enabled = false;
            }

            _fireCooldown -= dt;
            if (_fireCooldown > 0f) return;

            RobotEnemy target = NearestRobotInRange();
            if (target == null) return;

            _fireCooldown = _fireInterval;

            // Max's CURRENT primary per-tick damage, read live — this is what keeps the sentinel
            // "always weaker" as Max's own Damage track climbs (see the class doc comment).
            float primaryDamage = WeaponCatalog.EffectiveDamagePerTick(
                WaterBlaster.DefaultDamagePerTick,
                WeaponSystemState.TrackLevel(WeaponTrackKind.Damage),
                WeaponCatalog.DefaultRcdaDamagePerLevel);

            int damageLevel = RigState.Level("u_dmg");
            float damage = AbilityTuning.SentinelDamagePerShot(
                primaryDamage, damageLevel,
                AbilityTuning.DefaultSentinelDamageFraction,
                AbilityTuning.DefaultSentinelDamageFractionPerLevel);

            Vector3 dir = target.transform.position - transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // the turret tracks its target

            if (damage > 0f)
            {
                target.TakeDamage(new DamageInfo(damage, target.transform.position, dir, Team,
                    source: DamageSource.Ability));
                FireBeam(target.transform.position);
            }
        }

        /// <summary>Flashes the tracer from the turret's muzzle to the point it just hit. Cosmetic
        /// only — the damage above has already landed regardless of whether this draws.</summary>
        private void FireBeam(Vector3 targetPosition)
        {
            if (_beamLine == null) _beamLine = BuildBeamLine();

            Vector3 muzzle = transform.position + Vector3.up * MuzzleHeight;
            Vector3 end = new Vector3(targetPosition.x, muzzle.y, targetPosition.z);
            _beamLine.SetPosition(0, muzzle);
            _beamLine.SetPosition(1, end);
            _beamLine.enabled = true;
            _beamTimer = BeamVisibleSeconds;
        }

        /// <summary>Built once and reused for this turret's whole life (a Sentinel is never pooled —
        /// see the class doc). World-space positions, same idiom as
        /// <see cref="MaxWorlds.VFX.RobotRig.BuildBeamLine"/>.</summary>
        private LineRenderer BuildBeamLine()
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(transform, worldPositionStays: false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 0;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.material = VfxMaterials.Additive(VfxMaterials.Glow());
            lr.widthMultiplier = BeamHalfWidth * 2f;
            lr.startColor = BeamColor;
            lr.endColor = BeamColor;
            lr.enabled = false;
            return lr;
        }

        private RobotEnemy NearestRobotInRange()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _range, s_hits, ~0, QueryTriggerInteraction.Ignore);

            RobotEnemy best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hits[i].TryGetComponent<RobotEnemy>(out var robot) || !robot.IsAlive) continue;
                float d = (robot.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = robot; }
            }
            return best;
        }

        private void Die()
        {
            // Remove from the registry BEFORE Destroy/DestroyImmediate, not after — OnDisable below
            // would eventually do this too, but Destroy() defers OnDisable to the end of the current
            // frame in play mode (MV-397: a dead sentinel still counted against the Slots cap for
            // the rest of that frame, so an immediate redeploy attempt read the slot as still full).
            // Removing here makes the slot free the instant the sentinel dies, matching "read live
            // off Active, never a separately-tracked balance" above.
            _active.Remove(this);
            Died?.Invoke(this);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
