using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// MV-1417: every transient effect a <see cref="Canister"/> plays — hit sparks, the fuse's spark
    /// fountain and orange glow, and the explosion (fireball, shockwave ring, red debris, smoke).
    /// A plain class, not a MonoBehaviour: <see cref="Canister.Tick"/> drives <see cref="Tick"/> (the same
    /// "Update calls it with Time.deltaTime, a test calls it directly" idiom the canister's fuse uses), so
    /// nothing here needs Play mode to advance. Pieces are plain primitives on shared additive / alpha
    /// materials (<see cref="VfxMaterials"/>), animated by scale and a MaterialPropertyBlock colour — no
    /// per-piece material, no particle system.
    /// </summary>
    internal sealed class CanisterFx
    {
        // --- Ticket numbers ---
        public const float HitStreakLife = 0.2f;            // "gone within 0.25 s"
        private const float FireballStartRadius = 0.45f;   // MV-1421: fireball scaled 1.5x
        public const float FireballMaxRadius = 3.75f;
        private const float FireballGrowTime = 0.2f;
        private const float FireballLife = 0.7f;
        // MV-1421: the ring of fire - 0 -> blast radius at 15 m/s (0.4 s for 6 m), fading over a further 0.3 s.
        public const float RingSpeed = 15f;
        private const float RingFadeTime = 0.3f;
        private const float BandLeading = 0.15f;            // white-yellow leading edge
        private const float BandMiddle = 0.20f;             // orange
        private const float BandTrailing = 0.25f;           // red trailing edge  (0.6 m of flame in all, >= 0.5 m)
        private const int BandSegments = 64;
        private const int TongueCount = 24;
        private const float ScorchFadeTime = 1f;
        private const float ScorchColourAlpha = 0.55f;
        private const float DebrisLife = 1.0f;
        private const float SmokeLife = 2.0f;
        private const float FountainRate = 70f;             // sparks per second while fusing
        private const float GlowDiameter = 2f;

        private enum Kind { Streak, Shard, Puff, Fireball, FireballEdge }

        private sealed class Piece
        {
            public Kind Kind;
            public GameObject Go;
            public Renderer Rend;
            public Vector3 Velocity;
            public float Gravity;
            public float Age;
            public float Life;
            public float Length;      // Streak: starting length
            public float Size;        // Shard/Puff: base size
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static readonly Color SparkColor = new Color(1f, 0.78f, 0.25f);
        private static readonly Color ShardColor = new Color(0.70f, 0.11f, 0.09f);
        private static readonly Color GlowColor = new Color(1f, 0.5f, 0.1f);
        private static readonly Color LeadingColor = new Color(1f, 0.95f, 0.6f);
        private static readonly Color MiddleColor = new Color(1f, 0.5f, 0.08f);
        private static readonly Color TrailingColor = new Color(0.75f, 0.1f, 0.04f);

        private readonly List<Piece> _pieces = new List<Piece>(48);
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        private GameObject _root;
        private GameObject _glow;
        private Renderer _glowRend;
        private float _glowT;
        private FlameBand[] _bands;
        private GameObject[] _tongues;
        private GroundRing _scorch;
        private Vector3 _ringOrigin;
        private float _ringAge;
        private float _ringMax;
        private float _tongueFlicker;
        private float _fountainBank;

        /// <summary>The live fireball (null until the canister explodes, and again once it is gone) —
        /// public to the Canister so an EditMode test can read its resolved radius.</summary>
        public GameObject Fireball { get; private set; }

        /// <summary>The fireball's resolved radius in metres (quad edge / 2), 0 when there is none.</summary>
        public float FireballRadius => Fireball != null ? Fireball.transform.localScale.x * 0.5f : 0f;

        public GameObject Root => _root;

        public bool Busy => _pieces.Count > 0 || _glow != null || _bands != null;

        /// <summary>Where the fx root hangs (the canister's own parent, so a level teardown takes it too).</summary>
        public Transform ParentHint { get; set; }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("CanisterFx");
            if (ParentHint != null) _root.transform.SetParent(ParentHint, false);
        }

        // ------------------------------------------------------------------ hit

        /// <summary>8-12 orange-yellow streaks, 0.3-0.7 m long, sprayed from the hit point and gone
        /// within <see cref="HitStreakLife"/>.</summary>
        public void HitSparks(Vector3 point)
        {
            int n = Random.Range(8, 13);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = Random.onUnitSphere;
                d.y = Mathf.Abs(d.y) * 0.8f + 0.2f;
                AddStreak(point, d.normalized * Random.Range(3f, 6f), Random.Range(0.3f, 0.7f), HitStreakLife, 0f);
            }
        }

        // ----------------------------------------------------------------- fuse

        public void BeginFuse(Vector3 top, Vector3 center)
        {
            EnsureRoot();
            if (_glow != null) return;
            _glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _glow.name = "FuseGlow";
            StripCollider(_glow);
            _glow.transform.SetParent(_root.transform, false);
            _glow.transform.position = center;
            _glowRend = _glow.GetComponent<Renderer>();
            _glowRend.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            Cosmetic(_glowRend);
            _glowT = 0f;
        }

        /// <summary>Continuous spark fountain from <paramref name="top"/> plus the pulsing glow.</summary>
        public void FuseTick(float dt, Vector3 top)
        {
            _fountainBank += dt * FountainRate;
            while (_fountainBank >= 1f)
            {
                _fountainBank -= 1f;
                Vector3 d = new Vector3(Random.Range(-0.35f, 0.35f), 1f, Random.Range(-0.35f, 0.35f)).normalized;
                AddStreak(top, d * Random.Range(4f, 6.5f), Random.Range(0.2f, 0.4f), Random.Range(0.35f, 0.55f), 9f);
            }

            if (_glow == null) return;
            _glowT += dt;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_glowT * 16f);
            float s = GlowDiameter * Mathf.Lerp(0.9f, 1.1f, pulse);
            _glow.transform.localScale = new Vector3(s, s, 1f);
            Face(_glow.transform);
            float a = Mathf.Lerp(0.5f, 1f, pulse);
            SetColor(_glowRend, new Color(GlowColor.r * a, GlowColor.g * a, GlowColor.b * a, a));
        }

        // ------------------------------------------------------------ explosion

        public void Explode(Vector3 point, float blastRadius)
        {
            EnsureRoot();
            if (_glow != null) { Kill(_glow); _glow = null; _glowRend = null; }

            // Fireball: additive billboard + a dark smoke edge behind it.
            Vector3 fbPos = point + Vector3.up * 0.9f;   // lifted so the 2.5 m ball is not sliced by the floor
            Fireball = NewBillboard("Fireball", VfxMaterials.Additive(VfxMaterials.Glow()), fbPos);
            Piece core = new Piece { Kind = Kind.Fireball, Go = Fireball, Rend = Fireball.GetComponent<Renderer>(), Life = FireballLife };
            _pieces.Add(core);
            GameObject edge = NewBillboard("FireballSmokeEdge", VfxMaterials.AlphaBlend(VfxMaterials.Glow()), fbPos);
            _pieces.Add(new Piece { Kind = Kind.FireballEdge, Go = edge, Rend = edge.GetComponent<Renderer>(), Life = FireballLife });
            ApplyFireball(core);

            // Ring of fire: three nested additive bands (white-yellow, orange, red) plus flame tongues.
            _ringOrigin = point;
            _ringMax = blastRadius;
            _ringAge = 0f;
            _tongueFlicker = 0f;
            Material solid = VfxMaterials.Additive(VfxMaterials.Solid());
            _bands = new FlameBand[3];
            for (int b = 0; b < 3; b++) _bands[b] = new FlameBand(_root.transform, "CanisterFireRing" + b, solid, _mpb);
            Material tongueMat = VfxMaterials.AdditiveTinted(MiddleColor);
            _tongues = new GameObject[TongueCount];
            for (int k = 0; k < TongueCount; k++)
            {
                GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                t.name = "FireTongue";
                StripCollider(t);
                t.transform.SetParent(_root.transform, false);
                Renderer tr = t.GetComponent<Renderer>();
                tr.sharedMaterial = tongueMat;
                Cosmetic(tr);
                _tongues[k] = t;
            }
            // Scorch tint on the floor inside the ring, fading in 1 s.
            _scorch = GroundRing.Create("CanisterScorchTint", VfxMaterials.Splat(), additive: false);
            _scorch.transform.SetParent(_root.transform, true);
            UpdateRing(0f);

            // Debris: 10-14 red shards thrown 2-4 m, falling, gone by 1.0 s.
            int shards = Random.Range(10, 15);
            for (int i = 0; i < shards; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float dist = Random.Range(2f, 4f);
                Vector3 horizontal = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (dist / 0.55f);
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Debris";
                StripCollider(go);
                go.transform.SetParent(_root.transform, false);
                go.transform.position = point;
                float size = Random.Range(0.1f, 0.2f);
                go.transform.localScale = new Vector3(size, size * 0.35f, size * 0.7f);
                go.transform.rotation = Random.rotation;
                Renderer r = go.GetComponent<Renderer>();
                r.sharedMaterial = VfxMaterials.AdditiveTinted(ShardColor);
                Cosmetic(r);
                _pieces.Add(new Piece { Kind = Kind.Shard, Go = go, Rend = r, Life = DebrisLife, Gravity = 14f, Size = size,
                    Velocity = horizontal + Vector3.up * Random.Range(4f, 6f) });
            }

            // Smoke: 2-3 grey puffs rising 1 m over 2 s.
            int puffs = Random.Range(2, 4);
            for (int i = 0; i < puffs; i++)
            {
                Vector3 start = point + new Vector3(Random.Range(-0.4f, 0.4f), 0.3f, Random.Range(-0.4f, 0.4f));
                GameObject go = NewBillboard("Smoke", VfxMaterials.AlphaBlend(VfxMaterials.Glow()), start);
                _pieces.Add(new Piece { Kind = Kind.Puff, Go = go, Rend = go.GetComponent<Renderer>(), Life = SmokeLife,
                    Velocity = new Vector3(0f, 0.5f, 0f), Size = Random.Range(0.7f, 1.0f) });
            }
        }

        // ----------------------------------------------------------------- tick

        public void Tick(float dt)
        {
            if (_bands != null)
            {
                _ringAge += dt;
                UpdateRing(dt);
                if (_ringAge >= _ringMax / RingSpeed + RingFadeTime) DisposeRing();
            }

            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                Piece p = _pieces[i];
                p.Age += dt;
                if (p.Go == null || p.Age >= p.Life)
                {
                    if (p.Go == Fireball) Fireball = null;
                    if (p.Go != null) Kill(p.Go);
                    _pieces.RemoveAt(i);
                    continue;
                }

                float t = p.Age / p.Life;
                Transform tr = p.Go.transform;
                switch (p.Kind)
                {
                    case Kind.Streak:
                    {
                        p.Velocity += Vector3.down * (p.Gravity * dt);
                        tr.position += p.Velocity * dt;
                        float len = p.Length * (1f - t);
                        tr.localScale = new Vector3(0.025f, 0.025f, Mathf.Max(0.001f, len));
                        if (p.Velocity.sqrMagnitude > 0.0001f) tr.rotation = Quaternion.LookRotation(p.Velocity);
                        break;
                    }
                    case Kind.Shard:
                    {
                        p.Velocity += Vector3.down * (p.Gravity * dt);
                        tr.position += p.Velocity * dt;
                        tr.Rotate(540f * dt, 360f * dt, 0f);
                        float k = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                        tr.localScale = new Vector3(p.Size, p.Size * 0.35f, p.Size * 0.7f) * k;
                        break;
                    }
                    case Kind.Puff:
                    {
                        tr.position += p.Velocity * dt;
                        float s = p.Size * Mathf.Lerp(0.6f, 1.3f, t);
                        tr.localScale = new Vector3(s, s, 1f);
                        Face(tr);
                        float a = (t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f) * 0.55f;
                        SetColor(p.Rend, new Color(0.45f, 0.45f, 0.47f, a));
                        break;
                    }
                    case Kind.Fireball:
                    case Kind.FireballEdge:
                        ApplyFireball(p);
                        break;
                }
            }

            if (_root != null && !Busy) { Kill(_root); _root = null; }
        }

        /// <summary>Places the three bands and the tongues for the current ring age.</summary>
        private void UpdateRing(float dt)
        {
            float travel = _ringMax / RingSpeed;
            float radius = Mathf.Min(_ringMax, _ringAge * RingSpeed);
            float fade = _ringAge <= travel ? 1f : Mathf.Clamp01(1f - (_ringAge - travel) / RingFadeTime);
            const float boost = 1.4f;

            float outer = radius;
            float[] widths = { BandLeading, BandMiddle, BandTrailing };
            Color[] cols = { LeadingColor, MiddleColor, TrailingColor };
            for (int b = 0; b < 3; b++)
            {
                float inner = Mathf.Max(0f, outer - widths[b]);
                Color c = cols[b] * (fade * boost);
                c.a = fade;
                _bands[b].Set(_ringOrigin, inner, outer, c);
                outer = inner;
            }

            // Flickering flame tongues on the outer edge: re-rolled length/width ~20 times a second.
            _tongueFlicker -= dt;
            bool reroll = _tongueFlicker <= 0f;
            if (reroll) _tongueFlicker = 0.05f;
            bool show = radius > 0.2f && fade > 0.01f;
            for (int k = 0; k < _tongues.Length; k++)
            {
                GameObject t = _tongues[k];
                if (t == null) continue;
                if (t.activeSelf != show) t.SetActive(show);
                if (!show) continue;
                float ang = (k + 0.5f * (k % 2)) / _tongues.Length * Mathf.PI * 2f;
                if (reroll)
                {
                    float len = Random.Range(0.5f, 1.2f) * fade;
                    t.transform.localScale = new Vector3(Random.Range(0.2f, 0.4f), Random.Range(0.3f, 0.7f), Mathf.Max(0.01f, len));
                }
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float len2 = t.transform.localScale.z;
                t.transform.position = _ringOrigin + dir * (radius + len2 * 0.25f) + Vector3.up * 0.2f;
                t.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            if (_scorch != null)
            {
                float a = ScorchColourAlpha * Mathf.Clamp01(_ringAge / ScorchFadeTime);
                float hold = 1f - Mathf.Clamp01((_ringAge - travel - RingFadeTime) / ScorchFadeTime);
                _scorch.Show(_ringOrigin, Mathf.Max(0.05f, radius), new Color(0.06f, 0.04f, 0.03f, a * hold));
            }
        }

        private void DisposeRing()
        {
            if (_bands != null) foreach (FlameBand b in _bands) b.Dispose();
            _bands = null;
            if (_tongues != null) foreach (GameObject t in _tongues) if (t != null) Kill(t);
            _tongues = null;
            if (_scorch != null) Kill(_scorch.gameObject);
            _scorch = null;
        }

        /// <summary>One flat annulus mesh whose inner/outer radius is rewritten in place each frame (a fixed
        /// WORLD thickness, which a scaled quad cannot give), tinted through a MaterialPropertyBlock.</summary>
        private sealed class FlameBand
        {
            private const float Lift = 0.12f;
            private readonly GameObject _go;
            private readonly Mesh _mesh;
            private readonly MeshRenderer _rend;
            private readonly MaterialPropertyBlock _block;
            private readonly Vector3[] _verts = new Vector3[BandSegments * 2];

            public FlameBand(Transform parent, string name, Material mat, MaterialPropertyBlock shared)
            {
                _block = shared;
                _go = new GameObject(name);
                _go.transform.SetParent(parent, false);
                _mesh = new Mesh { name = name };
                var uvs = new Vector2[BandSegments * 2];
                var tris = new int[BandSegments * 6];
                for (int i = 0; i < BandSegments; i++)
                {
                    uvs[i * 2] = uvs[i * 2 + 1] = new Vector2(0.5f, 0.5f);
                    int a = i * 2, b = i * 2 + 1, c = ((i + 1) % BandSegments) * 2, d = c + 1;
                    int t = i * 6;
                    tris[t] = a; tris[t + 1] = c; tris[t + 2] = b;
                    tris[t + 3] = b; tris[t + 4] = c; tris[t + 5] = d;
                }
                _mesh.vertices = _verts;
                _mesh.uv = uvs;
                _mesh.triangles = tris;
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _rend = _go.AddComponent<MeshRenderer>();
                _rend.sharedMaterial = mat;
                Cosmetic(_rend);
            }

            public void Set(Vector3 origin, float inner, float outer, Color color)
            {
                for (int i = 0; i < BandSegments; i++)
                {
                    float ang = i / (float)BandSegments * Mathf.PI * 2f;
                    float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                    _verts[i * 2] = new Vector3(origin.x + cx * inner, origin.y + Lift, origin.z + cz * inner);
                    _verts[i * 2 + 1] = new Vector3(origin.x + cx * outer, origin.y + Lift, origin.z + cz * outer);
                }
                _mesh.vertices = _verts;
                _mesh.RecalculateBounds();
                _rend.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                _rend.SetPropertyBlock(_block);
            }

            public void Dispose()
            {
                Kill(_mesh);
                if (_go != null) Kill(_go);
            }
        }

        private void ApplyFireball(Piece p)
        {
            float age = p.Age;
            float grow = Mathf.Clamp01(age / FireballGrowTime);
            float eased = 1f - (1f - grow) * (1f - grow);
            float radius = Mathf.Lerp(FireballStartRadius, FireballMaxRadius, eased);
            float fade = age < 0.35f ? 1f : Mathf.Clamp01(1f - (age - 0.35f) / (FireballLife - 0.35f));

            Transform tr = p.Go.transform;
            Face(tr);
            if (p.Kind == Kind.Fireball)
            {
                tr.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
                // white-yellow core -> orange -> red
                Color c = age < 0.3f
                    ? Color.Lerp(new Color(1f, 0.96f, 0.65f), new Color(1f, 0.5f, 0.08f), age / 0.3f)
                    : Color.Lerp(new Color(1f, 0.5f, 0.08f), new Color(0.75f, 0.1f, 0.04f), (age - 0.3f) / (FireballLife - 0.3f));
                const float boost = 1.3f;
                SetColor(p.Rend, new Color(c.r * fade * boost, c.g * fade * boost, c.b * fade * boost, fade));
            }
            else
            {
                float s = radius * 2.5f;
                tr.localScale = new Vector3(s, s, 1f);
                SetColor(p.Rend, new Color(0.06f, 0.04f, 0.03f, 0.55f * fade));
            }
        }

        // -------------------------------------------------------------- helpers

        private void AddStreak(Vector3 pos, Vector3 vel, float length, float life, float gravity)
        {
            EnsureRoot();
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Spark";
            StripCollider(go);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.025f, 0.025f, length);
            go.transform.rotation = Quaternion.LookRotation(vel);
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = VfxMaterials.AdditiveTinted(SparkColor);
            Cosmetic(r);
            _pieces.Add(new Piece { Kind = Kind.Streak, Go = go, Rend = r, Velocity = vel, Gravity = gravity,
                Life = life, Length = length });
        }

        private GameObject NewBillboard(string name, Material mat, Vector3 pos)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            Cosmetic(r);
            return go;
        }

        private static void Face(Transform t)
        {
            Camera cam = Camera.main;
            t.rotation = cam != null ? cam.transform.rotation : Quaternion.Euler(72f, 0f, 0f);
        }

        private void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        private static void Cosmetic(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Kill(col);
        }

        private static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        public void Dispose()
        {
            _pieces.Clear();
            DisposeRing();
            _glow = null;
            Fireball = null;
            if (_root != null) Kill(_root);
            _root = null;
        }
    }
}
