using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The Force Field's physical presence (MV-361): a solid, non-trigger sphere that follows Max and
    /// physically stops robots the same way <c>AreaGate</c>'s leaf collider stops a body — a plain
    /// <see cref="Collider"/> on a GameObject that is deliberately NOT itself a
    /// <see cref="CharacterController"/>, so <c>RobotEnemy.OnControllerColliderHit</c> treats it as a
    /// wall to slide around (<c>ObstacleSteering</c>) rather than "a character to walk into" — the
    /// exact CC-skip that line explicitly carves out. Building this around a CharacterController
    /// instead would make robots pile straight into it rather than route around the edge.
    ///
    /// Ignores collision against Max's OWN CharacterController (<see cref="Physics.IgnoreCollision(Collider,Collider,bool)"/>)
    /// so activating the field never shoves Max himself out of his own bubble — everyone else still
    /// bounces off it. Damage absorption is handled separately by <see cref="PlayerAbilities"/>/
    /// <see cref="MaxWorlds.Player.PlayerHealth"/>; this component is the "robots can't walk through
    /// it" half only.
    ///
    /// MV-391 (16 Aug DECISION): the visual is a subtle, mostly-transparent BLUE/CYAN energy-shield
    /// dome with a hexagonal/faceted panel pattern and a glowing rim (a
    /// <c>MaxWorlds/ForceFieldShield</c>-shaded sphere — see that shader for the Fresnel rim and hex
    /// panelling), matching the SG2/SG3 reference look — not the opaque orange sphere that originally
    /// shipped, and not the plain white ring the first fix landed as (which still read as "a circle
    /// around Max" from the fixed top-down camera; the hex-panel seams are what break that read into
    /// a faceted 3D shell). The opaque-orange bug itself was never the colour values below; it was
    /// that this renderer carried no <see cref="SelfDrivenTint"/> marker, so
    /// <c>RuntimeSurfaceDirector</c>'s sweep (MV-350's fix, now catching a VFX prop MV-350 itself
    /// flagged as still outstanding) claimed it a frame after spawn and stamped it with a generic
    /// opaque world-prop material, hiding Max completely. The marker is what actually fixes that; the
    /// colours only fix what it looks like once the sweep leaves it alone.
    ///
    /// Colour-shifts from ready-blue toward a warning amber as the absorb budget runs out (MV-361:
    /// "obvious from peripheral vision... a colour shift as it decays"; the 16 Aug DECISION replaces
    /// the steady-state colour with blue/cyan but keeps the amber decay cue, since only the "subtle
    /// white" call — not the warning shift — was superseded), driven every frame by
    /// <see cref="SetFraction"/> from <see cref="PlayerAbilities"/>.
    /// </summary>
    public sealed class ForceFieldBubble : MonoBehaviour
    {
        // Fill: subtle and mostly transparent at all times — Max must read through the centre of the
        // bubble for its whole active duration, not just when fresh.
        private static readonly Color FullFillColor = new Color(0.22f, 0.5f, 1f, 0.14f);     // ready: subtle blue
        private static readonly Color EmptyFillColor = new Color(0.95f, 0.40f, 0.16f, 0.30f); // about to pop: warm

        // Rim: the glowing edge. Bright cyan when fresh, warming toward the same amber as the fill
        // as the field nears popping — the DECISION's "colour-shift-on-decay" cue, expressed on the
        // edge (and hex seams, driven by the same _RimColor in the shader) that actually reads at a
        // glance.
        private static readonly Color FullRimColor = new Color(0.55f, 0.9f, 1f, 1f);
        private static readonly Color EmptyRimColor = new Color(1f, 0.45f, 0.18f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        private MeshRenderer _visual;
        private MaterialPropertyBlock _mpb;

        /// <summary>The solid, non-trigger collider that blocks robot bodies.</summary>
        public SphereCollider Collider { get; private set; }

        /// <summary>Builds the bubble as a child of <paramref name="owner"/> — following Max
        /// automatically via the parent transform, no per-frame position copy needed.</summary>
        public void Init(Transform owner, CharacterController ownerCc, float radius)
        {
            transform.SetParent(owner, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            Collider = gameObject.AddComponent<SphereCollider>();
            Collider.radius = radius;
            // MV-378 precedent (AreaGate): a shield that exists to physically block bodies must be
            // solid, not a trigger, or a CharacterController passes straight through it.
            Collider.isTrigger = false;
            if (ownerCc != null) Physics.IgnoreCollision(Collider, ownerCc, true);

            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.name = "Visual";
            var visCollider = vis.GetComponent<Collider>();
            if (visCollider != null)
            {
                // The SphereCollider built above is the only solid this GameObject should carry —
                // Destroy() throws in EditMode (an EditMode test builds this without ever playing).
                if (Application.isPlaying) Destroy(visCollider);
                else DestroyImmediate(visCollider);
            }
            vis.transform.SetParent(transform, worldPositionStays: false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = Vector3.one * radius * 2f;

            // MV-350's own precedent for exactly this situation: anything that drives its own
            // renderer colour through a MaterialPropertyBlock must carry SelfDrivenTint, or
            // RuntimeSurfaceDirector's sweep claims it within a frame and overwrites it with a
            // generic opaque world-prop material — the "opaque orange sphere" bug (MV-391).
            vis.AddComponent<SelfDrivenTint>();

            _visual = vis.GetComponent<MeshRenderer>();
            _visual.sharedMaterial = ShieldMaterial();
            _visual.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _visual.receiveShadows = false;
            _mpb = new MaterialPropertyBlock();
            SetFraction(1f);

            // Physics.autoSyncTransforms is off project-wide (see GateSolidityTests) — force a sync so
            // a robot's CharacterController.Move on this very frame already sees the new collider.
            Physics.SyncTransforms();
        }

        /// <summary>0 (about to pop) .. 1 (fresh) — drives the ready-white-to-warning-amber shift.</summary>
        public void SetFraction(float fraction)
        {
            if (_visual == null) return;
            float t = Mathf.Clamp01(fraction);
            _mpb.SetColor(BaseColorId, Color.Lerp(EmptyFillColor, FullFillColor, t));
            _mpb.SetColor(RimColorId, Color.Lerp(EmptyRimColor, FullRimColor, t));
            _visual.SetPropertyBlock(_mpb);
        }

        /// <summary>The shield's material — a real, view-dependent Fresnel rim
        /// (<c>MaxWorlds/ForceFieldShield</c>-shaded) so the bubble glows at its edge from any angle
        /// rather than reading as a flat coloured disc. Falls back to a plain alpha-blended fill if
        /// the hand-written shader is unavailable — a lost rim is a cosmetic regression, a magenta or
        /// opaque sphere is the bug this ticket exists to kill.</summary>
        private static Material ShieldMaterial()
        {
            var shader = Shader.Find("MaxWorlds/ForceFieldShield");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[ForceFieldBubble] 'MaxWorlds/ForceFieldShield' unavailable; " +
                                 "the shield falls back to a flat translucent fill (no rim).");
                return VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            }

            return new Material(shader)
            {
                name = "ForceFieldShield",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
