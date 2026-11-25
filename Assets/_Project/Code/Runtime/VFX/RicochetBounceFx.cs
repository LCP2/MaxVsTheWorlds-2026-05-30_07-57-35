using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1451 bounce flash: at a RICOCHET bounce (POWER 4+) a short cyan flash at the contact point (at
    /// most 1.0 m across, 0.12 s) plus a few small arcs that grab the surface. Self-destroying, pure
    /// presentation.
    /// </summary>
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class RicochetBounceFx : MonoBehaviour
    {
        public const float Seconds = 0.12f;
        public const float MaxFlashDiameter = 1.0f;
        private static readonly Color FlashColor = new Color(0.45f, 0.85f, 1f);

        private readonly List<JaggedBolt> _bolts = new List<JaggedBolt>();
        private readonly List<Vector3> _starts = new List<Vector3>();
        private readonly List<Vector3> _dirs = new List<Vector3>();
        private Transform _flash;
        private float _elapsed;
        private int _countdown;

        public static int ArcCountForPower(int power) =>
            power < 4 ? 0 : power <= 5 ? 1 : power == 6 ? 2 : power == 7 ? 3 : 4;

        public static RicochetBounceFx Spawn(Vector3 point, Vector3 normal, int power)
        {
            int arcs = ArcCountForPower(power);
            if (arcs == 0) return null;

            var go = new GameObject("RicochetBounceFx");
            go.transform.position = point;
            var fx = go.AddComponent<RicochetBounceFx>();

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Flash";
            var col = sphere.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
            sphere.transform.SetParent(go.transform, false);
            sphere.GetComponent<MeshRenderer>().sharedMaterial = VfxMaterials.AdditiveTinted(FlashColor);
            fx._flash = sphere.transform;
            fx._flash.localScale = Vector3.one * MaxFlashDiameter;

            normal.y = 0f;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.forward;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            for (int i = 0; i < arcs; i++)
            {
                var bolt = new JaggedBolt(go.transform, "SurfaceArc" + i, true, 0.08f, 0.025f);
                bolt.SetActive(true);
                fx._bolts.Add(bolt);
                float sign = i % 2 == 0 ? 1f : -1f;
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * (tangent * sign);
                fx._starts.Add(point);
                fx._dirs.Add(dir);
            }
            fx.Rerandomize();
            return fx;
        }

        private void Rerandomize()
        {
            _countdown = Random.Range(2, 4);
            for (int i = 0; i < _bolts.Count; i++)
            {
                Vector3 end = _starts[i] + _dirs[i] * Random.Range(0.3f, 0.5f);
                _bolts[i].Randomize(_starts[i], end, 0.05f);
            }
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= Seconds) { Destroy(gameObject); return; }
            if (_flash != null) _flash.localScale = Vector3.one * (MaxFlashDiameter * (1f - _elapsed / Seconds));
            if (--_countdown <= 0) Rerandomize();
        }
    }
}
