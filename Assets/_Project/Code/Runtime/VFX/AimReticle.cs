using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Draws the gadget's reach on the ground in front of Max (YT-84) — where he's pointing, and how
    /// far it actually gets.
    ///
    /// The Water Blaster has always been a 6 m cone and the player has never been told. You learned
    /// your range by walking into a robot and watching nothing happen, which is a tutorial made of
    /// deaths. This is the same information, drawn where the player is already looking.
    ///
    /// Two states, not two indicators. Idle, it's a whisper — just enough that reach is ALWAYS
    /// legible, so range is something you know rather than something you test. Aiming, it comes up
    /// to full. Same mesh, same material; only the alpha moves, so there is nothing to pop.
    ///
    /// It lives at the BOTTOM of the ground-mark stack, under the contact shadows, the anchor rings
    /// and the danger telegraphs. That ordering is not cosmetic: this is the biggest mark on the
    /// field by far, and the Craft Bible is explicit that juice must never obscure a threat. An
    /// enemy's anchor and its wind-up tell both have to be able to draw straight through it.
    ///
    /// It is drawn in Max's own colour, taken from his ground anchor rather than picked — see
    /// <see cref="Tint"/> for why that reference matters more than it looks.
    ///
    /// Unparented, and driven in LateUpdate — the same reason as <see cref="GroundAnchorVfx"/>:
    /// <see cref="CharacterSkinDirector"/> repaints every MeshRenderer under an IDamageable each
    /// frame, and Max is one, so a reticle childed to him would have its material silently
    /// overwritten with his skin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AimReticle : MonoBehaviour
    {
        /// <summary>Below the contact shadow (0.012), which is below the anchor ring (0.020), which
        /// is below the danger telegraph (0.030). Bottom of the stack — see the class remarks.</summary>
        public const float GroundLift = 0.006f;

        /// <summary>
        /// Max's colour — taken from his ground anchor rather than chosen, and that is deliberate.
        ///
        /// This reticle was written cyan, and cyan was right for about four hours. YT-85 gave the
        /// ground rings one team axis (cyan = you) and YT-86 gave the BODIES the opposite one (Max
        /// warm, the swarm cold); each was coherent alone, and together they haloed every actor in
        /// the other side's colour. YT-87 had to unpick it. A hardcoded cyan here would have walked
        /// straight back into that: Max's aim cone drawn in what is now the ENEMY colour.
        ///
        /// So this is not a colour, it's a reference. Whatever Max's ring is, his reach is drawn in
        /// it, and the two cannot drift apart again — because there is only one of them.
        /// </summary>
        public static Color Tint => GroundAnchorTuning.PlayerRing;

        /// <summary>Alpha while the trigger is idle: legible, ignorable. You should be able to forget
        /// it's there and still know your reach.</summary>
        public const float IdleAlpha = 0.16f;

        /// <summary>Alpha while aiming.</summary>
        public const float AimingAlpha = 0.42f;

        /// <summary>How fast it comes up and down. Fast enough to feel connected to the stick, slow
        /// enough that flicking the aim doesn't strobe the lawn.</summary>
        private const float FadeSpeed = 9f;

        private Transform _owner;
        private GameObject _quadGo;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private float _alpha;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Current drawn alpha — a test can read what the player would actually see.</summary>
        public float Alpha => _alpha;

        /// <summary>
        /// Build the reticle from the gadget's REAL numbers. Callers pass what the weapon actually
        /// does, never a shape someone liked the look of — that's the ticket's whole point, and it's
        /// what makes a future Beam or Lob draw itself correctly for free.
        /// </summary>
        public void Init(Transform owner, float range, float coneHalfAngle)
        {
            _owner = owner;

            if (_quadGo == null)
            {
                _quadGo = new GameObject("AimReticle");

                // Unparenting dodges CharacterSkinDirector (see the class remarks) — and walks
                // straight into RuntimeSurfaceDirector, which sweeps every LOOSE renderer and paints
                // it as scenery. It claimed the reticle and swapped this transparent material for the
                // OPAQUE ground one, so the reach cone was drawn as a wedge of grass lying 6 mm above
                // the grass: perfectly invisible, alpha meaningless, and no error anywhere. The whole
                // feature shipped and never drew a pixel. This is the marker that says "the material
                // is mine" — the same one the imported art uses.
                _quadGo.AddComponent<KeepsOwnMaterial>();

                _quadGo.AddComponent<MeshFilter>();
                _renderer = _quadGo.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _mpb = new MaterialPropertyBlock();
            }

            _quadGo.GetComponent<MeshFilter>().sharedMesh =
                AimReticleMesh.Build(range, coneHalfAngle);

            _alpha = IdleAlpha;   // it's already on when the run starts; it doesn't fade in
        }

        /// <summary>Aiming or not. Cosmetic only — this never gates a shot.</summary>
        public void SetAiming(bool aiming) => _target = aiming ? AimingAlpha : IdleAlpha;
        private float _target = IdleAlpha;

        private void LateUpdate()
        {
            if (_owner == null || _quadGo == null) return;

            // Rate in alpha-units per second, so the full idle→aiming travel takes ~1/FadeSpeed s
            // regardless of what those two alphas happen to be tuned to.
            float rate = (AimingAlpha - IdleAlpha) * FadeSpeed;
            _alpha = Mathf.MoveTowards(_alpha, _target, rate * Time.deltaTime);

            // Flatten onto the lawn and yaw to face where Max is pointing. Max's origin is his
            // capsule's centre, a metre off the ground — a mark that inherited that would float.
            Vector3 p = _owner.position;
            _quadGo.transform.position = new Vector3(p.x, GroundLift, p.z);

            Vector3 f = _owner.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f)
                _quadGo.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);

            var c = Tint; c.a = _alpha;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _renderer.SetPropertyBlock(_mpb);
        }

        private void OnDestroy()
        {
            if (_quadGo != null) Destroy(_quadGo);
        }
    }
}
