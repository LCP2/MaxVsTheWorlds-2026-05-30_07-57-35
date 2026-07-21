using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Chunky low-poly wreckage for the boss blow-up (YT-153).
    ///
    /// <see cref="BossSpectacle"/> already throws the fire, the smoke and the shockwave when the boss
    /// dies — but a puff of particles reads as a fireball, not as a machine COMING APART. This adds the
    /// solid bits: a dozen-odd chunky shards in the boiler's own colours that punch out of the blast,
    /// tumble through the air and land on the lawn it spent the fight cutting. That is the read the
    /// ticket asks for — debris, low-poly styled — and it is the difference between "it exploded" and
    /// "it broke".
    ///
    /// A separate director from BossSpectacle on purpose: the shards are pooled GameObjects with their
    /// own hand-rolled ballistics, not a ParticleSystem, so they do not belong in the burst code. Both
    /// simply react to the same <see cref="HudSignals.BossDefeated"/> the boss already raises — no boss
    /// logic, no fight timing touched.
    ///
    /// UNSCALED time, for the reason BossSpectacle documents at length: the run ends the frame the boss
    /// dies and <c>ResultScreen</c> freezes the game with timeScale 0, so anything on scaled time is a
    /// statue before it moves. The shards fly and settle behind the result card on realtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossDebris : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BossDebris>() != null) return;
            if (FindFirstObjectByType<BigBermudaBoss>() == null) return;   // no boss, nothing to wreck
            new GameObject("BossDebris").AddComponent<BossDebris>();
        }

        // The boiler palette, so the bits read as pieces of the machine that just blew. Steel and a
        // dark iron dominate; a little red and brass so it isn't a grey heap.
        private static readonly Color[] ChunkColors =
        {
            new Color(0.30f, 0.31f, 0.34f),   // steel
            new Color(0.20f, 0.21f, 0.24f),   // dark iron
            new Color(0.55f, 0.16f, 0.16f),   // red plate
            new Color(0.52f, 0.40f, 0.18f),   // brass
        };

        private const int MaxChunks = 16;     // one-shot on death; well under the 200 ambience budget
        private const float Gravity = 24f;    // m/s^2, snappy — this is spectacle, not a physics sim
        private const float GroundY = 0.0f;

        private struct Chunk
        {
            public Transform T;
            public Vector3 Vel;
            public Vector3 Spin;      // degrees/sec per axis
            public float Age;
            public float Life;
            public float Rest;        // y the chunk sits at once it settles (half its height)
            public Vector3 Scale;
            public bool Settled;
        }

        private readonly List<Chunk> _chunks = new List<Chunk>(MaxChunks);
        private Material[] _mats;
        private BigBermudaBoss _boss;
        private int _seed;            // varies the scatter without Random state (deterministic per index)

        private void Awake()
        {
            _boss = FindFirstObjectByType<BigBermudaBoss>();
            gameObject.AddComponent<KeepsOwnMaterial>();   // the surface sweep leaves our shards alone

            _mats = new Material[ChunkColors.Length];
            for (int i = 0; i < ChunkColors.Length; i++)
                _mats[i] = MaterialLibrary.Tinted(SurfaceKind.Metal, ChunkColors[i]);
        }

        private void OnEnable() => HudSignals.BossDefeated += OnDefeated;
        private void OnDisable() => HudSignals.BossDefeated -= OnDefeated;

        private void OnDestroy()
        {
            foreach (var c in _chunks) if (c.T != null) Destroy(c.T.gameObject);
            _chunks.Clear();
        }

        private void OnDefeated()
        {
            Vector3 at = BossPos() + Vector3.up * 1.4f;   // burst from the machine's mass, not its feet

            for (int i = 0; i < MaxChunks; i++)
            {
                // Deterministic spread — a ring of directions fanned upward, jittered per chunk so it
                // scatters rather than fires an even fountain. No Random state, so it can't drift a test.
                float a = (i * 137.5f + _seed * 53f) * Mathf.Deg2Rad;   // golden-angle spray
                float up = 0.55f + Frac(i * 0.618f) * 0.5f;
                Vector3 dir = new Vector3(Mathf.Cos(a), up * 3.2f, Mathf.Sin(a)).normalized;
                float speed = 6f + Frac(i * 0.37f + 0.2f) * 10f;

                float s = 0.22f + Frac(i * 0.911f) * 0.34f;                       // chunk size
                var scale = new Vector3(s * (0.7f + Frac(i * 0.13f) * 0.9f),
                                        s * (0.6f + Frac(i * 0.51f) * 0.7f),
                                        s * (0.7f + Frac(i * 0.29f) * 0.9f));

                Spawn(at, dir * speed, scale, i);
            }
            _seed++;
        }

        private void Spawn(Vector3 at, Vector3 vel, Vector3 scale, int i)
        {
            // Cube shards mostly, a cylinder now and then for a length of pipe — cheap variety.
            var shape = (i % 4 == 0) ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            var go = GameObject.CreatePrimitive(shape);
            go.name = "Shard";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);                 // theatre — the boss's own hitbox is long gone

            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(i * 41f, i * 73f, i * 17f);
            go.transform.localScale = scale;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _mats[i % _mats.Length];
            r.shadowCastingMode = ShadowCastingMode.Off;

            _chunks.Add(new Chunk
            {
                T = go.transform,
                Vel = vel,
                Spin = new Vector3((Frac(i * 0.7f) - 0.5f), (Frac(i * 0.9f) - 0.5f), (Frac(i * 0.3f) - 0.5f)) * 900f,
                Age = 0f,
                Life = 1.6f + Frac(i * 0.44f) * 1.1f,
                Rest = GroundY + scale.y * 0.5f,
                Scale = scale,
                Settled = false,
            });
        }

        private void Update()
        {
            if (_chunks.Count == 0) return;

            float dt = Time.unscaledDeltaTime;

            for (int i = _chunks.Count - 1; i >= 0; i--)
            {
                var c = _chunks[i];
                if (c.T == null) { _chunks.RemoveAt(i); continue; }

                c.Age += dt;

                if (!c.Settled)
                {
                    c.Vel.y -= Gravity * dt;
                    Vector3 p = c.T.position + c.Vel * dt;

                    if (p.y <= c.Rest)
                    {
                        // Land: a low bounce that bleeds most of the energy, and kill it once it's slow
                        // so shards don't jitter on the lawn forever.
                        p.y = c.Rest;
                        if (c.Vel.y < -1.2f)
                        {
                            c.Vel = new Vector3(c.Vel.x * 0.5f, -c.Vel.y * 0.32f, c.Vel.z * 0.5f);
                            c.Spin *= 0.5f;
                        }
                        else
                        {
                            c.Vel = Vector3.zero;
                            c.Spin = Vector3.zero;
                            c.Settled = true;
                        }
                    }

                    c.T.position = p;
                    c.T.Rotate(c.Spin * dt, Space.Self);
                }

                // Fade out over the last third of a second by shrinking — the lawn should not slowly fill
                // with a permanent scrapyard, and a shrink reads cleaner than a pop.
                float fade = c.Life - c.Age;
                if (fade < 0.35f)
                    c.T.localScale = c.Scale * Mathf.Clamp01(fade / 0.35f);

                if (c.Age >= c.Life)
                {
                    Destroy(c.T.gameObject);
                    _chunks.RemoveAt(i);
                    continue;
                }

                _chunks[i] = c;
            }
        }

        private Vector3 BossPos()
        {
            if (_boss == null) _boss = FindFirstObjectByType<BigBermudaBoss>();
            return _boss != null ? _boss.transform.position : Vector3.zero;
        }

        /// <summary>Fractional part of x — a cheap deterministic hash-ish spread in [0,1) that keeps the
        /// scatter varied per chunk without a Random the tests would have to seed.</summary>
        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
