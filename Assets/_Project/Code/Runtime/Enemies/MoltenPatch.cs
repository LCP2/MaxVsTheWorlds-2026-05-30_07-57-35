using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1283: a Molten elite's own trailing ground hazard — dropped by <see cref="RobotEnemy"/>'s own
    /// TickMolten every 0.6 s of movement, capped at 6 live patches per owner. Once the cap is hit the
    /// OLDEST patch is recycled in place (<see cref="ResetAt"/>) rather than destroyed and a new one
    /// instantiated — the ticket's own "pooled". All of an owner's patches are force-expired
    /// (<see cref="Expire"/>) when it dies or is captured (<see cref="RobotEnemy.Die"/>/
    /// <see cref="RobotEnemy.TryConvert"/>).
    ///
    /// Damages ONLY Max and a deployed <see cref="Sentinel"/> — same receiver set and <see cref="Team.Neutral"/>
    /// routing as <see cref="SludgePuddle.ApplyDamageTick"/>, NEVER a robot (not filtered out, simply
    /// never looped — same "never a robot" idiom). Deliberately does NOT register with
    /// <see cref="MaxWorlds.Arena.MapSlowZones"/> the way <see cref="SludgePuddle"/> does — this affix's
    /// purpose is "the player must pick clean ground", not a slow, and the ticket's own "Do not re-raise"
    /// is explicit that nothing here ever slows anyone, robot or Max alike.
    ///
    /// Same "irregular fan, deterministic from a seed, never <see cref="Random"/>" visual idiom as
    /// <see cref="SludgePuddle"/>/<see cref="CorrosionPuddle"/>, duplicated rather than shared per those
    /// classes' own precedent (vertex count/amplitude are this ticket's own). Colour cools from bright
    /// orange to dark over its 3 s life via a <see cref="MaterialPropertyBlock"/> so the shared cached
    /// material itself (<see cref="StormdrainKit.Unlit"/>) is never mutated — same per-instance-colour-
    /// without-a-unique-material idiom <see cref="MaxWorlds.VFX.RobotRig"/>'s own body-heat write uses.
    /// </summary>
    [MaxWorlds.Core.PerfSection("molten")]
    public sealed class MoltenPatch : MonoBehaviour
    {
        public const float Radius = 0.6f;
        public const float Lifetime = 3f;
        public const float DamagePerSecond = 10f;

        /// <summary>Fixed damage cadence, same reasoning as <see cref="SludgePuddle"/>'s own — frame-rate
        /// independent, and an EditMode test gets the same answer for any dt.</summary>
        private const float DamageTickInterval = 0.25f;

        private static readonly Color HotColor = new Color(1f, 0.45f, 0.05f);
        private static readonly Color CoolColor = new Color(0.10f, 0.05f, 0.03f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private const int FanVertexCount = 9;
        private const float FanRadiusBase = 0.70f;
        private const float FanRadiusJitter = 0.55f;
        private const string FanMeshName = "MoltenPatchFan";

        private float _remaining;
        private float _sinceDamageTick;
        private Transform _playerTarget;
        private IDamageable _playerDamageable;
        private MeshFilter _blobFilter;
        private MeshRenderer _blobRenderer;
        private MaterialPropertyBlock _mpb;

        public float RemainingLife => _remaining;

        public static MoltenPatch Spawn(Vector3 position)
        {
            var go = new GameObject("MoltenPatch (stand-in)");
            // Same reason as every other free-flying hazard's visual in this roster (MV-350).
            go.AddComponent<KeepsOwnMaterial>();

            var patch = go.AddComponent<MoltenPatch>();
            patch._mpb = new MaterialPropertyBlock();
            patch.BuildBlob();
            patch.ResetAt(position);
            return patch;
        }

        private void BuildBlob()
        {
            var blob = new GameObject("Molten Blob");
            blob.transform.SetParent(transform, false);
            _blobFilter = blob.AddComponent<MeshFilter>();
            _blobRenderer = blob.AddComponent<MeshRenderer>();
            Material mat = StormdrainKit.Unlit(HotColor, "MoltenPatch");
            if (mat != null) _blobRenderer.sharedMaterial = mat;
        }

        /// <summary>Repositions and fully refreshes this patch for reuse — the cap-6 recycle path
        /// (<see cref="RobotEnemy"/>'s own DropMoltenPatch), and also what <see cref="Spawn"/> itself
        /// calls for the first-ever placement, so there is exactly one place that seeds/colours a patch.</summary>
        public void ResetAt(Vector3 position)
        {
            transform.position = position;
            _remaining = Lifetime;
            _sinceDamageTick = 0f;
            _playerTarget = null;
            _playerDamageable = null;

            DestroyOwnedMesh();
            int seed = SeedFromPosition(position);
            _blobFilter.sharedMesh = BuildFanMesh(Radius, seed);

            ApplyCoolTint(0f);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One evaluation: damage Max/a Sentinel standing inside, advance the cooling tint, then
        /// count down this patch's own lifetime. Public and dt-parameterized, same "an EditMode test can
        /// drive it directly" reasoning as <see cref="SludgePuddle.Tick"/> — <c>Update()</c> never runs
        /// outside Play mode and this project authors no PlayMode tests.</summary>
        public void Tick(float dt)
        {
            TickDamage(dt);
            ApplyCoolTint(1f - Mathf.Clamp01(_remaining / Lifetime));

            _remaining -= dt;
            if (_remaining <= 0f) DestroySelf();
        }

        /// <summary>Forces this patch's life to end right now — the owner's own death/capture cleanup
        /// (MV-1283 point 2's "all its patches are removed when it dies or is captured"), not a natural
        /// expiry.</summary>
        public void Expire() => DestroySelf();

        private void DestroySelf()
        {
            DestroyOwnedMesh();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private void ApplyCoolTint(float t)
        {
            if (_blobRenderer == null) return;
            Color c = Color.Lerp(HotColor, CoolColor, t);
            _blobRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(EmissionId, c);
            _blobRenderer.SetPropertyBlock(_mpb);
        }

        private void TickDamage(float dt)
        {
            _sinceDamageTick += dt;
            while (_sinceDamageTick >= DamageTickInterval)
            {
                _sinceDamageTick -= DamageTickInterval;
                ApplyDamageTick();
            }
        }

        /// <summary>MV-1283 point 3: Max and a deployed Sentinel ONLY, via <see cref="Team.Neutral"/> —
        /// exactly the receiver set and routing <see cref="SludgePuddle.ApplyDamageTick"/> uses. A robot
        /// standing here is simply never looped, never a robot per the ticket's own instruction.</summary>
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
                && InRadius(transform.position, _playerTarget.position))
            {
                _playerDamageable.TakeDamage(info);
            }

            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
            {
                Sentinel s = sentinels[i];
                if (s == null || !s.IsAlive) continue;
                if (InRadius(transform.position, s.transform.position)) s.TakeDamage(info);
            }
        }

        /// <summary>Pure so the patch's own dwell check is testable without a scene or a clock.</summary>
        public static bool InRadius(Vector3 patchPosition, Vector3 receiverPosition)
        {
            float dx = patchPosition.x - receiverPosition.x, dz = patchPosition.z - receiverPosition.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }

        /// <summary>Same leak guard as <see cref="SludgePuddle.DestroyOwnedMeshes"/> — <see cref="BuildFanMesh"/>
        /// marks its mesh <c>HideAndDontSave</c>, exempt from Unity's own unused-asset sweep. Matched by
        /// exact name, same reason as that method's own doc comment (a hideFlags bitflag test false-matches
        /// Unity's own built-in meshes).</summary>
        private void DestroyOwnedMesh()
        {
            if (_blobFilter == null || _blobFilter.sharedMesh == null) return;
            if (_blobFilter.sharedMesh.name != FanMeshName) return;
            if (Application.isPlaying) Destroy(_blobFilter.sharedMesh);
            else DestroyImmediate(_blobFilter.sharedMesh, true);
        }

        /// <summary>The irregular fan outline — a centre vertex plus <paramref name="vertexCount"/> rim
        /// vertices, each at <c>radius * (FanRadiusBase + Hash01(seed, i) * FanRadiusJitter)</c>, same
        /// winding-by-normal idiom as <see cref="SludgePuddle.BuildFanMesh"/>/<see cref="CorrosionPuddle.BuildFanMesh"/>.</summary>
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

            var mesh = new Mesh { name = FanMeshName, hideFlags = HideFlags.HideAndDontSave };
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

        /// <summary>Same "quantised position mixes down to a deterministic seed" idiom as
        /// <see cref="CorrosionPuddle.SeedFromPosition"/> — a recycled patch (<see cref="ResetAt"/>)
        /// dropped at a new position draws a different outline, never the same shape twice in a row.</summary>
        private static int SeedFromPosition(Vector3 position)
        {
            int ix = Mathf.RoundToInt(position.x * 131f);
            int iz = Mathf.RoundToInt(position.z * 131f);
            unchecked
            {
                return ix * 73856093 ^ iz * 19349663;
            }
        }
    }
}
