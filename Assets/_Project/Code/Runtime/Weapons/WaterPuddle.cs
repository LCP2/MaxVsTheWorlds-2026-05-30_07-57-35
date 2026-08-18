using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// DELUGE's puddle (MV-426 fusion <c>f_del</c>): a lingering marker left where a Water Balloon
    /// splashed once DELUGE is forged. Carries no damage or VFX of its own — it is purely a
    /// position/radius/lifetime record <see cref="MaxWorlds.Combat.WaterBlaster"/> checks each tick so
    /// the RCDA stream can "arc between every wet robot standing in it" (the ticket's own wording); the
    /// stream itself is what actually deals the arced damage.
    /// </summary>
    public sealed class WaterPuddle : MonoBehaviour
    {
        private static readonly List<WaterPuddle> s_active = new List<WaterPuddle>(4);

        /// <summary>Every puddle currently on the ground.</summary>
        public static IReadOnlyList<WaterPuddle> Active => s_active;

        public Vector3 Position { get; private set; }
        public float Radius { get; private set; }

        private float _timeRemaining;

        /// <summary>Places the puddle and starts its countdown to popping.</summary>
        public void Init(Vector3 position, float radius, float durationSeconds)
        {
            Position = position;
            Radius = radius;
            _timeRemaining = durationSeconds;
            transform.position = position;
            if (!s_active.Contains(this)) s_active.Add(this);
        }

        private void Update()
        {
            _timeRemaining -= Time.deltaTime;
            if (_timeRemaining <= 0f) Pop();
        }

        private void Pop()
        {
            s_active.Remove(this);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private void OnDestroy() => s_active.Remove(this);

        /// <summary>Ground-plane (XZ) containment test — pure so "is this point standing in the
        /// puddle" is unit-testable without a physics scene.</summary>
        public static bool IsInside(Vector3 center, float radius, Vector3 point)
        {
            float dx = point.x - center.x, dz = point.z - center.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>Test isolation only — mirrors <see cref="MaxWorlds.Arena.Sentinel.ResetRegistry"/>'s
        /// list-only contract; does not destroy any GameObject.</summary>
        public static void ResetRegistry() => s_active.Clear();
    }
}
