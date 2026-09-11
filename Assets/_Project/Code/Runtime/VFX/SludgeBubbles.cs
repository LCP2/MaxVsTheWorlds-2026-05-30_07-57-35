using UnityEngine;
using MaxWorlds.Feel;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Rising, popping bubbles for a body of sludge (MV-769) — a handful of small flattened spheres
    /// that rise from the surface and pop on a staggered loop, so both a Sludge Drone's puddle
    /// (<see cref="MaxWorlds.Enemies.SludgePuddle"/>) and the map's authored sludge lanes
    /// (<c>MapRuntime.BuildSludge</c>) read as one living material instead of a flat scrolling tint.
    ///
    /// Timing/easing goes through <see cref="MaxWorlds.Feel.AnimSequence"/>'s own static ease functions
    /// (no Animator, no tween library, per project_animation_substrate) — each bubble loops on its own
    /// modulo clock rather than a fresh <c>AnimSequence</c> instance per cycle, since a rise-then-pop is
    /// a repeating one-step animation, not the multi-step choreography that class is built for. Each
    /// bubble gets its own phase offset from the same seed so the pops never synchronise. Visual only:
    /// <see cref="Update"/> never runs outside Play mode and this project authors no PlayMode tests, so
    /// nothing here is EditMode-tested (Rule 2 — EditMode asserts resolved gameplay values, never
    /// rendered pixels).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SludgeBubbles : MonoBehaviour
    {
        private const float RiseHeight = 0.12f;
        private const float LoopDuration = 1.8f;
        private const float RiseFraction = 0.7f; // rises across the first 70% of the loop, pops for the rest
        private const float BubbleDiameter = 0.10f;

        private Transform[] _bodies;
        private float[] _phase;
        private float[] _baseY;
        private float _time;

        /// <summary>
        /// Builds and parents a fresh bubble emitter. <paramref name="halfExtents"/> is the (x, z) half
        /// footprint bubbles may scatter within, scaled down so every bubble stays comfortably inside
        /// whatever surface it dresses — a puddle passes its own radius twice, a rectangular sludge
        /// lane tile passes its half-width/half-depth. <paramref name="baseHeight"/> is the local Y the
        /// surface's own top sits at, so a bubble rises from that surface rather than from world zero.
        /// </summary>
        public static SludgeBubbles Attach(Transform parent, string name, Vector2 halfExtents,
            float baseHeight, int seed, int count, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bubbles = go.AddComponent<SludgeBubbles>();
            bubbles.Build(halfExtents, baseHeight, seed, count, tone);
            return bubbles;
        }

        private void Build(Vector2 halfExtents, float baseHeight, int seed, int count, Color tone)
        {
            count = Mathf.Max(0, count);
            _bodies = new Transform[count];
            _phase = new float[count];
            _baseY = new float[count];

            Material mat = StormdrainKit.Unlit(tone, "SludgeBubble");

            for (int i = 0; i < count; i++)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = $"Bubble{i}";
                var col = body.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
                }
                body.transform.SetParent(transform, false);
                body.transform.localScale = new Vector3(BubbleDiameter, BubbleDiameter * 0.55f, BubbleDiameter);

                // Box-scattered at 60% of the given half-extents, so every bubble sits well clear of a
                // puddle's own irregular rim (never at the exact geometric edge, per the same
                // readability instinct as every other scatter in this project).
                float x = (Hash01(seed, i * 2) * 2f - 1f) * halfExtents.x * 0.6f;
                float z = (Hash01(seed, i * 2 + 1) * 2f - 1f) * halfExtents.y * 0.6f;
                body.transform.localPosition = new Vector3(x, baseHeight, z);

                var rend = body.GetComponent<Renderer>();
                if (rend != null && mat != null) rend.sharedMaterial = mat;

                _bodies[i] = body.transform;
                _phase[i] = Hash01(seed, i * 2 + 97) * LoopDuration;
                _baseY[i] = baseHeight;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One evaluation, public so it CAN be driven directly (same "Update() never runs
        /// outside Play mode" reasoning as every other hazard in this roster) even though this ticket's
        /// own AC has no assertion over the rendered result here (Rule 2 — Tier 3 belongs to the
        /// conformance harness, not EditMode).</summary>
        public void Tick(float dt)
        {
            _time += dt;
            for (int i = 0; i < _bodies.Length; i++)
            {
                if (_bodies[i] == null) continue;
                float u = Mathf.Repeat((_time + _phase[i]) / LoopDuration, 1f);
                Vector3 p = _bodies[i].localPosition;
                p.y = _baseY[i] + (u < RiseFraction ? AnimSequence.SmoothStep(u / RiseFraction) * RiseHeight : 0f);
                _bodies[i].localPosition = p;
            }
        }

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
    }
}
