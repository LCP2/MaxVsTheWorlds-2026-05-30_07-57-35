using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A vertical, additive light shaft (MV-1131, the Weapon Core's own ground beacon) — a quad that
    /// rotates about world Y only to face the camera horizontally, so its authored height always reads
    /// as true world-space metres no matter which way it happens to face. The fixed top-down camera
    /// never yaws, so Y is the only billboard axis this ever needs; unlike <see cref="CameraFacingFlare"/>
    /// it deliberately does NOT match the camera's own pitch — doing that would tip the shaft over and
    /// shrink its resolved world height along with it.
    /// </summary>
    public sealed class LightPillar : MonoBehaviour
    {
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static LightPillar Create(string name)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;

            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            var pillar = quad.AddComponent<LightPillar>();
            pillar._renderer = quad.GetComponent<MeshRenderer>();
            pillar._mpb = new MaterialPropertyBlock();
            pillar._renderer.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            pillar._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pillar._renderer.receiveShadows = false;
            pillar.Hide();
            return pillar;
        }

        public bool Visible => gameObject.activeSelf;

        /// <summary>Stands the pillar on <paramref name="basePos"/>, <paramref name="width"/> m wide and
        /// <paramref name="height"/> m tall. Falls back to no rotation change with no live camera (a
        /// geometry-independent test/fixture with nothing built at all).</summary>
        public void Show(Vector3 basePos, float width, float height, Color color)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            Camera cam = Camera.main;
            Vector3 awayFromCamera = Vector3.forward;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - basePos;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-6f) awayFromCamera = -toCam.normalized;
            }

            transform.position = basePos + Vector3.up * (height * 0.5f);
            transform.rotation = Quaternion.LookRotation(awayFromCamera, Vector3.up);
            transform.localScale = new Vector3(width, height, 1f);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>The pillar's own resolved world-space bounds height — the renderer's actual
        /// transformed mesh extent, not the authored constant that produced it. Exact because
        /// <see cref="Show"/> only ever rotates about world Y, which never tips the local-up axis.</summary>
        public float ResolvedHeight => _renderer != null ? _renderer.bounds.size.y : 0f;

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
