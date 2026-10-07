using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1125: a flat, additive trapezoid lying on the arena floor from the finale's exit doorway —
    /// narrower at the door, widening as it runs into the room. A real mesh (its own near/far width and
    /// length are authored, testable numbers) rather than a <see cref="GroundRing"/> quad.
    /// </summary>
    public sealed class GroundWedge : MonoBehaviour
    {
        private const float Lift = 0.03f;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static GroundWedge Create(string name, float nearWidth, float farWidth, float length)
        {
            var go = new GameObject(name);
            var wedge = go.AddComponent<GroundWedge>();

            go.AddComponent<MeshFilter>().sharedMesh = BuildMesh(nearWidth, farWidth, length);
            wedge._renderer = go.AddComponent<MeshRenderer>();
            wedge._mpb = new MaterialPropertyBlock();
            wedge._renderer.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Solid());
            wedge._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wedge._renderer.receiveShadows = false;
            wedge.Hide();
            return wedge;
        }

        private static Mesh BuildMesh(float nearWidth, float farWidth, float length)
        {
            var mesh = new Mesh { name = "GroundWedge" };
            mesh.vertices = new[]
            {
                new Vector3(-nearWidth * 0.5f, 0f, 0f),
                new Vector3(nearWidth * 0.5f, 0f, 0f),
                new Vector3(farWidth * 0.5f, 0f, length),
                new Vector3(-farWidth * 0.5f, 0f, length),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public bool Visible => gameObject.activeSelf;

        /// <summary>Lays this wedge flat at <paramref name="basePos"/> (the doorway), running away from
        /// it along <paramref name="directionXZ"/> (the Y component is ignored).</summary>
        public void Show(Vector3 basePos, Vector3 directionXZ, Color color)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            Vector3 dir = new Vector3(directionXZ.x, 0f, directionXZ.z);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();

            transform.position = new Vector3(basePos.x, basePos.y + Lift, basePos.z);
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
