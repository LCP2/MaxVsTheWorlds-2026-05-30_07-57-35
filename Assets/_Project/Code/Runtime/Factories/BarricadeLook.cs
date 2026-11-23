using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// MV-1419: the one look every City destructible cover piece wears — a footprint tiled into 1 m
    /// barricade modules (orange top with white diagonal stripes, darker orange front, dark bolt plate),
    /// the crack/chip damage stages, and the hit/break effects. Owned by one <see cref="DestructibleCover"/>;
    /// every transient effect is world-rooted (a scaled map parent would distort it) and is destroyed with
    /// its owner.
    /// </summary>
    public sealed class BarricadeLook
    {
        private static readonly Color TopColor = new Color32(0xF0, 0x6E, 0x14, 255);     // #F06E14
        private static readonly Color FrontColor = new Color32(0x96, 0x3C, 0x0A, 255);   // darker orange front
        private static readonly Color StripeColor = Color.white;
        private static readonly Color PlateColor = new Color32(0x26, 0x26, 0x28, 255);   // dark bolt plate
        private static readonly Color CrackColor = new Color32(0x14, 0x0C, 0x08, 255);
        private static readonly Color ChipHoleColor = new Color32(0x2A, 0x26, 0x24, 255);
        private static readonly Color DustColor = new Color(0.55f, 0.55f, 0.53f);

        private const float TopThickness = 0.05f;
        private const float StageOneNormalized = 0.66f;
        private const float StageTwoNormalized = 0.33f;
        public const float FlashSeconds = 0.08f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private sealed class Module
        {
            public Transform Root;
            public Renderer Top, Front;
            public readonly List<Renderer> Cracks1 = new List<Renderer>(2);
            public readonly List<Renderer> Cracks2 = new List<Renderer>(2);
            public readonly List<Renderer> Chips = new List<Renderer>(2);
            public float Size, Height;
        }

        private readonly List<Module> _modules = new List<Module>(8);
        private readonly List<GameObject> _fx = new List<GameObject>(24);
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private Transform _root;
        private float _flashLeft;
        private int _stage;

        public int ModuleCount => _modules.Count;

        /// <summary>The first module top's resolved colour as last pushed through its property block.</summary>
        public Color ModuleTopResolvedColor
        {
            get
            {
                if (_modules.Count == 0 || _modules[0].Top == null) return Color.clear;
                var mpb = new MaterialPropertyBlock();
                _modules[0].Top.GetPropertyBlock(mpb);
                return mpb.GetColor(BaseColorId);
            }
        }

        /// <summary>True when every module carries at least one active crack or chip renderer.</summary>
        public bool EveryModuleShowsDamage
        {
            get
            {
                if (_modules.Count == 0) return false;
                foreach (Module m in _modules)
                {
                    bool any = false;
                    foreach (Renderer r in m.Cracks1) any |= r != null && r.gameObject.activeSelf;
                    foreach (Renderer r in m.Cracks2) any |= r != null && r.gameObject.activeSelf;
                    foreach (Renderer r in m.Chips) any |= r != null && r.gameObject.activeSelf;
                    if (!any) return false;
                }
                return true;
            }
        }

        public bool HasLiveFx
        {
            get
            {
                foreach (GameObject g in _fx) if (g != null) return true;
                return false;
            }
        }

        /// <summary>Tile the footprint of <paramref name="body"/> (authored <paramref name="size"/> w x h x d) into barricade modules.</summary>
        public void Build(Transform body, Vector3 size, bool cylinder)
        {
            if (_root != null) return;

            // The footprint comes from the authored size, not the body's scale: a box body's true size lives
            // in its chamfered mesh and collider (MapRuntime.ApplyBevelledBoxMesh resets its scale to one).
            float w = size.x, d = size.z, h = size.y;
            _root = ParentScale.MakeMetreSpace(new GameObject("Barricade").transform, body);
            _root.gameObject.AddComponent<KeepsOwnMaterial>();

            int nx = cylinder ? 1 : Mathf.Max(1, Mathf.RoundToInt(w));
            int nz = cylinder ? 1 : Mathf.Max(1, Mathf.RoundToInt(d));
            float cx = w / nx, cz = d / nz;

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    BuildModule(new Vector3(-w * 0.5f + cx * (i + 0.5f), 0f, -d * 0.5f + cz * (j + 0.5f)), cx, cz, h, i * 7 + j);
        }

        private void BuildModule(Vector3 centre, float cx, float cz, float h, int seed)
        {
            var m = new Module { Height = h, Size = Mathf.Min(cx, cz) };
            var root = new GameObject("Module").transform;
            root.SetParent(_root, false);
            root.localPosition = centre;
            m.Root = root;

            float frontH = Mathf.Max(0.05f, h - TopThickness);
            m.Front = Part(root, "Front", new Vector3(0f, -TopThickness * 0.5f, 0f), new Vector3(cx, frontH, cz), Quaternion.identity, FrontColor);
            float topY = h * 0.5f - TopThickness * 0.5f;
            m.Top = Part(root, "Top", new Vector3(0f, topY, 0f), new Vector3(cx, TopThickness, cz), Quaternion.identity, TopColor);

            float y = h * 0.5f + 0.004f;
            float len = m.Size * 1.2f, flank = m.Size * 0.7f, off = m.Size * 0.3f;
            Part(root, "Stripe", new Vector3(0f, y, 0f), new Vector3(m.Size * 0.12f, 0.008f, len), Quaternion.Euler(0f, 45f, 0f), StripeColor);
            Vector3 perp = new Vector3(0.7071f, 0f, 0.7071f) * off;
            Part(root, "Stripe", new Vector3(perp.x, y, -perp.z), new Vector3(m.Size * 0.12f, 0.008f, flank), Quaternion.Euler(0f, 45f, 0f), StripeColor);
            Part(root, "Stripe", new Vector3(-perp.x, y, perp.z), new Vector3(m.Size * 0.12f, 0.008f, flank), Quaternion.Euler(0f, 45f, 0f), StripeColor);
            Part(root, "Plate", new Vector3(0f, y + 0.004f, 0f), new Vector3(m.Size * 0.26f, 0.008f, m.Size * 0.26f), Quaternion.identity, PlateColor);

            _modules.Add(m);
            _ = seed;
        }

        private Renderer Part(Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var rend = go.GetComponent<MeshRenderer>();
            Material mat = MaterialLibrary.Tinted(SurfaceKind.Stone, color);
            if (mat != null) rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(rend, color);
            return rend;
        }

        private void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        // ------------------------------------------------------------------ damage stages

        /// <summary>66% HP or below: cracks on every block; 33% or below: more cracks plus corner chips.</summary>
        public void SetStage(float normalized)
        {
            int stage = normalized <= StageTwoNormalized ? 2 : normalized <= StageOneNormalized ? 1 : 0;
            if (stage <= _stage) return;
            _stage = stage;

            for (int i = 0; i < _modules.Count; i++)
            {
                Module m = _modules[i];
                if (stage >= 1 && m.Cracks1.Count == 0) BuildCracks(m, m.Cracks1, i, 0);
                if (stage >= 2)
                {
                    if (m.Cracks2.Count == 0) BuildCracks(m, m.Cracks2, i, 1);
                    if (m.Chips.Count == 0) BuildChips(m, i);
                }
            }
        }

        private void BuildCracks(Module m, List<Renderer> into, int seed, int set)
        {
            float y = m.Height * 0.5f + 0.012f;
            for (int k = 0; k < 2; k++)
            {
                float a = ((seed * 37 + set * 61 + k * 83) % 180) * Mathf.Deg2Rad * 0.5f + set * 0.9f;
                var pos = new Vector3(Mathf.Cos(a * 2.3f) * m.Size * 0.22f, y, Mathf.Sin(a * 1.7f) * m.Size * 0.22f);
                Renderer r = Part(m.Root, "Crack", pos, new Vector3(0.025f, 0.006f, m.Size * 0.6f),
                    Quaternion.Euler(0f, a * Mathf.Rad2Deg * 2f + k * 70f, 0f), CrackColor);
                into.Add(r);
            }
        }

        private void BuildChips(Module m, int seed)
        {
            int count = 1 + (seed % 2); // 1-2 corner chips per block
            float y = m.Height * 0.5f + 0.01f;
            float c = m.Size * 0.5f;
            for (int k = 0; k < count; k++)
            {
                float sx = ((seed + k) % 2 == 0) ? 1f : -1f;
                float sz = (k == 0) ? 1f : -1f;
                m.Chips.Add(Part(m.Root, "Chip", new Vector3(sx * (c - 0.09f), y, sz * (c - 0.09f)),
                    new Vector3(0.18f, 0.012f, 0.18f), Quaternion.Euler(0f, 45f, 0f), ChipHoleColor));
            }
        }

        // ------------------------------------------------------------------ hit feedback

        public void Hit(Vector3 hitPoint)
        {
            _flashLeft = FlashSeconds;
            Tint(1f);
            Burst(hitPoint, Random.Range(6, 11), 0.15f, 1.8f, 0.25f, new[] { new Color(1f, 0.9f, 0.5f), Color.white }, 0.05f, false);
            Burst(hitPoint, Random.Range(3, 6), 0.5f, 1.0f, 0.5f, new[] { TopColor, Color.white }, 0.09f, true);
        }

        /// <summary>Advance the white flash.</summary>
        public void Tick(float dt)
        {
            if (_flashLeft <= 0f) return;
            _flashLeft -= dt;
            if (_flashLeft <= 0f) Tint(0f);
        }

        public bool Flashing => _flashLeft > 0f;

        private void Tint(float amount)
        {
            foreach (Module m in _modules)
            {
                SetColor(m.Top, Color.Lerp(TopColor, Color.white, amount));
                SetColor(m.Front, Color.Lerp(FrontColor, Color.white, amount));
            }
        }

        // ------------------------------------------------------------------ break

        public void Break(Vector3 centre)
        {
            Burst(centre + Vector3.up * 0.4f, Random.Range(12, 17), 1f, 3f, 0.6f, new[] { TopColor, FrontColor, Color.white }, 0.22f, true);

            GameObject dust = NewFx(PrimitiveType.Sphere, "Dust", centre + Vector3.up * 0.5f, new Vector3(0.3f, 0.3f, 0.3f), DustColor);
            if (dust != null) dust.AddComponent<DebrisDriver>().BeginDust(0.8f, 2.5f);
        }

        private void Burst(Vector3 origin, int count, float minReach, float maxReach, float life, Color[] colors, float size, bool gravity)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject g = NewFx(PrimitiveType.Cube, "Fx", origin, Vector3.one * size * Random.Range(0.7f, 1.2f), colors[i % colors.Length]);
                if (g == null) continue;
                Vector3 dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.9f + 0.2f;
                float reach = Random.Range(minReach, maxReach);
                g.AddComponent<DebrisDriver>().BeginDebris(dir.normalized, reach, life, gravity);
            }
        }

        private GameObject NewFx(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.AddComponent<KeepsOwnMaterial>();
            var rend = go.GetComponent<MeshRenderer>();
            Material mat = MaterialLibrary.Tinted(SurfaceKind.Stone, color);
            if (mat != null) rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(rend, color);
            _fx.RemoveAll(f => f == null);
            _fx.Add(go);
            return go;
        }

        public void Dispose()
        {
            foreach (GameObject g in _fx)
            {
                if (g == null) continue;
                if (Application.isPlaying) Object.Destroy(g); else Object.DestroyImmediate(g);
            }
            _fx.Clear();
        }

        /// <summary>Flies a chunk/spark out along a direction while it falls and shrinks away, or grows
        /// then shrinks a dust puff; destroys itself when its life ends.</summary>
        [MaxWorlds.Core.PerfSection("factories")]
        private sealed class DebrisDriver : MonoBehaviour
        {
            private Vector3 _start, _rest;
            private Vector3 _dir;
            private float _reach, _life, _t, _dustPeak;
            private bool _gravity, _dust;

            public void BeginDebris(Vector3 dir, float reach, float life, bool gravity)
            {
                _start = transform.position; _rest = transform.localScale;
                _dir = dir; _reach = reach; _life = life; _gravity = gravity;
            }

            public void BeginDust(float life, float peakDiameter)
            {
                _start = transform.position; _rest = transform.localScale;
                _life = life; _dust = true; _dustPeak = peakDiameter;
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float u = Mathf.Clamp01(_life > 0f ? _t / _life : 1f);
                if (_dust)
                {
                    float grow = Mathf.Clamp01(u / 0.4f);
                    float shrink = 1f - Mathf.Clamp01((u - 0.4f) / 0.6f);
                    transform.localScale = Vector3.one * (Mathf.Lerp(_rest.x, _dustPeak, 1f - (1f - grow) * (1f - grow)) * shrink);
                }
                else
                {
                    float eased = 1f - (1f - u) * (1f - u);
                    Vector3 p = _start + _dir * (_reach * eased);
                    if (_gravity) p.y -= 4f * _t * _t * 0.5f;
                    p.y = Mathf.Max(0.02f, p.y);
                    transform.position = p;
                    transform.localScale = _rest * (1f - u * u);
                }
                if (u >= 1f) Destroy(gameObject);
            }
        }
    }
}
