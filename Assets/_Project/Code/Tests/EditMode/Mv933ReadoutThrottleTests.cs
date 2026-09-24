using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-933 — the debug readout cost up to 104.9 ms/frame in World 2 a10 because its text (including
    /// <c>Bootstrap.WorldProbeLineProvider</c>'s scene-walking Find calls) was rebuilt on every single
    /// <c>OnGUI</c> invocation — at least twice per rendered frame, more with queued input events — with
    /// no cache between calls. The fix's AC: rebuilt no more than 4 times per second, cached in between.
    ///
    /// <see cref="Bootstrap.ShouldRebuildReadout"/> does not exist before this ticket, so this fails to
    /// compile on the base commit — the same "doesn't exist there yet" failure MV766WorldProbeTests
    /// documents for its own base commit.
    ///
    /// Drives the throttle directly with a simulated clock (a pure static predicate, same
    /// "extract for testability" idiom as <see cref="Bootstrap.ShouldShowDebugOverlay"/>) rather than
    /// instantiating Bootstrap or calling OnGUI, which only ever run in Play mode.
    /// </summary>
    public sealed class Mv933ReadoutThrottleTests
    {
        [Test]
        public void RebuildsNoMoreThanFourTimesAcross60SimulatedFramesAtSixtyFps()
        {
            const float refreshSeconds = 0.25f;
            const float simulatedFrameSeconds = 1f / 60f;

            float now = 0f;
            float lastBuiltAt = float.NegativeInfinity;
            int rebuildCount = 0;

            for (int frame = 0; frame < 60; frame++)
            {
                now += simulatedFrameSeconds;
                if (Bootstrap.ShouldRebuildReadout(now, lastBuiltAt, refreshSeconds))
                {
                    rebuildCount++;
                    lastBuiltAt = now;
                }
            }

            Assert.LessOrEqual(rebuildCount, 4,
                $"the readout must rebuild at most 4 times across 60 simulated frames (~1s at 60 fps); rebuilt {rebuildCount} times");
        }
    }
}
