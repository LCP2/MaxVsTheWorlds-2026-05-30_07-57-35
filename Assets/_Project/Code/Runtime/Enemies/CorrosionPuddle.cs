using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Pipe Turret's coolant puddle (MV-691) — a timed ground hazard <see cref="CorrosiveGlob"/>
    /// leaves at its impact point. Anything standing inside it while it lives is marked CORRODED
    /// (<see cref="CorrodedStatus"/>) every tick — which just keeps refreshing that status's own
    /// clock (see its own doc comment); leaving the puddle is what actually starts the 4 s countdown
    /// running down. Applies to Max AND robots, per the ticket ("Max (and robots)").
    ///
    /// MV-789: this is the class MV-769 never touched — that ticket fixed <see cref="SludgePuddle"/>,
    /// which only a Sludge Drone's death spawns, and Area 1 has none. This puddle is what the Pipe
    /// Turret actually leaves, and it was still a literal <c>PrimitiveType.Cylinder</c> doing no
    /// damage. It now gets the same irregular-fan treatment and the same damage-over-time
    /// (<see cref="DamagePerSecond"/> on a <see cref="DamageTickInterval"/> tick, exactly
    /// <see cref="SludgePuddle"/>'s own rate) — <see cref="CorrodedStatus"/> stacks on top, unchanged.
    ///
    /// Free-flying and self-destroying, same "one and done" lifetime as <see cref="CorrosiveGlob"/>/
    /// <see cref="HomingMissile"/> — not pooled, since a swarm never has more than a handful of these
    /// live at once.
    /// </summary>
    public sealed class CorrosionPuddle : MonoBehaviour
    {
        /// <summary>MV-789's own authored rate — deliberately the same numbers as
        /// <see cref="SludgePuddle.DamagePerSecond"/>/<see cref="SludgePuddle"/>'s own tick interval,
        /// per the ticket's explicit "exactly as SludgePuddle.DamagePerSecond and DamageTickInterval
        /// already do".</summary>
        public const float DamagePerSecond = 6f;

        private const float DamageTickInterval = 0.25f;

        private float _radius;
        private float _remaining;
        private float _sinceDamageTick;
        private Transform _playerTarget;
        private IDamageable _playerDamageable;
        private Transform _churnPivot;

        public static CorrosionPuddle Spawn(Vector3 position, float radius, float duration)
        {
            var go = new GameObject("CorrosionPuddle (stand-in)");
            go.transform.position = position;
            var puddle = go.AddComponent<CorrosionPuddle>();
            int seed = SeedFromPosition(position);
            puddle._churnPivot = BuildVisual(go.transform, radius, seed);
            puddle.Init(radius, duration);
            return puddle;
        }

        /// <summary>MV-789's three tones: the base fill, the brighter churn lobes, and the hot rim —
        /// replacing the single flat <c>PuddleColor</c> the old cylinder used.</summary>
        private static readonly Color BaseTone = new Color(0.150f, 0.300f, 0.115f);
        private static readonly Color ChurnTone = new Color(0.300f, 0.480f, 0.150f);
        private static readonly Color RimTone = new Color(0.480f, 0.680f, 0.230f);

        /// <summary>MV-789, change 1: a 9-vertex fan, each rim vertex at 0.68-1.30x the radius — about
        /// a +/-31% swing, wider than <see cref="SludgePuddle"/>'s own +/-19% (MV-769), which the ticket
        /// measured as still reading as a circle at gameplay scale.</summary>
        private const int FanVertexCount = 9;
        private const float FanRadiusBase = 0.68f;
        private const float FanRadiusJitter = 0.62f;

        private const float RimScale = 1.005f;
        private const float RimEmissive = 0.35f;
        private const float LipScale = 0.93f;

        private const int ChurnLobeCount = 4;
        private const int ChurnLobeSegments = 7;
        private const float ChurnLobeRadiusMin = 0.30f;
        private const float ChurnLobeRadiusMax = 0.64f;
        private const float ChurnRingT = 0.32f; // how far off-centre a lobe's own pivot sits
        private const float ChurnDriftDegPerSecond = 5f;

        private const int MinBubbles = 3;
        private const int MaxBubbles = 5;

        private const float PoolScale = 1.5f;
        private const float PoolStrength = 0.11f;

        /// <summary>
        /// Builds the puddle's full visual: base fill, hot rim (with the base tone redrawn inside it as
        /// a lip), four drifting churn lobes, 3-5 bubbles and a wide faint additive pool — all seeded
        /// from <paramref name="seed"/>, never <see cref="Random"/>, so the same impact always draws
        /// the same splat. Returns the churn pivot so <see cref="Tick"/> can drift it.
        /// </summary>
        private static Transform BuildVisual(Transform parent, float radius, int seed)
        {
            // Same reason as every other free-flying hazard's visual in this roster (MV-350).
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            // Rim first (slightly larger, sits underneath), base redrawn smaller on top — the 0.93x..1.005x
            // band is what reads as a lip rather than a hoop.
            GameObject rim = BuildFanBlob(parent, "Puddle Rim", radius * RimScale, seed, 0.008f);
            Material rimMat = StormdrainKit.Unlit(RimTone, "CorrosionRim");
            if (rimMat != null) rim.GetComponent<MeshRenderer>().sharedMaterial = rimMat;
            AddAdditiveOverlay(rim.transform, "Rim Glow", radius * RimScale, RimTone * RimEmissive, 0.001f);

            GameObject baseBlob = BuildFanBlob(parent, "Puddle Base", radius * LipScale, seed, 0.012f);
            Material baseMat = StormdrainKit.Unlit(BaseTone, "CorrosionBase");
            if (baseMat != null) baseBlob.GetComponent<MeshRenderer>().sharedMaterial = baseMat;

            // Four inner churn lobes, scattered on a ring and parented under one pivot so Tick() can
            // drift the whole group slowly around the centre with a single rotation.
            var churnPivot = new GameObject("Churn").transform;
            churnPivot.SetParent(parent, false);
            churnPivot.localPosition = new Vector3(0f, 0.016f, 0f);
            for (int i = 0; i < ChurnLobeCount; i++)
            {
                float lobeRadius = radius * Mathf.Lerp(ChurnLobeRadiusMin, ChurnLobeRadiusMax, Hash01(seed, i + 50));
                float ring = radius * ChurnRingT;
                float angle = (i / (float)ChurnLobeCount + Hash01(seed, i + 60)) * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);

                var lobe = new GameObject($"Churn Lobe{i}");
                lobe.transform.SetParent(churnPivot, false);
                lobe.transform.localPosition = at;
                lobe.AddComponent<MeshFilter>().sharedMesh = BuildFanMesh(lobeRadius, seed + i + 70, ChurnLobeSegments);
                lobe.AddComponent<MeshRenderer>();
                Material churnMat = StormdrainKit.Unlit(ChurnTone, "CorrosionChurn");
                if (churnMat != null) lobe.GetComponent<MeshRenderer>().sharedMaterial = churnMat;
            }

            // Three to five bubbles, deterministic count from the same seed (same idiom as
            // SludgePuddle's own bubble count).
            int bubbleCount = MinBubbles + Mathf.RoundToInt(Hash01(seed, 199) * (MaxBubbles - MinBubbles));
            SludgeBubbles.Attach(parent, "Bubbles", new Vector2(radius, radius), 0.02f, seed + 2, bubbleCount, RimTone);

            // One wide faint additive pool lighting the floor the puddle is eating.
            AddAdditiveOverlay(parent, "Pool", radius * PoolScale, BaseTone * PoolStrength, 0.006f);

            return churnPivot;
        }

        private static GameObject BuildFanBlob(Transform parent, string name, float radius, int seed, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = BuildFanMesh(radius, seed);
            go.AddComponent<MeshRenderer>();
            return go;
        }

        /// <summary>A flat additive-blended quad — the rim's own glow bloom, and the wide faint pool —
        /// same genuinely-additive material <see cref="StormdrainLightKit.AdditiveUnlit"/> already
        /// builds for a lamp's own bloom/pool quads.</summary>
        private static void AddAdditiveOverlay(Transform parent, string name, float radius, Color tone, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = StormdrainLightKit.AdditiveUnlit(tone, name);
        }

        /// <summary>
        /// The irregular fan outline (MV-789, EXACT): a centre vertex plus <paramref name="vertexCount"/>
        /// rim vertices, each at <c>radius * (FanRadiusBase + Hash01(seed, i) * FanRadiusJitter)</c> —
        /// deterministic from <paramref name="seed"/>, never <see cref="Random"/>. Same winding-by-normal
        /// idiom as <see cref="SludgePuddle.BuildFanMesh"/>, duplicated rather than shared because that
        /// method's own vertex count/amplitude are pinned to MV-769's ticket, not this one's.
        /// </summary>
        public static Mesh BuildFanMesh(float radius, int seed, int vertexCount = FanVertexCount)
        {
            vertexCount = Mathf.Max(3, vertexCount);
            var vertices = new Vector3[vertexCount + 1];
            var uv = new Vector2[vertexCount + 1];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < vertexCount; i++)
            {
                float angle = i / (float)vertexCount * Mathf.PI * 2f;
                float r = radius * (FanRadiusBase + Hash01(seed, i) * FanRadiusJitter);
                float x = Mathf.Cos(angle) * r;
                float z = Mathf.Sin(angle) * r;
                vertices[i + 1] = new Vector3(x, 0f, z);
                uv[i + 1] = new Vector2(x / (radius * 2f) + 0.5f, z / (radius * 2f) + 0.5f);
            }

            var triangles = new int[vertexCount * 3];
            for (int i = 0; i < vertexCount; i++)
            {
                int b = i + 1;
                int c = (i + 1) % vertexCount + 1;
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[0], vertices[c] - vertices[0]);
                int t = i * 3;
                if (normal.y >= 0f) { triangles[t] = 0; triangles[t + 1] = b; triangles[t + 2] = c; }
                else { triangles[t] = 0; triangles[t + 1] = c; triangles[t + 2] = b; }
            }

            var mesh = new Mesh { name = "CorrosionPuddleFan", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Deterministic 0..1 from a seed and index — never <see cref="Random"/>, same
        /// integer-mix idiom as <see cref="SludgePuddle.Hash01"/>.</summary>
        private static float Hash01(int seed, int i)
        {
            unchecked
            {
                int h = seed * 374761393 + i * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>MV-789's own "seeded from the glob's impact position" requirement: quantised so a
        /// world position always mixes down to the same integer seed, never <see cref="Random"/>.
        /// Different impact points give (overwhelmingly likely) different seeds; the same point given
        /// twice always gives the identical one.</summary>
        private static int SeedFromPosition(Vector3 position)
        {
            int ix = Mathf.RoundToInt(position.x * 131f);
            int iz = Mathf.RoundToInt(position.z * 131f);
            unchecked
            {
                return ix * 73856093 ^ iz * 19349663;
            }
        }

        private void Init(float radius, float duration)
        {
            _radius = radius;
            _remaining = duration;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One evaluation: mark anything standing inside this puddle CORRODED (unchanged from
        /// before MV-789), damage-over-time on top (MV-789's own change 3, the same fixed-interval
        /// cadence <see cref="SludgePuddle.TickDamage"/> already uses), drift the churn lobes, then
        /// count down this puddle's own lifetime. Public and dt-parameterized (MV-691), same "an
        /// EditMode test can drive it directly" reasoning as
        /// <see cref="MaxWorlds.Factories.Replicator.TickLure"/> — <c>Update()</c> never runs outside
        /// Play mode and this project authors no PlayMode tests.</summary>
        public void Tick(float dt)
        {
            if (_playerTarget == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null)
                {
                    _playerTarget = p.transform;
                    _playerDamageable = p.GetComponent<IDamageable>();
                }
            }

            // MV-789: this tick's OWN damage must land at the ticket's authored 6 dmg/s, not get
            // self-amplified by the CORRODED multiplier this same tick is about to (re)apply — so the
            // damage pass reads whatever CORRODED state carried over from an EARLIER tick first, and
            // only then refreshes it for the next one.
            TickDamage(dt);

            if (_playerTarget != null && InRadius(transform.position, _playerTarget.position, _radius))
            {
                PlayerHealth health = _playerTarget.GetComponent<PlayerHealth>();
                health?.ApplyCorroded();
            }

            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                if (r == null || !r.IsAlive) continue;
                if (InRadius(transform.position, r.transform.position, _radius)) r.ApplyCorroded();
            }

            if (_churnPivot != null) _churnPivot.Rotate(Vector3.up, ChurnDriftDegPerSecond * dt, Space.Self);

            _remaining -= dt;
            if (_remaining <= 0f)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        /// <summary>MV-789, change 3: damage-over-time on a fixed <see cref="DamageTickInterval"/>
        /// cadence (never per-frame), applied to Max and every robot standing inside — exactly
        /// <see cref="SludgePuddle.TickDamage"/>'s own idiom, corrosive coolant does not check
        /// allegiance either.</summary>
        private void TickDamage(float dt)
        {
            _sinceDamageTick += dt;
            while (_sinceDamageTick >= DamageTickInterval)
            {
                _sinceDamageTick -= DamageTickInterval;
                ApplyDamageTick();
            }
        }

        private void ApplyDamageTick()
        {
            var info = new DamageInfo(DamagePerSecond * DamageTickInterval, transform.position, Vector3.up,
                Team.Neutral, source: DamageSource.Environment);

            if (_playerTarget != null && _playerDamageable != null && _playerDamageable.IsAlive
                && InRadius(transform.position, _playerTarget.position, _radius))
            {
                _playerDamageable.TakeDamage(info);
            }

            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                if (r == null || !r.IsAlive) continue;
                if (InRadius(transform.position, r.transform.position, _radius)) r.TakeDamage(info);
            }
        }

        /// <summary>Pure so the puddle's own dwell check is testable without a scene or a clock.</summary>
        public static bool InRadius(Vector3 puddlePosition, Vector3 receiverPosition, float radius)
        {
            float dx = puddlePosition.x - receiverPosition.x, dz = puddlePosition.z - receiverPosition.z;
            return dx * dx + dz * dz <= radius * radius;
        }
    }
}
