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
        private const float FireballStartRadius = 0.3f;
        public const float FireballMaxRadius = 2.5f;
        private const float FireballGrowTime = 0.2f;
        private const float FireballLife = 0.7f;
        private const float RingLife = 0.3f;
        private const float RingStartRadius = 0.05f;
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
        private static readonly Color RingColor = new Color(1f, 0.82f, 0.55f);

        private readonly List<Piece> _pieces = new List<Piece>(48);
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        private GameObject _root;
        private GameObject _glow;
        private Renderer _glowRend;
        private float _glowT;
        private GroundRing _ring;
        private Vector3 _ringOrigin;
        private float _ringAge;
        private float _ringMax;
        private float _fountainBank;

        /// <summary>The live fireball (null until the canister explodes, and again once it is gone) —
        /// public to the Canister so an EditMode test can read its resolved radius.</summary>
        public GameObject Fireball { get; private set; }

        /// <summary>The fireball's resolved radius in metres (quad edge / 2), 0 when there is none.</summary>
        public float FireballRadius => Fireball != null ? Fireball.transform.localScale.x * 0.5f : 0f;

        public GameObject Root => _root;

        public bool Busy => _pieces.Count > 0 || _glow != null || _ring != null;

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

            // Shockwave: a thick additive annulus, 0.05 m -> the blast radius in 0.3 s.
            _ring = GroundRing.Create("CanisterShockwave", VfxMaterials.Annulus(), additive: true);
            _ring.transform.SetParent(_root.transform, true);
            _ringOrigin = point;
            _ringMax = blastRadius;
            _ringAge = 0f;
            _ring.Show(_ringOrigin, RingStartRadius, RingColor);

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
            if (_ring != null)
            {
                _ringAge += dt;
                float f = Mathf.Clamp01(_ringAge / RingLife);
                float e = 1f - (1f - f) * (1f - f);
                float a = (1f - f * f) * 1.3f;   // bright orange-white, held until late
                _ring.Show(_ringOrigin, Mathf.Lerp(RingStartRadius, _ringMax, e),
                    new Color(RingColor.r * a, RingColor.g * a, RingColor.b * a, a));
                if (f >= 1f) { Kill(_ring.gameObject); _ring = null; }
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
            _ring = null;
            _glow = null;
            Fireball = null;
            if (_root != null) Kill(_root);
            _root = null;
        }
    }
}
