using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Scrolls a sludge tile's flow bands, chevrons and foam clumps along the channel's flow axis
    /// (MV-785), wrapping each piece back to the start of the channel once it scrolls past the far end
    /// rather than letting it run off the tile.
    ///
    /// <c>MaxWorlds.Feel.AnimSequence</c> is this project's shared VFX-timing substrate and the ticket's
    /// own named mechanism, but it lives in <c>MaxWorlds.Gameplay</c> — which already references
    /// <c>MaxWorlds.Rendering</c> (for <see cref="StormdrainKit"/> itself), so the reverse reference this
    /// rig would need does not compile. This rig instead hand-rolls the exact same "accumulate _time,
    /// read it" idiom AnimSequence itself uses, self-contained in the Rendering assembly where
    /// <see cref="StormdrainKit.DressSludgeTile"/> needs to build and own it. It still evaluates TIME
    /// ONLY plus the Transforms it was configured with — never an Animator, a .anim clip, or a tween
    /// library — and <see cref="Tick"/> is public and takes a plain dt, so it is fully driveable with no
    /// scene running: an EditMode test calls it directly, with no Update() involved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SludgeFlowRig : MonoBehaviour
    {
        /// <summary>Bands and chevrons scroll together at this speed (MV-785's own number).</summary>
        public const float FastSpeed = 0.35f;

        /// <summary>Foam scrolls slower than the bands (MV-785's own number) — the differential is what
        /// sells the sludge as fluid rather than a conveyor.</summary>
        public const float SlowSpeed = 0.12f;

        private Vector3 _fastAxis;
        private float _fastRun;
        private Transform[] _fastTransforms = System.Array.Empty<Transform>();
        private Vector3[] _fastExtra = System.Array.Empty<Vector3>();
        private float[] _fastPhase = System.Array.Empty<float>();

        private Vector3 _slowAxis;
        private float _slowRun;
        private Transform[] _slowTransforms = System.Array.Empty<Transform>();
        private Vector3[] _slowExtra = System.Array.Empty<Vector3>();
        private float[] _slowPhase = System.Array.Empty<float>();

        private float _fastTime;
        private float _slowTime;

        /// <param name="fastAxis">Unit vector the bands/chevrons scroll along.</param>
        /// <param name="fastRun">Channel length the bands/chevrons wrap at.</param>
        /// <param name="fastTransforms">Every band and chevron Transform, sharing one timer.</param>
        /// <param name="fastExtra">Each fast Transform's own fixed local offset (cross-axis jitter,
        /// height, and — for a chevron leg — its own diagonal offset), added on top of the scroll.</param>
        /// <param name="fastPhase">Each fast Transform's own starting position along <paramref name="fastAxis"/>.</param>
        /// <param name="slowAxis">Unit vector the foam scrolls along.</param>
        /// <param name="slowRun">Channel length the foam wraps at.</param>
        /// <param name="slowTransforms">Every foam Transform.</param>
        /// <param name="slowExtra">Each foam Transform's own fixed local offset.</param>
        /// <param name="slowPhase">Each foam Transform's own starting position along <paramref name="slowAxis"/>.</param>
        public void Configure(Vector3 fastAxis, float fastRun, Transform[] fastTransforms, Vector3[] fastExtra, float[] fastPhase,
                              Vector3 slowAxis, float slowRun, Transform[] slowTransforms, Vector3[] slowExtra, float[] slowPhase)
        {
            _fastAxis = fastAxis;
            _fastRun = fastRun;
            _fastTransforms = fastTransforms;
            _fastExtra = fastExtra;
            _fastPhase = fastPhase;

            _slowAxis = slowAxis;
            _slowRun = slowRun;
            _slowTransforms = slowTransforms;
            _slowExtra = slowExtra;
            _slowPhase = slowPhase;

            _fastTime = 0f;
            _slowTime = 0f;

            // Snap every piece to its own starting phase immediately, so the tile looks right on the
            // very first frame rather than at (0,0,0) until Update first runs.
            Tick(0f);
        }

        public void Tick(float dt)
        {
            _fastTime += dt;
            _slowTime += dt;
            Apply(_fastTransforms, _fastExtra, _fastPhase, _fastAxis, _fastTime, FastSpeed, _fastRun);
            Apply(_slowTransforms, _slowExtra, _slowPhase, _slowAxis, _slowTime, SlowSpeed, _slowRun);
        }

        private static void Apply(Transform[] transforms, Vector3[] extra, float[] phase, Vector3 axis,
                                  float time, float speed, float run)
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                float coord = Mathf.Repeat(phase[i] + speed * time, run) - run * 0.5f;
                transforms[i].localPosition = extra[i] + axis * coord;
            }
        }

        private void Update() => Tick(Time.deltaTime);
    }
}
