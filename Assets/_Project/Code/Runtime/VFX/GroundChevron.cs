using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1125: a single flat, ground-hugging arrowhead — one link in the finale's own post-exit-beat
    /// trail from Max to the exit door. A real mesh (not a textured quad like <see cref="GroundRing"/>)
    /// since its own width is an authored, testable number (1.0 m, ticket's own figure) rather than a
    /// soft falloff radius.
    /// </summary>
    public sealed class GroundChevron : MonoBehaviour
    {
        private const float Width = 1.0f;
        private const float Length = 0.6f;
        private const float Lift = 0.02f;

        private static Mesh s_mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static GroundChevron Create(string name)
        {
            var go = new GameObject(name);
            var chevron = go.AddComponent<GroundChevron>();

            go.AddComponent<MeshFilter>().sharedMesh = SharedMesh();
            chevron._renderer = go.AddComponent<MeshRenderer>();
            chevron._mpb = new MaterialPropertyBlock();
            chevron._renderer.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            chevron._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            chevron._renderer.receiveShadows = false;
            chevron.Hide();
            return chevron;
        }

        /// <summary>A solid arrowhead, tip pointing local +Z (the direction <see cref="Show"/> aims at
        /// whatever world direction it's given), <see cref="Width"/> m wide — built once and shared by
        /// every chevron the trail lays down.</summary>
        private static Mesh SharedMesh()
        {
            if (s_mesh != null) return s_mesh;

            s_mesh = new Mesh { name = "GroundChevron" };
            s_mesh.vertices = new[]
            {
                new Vector3(0f, 0f, Length * 0.5f),
                new Vector3(-Width * 0.5f, 0f, -Length * 0.5f),
                new Vector3(Width * 0.5f, 0f, -Length * 0.5f),
            };
            s_mesh.triangles = new[] { 0, 1, 2 };
            s_mesh.uv = new[] { new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            s_mesh.RecalculateNormals();
            s_mesh.RecalculateBounds();
            return s_mesh;
        }

        public bool Visible => gameObject.activeSelf;

        /// <summary>Lays this chevron flat at <paramref name="groundPos"/>, its tip pointing along
        /// <paramref name="forwardXZ"/> (the Y component is ignored — this never tips off the ground).</summary>
        public void Show(Vector3 groundPos, Vector3 forwardXZ, Color color)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            Vector3 dir = new Vector3(forwardXZ.x, 0f, forwardXZ.z);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();

            transform.position = new Vector3(groundPos.x, groundPos.y + Lift, groundPos.z);
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

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
