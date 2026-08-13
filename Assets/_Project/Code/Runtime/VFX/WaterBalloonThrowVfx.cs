using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Water Balloon's thrown body (MV-334). The aim arc (<see cref="WaterBalloonAimMesh"/>,
    /// WV-241) only PREVIEWS a throw before release — nothing was ever visible during the actual
    /// flight, so a throw read as a silent pause before the splash showed up. This is the missing
    /// middle: a body that flies the exact same parabola the preview arc drew
    /// (<see cref="WaterBalloonAimMesh.LocalPositionOnArc"/>), so what you aimed is what you watch fly.
    ///
    /// Free-flying, not pooled — the same "short-lived, never seen twice, not worth a pool" lifetime
    /// <see cref="MaxWorlds.Enemies.HomingMissile"/> already uses for the Bomber's own projectile.
    /// Owned entirely by the art stream: it carries no gameplay state and makes no gameplay decision —
    /// <see cref="MaxWorlds.Weapons.PlayerAbilities.TryThrowWaterBalloon"/> computes the landing point
    /// and flight time and hands both to <see cref="Fire"/>; this only answers "what does the throw
    /// look like".
    /// </summary>
    public sealed class WaterBalloonThrowVfx : MonoBehaviour
    {
        private const float BodyDiameter = 0.32f;
        private const float WobbleCyclesPerFlight = 4f;
        private const float WobbleAmount = 0.12f;
        private const float TrailParticleLifetime = 0.25f;

        private static readonly Color BodyColor = new Color(0.31f, 0.76f, 0.97f, 0.92f);
        private static readonly Color KnotColor = new Color(0.16f, 0.42f, 0.58f);

        private Vector3 _origin;
        private Vector3 _direction = Vector3.forward;
        private float _distance = 1f;
        private float _duration = 1f;
        private float _age;
        private Transform _body;
        private ParticleSystem _trail;

        /// <summary>Launch a visible balloon from <paramref name="origin"/> toward
        /// <paramref name="landing"/>, arriving after <paramref name="durationSeconds"/> — the same
        /// point and timing the caller already computed for the splash it will land into, so the
        /// picture and the mechanic never drift apart.</summary>
        public static WaterBalloonThrowVfx Fire(Vector3 origin, Vector3 landing, float durationSeconds)
        {
            var go = new GameObject("WaterBalloonThrow (stand-in)");

            Vector3 toLanding = landing - origin;
            toLanding.y = 0f;
            Vector3 dir = toLanding.sqrMagnitude > 1e-4f ? toLanding.normalized : Vector3.forward;

            var vfx = go.AddComponent<WaterBalloonThrowVfx>();
            vfx.Init(origin, dir, toLanding.magnitude, Mathf.Max(0.01f, durationSeconds));
            return vfx;
        }

        private void Init(Vector3 origin, Vector3 direction, float distance, float durationSeconds)
        {
            _origin = origin;
            _direction = direction;
            _distance = Mathf.Max(0.01f, distance);
            _duration = durationSeconds;

            _body = BuildBody(transform);
            _trail = BuildTrail();
            ApplyProgress(0f);
        }

        /// <summary>World position at fraction <paramref name="t"/> of the flight — the same curve
        /// <see cref="WaterBalloonAimMesh.LocalPositionOnArc"/> draws for the preview arc, carried into
        /// world space by this throw's own origin and direction.</summary>
        public Vector3 PositionAt(float t)
        {
            Vector3 local = WaterBalloonAimMesh.LocalPositionOnArc(_distance, t);
            return _origin + _direction * local.z + Vector3.up * local.y;
        }

        /// <summary>Moves the balloon to fraction <paramref name="t"/> of its flight and re-shapes its
        /// squash/stretch. Public and separate from <see cref="Update"/> so a test can drive the flight
        /// directly — Unity never ticks <c>Update</c> outside Play mode.</summary>
        public void ApplyProgress(float t)
        {
            t = Mathf.Clamp01(t);
            transform.position = PositionAt(t);

            if (_body == null) return;

            // A rubber balloon flexes as it flies — a rigid sphere reads as a thrown ball, not water.
            // Squash along the flight axis, stretch across it.
            float wobble = Mathf.Sin(t * Mathf.PI * WobbleCyclesPerFlight) * WobbleAmount;
            _body.localScale = new Vector3(
                BodyDiameter * (1f - wobble),
                BodyDiameter * (1f - wobble),
                BodyDiameter * (1f + wobble));
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _duration > 0f ? _age / _duration : 1f;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            ApplyProgress(t);
            EmitTrail();
        }

        private void EmitTrail()
        {
            if (_trail == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = transform.position,
                velocity = Vector3.zero,
                startSize = BodyDiameter * Random.Range(0.35f, 0.6f),
                startLifetime = TrailParticleLifetime,
                startColor = BodyColor,
            };
            _trail.Emit(ep, 1);
        }

        /// <summary>The visible balloon: a tinted sphere plus a small tied knot at the trailing end —
        /// the one shape detail that reads "balloon" over "ball" at gameplay zoom, same idea as
        /// <see cref="MaxWorlds.Enemies.HomingMissile"/>'s own tail fins.</summary>
        private static Transform BuildBody(Transform parent)
        {
            // MV-350 audit: same shape as HomingMissile — neither the Body nor the Knot below is
            // IDamageable, so with nothing marking them RuntimeSurfaceDirector would claim them the
            // frame after a throw and overwrite this tint with the generic white world-prop material.
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            StripCollider(body);
            body.transform.SetParent(parent, false);
            body.transform.localScale = Vector3.one * BodyDiameter;
            var mat = MaterialLibrary.Tinted(SurfaceKind.Metal, BodyColor);
            if (mat != null) body.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var knot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            knot.name = "Knot";
            StripCollider(knot);
            knot.transform.SetParent(body.transform, false);
            knot.transform.localPosition = new Vector3(0f, 0f, -0.55f);
            knot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            knot.transform.localScale = new Vector3(0.18f, 0.12f, 0.18f);
            var knotMat = MaterialLibrary.Tinted(SurfaceKind.Metal, KnotColor);
            if (knotMat != null) knot.GetComponent<MeshRenderer>().sharedMaterial = knotMat;

            return body.transform;
        }

        private ParticleSystem BuildTrail()
        {
            var go = new GameObject("WaterBalloonTrail");
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0f;
            main.startLifetime = TrailParticleLifetime;
            main.startSize = BodyDiameter * 0.5f;
            main.startColor = BodyColor;
            main.maxParticles = 60;
            main.gravityModifier = 0.8f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.enabled = false;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Droplet());

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(BodyColor, 0f), new GradientColorKey(BodyColor, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
            return ps;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c == null) return;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }

        private void OnDestroy()
        {
            if (_trail == null) return;
            var go = _trail.gameObject;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
