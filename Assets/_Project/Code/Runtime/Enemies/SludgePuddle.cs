using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// A Sludge Drone's death puddle (MV-705) — a temporary, runtime-spawned version of MV-692's
    /// map-authored <c>sludge[]</c> slow zones: anything standing inside it while it lives is slowed to
    /// <see cref="SpeedMultiplier"/> of normal speed, the same idea a map-authored sludge rect already
    /// applies, but timed rather than permanent. Consulted by
    /// <see cref="MaxWorlds.Arena.MapSlowZones.SpeedMultiplierAt"/> alongside the map's own static
    /// zones, so both <see cref="MaxWorlds.Player.PlayerController"/> and <see cref="RobotEnemy"/> slow
    /// inside it through the one shared hook — except a Sludger itself, which
    /// <see cref="RobotEnemy.EffectiveMoveSpeed"/> exempts outright (the ticket's own "immune to sludge
    /// slow").
    ///
    /// MV-769: it also DAMAGES now — Lee's own bug report ("does no damage, I assume it should") — and
    /// it is no longer a literal disc. <see cref="BuildFanMesh"/> is the irregular art; the SLOW and
    /// DAMAGE checks both keep using the plain circular <see cref="_radius"/> via <see cref="InRadius"/>
    /// — the art is irregular, the hitbox stays round, per the ticket's own instruction not to make
    /// collision follow the visual.
    ///
    /// Deliberately does NOT rely on OnEnable/OnDestroy to maintain <see cref="_active"/> — Unity does
    /// not reliably run those outside Play mode (the same lesson <c>RobotEnemy.Active</c>'s own doc
    /// comment and half this project's EditMode tests already learned) — <see cref="Spawn"/> and
    /// <see cref="Tick"/> add/remove directly instead, so the registry is correct under a synchronous
    /// EditMode test too.
    /// </summary>
    public sealed class SludgePuddle : MonoBehaviour
    {
        /// <summary>The ticket's own slow amount — same value <see cref="MaxWorlds.Arena.WorldDials.sludgeSpeedMultiplier"/>
        /// defaults to, kept as its own constant rather than threaded through from a live world config:
        /// this puddle is a self-contained timed hazard, not an authored map feature.</summary>
        public const float SpeedMultiplier = 0.6f;

        /// <summary>MV-769's own authored rate: corrosive sludge does not check allegiance, so this
        /// applies to Max and every robot alike via <see cref="ApplyDamageTick"/>.</summary>
        public const float DamagePerSecond = 6f;

        /// <summary>Fixed damage cadence (MV-769) — ticks on this interval rather than per-frame, so
        /// the rate is frame-rate independent and an EditMode test gets the same answer for any dt.</summary>
        private const float DamageTickInterval = 0.25f;

        private static readonly List<SludgePuddle> _active = new List<SludgePuddle>(4);

        private float _radius;
        private float _remaining;
        private float _sinceDamageTick;
        private Transform _playerTarget;
        private IDamageable _playerDamageable;

        public float Radius => _radius;
        public float RemainingLife => _remaining;

        /// <summary>Spawn a puddle. <paramref name="seed"/> drives <see cref="BuildFanMesh"/>'s outline
        /// only — never <see cref="Random"/>, so the same seed always draws the same blob (MV-769's own
        /// requirement) while two different puddles still look different.</summary>
        public static SludgePuddle Spawn(Vector3 position, float radius, float duration, int seed = 0)
        {
            var go = new GameObject("SludgePuddle (stand-in)");
            go.transform.position = position;
            BuildVisual(go.transform, radius, seed);

            var puddle = go.AddComponent<SludgePuddle>();
            puddle.Init(radius, duration);
            return puddle;
        }

        /// <summary>The ticket's own acid-green puddle colour — the same hue family as
        /// <see cref="MaxWorlds.VFX.CharacterSkin"/>'s Sludger body, so the hazard reads as one thing
        /// (drone → puddle), not two unrelated effects.</summary>
        private static readonly Color PuddleColor = new Color(0.42f, 0.58f, 0.05f);

        /// <summary>MV-769: the fan's rim vertices sit at <c>radius * (FanRadiusBase + hash * FanRadiusJitter)</c>
        /// — 0.72..1.10x the authored radius, a blobby outline rather than a perfect circle.</summary>
        private const int FanVertexCount = 11; // ticket's own 9-13 range
        private const float FanRadiusBase = 0.72f;
        private const float FanRadiusJitter = 0.38f;
        private const float BaseBlobHeight = 0.01f;
        private const float FlowBlobHeight = 0.02f;
        private const float FlowBlobScale = 0.82f;
        private static readonly Vector2 PuddleFlowSpeed = new Vector2(0f, 0.10f);
        private const int MinBubbles = 3;
        private const int MaxBubbles = 5;

        private static void BuildVisual(Transform parent, float radius, int seed)
        {
            // Same reason as every other free-flying hazard's visual in this roster (MV-350).
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            // Layer 1: the base blob — unlit emissive so it glows in this deliberately dark world,
            // rather than the flat lit "dull olive coin" the bug report described.
            GameObject baseBlob = BuildFanBlob(parent, "Puddle Base", radius, seed, BaseBlobHeight);
            Material baseMat = StormdrainKit.Unlit(PuddleColor, "SludgePuddle");
            if (baseMat != null) baseBlob.GetComponent<MeshRenderer>().sharedMaterial = baseMat;

            // Layer 2: a second, smaller blob that scrolls via the sludge lanes' own SludgeFlow idiom.
            GameObject flowBlob = BuildFanBlob(parent, "Puddle Flow", radius * FlowBlobScale, seed + 1, FlowBlobHeight);
            Material flowMat = MaterialLibrary.Tinted(SurfaceKind.Prop, PuddleColor);
            if (flowMat != null)
            {
                flowBlob.GetComponent<MeshRenderer>().sharedMaterial = flowMat;
                flowBlob.AddComponent<SludgeFlow>().Configure(flowMat, PuddleFlowSpeed);
            }

            // Layer 3: 3-5 rising, popping bubbles (MV-769) — deterministic count from the same seed.
            int bubbleCount = MinBubbles + Mathf.RoundToInt(Hash01(seed, 199) * (MaxBubbles - MinBubbles));
            SludgeBubbles.Attach(parent, "Bubbles", new Vector2(radius, radius), FlowBlobHeight, seed + 2,
                bubbleCount, PuddleColor);
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

        /// <summary>
        /// The irregular fan outline (MV-769, EXACT): a centre vertex plus <paramref name="vertexCount"/>
        /// rim vertices, each at <c>radius * (FanRadiusBase + Hash01(seed, i) * FanRadiusJitter)</c> —
        /// deterministic from <paramref name="seed"/>, never <see cref="Random"/>, so the same seed
        /// always returns the identical outline and a different seed returns a different one. Winding
        /// per triangle is picked so its own normal faces up, regardless of which way the angle sweep
        /// runs, so the fan always renders lit-face-up without relying on a fixed convention.
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

            var mesh = new Mesh { name = "SludgePuddleFan", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Deterministic 0..1 from a seed and index — never <see cref="Random"/> (MV-769's own
        /// requirement), same integer-mix idiom as <c>StylizedTextures.Hash</c>.</summary>
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

        private void Init(float radius, float duration)
        {
            _radius = radius;
            _remaining = duration;
            _active.Add(this);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One evaluation: damage anything standing inside (MV-769), then count down this
        /// puddle's own lifetime, self-destroying (and unregistering) once it expires. Public and
        /// dt-parameterized (same idiom as <see cref="CorrosionPuddle.Tick"/>) so an EditMode test can
        /// drive it directly — <c>Update()</c> never runs outside Play mode and this project authors no
        /// PlayMode tests.</summary>
        public void Tick(float dt)
        {
            TickDamage(dt);

            _remaining -= dt;
            if (_remaining <= 0f)
            {
                _active.Remove(this);
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        /// <summary>MV-769: damage-over-time on a fixed <see cref="DamageTickInterval"/> cadence
        /// (never per-frame), applied to Max and every robot standing inside — corrosive sludge does
        /// not check allegiance, so this skips <see cref="DamageRules"/> entirely and hits both via
        /// <see cref="Team.Neutral"/> (which every receiver's own <c>TakeDamage</c> always lets through).
        /// Same "find the tagged Player, iterate RobotEnemy.Active" idiom as
        /// <see cref="CorrosionPuddle.Tick"/> — Max and a robot are the only two movers this world has.</summary>
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

            if (_playerTarget == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null)
                {
                    _playerTarget = p.transform;
                    _playerDamageable = p.GetComponent<IDamageable>();
                }
            }
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

        /// <summary>The slowest multiplier among every live puddle containing <paramref name="worldPosition"/>
        /// (1 = unaffected) — consulted by <see cref="MaxWorlds.Arena.MapSlowZones.SpeedMultiplierAt"/>
        /// alongside the map's own static sludge zones.</summary>
        public static float SpeedMultiplierAt(Vector3 worldPosition)
        {
            float best = 1f;
            for (int i = 0; i < _active.Count; i++)
            {
                SludgePuddle puddle = _active[i];
                if (puddle == null) continue;
                if (InRadius(puddle.transform.position, worldPosition, puddle._radius))
                    best = Mathf.Min(best, SpeedMultiplier);
            }
            return best;
        }

        /// <summary>Test/level-reset hygiene, same idiom as <see cref="RobotEnemy.ResetRegistry"/> —
        /// belt-and-braces against a puddle whose <see cref="Tick"/> hasn't expired it yet when the next
        /// level (or test) starts.</summary>
        public static void ResetRegistry() => _active.Clear();
    }
}
