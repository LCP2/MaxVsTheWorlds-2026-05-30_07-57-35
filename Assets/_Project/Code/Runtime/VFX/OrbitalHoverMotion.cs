using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1346 — the ORBITAL's hover. Lives on the hover pivot (parent of the drum, eye and crown):
    /// bobs it ±<see cref="BobAmplitude"/> m on a <see cref="BobPeriod"/> s sine from a random phase, tilts
    /// it up to <see cref="MaxTiltDegrees"/> toward its direction of travel, and keeps a soft shadow blob
    /// on the floor (smaller + darker at the low point) plus a faint crimson thruster glow on the drum's
    /// underside. Purely visual — the collider and the missile launch maths are not touched.
    /// </summary>
    public sealed class OrbitalHoverMotion : MonoBehaviour
    {
        public const float BobAmplitude = 0.08f;
        public const float BobPeriod = 2.2f;
        public const float MaxTiltDegrees = 4f;
        public const float ShadowDiameter = 1.2f;
        private const float ShadowLift = 0.04f;
        private const float TiltFullSpeed = 3f;   // m/s at which the tilt reaches its maximum
        private static readonly Color ShadowDark = new Color(0.04f, 0.04f, 0.07f, 0.35f);
        private static readonly Color ThrusterGlow = new Color(0.9f, 0.1f, 0.15f, 1f) * 0.35f;

        private static Mesh s_quad;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private float _phase, _t;
        private Transform _ground;          // the node whose local origin is the floor under the robot
        private Transform _shadow;
        private MeshRenderer _shadowRenderer;
        private MaterialPropertyBlock _mpb;
        private Vector3 _lastPos;
        private bool _hasLast;
        private Quaternion _tilt = Quaternion.identity;

        /// <summary>Hover offset (metres) of the drum at <paramref name="t"/> seconds for a start phase.</summary>
        public static float Offset(float t, float phase) =>
            BobAmplitude * Mathf.Sin(2f * Mathf.PI * t / BobPeriod + phase);

        public static void Attach(Transform hover, Transform ground, float glowY)
        {
            var m = hover.gameObject.AddComponent<OrbitalHoverMotion>();
            m._phase = Random.value * 2f * Mathf.PI;
            m._ground = ground;
            m._mpb = new MaterialPropertyBlock();

            Material alpha = VfxMaterials.AlphaBlend(VfxMaterials.Glow());
            m._shadow = MakeQuad("OrbitalShadow", hover, alpha, out m._shadowRenderer);
            m.Apply(0f);

            Material add = VfxMaterials.Additive(VfxMaterials.Glow());
            Transform glow = MakeQuad("OrbitalThrusterGlow", hover, add, out MeshRenderer gr);
            glow.localPosition = new Vector3(0f, glowY, 0f);
            glow.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // faces down
            glow.localScale = Vector3.one * 1.3f;
            gr.GetPropertyBlock(m._mpb);
            m._mpb.SetColor(BaseColorId, ThrusterGlow);
            m._mpb.SetColor(ColorId, ThrusterGlow);
            gr.SetPropertyBlock(m._mpb);
        }

        private static Transform MakeQuad(string name, Transform parent, Material mat, out MeshRenderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // A generated unit quad in the XY plane facing -Z; rotating it -90/+90 about X puts it flat.
        private static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "OrbitalQuad" };
            s_quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            s_quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            s_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            s_quad.RecalculateNormals();
            s_quad.RecalculateBounds();
            return s_quad;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Advance(dt);
        }

        /// <summary>Step the hover by <paramref name="dt"/> seconds (also the test seam).</summary>
        public void Advance(float dt)
        {
            _t += dt;
            Apply(dt);
        }

        private void Apply(float dt)
        {
            float off = Offset(_t, _phase);
            transform.localPosition = new Vector3(0f, off, 0f);

            // Tilt toward the direction of travel, from the ground node's world displacement.
            Vector3 pos = _ground != null ? _ground.position : transform.position;
            if (dt > 0f && _hasLast)
            {
                Vector3 v = (pos - _lastPos) / dt; v.y = 0f;
                float k = Mathf.Clamp01(v.magnitude / TiltFullSpeed);
                Quaternion target = Quaternion.identity;
                if (k > 0.001f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, v.normalized);
                    target = Quaternion.AngleAxis(MaxTiltDegrees * k, axis);
                    if (_ground != null) target = Quaternion.Inverse(_ground.rotation) * target * _ground.rotation;
                }
                _tilt = Quaternion.Slerp(_tilt, target, Mathf.Clamp01(8f * dt));
            }
            _lastPos = pos; _hasLast = true;
            transform.localRotation = _tilt;

            // Shadow: flat on the floor under the robot, smaller + darker at the low point of the bob.
            if (_shadow == null) return;
            float low = Mathf.Clamp01(0.5f - 0.5f * off / BobAmplitude);   // 1 at the lowest, 0 at the highest
            float size = ShadowDiameter * (1f + 0.08f * (1f - low) - 0.08f * low);
            Transform g = _ground != null ? _ground : transform.parent;
            Vector3 floor = g != null ? g.position : transform.position;
            _shadow.position = new Vector3(floor.x, floor.y + ShadowLift, floor.z);
            _shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
            float ls = Mathf.Max(0.0001f, transform.lossyScale.x);
            _shadow.localScale = Vector3.one * (size / ls);
            Color c = ShadowDark; c.a *= Mathf.Lerp(0.85f, 1.15f, low);
            _shadowRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            _shadowRenderer.SetPropertyBlock(_mpb);
        }
    }
}
