using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Pipe Turret's corrosive coolant glob (MV-691) — a ballistic lob with NO homing of any
    /// kind, the same "locked at fire time" idiom <see cref="BolterBolt"/> already uses for its own
    /// straight rod: <see cref="_targetPosition"/> is captured once in <see cref="Fire"/> and never
    /// re-read, so the glob still lands where Max WAS the instant it was fired, whether or not he's
    /// still there. Arced rather than flat (<see cref="Update"/>'s sine lob), and aimed at a fixed
    /// POINT rather than flying forever along a heading the way <see cref="BolterBolt"/> does.
    ///
    /// On arrival it splash-damages whichever receiver it was fired at, if still in splash range —
    /// same "read live off the target's CURRENT position, not the locked aim point" idiom
    /// <see cref="HomingMissile.Detonate"/> already uses — and, unlike every other ranged projectile
    /// in the roster, leaves a <see cref="CorrosionPuddle"/> behind at the impact point instead of
    /// just disappearing.
    /// </summary>
    public sealed class CorrosiveGlob : MonoBehaviour
    {
        private const float FlightHeight = 1.0f;

        /// <summary>Peak height of the lob arc above <see cref="FlightHeight"/> — purely visual (the
        /// ticket's own "arc"), the hit/splash maths below are horizontal-only, same as every other
        /// ranged projectile in the roster.</summary>
        private const float ArcHeight = 1.4f;

        private Vector3 _origin;
        private Vector3 _targetPosition;   // locked at Fire time (AC: "at Max's position at fire time")
        private Transform _targetTransform;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _damage;
        private float _splashRadius;
        private float _puddleRadius;
        private float _puddleDuration;
        private float _totalDistance;
        private float _traveled;
        private Vector3 _direction;

        /// <summary>Fire one glob from <paramref name="origin"/>, locked at <paramref name="target"/>'s
        /// position RIGHT NOW — never re-aimed, never homed. <paramref name="damage"/>/<paramref name="splashRadius"/>
        /// come straight off the Turret's <see cref="EnemyArchetype.ContactDamage"/>/<see cref="EnemyArchetype.ContactRadius"/>,
        /// same "ranged kind's ContactRadius feeds the projectile" idiom <see cref="HomingMissile"/>
        /// already uses. <paramref name="puddleRadius"/>/<paramref name="puddleDuration"/> are the
        /// ticket's own authored puddle size/lifetime, handed straight to <see cref="CorrosionPuddle.Spawn"/>
        /// once this glob lands.</summary>
        public static CorrosiveGlob Fire(Vector3 origin, Transform target, float speed, float damage,
            float splashRadius, float puddleRadius, float puddleDuration)
        {
            var go = new GameObject("CorrosiveGlob (stand-in)");
            go.transform.position = origin + Vector3.up * FlightHeight;
            BuildVisual(go.transform);

            var glob = go.AddComponent<CorrosiveGlob>();
            Vector3 targetPosition = target != null ? target.position : origin + Vector3.forward;
            IDamageable damageable = target != null ? target.GetComponent<IDamageable>() : null;
            glob.Init(origin, targetPosition, target, damageable, speed, damage, splashRadius,
                puddleRadius, puddleDuration);
            return glob;
        }

        /// <summary>The ticket's own "0.35 m green blob" colour — a distinct green so it never reads
        /// as one more instance of the game's warm rust/orange telegraph family.</summary>
        private static readonly Color GlobColor = new Color(0.24f, 0.55f, 0.18f);

        /// <summary>MV-699's own "glob trails": a short smear the same MV-508 fix
        /// <see cref="HomingMissile.BuildTrail"/> already uses — a lobbed ballistic arc with no
        /// connective motion between frames reads as strobing rather than flight.</summary>
        private const float TrailTime = 0.14f;
        private const float TrailWidth = 0.3f;

        private static void BuildVisual(Transform parent)
        {
            // Same reason as HomingMissile.BuildVisual/BolterBolt.BuildVisual: without this,
            // RuntimeSurfaceDirector's sweep claims this a frame after spawn and overwrites the
            // deliberate paint with the generic world-prop material (MV-350).
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, GlobColor);
            var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blob.name = "Glob";
            Strip(blob);
            blob.transform.SetParent(parent, false);
            blob.transform.localScale = Vector3.one * 0.35f;   // the ticket's own authored size
            if (mat != null) blob.GetComponent<MeshRenderer>().sharedMaterial = mat;

            BuildTrail(parent, mat);
        }

        /// <summary>Same shape as <see cref="HomingMissile.BuildTrail"/>: a short motion smear, not a
        /// ribbon, sourced from the glob's own material so it shares its tint/caching rules.</summary>
        private static void BuildTrail(Transform parent, Material mat)
        {
            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = TrailTime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = TrailWidth;
            trail.minVertexDistance = 0.03f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = mat;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(GlobColor, 0f), new GradientColorKey(GlobColor, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            trail.Clear();
        }

        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            // Manual proximity check in Update, not physics — same Application.isPlaying idiom as
            // HomingMissile/BolterBolt: Destroy is illegal outside play mode, which an EditMode test
            // calling Fire() directly hits.
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }

        private void Init(Vector3 origin, Vector3 targetPosition, Transform targetTransform,
            IDamageable targetDamageable, float speed, float damage, float splashRadius,
            float puddleRadius, float puddleDuration)
        {
            _origin = origin;
            _targetPosition = targetPosition;
            _targetTransform = targetTransform;
            _targetDamageable = targetDamageable;
            _speed = Mathf.Max(0.01f, speed);
            _damage = damage;
            _splashRadius = splashRadius;
            _puddleRadius = puddleRadius;
            _puddleDuration = puddleDuration;

            Vector3 flat = targetPosition - origin;
            flat.y = 0f;
            _totalDistance = flat.magnitude;
            _direction = _totalDistance > 1e-4f ? flat / _totalDistance : Vector3.forward;
        }

        private void Update()
        {
            _traveled += _speed * Time.deltaTime;

            float t = _totalDistance > 0f ? Mathf.Clamp01(_traveled / _totalDistance) : 1f;
            Vector3 flatPos = _origin + _direction * (_totalDistance * t);
            float arc = Mathf.Sin(t * Mathf.PI) * ArcHeight;
            transform.position = new Vector3(flatPos.x, FlightHeight + arc, flatPos.z);

            if (_traveled >= _totalDistance) Detonate();
        }

        /// <summary>Splash-damages the receiver it was fired at (if it's still within splash range of
        /// the impact point, read off its CURRENT position — same "never trust the locked aim point
        /// for the hit check" rule <see cref="HomingMissile.Detonate"/> already follows), then always
        /// leaves a <see cref="CorrosionPuddle"/> at the impact point regardless of whether anything
        /// was hit — an area-denial puddle earns its keep even on a miss.</summary>
        private void Detonate()
        {
            Vector3 impactPoint = new Vector3(_targetPosition.x, 0f, _targetPosition.z);

            bool inSplash = _targetTransform != null &&
                HorizontalDistanceSq(impactPoint, _targetTransform.position) <= _splashRadius * _splashRadius;

            if (inSplash && _targetDamageable != null && _targetDamageable.IsAlive)
            {
                _targetDamageable.TakeDamage(new DamageInfo(_damage, impactPoint, _direction, Team.Enemy));
            }

            CorrosionPuddle.Spawn(impactPoint, _puddleRadius, _puddleDuration);
            Destroy(gameObject);
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
