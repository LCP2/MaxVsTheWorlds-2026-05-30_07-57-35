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
    /// Free-flying and self-destroying, same "one and done" lifetime as <see cref="CorrosiveGlob"/>/
    /// <see cref="HomingMissile"/> — not pooled, since a swarm never has more than a handful of these
    /// live at once.
    /// </summary>
    public sealed class CorrosionPuddle : MonoBehaviour
    {
        private float _radius;
        private float _remaining;
        private Transform _playerTarget;

        public static CorrosionPuddle Spawn(Vector3 position, float radius, float duration)
        {
            var go = new GameObject("CorrosionPuddle (stand-in)");
            go.transform.position = position;
            BuildVisual(go.transform, radius);

            var puddle = go.AddComponent<CorrosionPuddle>();
            puddle.Init(radius, duration);
            return puddle;
        }

        /// <summary>The ticket's own green puddle colour — the same hue family as
        /// <see cref="CorrosiveGlob"/>'s own blob, so the hazard reads as one thing (glob → puddle),
        /// not two unrelated effects.</summary>
        private static readonly Color PuddleColor = new Color(0.20f, 0.48f, 0.16f);

        private static void BuildVisual(Transform parent, float radius)
        {
            // Same reason as every other free-flying projectile's visual in this roster (MV-350).
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Puddle";
            var col = disc.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            // Unity's cylinder primitive is 1 unit in radius at scale 1 (2 units across) — a flat
            // puddle, so height (Y) stays tiny while X/Z scale to the authored radius.
            disc.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, PuddleColor);
            if (mat != null) disc.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void Init(float radius, float duration)
        {
            _radius = radius;
            _remaining = duration;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One evaluation: mark anything standing inside this puddle CORRODED, then count
        /// down its own lifetime. Public and dt-parameterized (MV-691), same "an EditMode test can
        /// drive it directly" reasoning as <see cref="MaxWorlds.Factories.Replicator.TickLure"/> —
        /// <c>Update()</c> never runs outside Play mode and this project authors no PlayMode tests.</summary>
        public void Tick(float dt)
        {
            if (_playerTarget == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _playerTarget = p.transform;
            }

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

            _remaining -= dt;
            if (_remaining <= 0f)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
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
