using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-1335: the lane visual every authored <c>current</c> map entity needed but never got — MV-1300
    /// shipped the drift and MV-1301/MV-1307 placed the lanes, but MV-1300's own item 4 (Look) was never
    /// built, so Max was pushed by an invisible rect. Resolves its own on-screen flow direction and
    /// pre-flip pulse off the SAME shared <see cref="MapCurrentReversalRunner.Age"/> clock and the SAME
    /// <see cref="MapGeometry.IsReversedNow"/> rule <see cref="MapGeometry.CurrentDriftAt"/> itself uses
    /// — the visual can therefore never show a direction the drift disagrees with (MV-1335 AC2).
    ///
    /// Every per-frame write lands on its own material instance (<c>ReefCurrentLaneKit.BuildLane</c>
    /// hands one back per lane, never shared/cached) — same "mutate your own instance" reasoning
    /// <see cref="SludgeFlow"/> already follows for a sludge tile's own tint.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("currents")]
    public sealed class MapCurrentLaneVisual : MonoBehaviour
    {
        /// <summary>Seconds before a flip the band starts pulsing (MV-1335 change item 2: "for the
        /// final 1.5 s before a flip the band pulses so the player sees it coming").</summary>
        private const float PulseWindowSec = 1.5f;

        /// <summary>How much extra alpha the pulse's own blink can add at its brightest, on top of the
        /// band/chevron's own baked alpha (<c>CurrentLaneBand.shader</c>'s own <c>_PulseBoost</c>) —
        /// enough to read as a warning without the band ever looking solid.</summary>
        private const float PulseAlphaBoost = 0.35f;

        /// <summary>Blink rate inside the pulse window, in Hz — fast enough to read as "about to flip",
        /// not a slow fade.</summary>
        private const float PulseBlinkHz = 4f;

        public string EntityId { get; private set; }
        public Rect Footprint { get; private set; }
        public Vector3 AuthoredDir { get; private set; }
        public float SpeedMps { get; private set; }
        public float ReverseEverySec { get; private set; }

        private Material _material;
        private float _scrollOffset;

        public void Configure(string entityId, Rect footprint, Vector3 authoredDir, float speedMps,
            float reverseEverySec, Material material)
        {
            EntityId = entityId;
            Footprint = footprint;
            AuthoredDir = authoredDir;
            SpeedMps = speedMps;
            ReverseEverySec = reverseEverySec;
            _material = material;
        }

        /// <summary>This frame's resolved flow direction (MV-1335 AC2) — mirrors
        /// <see cref="MapGeometry.CurrentDriftAt"/>'s own reversal rule exactly, off the same shared
        /// <see cref="MapCurrentReversalRunner.Age"/> clock, so a test (or the real drift) can never
        /// catch this visual disagreeing with the push it is supposed to be showing.</summary>
        public Vector3 ResolvedFlowDirection => MapGeometry.IsReversedNow(ReverseEverySec) ? -AuthoredDir : AuthoredDir;

        /// <summary>0 outside the final <see cref="PulseWindowSec"/> before this lane's next flip,
        /// ramping linearly to 1 at the flip instant. Always 0 for a lane that never reverses
        /// (<paramref name="ReverseEverySec"/> &lt;= 0, MV-1307's own "0 never reverses" convention).</summary>
        public float PulseFactor
        {
            get
            {
                if (ReverseEverySec <= 0f) return 0f;
                float phase = Mathf.Repeat(MapCurrentReversalRunner.Age, ReverseEverySec);
                float timeToFlip = ReverseEverySec - phase;
                return timeToFlip <= PulseWindowSec
                    ? Mathf.Clamp01((PulseWindowSec - timeToFlip) / PulseWindowSec)
                    : 0f;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Public so a test can drive it directly — same "Update() never runs outside Play
        /// mode" idiom as every other per-frame hazard in this roster
        /// (<see cref="SludgeFlow.Update"/>/<c>MaxWorlds.VFX.SludgeBubbles.Tick</c>).</summary>
        public void Tick(float dt)
        {
            if (_material == null) return;

            // CurrentLaneBand.shader reads IN.uv.x directly in metres along the flow axis (same
            // convention ReefCurrentLaneKit.BuildQuad bakes into the mesh), so scrolling is a plain
            // metres/second accumulation -- no unit conversion needed. Sign flips with the resolved
            // direction so a reversal visibly reverses the scroll at the same instant it reverses the
            // drift (MV-1335 AC2), not just the chevron's static orientation.
            float sign = Vector3.Dot(ResolvedFlowDirection, AuthoredDir) >= 0f ? 1f : -1f;
            _scrollOffset += sign * SpeedMps * dt;
            _material.SetFloat("_ScrollOffset", _scrollOffset);

            float pulse = PulseFactor;
            float blink = pulse > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * PulseBlinkHz) : 0f;
            _material.SetFloat("_PulseBoost", pulse * blink * PulseAlphaBoost);
        }
    }
}
