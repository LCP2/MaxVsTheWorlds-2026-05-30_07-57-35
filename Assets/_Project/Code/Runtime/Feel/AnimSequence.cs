using UnityEngine;

namespace MaxWorlds.Feel
{
    /// <summary>Pure eases over normalised <c>u</c> in [0,1]. Static functions only, dispatched by
    /// <see cref="AnimSequence"/> through a plain switch on <see cref="AnimEase"/> — no closures and
    /// nothing held per step beyond the enum value itself.</summary>
    public enum AnimEase
    {
        Linear,
        SmoothStep,
        InQuad,
        OutQuad,
        InOutQuad,
        OutBack,
        OutElastic
    }

    public readonly struct AnimStep
    {
        public readonly float Delay;
        public readonly float Duration;
        public readonly AnimEase Ease;

        public AnimStep(float delay, float duration, AnimEase ease)
        {
            Delay = delay;
            Duration = duration;
            Ease = ease;
        }
    }

    /// <summary>
    /// MV-684 — declarative, allocation-free substrate for scripted VFX timing. Every hand-rolled
    /// "accumulate _t, Clamp01(_t / _duration), Lerp" pattern in the VFX folder re-implements this by
    /// hand; this is the shared version.
    ///
    /// AnimSequence evaluates TIME ONLY — it never touches a Transform, Material or GameObject. The
    /// caller reads <see cref="Progress"/> and applies it to whatever it owns. That separation is what
    /// keeps this EditMode-testable with no scene: nothing here depends on the engine being alive.
    ///
    /// The step array is caller-sized and stored by reference at construction — no copy, no per-frame
    /// allocation. <see cref="Tick"/> and <see cref="Progress"/> touch no heap: no boxing, no LINQ,
    /// no indirect dispatch, just field reads and arithmetic.
    /// </summary>
    public sealed class AnimSequence
    {
        private readonly AnimStep[] _steps;
        private readonly float _endTime;
        private float _time;

        public AnimSequence(AnimStep[] steps)
        {
            _steps = steps;
            float maxEnd = 0f;
            for (int i = 0; i < steps.Length; i++)
            {
                float end = steps[i].Delay + steps[i].Duration;
                if (end > maxEnd) maxEnd = end;
            }
            _endTime = maxEnd;
        }

        public bool IsComplete => _time >= _endTime;

        public void Tick(float dt)
        {
            _time += dt;
        }

        /// <summary>Eased 0..1 progress of step <paramref name="stepIndex"/> at the current time —
        /// exactly 0 before its <see cref="AnimStep.Delay"/> has elapsed, exactly 1 at and after
        /// <see cref="AnimStep.Delay"/> + <see cref="AnimStep.Duration"/>.</summary>
        public float Progress(int stepIndex)
        {
            AnimStep step = _steps[stepIndex];
            float elapsed = _time - step.Delay;
            if (elapsed <= 0f) return Ease(step.Ease, 0f);

            float duration = Mathf.Max(step.Duration, 0.0001f);
            float u = elapsed >= duration ? 1f : elapsed / duration;
            return Ease(step.Ease, u);
        }

        private static float Ease(AnimEase ease, float u)
        {
            switch (ease)
            {
                case AnimEase.Linear: return Linear(u);
                case AnimEase.SmoothStep: return SmoothStep(u);
                case AnimEase.InQuad: return InQuad(u);
                case AnimEase.OutQuad: return OutQuad(u);
                case AnimEase.InOutQuad: return InOutQuad(u);
                case AnimEase.OutBack: return OutBack(u);
                case AnimEase.OutElastic: return OutElastic(u);
                default: return u;
            }
        }

        public static float Linear(float u) => u;

        public static float SmoothStep(float u) => u * u * (3f - 2f * u);

        public static float InQuad(float u) => u * u;

        public static float OutQuad(float u) => 1f - (1f - u) * (1f - u);

        public static float InOutQuad(float u) =>
            u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) / 2f;

        public static float OutBack(float u)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float t = u - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        public static float OutElastic(float u)
        {
            const float c4 = 2f * Mathf.PI / 3f;
            if (u <= 0f) return 0f;
            if (u >= 1f) return 1f;
            return Mathf.Pow(2f, -10f * u) * Mathf.Sin((u * 10f - 0.75f) * c4) + 1f;
        }
    }
}
