using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// RICOCHET's aim-preview line (MV-1197, design v5 3.1, BUILD MODE: EXACT) -- drawn while the aim
    /// stick is held with RICOCHET active: the disc's REAL first two bounce legs, computed by
    /// <see cref="RicochetDisc.ComputePreviewLegs"/> (the SAME swept-sphere hit/bounce code the disc
    /// itself uses in <see cref="RicochetDisc.Tick"/>, never a separate approximation), plus a marker on
    /// whichever robot AIMING would bend the disc toward (<see cref="RicochetDisc.FindSteerCandidate"/>,
    /// also the disc's own real selection logic).
    ///
    /// Every element here is a <see cref="LineRenderer"/>, never a <see cref="MeshRenderer"/>. This
    /// instance is parented directly onto the player (see <see cref="MaxWorlds.Combat.Ricochet"/>'s own
    /// self-attach), and a MeshRenderer sitting under an IDamageable gets reskinned by
    /// <see cref="CharacterSkinDirector"/>'s one-time sweep -- the exact bug <see cref="AimReticle"/>'s
    /// own doc comment describes. LineRenderer isn't a MeshRenderer, so it's never swept at all, which is
    /// simpler here than unparenting and hand-tracking the owner every frame the way AimReticle has to.
    /// </summary>
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class RicochetAimPreviewVfx : MonoBehaviour
    {
        private const float LineWidth = 0.05f;
        private const float RingDiameter = 0.4f;
        private const float MarkerDiameter = 0.3f;
        private const float MarkerHeight = 1.0f;
        private const int RingSegments = 12;

        /// <summary>Disc blue at 60% alpha -- the ticket's own authored style, not picked.</summary>
        private static readonly Color LineColor = new Color(0.35f, 0.65f, 1.0f, 0.6f);
        private static readonly Color RingColor = new Color(0.35f, 0.65f, 1.0f, 0.9f);

        private LineRenderer _line;
        private LineRenderer _ring1;
        private LineRenderer _ring2;
        private LineRenderer _marker;
        private bool _built;

        /// <summary>Reused every <see cref="Refresh"/> call -- no per-frame allocation (AC2).</summary>
        private readonly Vector3[] _points = new Vector3[3];

        /// <summary>Self-attach onto <paramref name="owner"/> -- the same shape
        /// <see cref="MaxWorlds.Combat.Ricochet"/> itself is attached onto the player with. Deliberately
        /// does NOT build the line/rings yet (see <see cref="Refresh"/>) -- most of a run is never spent
        /// holding RICOCHET's aim stick, so most players never pay for this at all, and resolving
        /// <see cref="VfxMaterials"/> here would do it from inside <see cref="MaxWorlds.Combat.Ricochet.Awake"/>,
        /// which an EditMode test can invoke directly without the one-time shader-resolve log guard
        /// <see cref="RicochetDisc.Spawn"/>'s own callers already have to wrap.</summary>
        public static RicochetAimPreviewVfx Attach(Transform owner)
        {
            var go = new GameObject("RicochetAimPreview");
            go.transform.SetParent(owner, false);
            return go.AddComponent<RicochetAimPreviewVfx>();
        }

        private void Build()
        {
            Material mat = VfxMaterials.Additive(VfxMaterials.Glow());

            _line = NewLineRenderer("Line", mat, LineWidth, LineColor);
            _line.useWorldSpace = true;
            _line.positionCount = 3;

            _ring1 = NewRing("Ring1", mat, RingDiameter);
            _ring2 = NewRing("Ring2", mat, RingDiameter);
            _marker = NewRing("BendTarget", mat, MarkerDiameter);
        }

        private LineRenderer NewLineRenderer(string name, Material mat, float width, Color color)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.widthMultiplier = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = mat;
            lr.startColor = color;
            lr.endColor = color;
            return lr;
        }

        /// <summary>A small flat ring, local-space, so dropping it on a world point is just setting the
        /// GameObject's own <see cref="Transform.position"/> -- the same idiom <see cref="LockBracketVfx"/>
        /// uses for its corner brackets.</summary>
        private LineRenderer NewRing(string name, Material mat, float diameter)
        {
            LineRenderer lr = NewLineRenderer(name, mat, LineWidth * 0.6f, RingColor);
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = RingSegments;
            float radius = diameter * 0.5f;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * (360f / RingSegments) * Mathf.Deg2Rad;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius));
            }
            return lr;
        }

        /// <summary>Recompute and show (or hide) the whole preview for this frame -- called every
        /// <see cref="MaxWorlds.Combat.Ricochet.Tick"/>, not a separate Update, so visibility tracks the
        /// exact same frame the weapon itself decides to aim or not.</summary>
        public void Refresh(bool visible, Vector3 origin, Vector3 direction, float radius,
            float maxLegDistance, float turnRateDegPerSec, float lookaheadMetres)
        {
            if (!visible) { if (_built) SetVisible(false); return; }
            if (!_built) { Build(); _built = true; }
            SetVisible(true);

            int pointCount = RicochetDisc.ComputePreviewLegs(origin, direction, radius, maxLegDistance,
                _points, out int bounceCount);

            _line.positionCount = pointCount;
            for (int i = 0; i < pointCount; i++) _line.SetPosition(i, _points[i]);

            SetRingAt(_ring1, bounceCount >= 1, _points[1]);
            SetRingAt(_ring2, pointCount > 2 && bounceCount >= 2, pointCount > 2 ? _points[2] : Vector3.zero);

            RobotEnemy bendTarget = turnRateDegPerSec > 0f
                ? RicochetDisc.FindSteerCandidate(origin, direction, lookaheadMetres)
                : null;
            SetRingAt(_marker, bendTarget != null,
                bendTarget != null ? bendTarget.transform.position + Vector3.up * MarkerHeight : Vector3.zero);
        }

        private static void SetRingAt(LineRenderer ring, bool show, Vector3 point)
        {
            ring.gameObject.SetActive(show);
            if (show) ring.transform.position = point;
        }

        private void SetVisible(bool visible)
        {
            _line.gameObject.SetActive(visible);
            if (!visible)
            {
                _ring1.gameObject.SetActive(false);
                _ring2.gameObject.SetActive(false);
                _marker.gameObject.SetActive(false);
            }
        }
    }
}
