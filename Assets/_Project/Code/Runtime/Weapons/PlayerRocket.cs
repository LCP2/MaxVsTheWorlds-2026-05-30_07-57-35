using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
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
    /// short-lived, self-destroys on impact/timeout. Flight/detonation timing runs on
    /// <c>Time.deltaTime</c> inside <see cref="Update"/> and isn't covered by an EditMode test — per the
    /// project's standing PlayMode-is-CI's-problem rule — <see cref="ShoulderRack"/>'s own test only
    /// exercises the firing/targeting decision that calls <see cref="Fire"/>.
    /// </summary>
    public sealed class PlayerRocket : MonoBehaviour
    {
        private const float TurnRateDegPerSec = 120f;
        private const float ContactRadius = 0.4f;
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
        private float _speed;
        private float _damage;
        private float _splashRadius;
        private bool _cluster;
        private float _age;
        private bool _detonated;

        /// <summary>The robot this rocket was launched at — <c>MV694ShoulderRackTests</c> asserts the
        /// salvo picked the nearer, in-range Rusher over the out-of-range Heavy.</summary>
        public Transform TargetForTests => _target;

        /// <summary>Launch one rocket from <paramref name="origin"/> toward <paramref name="target"/>.
        /// Mirrors <see cref="MaxWorlds.Enemies.HomingMissile.Fire"/>'s static-builder shape.</summary>
        public static PlayerRocket Fire(Vector3 origin, Transform target, float speed, float damage,
            float splashRadius, bool cluster)
        {
            var go = new GameObject("PlayerRocket (stand-in)");
            go.transform.position = origin;

            Vector3 aim = target != null ? target.position - origin : Vector3.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) aim = Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

            BuildVisual(go.transform);

            var rocket = go.AddComponent<PlayerRocket>();
            rocket.Init(target, speed, damage, splashRadius, cluster);
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
            BuildExhaustFlame(parent, halfLength);
            BuildSmokeTrail(parent);
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

        /// <summary>The amber unlit flame (MV-770) layered behind the existing grey smoke trail — an
        /// exhaust needs to look like it's BURNING, which a lit grey-tinted capsule never could.</summary>
        private static void BuildExhaustFlame(Transform parent, float halfLength)
        {
            var flame = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            flame.name = "ExhaustFlame";
            Strip(flame);
            flame.transform.SetParent(parent, false);
            flame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float flameLength = BodyTuning.ExhaustFlameSize;
            flame.transform.localPosition = new Vector3(0f, 0f, -halfLength - flameLength * 0.5f);
            flame.transform.localScale = new Vector3(0.07f, flameLength * 0.5f, 0.07f);
            Material flameMat = VfxMaterials.AdditiveTinted(ExhaustFlameColor);
            if (flameMat != null) flame.GetComponent<MeshRenderer>().sharedMaterial = flameMat;
        }

        /// <summary>The ticket's "rocket smoke trail" — same build idiom
        /// <see cref="MaxWorlds.Enemies.HomingMissile.BuildTrail"/> and <see cref="SeekerPulse"/>'s own
        /// bolt trail use, just wider/longer-lived and grey rather than a weapon-coloured bolt smear, so
        /// it reads as smoke rather than an energy streak.</summary>
        private static void BuildSmokeTrail(Transform parent)
        {
            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = 0.14f;
            trail.minVertexDistance = 0.03f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, SmokeColor);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(SmokeColor, 0f), new GradientColorKey(SmokeColor, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            trail.Clear();
        }

        /// <summary>Gunmetal — restyled by MV-702, the [ART] ticket MV-694's own doc pointed at.</summary>
        private static readonly Color BodyColor = new Color(0.35f, 0.36f, 0.4f);

        /// <summary>The smoke trail's colour (MV-702) — pale grey, distinct from every weapon-coloured
        /// trail in the cast (the LPPE bolt's cyan-white, the missile's own shaft tint).</summary>
        private static readonly Color SmokeColor = new Color(0.6f, 0.6f, 0.58f);

        /// <summary>The exhaust flame's colour (MV-770) — pushed past 1.0 the same way
        /// <c>LppeVfx.MuzzleColor</c> is, so it actually clears the bloom threshold rather than sitting
        /// at the same brightness as everything else on screen.</summary>
        private static readonly Color ExhaustFlameColor = new Color(1.6f, 0.9f, 0.3f, 1f);

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
            _speed = speed;
            _damage = damage;
            _splashRadius = splashRadius;
            _cluster = cluster;
        }

        private void Update()
        {
            if (_detonated) return;
            float dt = Time.deltaTime;
            _age += dt;

            if (_target != null)
            {
                transform.rotation = HomingSteering.TurnToward(transform.rotation, transform.position,
                    _target.position, TurnRateDegPerSec, dt);
            }

            Vector3 from = transform.position;
            Vector3 next = from + transform.forward * (_speed * dt);

            if (HomingSteering.BlockedByGeometry(from, next, out RaycastHit hit))
            {
                transform.position = hit.point;
                Detonate();
                return;
            }

            transform.position = next;

            bool closeEnough = _target != null &&
                (transform.position - _target.position).sqrMagnitude <= ContactRadius * ContactRadius;
            if (closeEnough || _age >= FuelBudgetSeconds) Detonate();
        }

        private void Detonate()
        {
            _detonated = true;
            s_active.Remove(this);

            ApplySplashDamage(transform.position, _damage, _splashRadius);
            RocketImpactVfx.PlaySplashRing(transform.position, _splashRadius);
            if (_cluster) SpawnClusterBomblets(transform.position);

            // MV-770: the impact's own flash+sparks (CombatVfx) and feel (GameFeel's hitstop/shake) —
            // same "either way" bus idiom HomingMissile.Detonate uses for HudSignals.MissileImpact.
            HudSignals.EmitRocketImpact(transform.position, _damage);

            Destroy(gameObject);
        }

        /// <summary>One AOE damage query — reused for both the rocket's own splash and each cluster
        /// bomblet (MV-694 <c>s_clu</c>), same dedupe idiom <see cref="PlayerAbilities.Land"/> uses for
        /// the Water Balloon splash.</summary>
        private static void ApplySplashDamage(Vector3 point, float damage, float radius)
        {
            s_hitIds.Clear();
            int count = Physics.OverlapSphereNonAlloc(point, radius, s_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hitIds.Add(s_hits[i].gameObject.GetInstanceID())) continue;
                if (!s_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive) continue;
                if (!DamageRules.Applies(Team.Player, d.Team)) continue;
                d.TakeDamage(new DamageInfo(damage, point, Vector3.up, Team.Player,
                    source: DamageSource.SecondaryWeapon));
            }
        }

        /// <summary>MV-768 <c>s_clu</c>: three bomblets in a ring around the impact point, each its own
        /// small splash — gated by <see cref="ShoulderRack"/> on <c>s_clu</c>'s own RIG node level, not
        /// (as it was pre-MV-768) a maxed Salvo track.</summary>
        private static void SpawnClusterBomblets(Vector3 center)
        {
            for (int i = 0; i < ClusterBombletCount; i++)
            {
                float angle = i * (360f / ClusterBombletCount) * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ClusterRingRadius;
                ApplySplashDamage(point, ClusterBombletDamage, ClusterBombletSplash);
                RocketImpactVfx.PlayBombletPop(point);
            }
        }
    }
}
