using UnityEngine;
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
    /// Colour-shifts from ready-cyan toward a warning red as the absorb budget runs out (MV-361:
    /// "obvious from peripheral vision... a colour shift as it decays"), driven every frame by
    /// <see cref="SetFraction"/> from <see cref="PlayerAbilities"/>.
    /// </summary>
    public sealed class ForceFieldBubble : MonoBehaviour
    {
        private static readonly Color FullColor = new Color(0.31f, 0.76f, 0.97f, 0.30f);   // ready cyan
        private static readonly Color EmptyColor = new Color(0.90f, 0.22f, 0.20f, 0.55f);  // warning red
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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

            _visual = vis.GetComponent<MeshRenderer>();
            _visual.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            _visual.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _visual.receiveShadows = false;
            _mpb = new MaterialPropertyBlock();
            SetFraction(1f);

            // Physics.autoSyncTransforms is off project-wide (see GateSolidityTests) — force a sync so
            // a robot's CharacterController.Move on this very frame already sees the new collider.
            Physics.SyncTransforms();
        }

        /// <summary>0 (about to pop) .. 1 (fresh) — drives the ready-cyan-to-warning-red shift.</summary>
        public void SetFraction(float fraction)
        {
            if (_visual == null) return;
            Color c = Color.Lerp(EmptyColor, FullColor, Mathf.Clamp01(fraction));
            _mpb.SetColor(BaseColorId, c);
            _visual.SetPropertyBlock(_mpb);
        }
    }
}
