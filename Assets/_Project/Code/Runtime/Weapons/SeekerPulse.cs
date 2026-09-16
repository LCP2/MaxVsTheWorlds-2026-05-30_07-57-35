using System;
using System.Collections.Generic;
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

        /// <summary>MV-814: the same fire-orange family pushed past 1.0 headroom (same idiom
        /// <c>LppeVfx.MuzzleColor</c>/<c>WindupColor</c> already use) so a fork's own bolt+trail reads
        /// as a visibly brighter event than an ordinary pulse, not just one more identical bolt.</summary>
        private static readonly Color ForkBoltColor = new Color(1.55f, 0.75f, 0.16f);

        /// <summary>MV-814: how much wider than an ordinary pulse's a forked bolt's cross-section is
        /// (spec: "1.25x cross-section") -- scales only <see cref="Transform.localScale"/>'s X/Z, which
        /// the lathed mesh's authored Y-axis length ignores (scale is applied in the mesh's own local
        /// axes before <see cref="BuildVisual"/>'s 90-degree rotation swings Y to the travel axis).</summary>
        private const float ForkCrossSectionScale = 1.25f;

        /// <summary>MV-814: a pulse whose hit didn't kill but left its target under this fraction of
        /// max health also releases a FORK, widening the old kill-only trigger -- against World 2's
        /// health pools an outright kill was rare enough that FORK read as doing nothing (measured:
        /// see PulseLaser.RegisterKill's own doc comment).</summary>
        private const float NearDeathForkThreshold = 0.15f;

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

        /// <summary>MV-814: the resolved bolt renderer, for a test to compare cross-section bounds
        /// between an ordinary pulse and a forked one — same "public accessor for a test" idiom as
        /// <see cref="PulseLaser.LastForkedPulseForTests"/>.</summary>
        public MeshRenderer BoltRendererForTests => GetComponentInChildren<MeshRenderer>();

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
            Action<RobotEnemy, Vector3> onKill = null, bool isFork = false)
        {
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 1e-4f) aimDir = Vector3.forward;
            aimDir.Normalize();

            var go = new GameObject("SeekerPulse (stand-in)");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            BuildVisual(go.transform, isFork);

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
                // MV-814: widened past kill-only -- a hit that leaves the target under
                // NearDeathForkThreshold also releases a fork, measured to trigger far more often than
                // an outright kill against World 2's health pools (see PulseLaser.RegisterKill's doc).
                bool nearDeath = killedTarget.IsAlive && killedTarget.HealthNormalized < NearDeathForkThreshold;
                if (_canFork && (!killedTarget.IsAlive || nearDeath)) _onKill?.Invoke(killedTarget, point);
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
        /// MV-814: <paramref name="isFork"/> gives a FORK-released bolt its own tell — the same
        /// fire-orange family pushed brighter (<see cref="ForkBoltColor"/>) on both the bolt and its
        /// trail, plus a <see cref="ForkCrossSectionScale"/>-wider cross-section — so one extra bolt
        /// appearing out of a kill reads as deliberate, not as another identical pulse.</summary>
        private static void BuildVisual(Transform parent, bool isFork)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Color color = isFork ? ForkBoltColor : BoltColor;
            Material boltMat = VfxMaterials.AdditiveTinted(color);

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
            // MV-815: the crescent is built directly in this object's own local axes (chord across
            // local X, sagitta along local Z, cross-section in Y/outward) -- no rotation needed to
            // point it down the travel axis, unlike the old lathed ogive. FORK's own wider
            // cross-section (MV-814) is now baked into a second cached mesh (see GetBoltMesh) rather
            // than a non-uniform Transform scale: the crescent's spine occupies two local axes at once
            // (X and Z), so scaling only X/Z (the old trick) would widen the chord itself, not just
            // the cross-section.
            bolt.transform.localRotation = Quaternion.identity;
            bolt.AddComponent<MeshFilter>().sharedMesh = GetBoltMesh(isFork);
            var meshRenderer = bolt.AddComponent<MeshRenderer>();
            if (boltMat != null) meshRenderer.sharedMaterial = boltMat;
        }

        // MV-805: sampled resolution for the Sentinel's own straight lathed bolt -- see
        // BuildStraightBoltMesh's own doc comment.
        private const int BoltProfilePoints = 24;
        private const int BoltRadialSegments = 16;
        private const float BoltPeakFractionFromNose = 0.45f;

        // MV-815: sampled resolution for Max's own crescent -- see BuildCrescentBoltMesh's own doc
        // comment. Spec: "16 samples along the spine, 10 around the cross-section (~160 vertices)".
        private const int CrescentSpineSamples = 16;
        private const int CrescentCrossSectionSegments = 10;

        /// <summary>MV-815: the sagitta (the belly's own lead ahead of the tip-to-tip chord, along the
        /// travel axis) -- spec: "sagitta 0.20 m".</summary>
        private const float CrescentSagitta = 0.20f;

        /// <summary>MV-810/815: the one instance every ordinary bolt shares -- see <see cref="GetBoltMesh"/>.</summary>
        private static Mesh s_boltMesh;

        /// <summary>MV-814/815: the one instance every FORK-released bolt shares -- a separate cached
        /// mesh (wider cross-section baked in) rather than a runtime Transform scale, see
        /// <see cref="BuildVisual"/>'s own doc comment for why the scale trick stopped working once
        /// the bolt became a crescent.</summary>
        private static Mesh s_forkBoltMesh;

        /// <summary>MV-810: the single cached bolt mesh for the given fork-ness, built lazily on first
        /// use and shared by every LPPE pulse of that kind -- never rebuilt per shot. MV-815: this is
        /// now Max's own crescent; the Sentinel's matching straight bolt is
        /// <see cref="MaxWorlds.Arena.SentinelBolt.GetBoltMesh"/>'s own separate cache, not this one.</summary>
        public static Mesh GetBoltMesh(bool isFork = false)
        {
            if (!isFork)
            {
                if (s_boltMesh == null) s_boltMesh = BuildCrescentBoltMesh(BoltTuning.CrossSection * 0.5f);
                return s_boltMesh;
            }
            if (s_forkBoltMesh == null)
                s_forkBoltMesh = BuildCrescentBoltMesh(BoltTuning.CrossSection * 0.5f * ForkCrossSectionScale);
            return s_forkBoltMesh;
        }

        /// <summary>Drop this class's own reference to the cached bolt meshes -- same idiom as
        /// <see cref="MaxWorlds.Weapons.RigFusionState.ResetForTests"/>, so a domain reload or a fresh
        /// test run never holds a pointer to a mesh a previous run already destroyed.</summary>
        public static void ResetForTests()
        {
            s_boltMesh = null;
            s_forkBoltMesh = null;
        }

        /// <summary>A point on the crescent's own spine at <paramref name="s"/> (0 at one tip, 1 at
        /// the other): the chord (tip-to-tip) runs along local X, and the sagitta -- the belly's own
        /// lead ahead of that chord, along local Z, the direction of travel -- eases in and back out
        /// with the same <see cref="Mathf.SmoothStep"/> shape <see cref="BuildCrescentBoltMesh"/> uses
        /// for the cross-section radius, so the curve is flat (zero slope) at every sample rather than
        /// only at the two ends -- the spine's own resolved deviation profile stays smooth
        /// sample-to-sample everywhere, not only near the tips, which a plain circular arc would not
        /// (its slope is steepest right where 16 samples are sparsest, next to the tips).</summary>
        private static Vector3 CrescentSpinePoint(float s, float chord, float sagitta)
        {
            float x = Mathf.Lerp(-chord * 0.5f, chord * 0.5f, s);
            float z = s <= 0.5f
                ? Mathf.SmoothStep(0f, sagitta, s * 2f)
                : Mathf.SmoothStep(sagitta, 0f, (s - 0.5f) * 2f);
            return new Vector3(x, 0f, z);
        }

        /// <summary>Newton's method inverse of the 3t^2-2t^3 Hermite ease <see cref="Mathf.SmoothStep"/>
        /// itself uses: given the eased fraction <paramref name="y"/> in [0,1], finds t such that
        /// SmoothStep(0,1,t) == y. Only used to choose sample placement, see
        /// <see cref="CrescentSampleParameter"/>.</summary>
        private static float InverseSmoothStep01(float y)
        {
            if (y <= 0f) return 0f;
            if (y >= 1f) return 1f;
            float t = y;
            for (int i = 0; i < 20; i++)
            {
                float f = 3f * t * t - 2f * t * t * t - y;
                float fp = 6f * t - 6f * t * t;
                if (Mathf.Abs(fp) < 1e-6f) break;
                t = Mathf.Clamp01(t - f / fp);
            }
            return t;
        }

        /// <summary>Where to place the <paramref name="i"/>'th of <see cref="CrescentSpineSamples"/>
        /// spine samples along the spine's own [0,1] parameter -- NOT i/(N-1) (evenly spaced in the raw
        /// parameter), which crowds most of the curve's actual bend into the handful of samples nearest
        /// each quarter-point (where <see cref="Mathf.SmoothStep"/> is steepest) and leaves the rest
        /// nearly flat: with only 16 samples, that concentration alone pushes the worst adjacent step
        /// just past the ticket's own 20%-of-peak ceiling. Placing samples evenly spaced in the
        /// resulting DEVIATION instead (via <see cref="InverseSmoothStep01"/>) keeps the exact same
        /// smooth curve -- flat at both tips and flat at the midpoint, the shape that avoids a visible
        /// crease at the bolt's own thickest point -- while keeping every step well under that
        /// ceiling.</summary>
        private static float CrescentSampleParameter(int i)
        {
            float mid = (CrescentSpineSamples - 1) * 0.5f;
            float y = 1f - Mathf.Abs(i - mid) / mid;
            float t = InverseSmoothStep01(y);
            return i <= (CrescentSpineSamples - 1) / 2 ? t * 0.5f : 1f - t * 0.5f;
        }

        /// <summary>MV-815: a crescent swept along a bowed spine, replacing the ogive of revolution
        /// Lee rejected -- "the LPPE bolt is literally arched, bowed like a drawn bow, curved across
        /// its travel axis. Not a straight needle, not a flat blade." The spine
        /// (<see cref="CrescentSpinePoint"/>) lies flat in this object's own local XZ plane (the
        /// ground plane the angled play camera actually reads), tips trailing and the midpoint
        /// leading. The cross-section is a circle swept perpendicular to the spine's own tangent at
        /// each sample, radius 0 at both tips and peaking at <paramref name="peakRadius"/> at the
        /// midpoint, eased both directions so the silhouette is one continuous curve with no crease
        /// anywhere -- the crease that was wrong with the old capsule and must not come back. Built
        /// once per <paramref name="peakRadius"/> (see <see cref="GetBoltMesh"/>), never per shot.</summary>
        private static Mesh BuildCrescentBoltMesh(float peakRadius)
        {
            float chord = BoltTuning.Length;
            float sagitta = CrescentSagitta;
            const float tangentEps = 0.001f;

            var verts = new List<Vector3>(CrescentSpineSamples * (CrescentCrossSectionSegments + 1));
            var tris = new List<int>();

            for (int i = 0; i < CrescentSpineSamples; i++)
            {
                float s = CrescentSampleParameter(i);
                Vector3 spine = CrescentSpinePoint(s, chord, sagitta);
                Vector3 tangent = (CrescentSpinePoint(Mathf.Min(1f, s + tangentEps), chord, sagitta)
                                  - CrescentSpinePoint(Mathf.Max(0f, s - tangentEps), chord, sagitta)).normalized;
                Vector3 outward = new Vector3(-tangent.z, 0f, tangent.x);

                float radius = s <= 0.5f
                    ? Mathf.SmoothStep(0f, peakRadius, s * 2f)
                    : Mathf.SmoothStep(peakRadius, 0f, (s - 0.5f) * 2f);

                for (int j = 0; j <= CrescentCrossSectionSegments; j++)
                {
                    float phi = (float)j / CrescentCrossSectionSegments * Mathf.PI * 2f;
                    verts.Add(spine + radius * (Mathf.Cos(phi) * Vector3.up + Mathf.Sin(phi) * outward));
                }
            }

            int w = CrescentCrossSectionSegments + 1;
            for (int i = 0; i < CrescentSpineSamples - 1; i++)
                for (int j = 0; j < CrescentCrossSectionSegments; j++)
                {
                    int a = i * w + j, b = a + 1, c = a + w, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            var mesh = new Mesh { name = "SeekerPulseCrescentBolt" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>MV-806/815: the Sentinel's own straight bolt -- the same ogive of revolution
        /// (<see cref="CharacterMeshes.Lathe"/>) Max's own bolt used before this ticket, kept
        /// unchanged and now exclusively the Sentinel's: MV-806 made the Sentinel's bolt small, red
        /// and straight-line-only precisely so it cannot be confused with Max's, and Max's own bolt
        /// becoming a crescent must not undo that. Internal (not private) so
        /// <see cref="MaxWorlds.Arena.SentinelBolt"/>'s own cache can call it -- see that type's
        /// <c>GetBoltMesh</c>.</summary>
        internal static Mesh BuildStraightBoltMesh()
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
