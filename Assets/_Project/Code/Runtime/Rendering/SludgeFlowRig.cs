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
    ///
    /// MV-796: a piece's centre wraps the tile's full run, but the piece itself has extent — so the
    /// naive centre-only placement let up to half a band's length hang off the tile, over the pavement.
    /// <see cref="Apply"/> now clips each piece's leading/trailing edge into the tile's run and scales
    /// it down to fit rather than letting it overhang, so a piece eases out of view as it approaches the
    /// wrap point instead of teleporting through the edge.
    ///
    /// MV-873: this rig no longer ticks itself. It registers with <see cref="SludgeFlowDirector"/> on
    /// enable and unregisters on disable, and the director decides — every frame, for every registered
    /// rig — whether it is close enough to the player to be worth the per-piece work. <see cref="Tick"/>
    /// itself is unchanged so the existing EditMode tests still drive it directly with no director and
    /// no scene running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SludgeFlowRig : MonoBehaviour
    {
        /// <summary>Bands and chevrons scroll together at this speed (MV-785's own number).</summary>
        public const float FastSpeed = 0.35f;

        /// <summary>Foam scrolls slower than the bands (MV-785's own number) — the differential is what
        /// sells the sludge as fluid rather than a conveyor.</summary>
        public const float SlowSpeed = 0.12f;

        /// <summary>MV-873: incremented once per piece inside <see cref="Apply"/>, across every rig —
        /// the measured signal an EditMode test uses to prove a gated-off rig's pieces truly did no
        /// work, rather than trusting that skipping <see cref="Tick"/> implies it. Test-only
        /// instrumentation; production code never reads it.</summary>
        public static int AppliedPieceCount;

        private Vector3 _fastAxis;
        private float _fastRun;
        private Transform[] _fastTransforms = System.Array.Empty<Transform>();
        private Renderer[] _fastRenderers = System.Array.Empty<Renderer>();
        private Vector3[] _fastExtra = System.Array.Empty<Vector3>();
        private float[] _fastPhase = System.Array.Empty<float>();
        private float[] _fastHalfExtent = System.Array.Empty<float>();
        private Vector3[] _fastScale = System.Array.Empty<Vector3>();

        private Vector3 _slowAxis;
        private float _slowRun;
        private Transform[] _slowTransforms = System.Array.Empty<Transform>();
        private Renderer[] _slowRenderers = System.Array.Empty<Renderer>();
        private Vector3[] _slowExtra = System.Array.Empty<Vector3>();
        private float[] _slowPhase = System.Array.Empty<float>();
        private float[] _slowHalfExtent = System.Array.Empty<float>();
        private Vector3[] _slowScale = System.Array.Empty<Vector3>();

        private float _fastTime;
        private float _slowTime;

        /// <param name="fastAxis">Unit vector the bands/chevrons scroll along.</param>
        /// <param name="fastRun">Channel length the bands/chevrons wrap at.</param>
        /// <param name="fastTransforms">Every band and chevron Transform, sharing one timer.</param>
        /// <param name="fastExtra">Each fast Transform's own fixed local offset (cross-axis jitter,
        /// height, and — for a chevron leg — its own diagonal offset), added on top of the scroll.</param>
        /// <param name="fastPhase">Each fast Transform's own starting position along <paramref name="fastAxis"/>.</param>
        /// <param name="fastHalfExtent">Each fast Transform's own half-extent, <b>projected onto
        /// <paramref name="fastAxis"/></b>, at its authored (unscaled) size — a band's is half its
        /// length; a chevron leg leans 45 degrees, so its is the diagonal projection of its length and
        /// thickness, not half its raw length.</param>
        /// <param name="slowAxis">Unit vector the foam scrolls along.</param>
        /// <param name="slowRun">Channel length the foam wraps at.</param>
        /// <param name="slowTransforms">Every foam Transform.</param>
        /// <param name="slowExtra">Each foam Transform's own fixed local offset.</param>
        /// <param name="slowPhase">Each foam Transform's own starting position along <paramref name="slowAxis"/>.</param>
        /// <param name="slowHalfExtent">Each foam Transform's own half-extent projected onto
        /// <paramref name="slowAxis"/> (its radius).</param>
        public void Configure(Vector3 fastAxis, float fastRun, Transform[] fastTransforms, Vector3[] fastExtra, float[] fastPhase, float[] fastHalfExtent,
                              Vector3 slowAxis, float slowRun, Transform[] slowTransforms, Vector3[] slowExtra, float[] slowPhase, float[] slowHalfExtent)
        {
            _fastAxis = fastAxis;
            _fastRun = fastRun;
            _fastTransforms = fastTransforms;
            _fastExtra = fastExtra;
            _fastPhase = fastPhase;
            _fastHalfExtent = fastHalfExtent;
            _fastRenderers = CaptureRenderers(fastTransforms);
            _fastScale = CaptureScales(fastTransforms);

            _slowAxis = slowAxis;
            _slowRun = slowRun;
            _slowTransforms = slowTransforms;
            _slowExtra = slowExtra;
            _slowPhase = slowPhase;
            _slowHalfExtent = slowHalfExtent;
            _slowRenderers = CaptureRenderers(slowTransforms);
            _slowScale = CaptureScales(slowTransforms);

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
            Apply(_fastTransforms, _fastRenderers, _fastExtra, _fastPhase, _fastHalfExtent, _fastScale, _fastAxis, _fastTime, FastSpeed, _fastRun);
            Apply(_slowTransforms, _slowRenderers, _slowExtra, _slowPhase, _slowHalfExtent, _slowScale, _slowAxis, _slowTime, SlowSpeed, _slowRun);
        }

        private static Renderer[] CaptureRenderers(Transform[] transforms)
        {
            var renderers = new Renderer[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
                renderers[i] = transforms[i].GetComponent<Renderer>();
            return renderers;
        }

        private static Vector3[] CaptureScales(Transform[] transforms)
        {
            var scales = new Vector3[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
                scales[i] = transforms[i].localScale;
            return scales;
        }

        private static void Apply(Transform[] transforms, Renderer[] renderers, Vector3[] extra, float[] phase,
                                  float[] halfExtent, Vector3[] scale, Vector3 axis, float time, float speed, float run)
        {
            float halfRun = run * 0.5f;
            for (int i = 0; i < transforms.Length; i++)
            {
                AppliedPieceCount++;
                float coord = Mathf.Repeat(phase[i] + speed * time, run) - halfRun;
                float half = halfExtent[i];

                // `extra` can itself carry a component along the scroll axis — a chevron leg's diagonal
                // offset does — so the piece's TRUE centre along the axis is that component plus the
                // scrolled coord, not the coord alone.
                float extraAlong = Vector3.Dot(extra[i], axis);
                float trueCenter = extraAlong + coord;

                // Clip the piece's own leading/trailing edge into the tile's run instead of letting its
                // centre-only placement carry the edges past it (MV-796) — a piece up to `half` past
                // either wall used to hang off the tile and draw over the pavement.
                float leading = Mathf.Clamp(trueCenter + half, -halfRun, halfRun);
                float trailing = Mathf.Clamp(trueCenter - half, -halfRun, halfRun);
                float clampedSpan = leading - trailing;

                if (clampedSpan <= 0f)
                {
                    renderers[i].enabled = false;
                    continue;
                }

                renderers[i].enabled = true;
                float clippedCenter = (leading + trailing) * 0.5f;
                Vector3 extraPerp = extra[i] - axis * extraAlong;
                transforms[i].localPosition = extraPerp + axis * clippedCenter;
                transforms[i].localScale = scale[i] * (clampedSpan / (2f * half));
            }
        }

        private void OnEnable() => SludgeFlowDirector.Register(this);

        private void OnDisable() => SludgeFlowDirector.Unregister(this);
    }
}
