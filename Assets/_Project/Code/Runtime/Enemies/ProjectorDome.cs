using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1284 point 5: a Shield Projector's own protective dome visual — a translucent white-blue
    /// hemisphere with a brighter rim, plus a thin beam from the Projector's own dish to the dome's
    /// apex. Built the same "ground a full sphere so only the upper half ever reads" shortcut
    /// <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>'s own energy-shield dome already uses — reusing
    /// its <c>MaxWorlds/ForceFieldShield</c> shader (Fresnel rim, falls back to a flat alpha-blended
    /// fill if the shader is unavailable) rather than authoring a second one, tinted white-blue via a
    /// <see cref="MaterialPropertyBlock"/> instead of that bubble's own blue-to-amber decay colours.
    ///
    /// Owned and repositioned every recompute by the <see cref="RobotEnemy"/> that projects it (see
    /// RobotEnemy's own TickProjectorDome) — NOT parented to it, since the dome's own centre is
    /// the pack's centroid, not the Projector's own position. Independent GameObject, same "VFX prop
    /// outlives a pooled owner's own reset" footing as <see cref="MoltenPatch"/>: <see cref="Collapse"/>
    /// detaches from its owner and keeps fading on its own for <see cref="CollapseSeconds"/> (point 4's
    /// "drops at once (0.3 s collapse VFX)") even if the Projector is pooled and re-<c>Apply</c>'d the
    /// very same frame it dies or is captured.
    /// </summary>
    [MaxWorlds.Core.PerfSection("projectordome")]
    public sealed class ProjectorDome : MonoBehaviour
    {
        /// <summary>Point 4's own "0.3 s collapse VFX".</summary>
        public const float CollapseSeconds = 0.3f;

        // Point 5's own "<=20% opacity fill" / "a brighter rim >=3 px" — translucent white-blue, a
        // noticeably brighter near-white rim, matching ForceFieldBubble's fill/rim split shape.
        private static readonly Color FillColor = new Color(0.72f, 0.88f, 1f, 0.18f);
        private static readonly Color RimColor = new Color(0.85f, 0.95f, 1f, 1f);
        private static readonly Color BeamColor = new Color(0.80f, 0.92f, 1f, 0.9f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        private MeshRenderer _domeRenderer;
        private MaterialPropertyBlock _mpb;
        private LineRenderer _beam;
        private float _radius;
        private bool _collapsing;
        private float _collapseRemaining;

        public static ProjectorDome Spawn(Vector3 center, float radius, Vector3 beamOrigin)
        {
            var go = new GameObject("ProjectorDome (stand-in)");
            var dome = go.AddComponent<ProjectorDome>();
            dome._mpb = new MaterialPropertyBlock();
            dome._radius = radius;
            dome.Build(radius);
            dome.Reposition(center, beamOrigin);
            return dome;
        }

        private void Build(float radius)
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.name = "Visual";
            var col = vis.GetComponent<Collider>();
            if (col != null)
            {
                // Pure visual — never a solid; a robot standing "inside" the dome must still walk
                // through it freely (point 2 is a damage rule, not a physical wall).
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            vis.transform.SetParent(transform, worldPositionStays: false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = Vector3.one * radius * 2f;

            // Same reason ForceFieldBubble's own visual carries this: anything driving its own
            // renderer colour through a MaterialPropertyBlock must carry SelfDrivenTint, or
            // RuntimeSurfaceDirector's sweep claims it within a frame and overwrites it with a
            // generic opaque world-prop material.
            vis.AddComponent<SelfDrivenTint>();

            _domeRenderer = vis.GetComponent<MeshRenderer>();
            _domeRenderer.sharedMaterial = DomeMaterial();
            _domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _domeRenderer.receiveShadows = false;

            var beamGo = new GameObject("Beam");
            beamGo.transform.SetParent(transform, worldPositionStays: false);
            _beam = beamGo.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.widthMultiplier = 0.025f;
            _beam.useWorldSpace = true;
            _beam.material = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            _beam.startColor = _beam.endColor = BeamColor;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;

            ApplyFraction(1f);
        }

        /// <summary>Re-centres the dome and re-aims its dish beam — called every
        /// RobotEnemy's own TickProjectorDome recompute while the dome is up. The sphere is
        /// grounded at the centre's own Y (always 0 — <see cref="RobotEnemy.DomeCenter"/> is always
        /// XZ-flattened) so only its upper half ever reads above the floor, same shortcut
        /// <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>'s own dome uses rather than building a true
        /// hemisphere mesh.</summary>
        public void Reposition(Vector3 center, Vector3 beamOrigin)
        {
            if (_collapsing) return;
            transform.position = center;
            gameObject.SetActive(true);
            if (_beam == null) return;
            _beam.SetPosition(0, beamOrigin);
            _beam.SetPosition(1, center + Vector3.up * _radius);
        }

        /// <summary>No robots left in range (point 1: "with no such robots, no dome") — hides the
        /// dome instantly so it can reappear the moment the pack drifts back in range, without paying
        /// for a fresh <see cref="Spawn"/>/<see cref="Collapse"/> round-trip. Distinct from
        /// <see cref="Collapse"/>: this is "temporarily nobody to protect", not "the Projector itself
        /// is gone" — no 0.3 s fade, point 4's collapse VFX is reserved for death/capture.</summary>
        public void HideImmediate()
        {
            if (!_collapsing) gameObject.SetActive(false);
        }

        /// <summary>Point 4: the Projector died or was captured — fades this dome out over
        /// <see cref="CollapseSeconds"/>, then destroys itself. Independent of its owner from this
        /// call onward (see the class doc comment for why).</summary>
        public void Collapse()
        {
            if (_collapsing) return;
            _collapsing = true;
            _collapseRemaining = CollapseSeconds;
            gameObject.SetActive(true);
        }

        /// <summary>The pooling-reset path (<see cref="RobotEnemy.Apply"/>) — a previous Projector
        /// life's dome must not leak into the next one, and this is a pooling reset, not a gameplay
        /// death, so no fade.</summary>
        public void DestroyImmediateSelf() => DestroySelf();

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Public and dt-parameterised, same "an EditMode test can drive it directly"
        /// reasoning as <see cref="MoltenPatch.Tick"/> — <c>Update()</c> never runs outside Play mode
        /// and this project authors no PlayMode tests.</summary>
        public void Tick(float dt)
        {
            if (!_collapsing) return;
            _collapseRemaining -= dt;
            float t = Mathf.Clamp01(_collapseRemaining / CollapseSeconds);
            ApplyFraction(t);
            if (_collapseRemaining <= 0f) DestroySelf();
        }

        private void ApplyFraction(float t)
        {
            if (_domeRenderer == null) return;
            Color fill = FillColor; fill.a *= t;
            Color rim = RimColor; rim.a *= t;
            _mpb.SetColor(BaseColorId, fill);
            _mpb.SetColor(RimColorId, rim);
            _domeRenderer.SetPropertyBlock(_mpb);
            if (_beam != null)
            {
                Color beam = BeamColor; beam.a *= t;
                _beam.startColor = _beam.endColor = beam;
            }
        }

        private void DestroySelf()
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>Same shader/fallback choice as <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>'s
        /// own dome material — a real Fresnel rim where available, a flat translucent fill (a lost
        /// rim, not a magenta or opaque sphere) where it isn't.</summary>
        private static Material DomeMaterial()
        {
            var shader = Shader.Find("MaxWorlds/ForceFieldShield");
            if (shader == null || !shader.isSupported)
                return VfxMaterials.AlphaBlend(VfxMaterials.Solid());

            return new Material(shader)
            {
                name = "ProjectorDome",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
