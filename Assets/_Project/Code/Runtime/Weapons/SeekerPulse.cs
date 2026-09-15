using System;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Max's own seeking projectile (MV-708) — the LPPE's discrete pulse, steered by the same
    /// <see cref="HomingSteering"/> the Launcher's <see cref="HomingMissile"/> already uses. Unlike that
    /// missile it carries no splash and never sputters/bounces on timeout: a pulse that never finds (or
    /// loses) a target just flies straight and quietly expires at its lifetime.
    ///
    /// Target acquisition happens once, at <see cref="Fire"/> time, against whatever <see cref="RobotEnemy"/>
    /// is nearest, awake and inside the lock cone — never re-evaluated mid-flight, so a locked pulse
    /// commits to the shot the player actually saw fire.
    /// </summary>
    public sealed class SeekerPulse : MonoBehaviour
    {
        private const float ContactRadius = 0.5f;

        /// <summary>The bolt's own resolved shape (MV-770) — was a 0.35m/0.08m-wide bolt, 3.8px across
        /// at the play camera and thinner than the nameplate text above the robot it hits. Read once;
        /// every renderer <see cref="BuildVisual"/> builds sizes off this same struct.</summary>
        private static readonly CombatVfxTuning.LppeBoltTuning BoltTuning = CombatVfxTuning.LppeBolt();

        /// <summary>MV-805: fire orange, not the cold cyan-white it shipped with -- the LPPE read as
        /// water (Lee, 2026-09-15) because this colour was identical to <c>WaterBlaster</c>'s own.</summary>
        private static readonly Color BoltColor = new Color(1.00f, 0.52f, 0.12f);

        private RobotEnemy _target;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _turnRateDegPerSec;
        private float _damage;
        private float _lifetime;
        private float _age;
        private Action<RobotEnemy, float> _onHit;
        private Action<RobotEnemy, Vector3> _onKill;
        private bool _canFork;
        private bool _spent;
        private GroundRing _groundGlow;

        /// <summary>Alpha the ground glow renders at — dim enough it reads as a light spilling onto
        /// the floor under the bolt, not a second bolt lying flat (MV-770).</summary>
        private static readonly Color GroundGlowColor = new Color(BoltColor.r, BoltColor.g, BoltColor.b, 0.5f);

        // Reused every tick so the per-frame obstruction check (below) allocates nothing, the same
        // idiom WaterBlaster.FireTick's static s_buffer/s_hits use.
        private static readonly RaycastHit[] s_worldHits = new RaycastHit[8];

        /// <summary>The robot this pulse locked onto at fire time, or null if none qualified — the
        /// resolved value MV-708 AC1 asserts against.</summary>
        public RobotEnemy Target => _target;

        /// <summary>True once this pulse has hit its target, been blocked, or expired — it takes no
        /// further action after this, so a test can keep ticking it without double-applying damage.</summary>
        public bool IsSpent => _spent;

        /// <summary>
        /// Fire one pulse from <paramref name="origin"/> along <paramref name="aimDir"/>. Locks at fire
        /// time onto the nearest awake <see cref="RobotEnemy"/> within <paramref name="lockRange"/> and
        /// <paramref name="lockHalfAngleDeg"/> of the aim direction (unless <paramref name="forcedTarget"/>
        /// is given — MV-768 FORK's own release at a specific robot, no cone check); flies straight and
        /// dies at its lifetime if none qualifies. <paramref name="onHit"/> (optional) lets the weapon
        /// track its own Shock combo per target without this projectile knowing anything about that
        /// mechanic. <paramref name="onKill"/> (optional, MV-768 FORK) fires once, in addition to
        /// <paramref name="onHit"/>, the instant a hit this pulse lands actually kills its target — but
        /// only while <paramref name="canFork"/> is true; a pulse fired with it false (a fork's own
        /// release) can still land a kill, it just never reports one, so FORK can never chain off its
        /// own forked pulse.
        /// </summary>
        public static SeekerPulse Fire(Vector3 origin, Vector3 aimDir, float speed, float turnRateDegPerSec,
            float lifetime, float damage, float lockRange, float lockHalfAngleDeg,
            Action<RobotEnemy, float> onHit = null, RobotEnemy forcedTarget = null, bool canFork = true,
            Action<RobotEnemy, Vector3> onKill = null)
        {
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 1e-4f) aimDir = Vector3.forward;
            aimDir.Normalize();

            var go = new GameObject("SeekerPulse (stand-in)");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            BuildVisual(go.transform);

            RobotEnemy target = forcedTarget != null
                ? forcedTarget
                : AcquireTarget(origin, aimDir, lockRange, lockHalfAngleDeg);
            LockBracketVfx.Show(target);   // MV-702: the reticle bracket MV-708 deferred as this ticket's own

            var pulse = go.AddComponent<SeekerPulse>();
            pulse.Init(target, speed, turnRateDegPerSec, lifetime, damage, onHit, onKill, canFork);
            pulse.BuildGroundGlow(origin);
            return pulse;
        }

        /// <summary>MV-770: a ground-hugging additive disc that tracks the bolt's XZ position — the
        /// weapon bolt has to be its own light source, and a light source spills onto the floor it
        /// passes over. Its own top-level object (not a child of <c>transform</c>) so it can stay flat
        /// on the ground while the bolt's own transform pitches/yaws under steering.</summary>
        private void BuildGroundGlow(Vector3 origin)
        {
            _groundGlow = GroundRing.Create("SeekerPulseGroundGlow", additive: true);
            _groundGlow.Show(new Vector3(origin.x, 0f, origin.z), BoltTuning.GroundGlowDiameter * 0.5f,
                GroundGlowColor);
        }

        /// <summary>Nearest awake, alive robot within range and the lock cone — "awake" excludes a
        /// still-<see cref="RobotEnemy.IsDormant"/> robot by rule (spec: "dormant robots are invisible
        /// targets"). Reads the field-wide registry, so any level/deck qualifies, not just this room.</summary>
        private static RobotEnemy AcquireTarget(Vector3 origin, Vector3 aimDir, float lockRange,
            float lockHalfAngleDeg)
        {
            var active = RobotEnemy.Active;
            RobotEnemy best = null;
            float bestDistSq = float.MaxValue;
            float rangeSq = lockRange * lockRange;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || !candidate.IsAlive || candidate.IsDormant) continue;

                Vector3 to = candidate.transform.position - origin;
                to.y = 0f;
                float distSq = to.sqrMagnitude;
                if (distSq > rangeSq || distSq < 1e-6f) continue;

                float angle = Vector3.Angle(aimDir, to.normalized);
                if (angle > lockHalfAngleDeg) continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = candidate;
                }
            }

            return best;
        }

        private void Init(RobotEnemy target, float speed, float turnRateDegPerSec, float lifetime,
            float damage, Action<RobotEnemy, float> onHit, Action<RobotEnemy, Vector3> onKill, bool canFork)
        {
            _target = target;
            _targetDamageable = target;
            _speed = speed;
            _turnRateDegPerSec = turnRateDegPerSec;
            _lifetime = lifetime;
            _damage = damage;
            _onHit = onHit;
            _onKill = onKill;
            _canFork = canFork;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advance one step. Public — rather than the ordinary private-Update-only shape — so
        /// an EditMode test can step the flight deterministically without a live Unity frame loop, the
        /// same reason <see cref="HomingMissile.BlockedByGeometry"/> is its own callable static query.</summary>
        public void Tick(float dt)
        {
            if (_spent) return;
            _age += dt;

            bool targetLive = _target != null && _target.IsAlive;
            if (targetLive)
            {
                transform.rotation = HomingSteering.TurnToward(transform.rotation, transform.position,
                    _target.transform.position, _turnRateDegPerSec, dt);
            }

            Vector3 from = transform.position;
            Vector3 next = from + transform.forward * (_speed * dt);

            // MV-749: a pulse must damage ANY IDamageable its flight path reaches, not only the
            // RobotEnemy it locked onto at fire time -- gates, Replicators and bosses all carry their
            // own collider + IDamageable already; this is what was missing. Checked before the
            // Cover-layer obstruction below (and ahead of a locked robot's own arrival check further
            // down) so a gate standing in the way is what the pulse meets first, exactly like a player
            // aiming squarely at it with no robot in the lock cone at all.
            if (TryHitWorldDamageable(from, next, out IDamageable worldTarget, out Vector3 worldPoint))
            {
                transform.position = worldPoint;
                worldTarget.TakeDamage(new DamageInfo(_damage, worldPoint, transform.forward, Team.Player,
                    source: DamageSource.PrimaryWeapon));
                // No onHit callback here (Shock is a robot stun -- rule 4: meaningless on a gate or a
                // Replicator, so it's simply never invoked for one) and no error either way.
                Retire();
                return;
            }

            if (HomingSteering.BlockedByGeometry(from, next, out RaycastHit hit))
            {
                transform.position = hit.point;
                Retire();
                return;
            }

            transform.position = next;
            UpdateGroundGlow();

            if (targetLive)
            {
                Vector3 toTarget = _target.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude <= ContactRadius * ContactRadius)
                {
                    ApplyHit();
                    return;
                }
            }

            if (_age >= _lifetime) Retire();
        }

        private void UpdateGroundGlow()
        {
            if (_groundGlow == null) return;
            Vector3 pos = transform.position;
            _groundGlow.Show(new Vector3(pos.x, 0f, pos.z), BoltTuning.GroundGlowDiameter * 0.5f, GroundGlowColor);
        }

        /// <summary>
        /// Any live, non-Player, non-<see cref="RobotEnemy"/> <see cref="IDamageable"/> whose collider
        /// the segment <paramref name="from"/>-&gt;<paramref name="to"/> actually crosses this tick
        /// (MV-749). Deliberately a multi-hit query (RaycastNonAlloc over the segment), not a single
        /// nearest-hit one: a closed <c>AreaGate</c>'s own leaf collider -- the one its
        /// <see cref="IDamageable"/> lives on -- sits exactly co-located with its Cover-layer
        /// <c>ThresholdObject</c> (MV-386's split), so a single-hit query could return either one
        /// non-deterministically and silently miss the gate. Robots are excluded on purpose -- MV-708's
        /// lock-on/arrival path (<see cref="ApplyHit"/>) is untouched by this ticket and must stay the
        /// only way a pulse ever damages a <see cref="RobotEnemy"/>.
        /// </summary>
        private static bool TryHitWorldDamageable(Vector3 from, Vector3 to, out IDamageable hit, out Vector3 point)
        {
            hit = null;
            point = to;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-4f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / dist, s_worldHits, dist, ~0,
                QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit rh = s_worldHits[i];
                if (rh.distance >= bestDist) continue;
                if (!rh.collider.TryGetComponent(out IDamageable d)) continue;
                if (d is RobotEnemy || !d.IsAlive || d.Team == Team.Player) continue;

                bestDist = rh.distance;
                hit = d;
                point = rh.point;
            }
            return hit != null;
        }

        private void ApplyHit()
        {
            if (_targetDamageable != null && _targetDamageable.IsAlive)
            {
                RobotEnemy killedTarget = _target;
                Vector3 point = transform.position;
                _targetDamageable.TakeDamage(new DamageInfo(_damage, point, transform.forward,
                    Team.Player, source: DamageSource.PrimaryWeapon));
                _onHit?.Invoke(killedTarget, _damage);

                // MV-768 FORK: TakeDamage above is synchronous, so a kill is already reflected in
                // IsAlive by the time we check it here -- see RobotEnemy.TakeDamage/Die. _canFork is
                // false for a pulse FORK itself released (see Fire's own doc), so a forked pulse's own
                // kill never reports one -- the "must not chain" rule.
                if (_canFork && !killedTarget.IsAlive) _onKill?.Invoke(killedTarget, point);
            }
            Retire();
        }

        private void Retire()
        {
            if (_spent) return;
            _spent = true;
            // Same Application.isPlaying guard as HomingMissile.Strip() — Destroy is illegal outside
            // Play mode, which an EditMode test driving Tick() directly hits every time.
            if (_groundGlow != null)
            {
                GameObject glowGo = _groundGlow.gameObject;
                if (Application.isPlaying) Destroy(glowGo); else DestroyImmediate(glowGo);
                _groundGlow = null;
            }
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>A bolt with weight and a short trail (MV-770: rescaled from a 0.35m/0.08m sliver
        /// to <see cref="BoltTuning"/>'s own numbers, and switched from a LIT surface material to an
        /// unlit additive one — a weapon bolt in a world this dark has to be its own light source, not
        /// a dimly-shaded sliver of metal). Same build idiom as <see cref="HomingMissile.BuildVisual"/>.
        /// </summary>
        private static void BuildVisual(Transform parent)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Material boltMat = VfxMaterials.AdditiveTinted(BoltColor);

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            // MV-770: a taut, fast taper (0.12s) rather than MV-758's 0.16s — the bolt itself is now
            // wide enough to read on its own, so the trail's job is a short streak behind it, not
            // carrying the bolt's own visibility.
            trail.time = BoltTuning.TrailLifetime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = BoltTuning.TrailWidth;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = boltMat;
            trail.Clear();

            var bolt = new GameObject("Bolt");
            bolt.transform.SetParent(parent, false);
            // Same local rotation the old capsule used: the lathe's revolve axis (Y) is the mesh's own
            // long axis, so this still points the bolt down the travel axis.
            bolt.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bolt.AddComponent<MeshFilter>().sharedMesh = GetBoltMesh();
            var meshRenderer = bolt.AddComponent<MeshRenderer>();
            if (boltMat != null) meshRenderer.sharedMaterial = boltMat;
        }

        // MV-805: sampled resolution for the lathed bolt -- see BuildBoltMesh's own doc comment.
        private const int BoltProfilePoints = 24;
        private const int BoltRadialSegments = 16;
        private const float BoltPeakFractionFromNose = 0.45f;

        /// <summary>MV-810: the one instance every bolt shares -- see <see cref="GetBoltMesh"/>.</summary>
        private static Mesh s_boltMesh;

        /// <summary>MV-810: the single cached bolt mesh, built lazily on first use and handed out by
        /// reference to every LPPE pulse AND (MV-806's own gap) the Sentinel's matching bolt
        /// (<see cref="MaxWorlds.Arena.SentinelBolt"/>) -- the profile depends only on compile-time
        /// constants and <see cref="BoltTuning"/>, so one mesh is correct for all of them. Before this,
        /// every single shot lathed a fresh ~384-vertex mesh (<see cref="BuildBoltMesh"/>) and never
        /// freed it.</summary>
        public static Mesh GetBoltMesh()
        {
            if (s_boltMesh == null) s_boltMesh = BuildBoltMesh();
            return s_boltMesh;
        }

        /// <summary>Drop this class's own reference to the cached bolt mesh. The <see cref="Mesh"/>
        /// object itself is owned by <see cref="CharacterMeshes"/>'s own cache (see
        /// <see cref="CharacterMeshes.Lathe"/>) and only ever destroyed by
        /// <see cref="CharacterMeshes.ClearCache"/> -- this just stops a domain reload or a fresh test
        /// run holding a pointer to whatever CharacterMeshes may since have cleared, same idiom as
        /// <see cref="MaxWorlds.Weapons.RigFusionState.ResetForTests"/>.</summary>
        public static void ResetForTests() => s_boltMesh = null;

        /// <summary>MV-805: an ogive of revolution (<see cref="CharacterMeshes.Lathe"/>), replacing the
        /// capsule primitive Lee reported as having "a bend" — a Unity capsule at this aspect
        /// (<see cref="BoltTuning"/>'s cross-section vs. length) is two hemispheres meeting in a hard
        /// crease across the middle. This profile is one continuous curve from nose to tail instead:
        /// radius rises from 0 at the nose to <see cref="BoltTuning"/>'s cross-section-derived peak at
        /// <see cref="BoltPeakFractionFromNose"/> of the length, then eases back to 0 at the tail. The
        /// nose sits at the profile's own high-Y end, which the 90-degree rotation in
        /// <see cref="BuildVisual"/> points down +Z — this object's own forward, i.e. the direction of
        /// travel — so the bolt's point genuinely leads.</summary>
        private static Mesh BuildBoltMesh()
        {
            float length = BoltTuning.Length;
            float peakRadius = BoltTuning.CrossSection * 0.5f;

            var profile = new Vector2[BoltProfilePoints];
            for (int i = 0; i < BoltProfilePoints; i++)
            {
                float tFromTail = (float)i / (BoltProfilePoints - 1);   // 0 = tail, 1 = nose
                float tFromNose = 1f - tFromTail;
                float radius = tFromNose <= BoltPeakFractionFromNose
                    ? Mathf.SmoothStep(0f, peakRadius, tFromNose / BoltPeakFractionFromNose)
                    : Mathf.SmoothStep(peakRadius, 0f,
                        (tFromNose - BoltPeakFractionFromNose) / (1f - BoltPeakFractionFromNose));
                profile[i] = new Vector2(radius, tFromTail * length);
            }
            return CharacterMeshes.Lathe(profile, BoltRadialSegments);
        }
    }
}
