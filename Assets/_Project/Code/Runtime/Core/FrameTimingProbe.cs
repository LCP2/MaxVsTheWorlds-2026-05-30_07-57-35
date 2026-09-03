using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>The raw Unity call <see cref="FrameTimingProbe"/> wraps, isolated behind an interface
    /// so a test can inject a fake with known values instead — the "small source" a test drives with
    /// hand-picked timings, the same idea <see cref="FpsMeter.Tick"/> uses an injected clock for.</summary>
    public interface IFrameTimingSource
    {
        /// <summary>Captures and returns the latest single-frame timing sample into
        /// <paramref name="buffer"/>[0]. Returns the number of valid samples written (0 or 1).</summary>
        uint CaptureLatest(FrameTiming[] buffer);
    }

    internal sealed class UnityFrameTimingSource : IFrameTimingSource
    {
        public uint CaptureLatest(FrameTiming[] buffer)
        {
            FrameTimingManager.CaptureFrameTimings();
            return FrameTimingManager.GetLatestTimings(1, buffer);
        }
    }

    /// <summary>
    /// MV-663: the "?" overlay's ms/frame line is derived from fps (<see cref="FpsMeter.FrameMs"/>),
    /// which cannot tell an idle-capped frame from a GPU-saturated one — both hold 60 fps and print
    /// the same number. This wraps the already-enabled <c>enableFrameTimingStats</c> instrument
    /// (<c>UnityEngine.FrameTimingManager</c>, turned on by MV-574) to expose the measured CPU/GPU
    /// cost of a frame as a second, differently-sourced line beside the fps one — it does not replace
    /// <see cref="FpsMeter"/>.
    ///
    /// <see cref="HasReading"/> is a first-class, DISPLAYED state, not a silent zero:
    /// <c>FrameTimingManager</c> reports nothing on some platforms and for the first frames after
    /// start, and printing "0 ms" there would repeat the exact failure <see cref="FpsMeter"/>'s own
    /// doc comment describes — a confident reading from an instrument that hasn't measured anything
    /// yet.
    /// </summary>
    public sealed class FrameTimingProbe
    {
        private readonly IFrameTimingSource _source;
        private readonly FrameTiming[] _buffer = new FrameTiming[1];

        public FrameTimingProbe() : this(new UnityFrameTimingSource()) { }

        /// <summary>Test seam — production code always uses the parameterless constructor.</summary>
        public FrameTimingProbe(IFrameTimingSource source)
        {
            _source = source;
        }

        public float CpuFrameTimeMs { get; private set; }
        public float CpuMainThreadFrameTimeMs { get; private set; }
        public float CpuRenderThreadFrameTimeMs { get; private set; }
        public float GpuFrameTimeMs { get; private set; }

        /// <summary>True once a real measurement exists for the most recent <see cref="Tick"/>. False
        /// is a legitimate, displayed state — see the class comment — never printed as a zero
        /// reading.</summary>
        public bool HasReading { get; private set; }

        /// <summary>Call once per rendered frame — same site Bootstrap already ticks FpsMeter from.</summary>
        public void Tick()
        {
            uint count = _source.CaptureLatest(_buffer);
            if (count == 0)
            {
                HasReading = false;
                return;
            }

            FrameTiming t = _buffer[0];
            CpuFrameTimeMs = (float)t.cpuFrameTime;
            CpuMainThreadFrameTimeMs = (float)t.cpuMainThreadFrameTime;
            CpuRenderThreadFrameTimeMs = (float)t.cpuRenderThreadFrameTime;
            GpuFrameTimeMs = (float)t.gpuFrameTime;
            HasReading = true;
        }
    }
}
