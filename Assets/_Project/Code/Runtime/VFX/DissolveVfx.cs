using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Dissolves enemies out on death instead of letting them pop off (YT-57).
    ///
    /// The catch: <see cref="RobotEnemy"/> deactivates itself the instant it dies (it's pooled), so
    /// there is no body left to dissolve. Delaying that despawn would mean changing enemy AI, which
    /// this stream doesn't own — and it would also change how long a corpse blocks/soaks, which is a
    /// gameplay question, not an art one.
    ///
    /// So the body isn't dissolved: a GHOST of it is. Each frame every live enemy's shape is
    /// snapshotted (mesh, transform, colour); on death, the snapshot nearest the kill is rebuilt as
    /// a throwaway copy and dissolved away with the stylised shader. The real enemy despawns exactly
    /// as it always did — the pool, the spawner and the AI never know this happened.
    ///
    /// When real models land (YT-51) this needs no change: it copies whatever mesh the enemy has.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DissolveVfx : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<DissolveVfx>() != null) return;
            new GameObject("DissolveVFX").AddComponent<DissolveVfx>();
        }

        [Tooltip("Seconds for a body to burn away.")]
        [SerializeField] private float dissolveSeconds = 0.55f;
        [Tooltip("Ghosts alive at once. Beyond this the oldest is retired early.")]
        [SerializeField] private int maxGhosts = 12;

        private struct Snapshot
        {
            public Mesh Mesh;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private sealed class Ghost
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Mpb;
            public float Age;
        }

        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<Snapshot> _snapshots = new List<Snapshot>(48);
        private readonly List<Ghost> _ghosts = new List<Ghost>(16);

        private void OnEnable() => HudSignals.EnemyKilled += OnEnemyKilled;
        private void OnDisable() => HudSignals.EnemyKilled -= OnEnemyKilled;

        private void OnDestroy()
        {
            foreach (var g in _ghosts)
            {
                if (g.Go != null) Destroy(g.Go);
            }
            _ghosts.Clear();
        }

        private void LateUpdate()
        {
            SnapshotLiveEnemies();
            TickGhosts(Time.deltaTime);
        }

        /// <summary>Kept fresh every frame, because by the time the kill signal arrives the body is
        /// already gone.</summary>
        private void SnapshotLiveEnemies()
        {
            _snapshots.Clear();
            foreach (var e in FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                if (!e.IsAlive) continue;

                var filter = e.GetComponentInChildren<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                var t = filter.transform;
                _snapshots.Add(new Snapshot
                {
                    Mesh = filter.sharedMesh,
                    Position = t.position,
                    Rotation = t.rotation,
                    Scale = t.lossyScale,
                });
            }
        }

        private void OnEnemyKilled(Vector3 pos)
        {
            if (!TryFindSnapshot(pos, out var snap)) return;
            SpawnGhost(snap);
        }

        private bool TryFindSnapshot(Vector3 pos, out Snapshot best)
        {
            best = default;
            float bestDist = 4f;   // if nothing was near the kill, don't invent a body
            bool found = false;

            foreach (var s in _snapshots)
            {
                float d = Vector3.SqrMagnitude(s.Position - pos);
                if (d > bestDist * bestDist) continue;
                if (found && d >= Vector3.SqrMagnitude(best.Position - pos)) continue;
                best = s;
                found = true;
            }
            return found;
        }

        private void SpawnGhost(Snapshot snap)
        {
            var mat = MaterialLibrary.Character();
            if (mat == null) return;   // no stylised shader -> no dissolve; the enemy just pops as before

            if (_ghosts.Count >= maxGhosts) Retire(0);

            var go = new GameObject("DissolveGhost");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.SetPositionAndRotation(snap.Position, snap.Rotation);
            go.transform.localScale = snap.Scale;

            go.AddComponent<MeshFilter>().sharedMesh = snap.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _ghosts.Add(new Ghost
            {
                Go = go,
                Renderer = r,
                Mpb = new MaterialPropertyBlock(),
                Age = 0f,
            });
        }

        private void TickGhosts(float dt)
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                var g = _ghosts[i];
                g.Age += dt;

                float p = Mathf.Clamp01(g.Age / Mathf.Max(0.05f, dissolveSeconds));

                g.Renderer.GetPropertyBlock(g.Mpb);
                g.Mpb.SetFloat(DissolveId, p);
                g.Mpb.SetColor(BaseColorId, Color.white);
                g.Renderer.SetPropertyBlock(g.Mpb);

                if (p >= 1f) Retire(i);
            }
        }

        private void Retire(int index)
        {
            var g = _ghosts[index];
            if (g.Go != null) Destroy(g.Go);
            _ghosts.RemoveAt(index);
        }

        /// <summary>Live ghost count — for tests.</summary>
        public int GhostCount => _ghosts.Count;
    }
}
