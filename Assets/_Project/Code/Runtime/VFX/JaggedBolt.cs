using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1451: one flickering electric bolt - a wide pale-cyan (#BFEFFF) additive glow line with a thin
    /// white core inside it, both on the same jagged vertex path. Used for RICOCHET's rim crackle,
    /// lightning trail, bounce arcs and the hit frazzle. Callers re-randomise it every 2-3 frames.
    /// </summary>
    public sealed class JaggedBolt
    {
        public static readonly Color GlowColor = new Color(0.749f, 0.937f, 1f, 0.75f); // #BFEFFF
        public static readonly Color CoreColor = new Color(1f, 1f, 1f, 1f);

        public const int Segments = 6;

        private readonly LineRenderer _glow;
        private readonly LineRenderer _core;
        private readonly Vector3[] _path = new Vector3[Segments + 1];

        public GameObject Root { get; }
        public bool Active => Root != null && Root.activeSelf;

        public JaggedBolt(Transform parent, string name, bool worldSpace, float glowWidth, float coreWidth)
        {
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            _glow = Line("Glow", worldSpace, glowWidth, GlowColor);
            _core = Line("Core", worldSpace, coreWidth, CoreColor);
            Root.SetActive(false);
        }

        private LineRenderer Line(string name, bool worldSpace, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = Segments + 1;
            line.widthMultiplier = width;
            line.useWorldSpace = worldSpace;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        public void SetActive(bool on)
        {
            if (Root != null && Root.activeSelf != on) Root.SetActive(on);
        }

        /// <summary>A jagged line a -> b (endpoints exact, interior vertices pushed sideways by up to
        /// <paramref name="jitter"/> metres, re-rolled on every call). <paramref name="sideAxis"/> is the
        /// axis the jitter is pushed along (up for the flat disc plane seen from above).</summary>
        public void Randomize(Vector3 a, Vector3 b, float jitter)
        {
            Vector3 axis = b - a;
            Vector3 side = Vector3.Cross(axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.forward, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            for (int i = 0; i <= Segments; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)i / Segments);
                if (i > 0 && i < Segments) p += side * Random.Range(-jitter, jitter);
                _path[i] = p;
            }
            Apply();
        }

        /// <summary>Samples an externally-built polyline (<paramref name="points"/> has Segments+1 entries).</summary>
        public void SetPath(IList<Vector3> points)
        {
            for (int i = 0; i <= Segments; i++) _path[i] = points[i];
            Apply();
        }

        private void Apply()
        {
            for (int i = 0; i <= Segments; i++)
            {
                _glow.SetPosition(i, _path[i]);
                _core.SetPosition(i, _path[i]);
            }
        }
    }
}
