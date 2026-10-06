using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A flat additive flare that matches the live camera's own rotation exactly (MV-1131) — not just a
    /// Y-axis billboard like <see cref="LightPillar"/>, since this is a close, lens-scale flash meant to
    /// read as a flat disc square-on to the lens, the fixed top-down camera's pitch included. This is
    /// the fix for the class doc comment's own observation: the old Beat A flash was a flat
    /// <see cref="GroundRing"/> lying on the ground, "not facing the camera" at all.
    /// </summary>
    public sealed class CameraFacingFlare : MonoBehaviour
    {
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static CameraFacingFlare Create(string name)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;

            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            var flare = quad.AddComponent<CameraFacingFlare>();
            flare._renderer = quad.GetComponent<MeshRenderer>();
            flare._mpb = new MaterialPropertyBlock();
            flare._renderer.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            flare._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flare._renderer.receiveShadows = false;
            flare.Hide();
            return flare;
        }

        public bool Visible => gameObject.activeSelf;

        /// <summary>Place a <paramref name="diameter"/> m flat disc at <paramref name="pos"/>, rotated to
        /// match whichever camera is live exactly (identity with no live camera — a geometry-independent
        /// test/fixture with nothing built at all).</summary>
        public void Show(Vector3 pos, float diameter, Color color)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            Camera cam = Camera.main;
            transform.position = pos;
            transform.rotation = cam != null ? cam.transform.rotation : Quaternion.identity;
            transform.localScale = new Vector3(diameter, diameter, 1f);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>The flare's own resolved world-space bounds width — the renderer's actual transformed
        /// mesh extent along world X, not the authored constant that produced it. Exact for this game's
        /// fixed top-down camera: its rotation is a pure pitch (rotation about world X only), which never
        /// changes an extent measured along X.</summary>
        public float ResolvedDiameter => _renderer != null ? _renderer.bounds.size.x : 0f;

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
