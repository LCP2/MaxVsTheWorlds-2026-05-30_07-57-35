using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The Shoulder Rack's own rocket (MV-694) — player faction, homing, splash. Reuses
    /// <see cref="HomingSteering"/> for turn-toward-target and Cover-layer obstruction, the same shared
    /// steering <see cref="MaxWorlds.Enemies.HomingMissile"/> already uses (MV-708 extracted it), just
    /// fired the other way (player -&gt; robot, not robot -&gt; Max). Damage goes through the ordinary
    /// <see cref="DamageRules"/> friendly-fire gate, so it naturally ignores Max (Team.Player) and hits
    /// anything else IDamageable — RobotEnemy, Replicator, bosses, gates — with no special-casing per
    /// receiver type.
    ///
    /// Free-flying and not pooled, same lifetime shape as <see cref="MaxWorlds.Enemies.HomingMissile"/>:
    /// short-lived, self-destroys on impact/timeout. <see cref="Update"/> just forwards
    /// <c>Time.deltaTime</c> into the public <see cref="Tick"/> (MV-842) — the same explicit-dt split
    /// <see cref="ShoulderRack.Tick"/> and <see cref="SeekerPulse.Tick"/> already use — so an EditMode
    /// test can drive the homing flight to detonation deterministically without a live PlayerLoop.
    /// </summary>
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class PlayerRocket : MonoBehaviour
    {
        /// <summary>MV-842: how long a rocket flies its fixed launch arc — pitched up, yawed off the
        /// aim line — before it starts homing. It must never travel along the bolt line, so this phase
        /// ignores the target entirely; see <see cref="Fire"/> for the arc itself.</summary>
        private const float LaunchDurationSeconds = 0.25f;
        private const float LaunchPitchDegrees = 35f;
        private const float LaunchYawDegrees = 30f;

        /// <summary>MV-842: was 120 deg/s (min turn radius 14 m/s / 120 deg/s ~ 6.7m) — too wide to
        /// ever close inside the old 0.4m ContactRadius, so the rocket circled until fuel-out instead
        /// of striking its target.
        ///
        /// The fix spec's own proposed 300 deg/s (radius ~2.7m) still measurably orbits: a rocket fired
        /// 90 degrees off Max's facing at a stationary target 5m out settled into a stable ~2-3m loop
        /// around it and never once closed inside the new 0.8m ContactRadius before the 4s fuel budget
        /// (confirmed by ticking <c>MV842RocketHomingTests</c>'s own scenario out to fuel-out — pure
        /// "always turn toward the target's current bearing" steering can lock into a limit-cycle
        /// orbit whenever the target ends up inside the turn circle, independent of turn rate, as long
        /// as that circle's radius exceeds the contact radius). 720 deg/s (~1.1m radius) is still above
        /// 0.8m in theory but was swept across angle (0/45/90/179 degrees) and range (5/8/12m) without
        /// producing an orbit in any of them, converging inside 0.9s in the worst case (12m, near the
        /// rack's own max engagement range) — see the fix comment for the swept numbers.</summary>
        private const float TurnRateDegPerSec = 720f;

        /// <summary>MV-842: was a 3D distance of 0.4m against the target's own transform, which a level
        /// flight path at spawn height could never reach. Horizontal-only now (see <see cref="IsCloseToTarget"/>),
        /// the same shape <see cref="SeekerPulse"/>'s own contact test already uses.</summary>
        private const float ContactRadius = 0.8f;

        /// <summary>MV-842: how fast the rocket's altitude eases toward its target's centre height once
        /// it starts homing — an ease, not a snap, so the transition out of the launch arc reads as a
        /// dive/climb rather than the rocket teleporting onto the target's exact height.</summary>
        private const float HeightEaseRate = 6f;

        /// <summary>MV-842: if the locked target dies or goes dormant mid-flight, retarget the nearest
        /// live awake robot within this range of the rocket's own current position — the same range
        /// <see cref="ShoulderRack"/> itself fires within.</summary>
        private const float RetargetRangeMeters = 12f;

        /// <summary>MV-842: "or on touching the target's collider — whichever first" — a small
        /// tolerance around <see cref="Collider.ClosestPoint"/>, which returns the query point itself
        /// once that point is already inside the collider (distance 0).</summary>
        private const float ColliderTouchDistance = 0.15f;

        private const float FuelBudgetSeconds = 4f;

        private const int ClusterBombletCount = 3;
        private const float ClusterRingRadius = 1.5f;
        private const float ClusterBombletDamage = 10f;
        private const float ClusterBombletSplash = 1.2f;

        private static readonly List<PlayerRocket> s_active = new List<PlayerRocket>();
        private static readonly Collider[] s_hits = new Collider[32];
        private static readonly HashSet<int> s_hitIds = new HashSet<int>();

        /// <summary>Every rocket currently in flight — <c>MV694ShoulderRackTests</c> counts salvos this
        /// way, the same static-registry shape <see cref="MaxWorlds.Arena.Sentinel.Active"/> and
        /// <see cref="MaxWorlds.Enemies.RobotEnemy.Active"/> already use.</summary>
        public static IReadOnlyList<PlayerRocket> Active => s_active;

        private Transform _target;
        private RobotEnemy _targetRobot;
        private Collider _targetCollider;
        private float _speed;
        private float _damage;
        private float _splashRadius;
        private bool _cluster;
        private float _age;
        private bool _detonated;

        /// <summary>MV-1025: the two flame layers' own transforms, resolved once in <see cref="Init"/>
        /// (after <see cref="BuildVisual"/> has already built them) so <see cref="UpdateFlameFlicker"/>
        /// can rescale them every frame without a per-frame <see cref="Transform.Find"/>.</summary>
        private Transform _outerFlame;
        private Transform _innerFlame;

        /// <summary>MV-1025: "flicker ... random, seeded per rocket" — a <see cref="System.Random"/>
        /// instance seeded off this rocket's own <see cref="Object.GetInstanceID"/>, not
        /// <see cref="UnityEngine.Random"/>'s shared global stream, so one rocket's flicker never
        /// perturbs any other system's random sequence. Allocated once at <see cref="Init"/>, not per
        /// frame — <see cref="System.Random.NextDouble"/> itself doesn't allocate.</summary>
        private System.Random _flicker;

        /// <summary>MV-944: the exact point this rocket left from, captured once at <see cref="Fire"/>
        /// and never updated — the combat-level reference every level check below uses, rather than this
        /// rocket's own live <c>transform.position</c>. A homing rocket's own Y eases toward its
        /// target's height as it flies (<see cref="Tick"/>'s <c>HeightEaseRate</c> ease), so by the time
        /// it detonates near a deck target it may have already climbed to deck height itself — a level
        /// check against the LIVE position would then read "same level" no matter which level Max
        /// actually fired from, exactly defeating the fix.</summary>
        private Vector3 _origin;

        /// <summary>The robot this rocket was launched at — <c>MV694ShoulderRackTests</c> asserts the
        /// salvo picked the nearer, in-range Rusher over the out-of-range Heavy.</summary>
        public Transform TargetForTests => _target;

        /// <summary>Launch one rocket from <paramref name="origin"/> toward <paramref name="target"/>.
        /// Mirrors <see cref="MaxWorlds.Enemies.HomingMissile.Fire"/>'s static-builder shape.
        ///
        /// MV-842: leaves pitched up <see cref="LaunchPitchDegrees"/> and yawed
        /// <see cref="LaunchYawDegrees"/> outward from the aim line (<paramref name="launchYawRight"/>
        /// picks which side — <see cref="ShoulderRack"/> alternates this per rocket in a salvo) rather
        /// than aimed flat down the same line the primary weapon fires on.</summary>
        public static PlayerRocket Fire(Vector3 origin, Transform target, float speed, float damage,
            float splashRadius, bool cluster, bool launchYawRight)
        {
            var go = new GameObject("PlayerRocket (stand-in)");
            go.transform.position = origin;

            Vector3 aim = target != null ? target.position - origin : Vector3.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) aim = Vector3.forward;
            Quaternion aimRot = Quaternion.LookRotation(aim.normalized, Vector3.up);

            float yawSign = launchYawRight ? 1f : -1f;
            Quaternion launchOffset = Quaternion.Euler(-LaunchPitchDegrees, LaunchYawDegrees * yawSign, 0f);
            go.transform.rotation = aimRot * launchOffset;

            BuildVisual(go.transform);

            var rocket = go.AddComponent<PlayerRocket>();
            rocket.Init(target, speed, damage, splashRadius, cluster);
            rocket._origin = origin;
            s_active.Add(rocket);
            return rocket;
        }

        /// <summary>Every rocket currently tracked in <see cref="Active"/> is force-cleared —
        /// <c>[TearDown]</c> hygiene, same shape as <see cref="MaxWorlds.Arena.Sentinel.DestroyAllActive"/>.</summary>
        public static void DestroyAllActive()
        {
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                if (s_active[i] != null) Object.DestroyImmediate(s_active[i].gameObject);
            }
            s_active.Clear();
        }

        /// <summary>MV-770: the body's own resolved length — was a bare 0.16m capsule (7.7px at the
        /// play camera, barely wider than its own smoke trail).</summary>
        private static readonly CombatVfxTuning.RocketBodyTuning BodyTuning = CombatVfxTuning.RocketBody();

        private static void BuildVisual(Transform parent)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Material bodyMat = MaterialLibrary.Tinted(SurfaceKind.Metal, BodyColor);
            float halfLength = BodyTuning.Length * 0.5f;

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shaft.name = "Shaft";
            Strip(shaft);
            shaft.transform.SetParent(parent, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localScale = new Vector3(0.12f, halfLength, 0.12f);
            if (bodyMat != null) shaft.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            // Nose cone (MV-770 "a 0.5m body with a nose cone and 3 fins") — a squashed sphere at the
            // tip. Greybox/free-kit only (spec), so this is a primitive doing a cone's JOB, not a
            // custom mesh: readable as a pointed tip from the fixed ~72° camera, not a faithful cone.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nose.name = "NoseCone";
            Strip(nose);
            nose.transform.SetParent(parent, false);
            nose.transform.localPosition = new Vector3(0f, 0f, halfLength + 0.06f);
            nose.transform.localScale = new Vector3(0.13f, 0.13f, 0.16f);
            if (bodyMat != null) nose.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            BuildFins(parent, halfLength, bodyMat);
            BuildHazardBand(parent, halfLength);
            BuildExhaustFlame(parent, halfLength);
            BuildFireTrail(parent);
        }

        /// <summary>Three thin blades at the tail, 120 degrees apart — the silhouette detail that
        /// reads "rocket" rather than "capsule" from the fixed top-down camera.</summary>
        private static void BuildFins(Transform parent, float halfLength, Material bodyMat)
        {
            for (int i = 0; i < 3; i++)
            {
                var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fin.name = $"Fin{i}";
                Strip(fin);
                fin.transform.SetParent(parent, false);
                fin.transform.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                fin.transform.localPosition =
                    fin.transform.localRotation * new Vector3(0f, 0.13f, -halfLength + 0.05f);
                fin.transform.localScale = new Vector3(0.02f, 0.14f, 0.09f);
                if (bodyMat != null) fin.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;
            }
        }

        /// <summary>MV-1025: "hard to see ... Brighter? Red flames?" (Lee). Two additive unlit layers
        /// instead of MV-770's single amber capsule — a wide HDR-red outer flame plus a narrower HDR
        /// hot yellow-white inner core nested at its base, both anchored off the tail the same way the
        /// old single flame was. <see cref="_outerFlame"/>/<see cref="_innerFlame"/> hold the built
        /// transforms so <see cref="UpdateFlameFlicker"/> can rescale them every frame.</summary>
        private static void BuildExhaustFlame(Transform parent, float halfLength)
        {
            var outer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            outer.name = "ExhaustFlameOuter";
            Strip(outer);
            outer.transform.SetParent(parent, false);
            outer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            outer.transform.localPosition = new Vector3(0f, 0f, -halfLength - BodyTuning.OuterFlameLength * 0.5f);
            outer.transform.localScale =
                new Vector3(BodyTuning.OuterFlameWidth, BodyTuning.OuterFlameLength * 0.5f, BodyTuning.OuterFlameWidth);
            Material outerMat = VfxMaterials.AdditiveTinted(OuterFlameColor);
            if (outerMat != null) outer.GetComponent<MeshRenderer>().sharedMaterial = outerMat;

            var inner = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            inner.name = "ExhaustFlameInner";
            Strip(inner);
            inner.transform.SetParent(parent, false);
            inner.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            inner.transform.localPosition = new Vector3(0f, 0f, -halfLength - BodyTuning.InnerFlameLength * 0.5f);
            inner.transform.localScale =
                new Vector3(BodyTuning.InnerFlameWidth, BodyTuning.InnerFlameLength * 0.5f, BodyTuning.InnerFlameWidth);
            Material innerMat = VfxMaterials.AdditiveTinted(InnerFlameColor);
            if (innerMat != null) inner.GetComponent<MeshRenderer>().sharedMaterial = innerMat;
        }

        /// <summary>MV-1025: the hazard-red band "behind the nose" — a thin painted ring on the
        /// otherwise-gunmetal shaft, same lit <see cref="MaterialLibrary"/> path as the body (a physical
        /// marking, not a light source, so it stays off the additive/unlit VFX path the flame uses).</summary>
        private static void BuildHazardBand(Transform parent, float halfLength)
        {
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "HazardBand";
            Strip(band);
            band.transform.SetParent(parent, false);
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float bandHalf = BodyTuning.HazardBandWidth * 0.5f;
            band.transform.localPosition = new Vector3(0f, 0f, halfLength - bandHalf);
            band.transform.localScale = new Vector3(0.13f, bandHalf, 0.13f);
            Material hazardMat = MaterialLibrary.Tinted(SurfaceKind.Metal, HazardBandColor);
            if (hazardMat != null) band.GetComponent<MeshRenderer>().sharedMaterial = hazardMat;
        }

        /// <summary>MV-1025: replaces the old grey smoke trail with a fire-to-smoke one — red-orange at
        /// the tail fading through dark red to grey smoke by the time it dissipates, additive/unlit so
        /// the fresh end actually reads as burning rather than merely tinted.</summary>
        private static void BuildFireTrail(Transform parent)
        {
            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = BodyTuning.TrailTime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = BodyTuning.TrailWidth;
            trail.minVertexDistance = 0.03f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = VfxMaterials.AdditiveTinted(TrailColorStart);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(TrailColorStart, 0f),
                    new GradientColorKey(TrailColorMid, 0.4f),
                    new GradientColorKey(TrailColorEnd, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.6f, 0.4f),
                    new GradientAlphaKey(0f, 1f),
                });
            trail.colorGradient = gradient;

            trail.Clear();
        }

        /// <summary>Gunmetal — restyled by MV-702, the [ART] ticket MV-694's own doc pointed at.</summary>
        private static readonly Color BodyColor = new Color(0.35f, 0.36f, 0.4f);

        /// <summary>MV-1025 outer flame — HDR red, pushed past 1.0 the same way <c>LppeVfx.MuzzleColor</c>
        /// is so it actually clears the bloom threshold.</summary>
        private static readonly Color OuterFlameColor = new Color(2.4f, 0.35f, 0.10f, 1f);

        /// <summary>MV-1025 inner core — HDR hot yellow-white, nested inside the outer flame's base.</summary>
        private static readonly Color InnerFlameColor = new Color(2.2f, 1.7f, 0.9f, 1f);

        /// <summary>MV-1025 hazard band, behind the nose.</summary>
        private static readonly Color HazardBandColor = new Color(1.0f, 0.2f, 0.15f);

        /// <summary>MV-1025 fire-to-smoke trail gradient stops (0% / 40% / 100%).</summary>
        private static readonly Color TrailColorStart = new Color(1.0f, 0.35f, 0.1f);
        private static readonly Color TrailColorMid = new Color(0.5f, 0.1f, 0.05f);
        private static readonly Color TrailColorEnd = new Color(0.35f, 0.35f, 0.35f);

        /// <summary>MV-1025: the resolved length-scale each flame layer sits at before a frame's own
        /// flicker multiplier is applied — precomputed once so <see cref="UpdateFlameFlicker"/> never
        /// has to re-derive it from <see cref="BodyTuning"/> every frame.</summary>
        private static readonly float OuterFlameBaseScaleY = BodyTuning.OuterFlameLength * 0.5f;
        private static readonly float InnerFlameBaseScaleY = BodyTuning.InnerFlameLength * 0.5f;

        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }

        private void Init(Transform target, float speed, float damage, float splashRadius, bool cluster)
        {
            _target = target;
            _targetRobot = target != null ? target.GetComponent<RobotEnemy>() : null;
            _targetCollider = target != null ? target.GetComponent<Collider>() : null;
            _speed = speed;
            _damage = damage;
            _splashRadius = splashRadius;
            _cluster = cluster;

            _flicker = new System.Random(GetInstanceID());
            _outerFlame = transform.Find("ExhaustFlameOuter");
            _innerFlame = transform.Find("ExhaustFlameInner");
        }

        /// <summary>MV-1025: "each frame, length scale = 1 +/- 0.2 (random, seeded per rocket). No
        /// allocation per frame." One shared draw per frame, applied to both layers, so they read as
        /// one burning plume rather than two independently-jittering capsules.</summary>
        private void UpdateFlameFlicker()
        {
            if (_outerFlame == null && _innerFlame == null) return;

            float scale = 1f + ((float)_flicker.NextDouble() * 2f - 1f) * BodyTuning.FlameFlickerScale;

            if (_outerFlame != null)
            {
                Vector3 sc = _outerFlame.localScale;
                sc.y = OuterFlameBaseScaleY * scale;
                _outerFlame.localScale = sc;
            }
            if (_innerFlame != null)
            {
                Vector3 sc = _innerFlame.localScale;
                sc.y = InnerFlameBaseScaleY * scale;
                _innerFlame.localScale = sc;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advance one step. Public so an EditMode test can drive the flight deterministically
        /// without a live PlayerLoop — the same reason <see cref="ShoulderRack.Tick"/> and
        /// <see cref="SeekerPulse.Tick"/> are public.</summary>
        public void Tick(float dt)
        {
            if (_detonated) return;
            _age += dt;

            UpdateFlameFlicker();

            bool launching = _age < LaunchDurationSeconds;
            if (!launching)
            {
                RetargetIfLost();

                if (_target != null)
                {
                    transform.rotation = HomingSteering.TurnToward(transform.rotation, transform.position,
                        _target.position, TurnRateDegPerSec, dt);
                }
            }

            Vector3 from = transform.position;
            Vector3 next = from + transform.forward * (_speed * dt);

            // MV-842 item 3: height eases toward the target's centre height once homing starts, rather
            // than however the launch pitch happened to leave it — an ease on top of the forward-driven
            // move above, not a replacement for it.
            if (!launching && _target != null)
            {
                next.y = Mathf.Lerp(next.y, _target.position.y, 1f - Mathf.Exp(-HeightEaseRate * dt));
            }

            if (HomingSteering.BlockedByGeometry(from, next, out RaycastHit hit))
            {
                transform.position = hit.point;
                Detonate();
                return;
            }

            transform.position = next;

            bool closeEnough = !launching && IsCloseToTarget();
            if (closeEnough || _age >= FuelBudgetSeconds) Detonate();
        }

        /// <summary>MV-842 item 4: "detonate when horizontal (x/z) distance to the target is <= 0.8m,
        /// or on touching the target's collider — whichever first." The collider check is a fallback
        /// for a target whose collider extends past the horizontal radius — see
        /// <see cref="ColliderTouchDistance"/>.</summary>
        private bool IsCloseToTarget()
        {
            if (_target == null) return false;

            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= ContactRadius * ContactRadius) return true;

            if (_targetCollider != null)
            {
                Vector3 closest = _targetCollider.ClosestPoint(transform.position);
                if ((closest - transform.position).sqrMagnitude <= ColliderTouchDistance * ColliderTouchDistance)
                    return true;
            }

            return false;
        }

        /// <summary>MV-842 item 5: "if the target dies or goes dormant mid-flight, re-target the
        /// nearest live, awake robot within 12m of the rocket; if none, fly straight and detonate at
        /// fuel-out." Reads <see cref="RobotEnemy.Active"/> directly — the same static-registry idiom
        /// this class's own <see cref="Active"/> and <see cref="ShoulderRack"/>'s in-range scan use —
        /// rather than a physics query, since every live robot is already in that list. MV-944: never
        /// retargets onto a robot on the other combat level from <see cref="_origin"/> — the level Max
        /// actually fired from, not this rocket's own live (possibly already-climbed) position.</summary>
        private void RetargetIfLost()
        {
            if (_target != null && _targetRobot != null && _targetRobot.IsAlive && !_targetRobot.IsDormant) return;

            MapData map = EnemyNavigation.Map;
            RobotEnemy replacement = null;
            float bestSq = RetargetRangeMeters * RetargetRangeMeters;
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                if (r == null || !r.IsAlive || r.IsDormant) continue;
                if (!CombatLevel.SameLevel(map, _origin, r.transform.position)) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d <= bestSq) { bestSq = d; replacement = r; }
            }

            _targetRobot = replacement;
            _target = replacement != null ? replacement.transform : null;
            _targetCollider = replacement != null ? replacement.GetComponent<Collider>() : null;
        }

        private void Detonate()
        {
            _detonated = true;
            s_active.Remove(this);

            ApplySplashDamage(_origin, transform.position, _damage, _splashRadius);
            RocketImpactVfx.PlaySplashRing(transform.position, _splashRadius);
            if (_cluster) SpawnClusterBomblets(_origin, transform.position);

            // MV-770: the impact's own flash+sparks (CombatVfx) and feel (GameFeel's hitstop/shake) —
            // same "either way" bus idiom HomingMissile.Detonate uses for HudSignals.MissileImpact.
            HudSignals.EmitRocketImpact(transform.position, _damage);

            // MV-842: an EditMode test now ticks a rocket to detonation directly (see
            // MV842RocketHomingTests) — Destroy() only defers to end-of-frame in Play mode, and edit
            // mode has no such frame to defer to, so it must be immediate here, same branch Strip()
            // already uses below for the same reason.
            if (Application.isPlaying) Destroy(gameObject);
            else Object.DestroyImmediate(gameObject);
        }

        /// <summary>One AOE damage query — reused for both the rocket's own splash and each cluster
        /// bomblet (MV-694 <c>s_clu</c>), same dedupe idiom <see cref="PlayerAbilities.Land"/> uses for
        /// the Water Balloon splash. MV-944: never damages a receiver on the other combat level from
        /// <paramref name="shooterOrigin"/> — the level Max actually fired from (see <see cref="_origin"/>'s
        /// own doc comment for why this must be the launch point, never <paramref name="point"/> itself,
        /// which a homing rocket may have already climbed to the target's own height by the time it gets
        /// here).</summary>
        private static void ApplySplashDamage(Vector3 shooterOrigin, Vector3 point, float damage, float radius)
        {
            MapData map = EnemyNavigation.Map;
            s_hitIds.Clear();
            int count = Physics.OverlapSphereNonAlloc(point, radius, s_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hitIds.Add(s_hits[i].gameObject.GetInstanceID())) continue;
                if (!s_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive) continue;
                if (!DamageRules.Applies(Team.Player, d.Team)) continue;
                if (!CombatLevel.SameLevel(map, shooterOrigin, s_hits[i].transform.position)) continue;
                d.TakeDamage(new DamageInfo(damage, point, Vector3.up, Team.Player,
                    source: DamageSource.SecondaryWeapon));
            }
        }

        /// <summary>MV-768 <c>s_clu</c>: three bomblets in a ring around the impact point, each its own
        /// small splash — gated by <see cref="ShoulderRack"/> on <c>s_clu</c>'s own RIG node level, not
        /// (as it was pre-MV-768) a maxed Salvo track. <paramref name="shooterOrigin"/> threads through to
        /// each bomblet's own <see cref="ApplySplashDamage"/> call, same MV-944 reasoning as the rocket's
        /// own splash.</summary>
        private static void SpawnClusterBomblets(Vector3 shooterOrigin, Vector3 center)
        {
            for (int i = 0; i < ClusterBombletCount; i++)
            {
                float angle = i * (360f / ClusterBombletCount) * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ClusterRingRadius;
                ApplySplashDamage(shooterOrigin, point, ClusterBombletDamage, ClusterBombletSplash);
                RocketImpactVfx.PlayBombletPop(point);
            }
        }
    }
}
