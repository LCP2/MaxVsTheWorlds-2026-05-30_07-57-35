using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A flat, ground-hugging danger indicator (YT-53) — the thing that makes an incoming attack
    /// readable at the fixed ~72° camera.
    ///
    /// It's a single quad laid flat, not a particle: it has to sit still on the ground and hold a
    /// crisp edge, which is exactly what particles are bad at. Colour and alpha are driven through
    /// a MaterialPropertyBlock so every ring in the arena shares one material and one draw setup —
    /// at 20–30 enemies, a material instance per ring would be the perf spike the AC forbids.
    /// </summary>
    public sealed class GroundRing : MonoBehaviour
    {
        /// <summary>Default lift: clear of the ground plane, because co-planar with it, it z-fights.
        /// The danger telegraph keeps this height and therefore always draws OVER the always-on
        /// anchors (YT-85), which sit lower — if a ground anchor could cover a telegraph, the anchor
        /// would be hiding the one mark the player has to react to.</summary>
        public const float GroundLift = 0.03f;

        /// <summary>Height above the ground this ring draws at. Ground marks are coplanar quads, so
        /// what stacks on top of what is decided here and nowhere else.</summary>
        public float Lift { get; set; } = GroundLift;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Create a ground-hugging quad. <paramref name="texture"/> defaults to the filled
        /// danger disc (YT-53); pass another to make a different kind of mark.</summary>
        public static GroundRing Create(string name, Texture2D texture = null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;

            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);   // a telegraph must never be something you collide with
            }

            var ring = quad.AddComponent<GroundRing>();
            ring._renderer = quad.GetComponent<MeshRenderer>();
            ring._mpb = new MaterialPropertyBlock();
            ring._renderer.sharedMaterial =
                VfxMaterials.AlphaBlend(texture != null ? texture : VfxMaterials.Ring());
            ring._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring._renderer.receiveShadows = false;
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lie flat
            ring.Hide();
            return ring;
        }

        public bool Visible => gameObject.activeSelf;

        /// <summary>Place and tint the ring. <paramref name="radius"/> is in world units.</summary>
        public void Show(Vector3 groundPos, float radius, Color color)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            transform.position = new Vector3(groundPos.x, groundPos.y + Lift, groundPos.z);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_mpb);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
