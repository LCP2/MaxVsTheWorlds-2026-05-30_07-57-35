using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A thin, dashed floor ring (MV-1337) — <see cref="DashedRingMesh"/> placed and shown/hidden the
    /// same way <see cref="GroundRing"/> is, for a ring whose radius and tint are fixed for its whole
    /// life (see <see cref="GroundRing.CreateFixedTint"/>'s own doc for why a fixed-colour mark bakes
    /// its tint into the material instead of pushing it through a MaterialPropertyBlock every frame).
    /// </summary>
    public sealed class DashedGroundRing : MonoBehaviour
    {
        private MeshFilter _filter;
        private Mesh _mesh;
        private float _builtRadius = -1f;
        private float _builtLineWidth = -1f;
        private int _builtDashCount = -1;

        public static DashedGroundRing Create(string name, Color color)
        {
            var go = new GameObject(name);
            var ring = go.AddComponent<DashedGroundRing>();

            ring._filter = go.AddComponent<MeshFilter>();
            ring._mesh = new Mesh { name = name };
            ring._filter.sharedMesh = ring._mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = VfxMaterials.AlphaBlendTinted(VfxMaterials.Solid(), color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ring.Hide();
            return ring;
        }

        public bool Visible => gameObject.activeSelf;

        /// <summary>Place the ring and (re)build its mesh only when <paramref name="radius"/>,
        /// <paramref name="lineWidth"/> or <paramref name="dashCount"/> actually change — a cradle
        /// calls this every frame with the same three numbers, so rebuilding the mesh on every call
        /// would be pure churn for a ring that only ever moves.</summary>
        public void Show(Vector3 groundPos, float lift, float radius, float lineWidth, int dashCount)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.position = new Vector3(groundPos.x, groundPos.y + lift, groundPos.z);

            if (Mathf.Approximately(_builtRadius, radius) &&
                Mathf.Approximately(_builtLineWidth, lineWidth) &&
                _builtDashCount == dashCount)
                return;

            DashedRingMesh.BuildInto(_mesh, radius, lineWidth, dashCount);
            _builtRadius = radius;
            _builtLineWidth = lineWidth;
            _builtDashCount = dashCount;
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
