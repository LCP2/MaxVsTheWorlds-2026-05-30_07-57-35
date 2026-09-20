using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-858 ARC's own instant connector between the hit point and the arc target (spec: "an instant
    /// jagged electric line ... 0.12 m wide, bright blue-white, emissive, visible for 0.12 s"). A
    /// short-lived world-space <see cref="LineRenderer"/>, not a travelling projectile like the old
    /// FORK release -- the damage already landed the instant the arc fired (<see cref="MaxWorlds.Combat.PulseLaser.TryArc"/>),
    /// so this is pure presentation with no collision or steering of its own.
    /// </summary>
    public sealed class ArcBoltVfx : MonoBehaviour
    {
        private const int Segments = 5;
        private const float JitterMetres = 0.15f;

        private float _timer;

        /// <summary>Spawns the jagged line from <paramref name="from"/> to <paramref name="to"/>, self-
        /// destructing after <paramref name="lifetime"/> seconds.</summary>
        public static void Show(Vector3 from, Vector3 to, float width, float lifetime, Color color)
        {
            var go = new GameObject("ArcBolt");
            var vfx = go.AddComponent<ArcBoltVfx>();
            vfx._timer = lifetime;
            vfx.Build(from, to, width, color);
        }

        private void Build(Vector3 from, Vector3 to, float width, Color color)
        {
            var line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = Segments + 1;
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            line.startColor = color;
            line.endColor = color;

            Vector3 delta = to - from;
            Vector3 axis = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.forward;
            Vector3 perp = Vector3.Cross(axis, Vector3.up);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.right;
            perp.Normalize();

            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                Vector3 point = Vector3.Lerp(from, to, t);
                // Endpoints stay exact -- only the interior vertices jitter -- so the line always
                // reads as connecting the hit point to the arc target, never missing either one.
                if (i > 0 && i < Segments) point += perp * Random.Range(-JitterMetres, JitterMetres);
                line.SetPosition(i, point);
            }
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) Destroy(gameObject);
        }
    }
}
