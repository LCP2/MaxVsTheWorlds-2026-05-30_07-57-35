using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-766: World 2 renders with the Backyard's lawn-green floor and fence-brown walls on a
    /// World 2 map with World 2's HUD. Five candidate causes were traced through the source and all
    /// five were eliminated — on paper the merged code resolves World 2 correctly, so static reading
    /// has run out. <see cref="BackyardPath.BuildWorldProbeLine"/> does not exist before this ticket,
    /// so this fails to COMPILE on the base commit — the same "doesn't exist there yet" failure
    /// MV755StormdrainDressingTests documents for its own base commit.
    ///
    /// The test reads the probe's OWN output, not <see cref="MaterialLibrary.Palette"/> directly —
    /// asserting the palette is what you just set it to would prove nothing about whether the probe
    /// actually reports it.
    /// </summary>
    public sealed class MV766WorldProbeTests
    {
        [Test]
        public void ProbeReportsResolvedWorldState()
        {
            BiomePalette original = MaterialLibrary.Palette;
            try
            {
                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                string stormdrainLine = BackyardPath.BuildWorldProbeLine();
                Assert.IsTrue(stormdrainLine.Contains("Stormdrain"),
                    $"probe line must report the resolved Stormdrain palette; got '{stormdrainLine}'");

                MaterialLibrary.Palette = BiomePalette.Backyard;
                string backyardLine = BackyardPath.BuildWorldProbeLine();
                Assert.IsTrue(backyardLine.Contains("Backyard"),
                    $"probe line must report the resolved Backyard palette; got '{backyardLine}'");
            }
            finally
            {
                MaterialLibrary.Palette = original;
            }
        }
    }
}
