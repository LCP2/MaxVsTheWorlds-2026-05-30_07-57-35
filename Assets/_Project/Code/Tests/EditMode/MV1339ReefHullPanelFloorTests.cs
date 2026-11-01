using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1339: after MV-1334 the Reef floor still read badly to Lee — "the lines on the floor are
    /// fewer now but it doesn't make it better" and "dark disks that you can hardly see" (the reactor
    /// drums). Direction C replaces the floor's circuit-inlay emission entirely with big 6 m hull
    /// panels (each split into 2 m plates with faint seams, heavy near-black seams between panels),
    /// moves the room's glow to the wall bases, and gives every reactor drum a bright orange rim plus
    /// glowing cyan core so it reads as a lit machine from above.
    ///
    /// Three RESOLVED-value checks (testing policy Tier 2), none a restated authored constant:
    /// (a) AC1 — the real deck material carries no emission map and a black _EmissionColor; (b) the
    /// baked albedo's own 2 m plate / 6 m panel period, found by sampling the actual pixel data, never
    /// a hardcoded array; (c) a built <see cref="ReefKit.BuildDrum"/> actually contains a rim child
    /// wearing <see cref="WorldMaterials.M_Hazard"/> and a core child wearing
    /// <see cref="WorldMaterials.M_Circuit_Cyan"/>.
    ///
    /// Fails on the base commit: the deck material still carries a non-null emission map and a
    /// non-black resolved _EmissionColor (MV-1334's own dimmed D3D/non-D3D pair), the baked albedo's
    /// repeating unit is a 4-cell/8 m supertile with no near-black seam every 6 m, and
    /// <see cref="ReefKit.BuildDrum"/> builds no child named "Rim" at all.
    /// </summary>
    public sealed class MV1339ReefHullPanelFloorTests
    {
        [Test]
        public void ReefDeck_NoEmission_SixMetrePanelTwoMetrePlatePeriod_AndDrumsReadAsLitMachines()
        {
            // --- AC1: no circuit inlay on the floor. ---
            Material floorMat = WorldMaterials.M_ShipFloor;
            Assert.IsNotNull(floorMat, "World 3's deck material must resolve");
            Assert.IsNull(floorMat.GetTexture("_EmissionMap"),
                "the deck material must carry no emission map once the floor's own circuit inlay is removed");

            Color emission = floorMat.GetColor("_EmissionColor");
            Assert.AreEqual(0f, emission.r, 0.0001f, "resolved deck _EmissionColor.r must be black");
            Assert.AreEqual(0f, emission.g, 0.0001f, "resolved deck _EmissionColor.g must be black");
            Assert.AreEqual(0f, emission.b, 0.0001f, "resolved deck _EmissionColor.b must be black");

            // --- The baked albedo's own 2 m plate / 6 m panel period (ticket's own numbers, used as
            // literals here rather than a new named constant so this test still compiles — and still
            // means something — against the pre-fix base commit). One texel column per plate's own
            // mean luminance, sampled through the vertical centre of the bake so plate/panel seams
            // (which live at a cell's own edge) never contaminate a "plate interior" sample. ---
            Texture2D albedo = StylizedTextures.ReefDeckAlbedo();
            int size = albedo.width;
            Color32[] px = albedo.GetPixels32();

            const int PlatesPerPanel = 3; // ticket: 6 m panel / 2 m plate
            const int CellsAcross = PlatesPerPanel * 2; // a 2-panel checker is the smallest unit that still tiles seamlessly
            int cellSize = size / CellsAcross;
            Assert.Greater(cellSize, 4, "precondition: the baked supertile must be big enough to resolve a 6-cell grid");

            // Row 1 (the middle plate of the first panel row), sampled at ITS OWN cell centre — not
            // size/2, which can land exactly on a panel-boundary row depending on size/CellsAcross
            // rounding (a plate's own top/bottom edge, right where the heavy seam lives) and silently
            // contaminate every "plate interior" sample with seam darkening.
            int midRow = 1 * cellSize + cellSize / 2;

            float CellLuma(int gx)
            {
                int x = gx * cellSize + cellSize / 2;
                Color32 c = px[midRow * size + Mathf.Min(x, size - 1)];
                return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            }

            float panelALuma = (CellLuma(0) + CellLuma(1) + CellLuma(2)) / 3f;
            float panelBLuma = (CellLuma(3) + CellLuma(4) + CellLuma(5)) / 3f;
            float panelDelta = Mathf.Abs(panelALuma - panelBLuma);
            Assert.That(panelDelta, Is.GreaterThan(10f),
                $"adjacent 6 m panels must read as two distinct navy tones (panel A ~{panelALuma:F1}, panel B ~{panelBLuma:F1})");

            // Within one panel, every plate must read close to its own panel's tone (the 2 m plate
            // period's own subtle per-plate variation), clearly closer to its own panel's mean than
            // the gap BETWEEN panels — i.e. the 6 m panel boundary, not the 2 m plate boundary, is
            // where the big tone jump happens. Relative to panelDelta rather than a hardcoded byte
            // count, since the bake stores a multiplier ratio, not final composited luminance.
            for (int gx = 0; gx < 3; gx++)
                Assert.That(Mathf.Abs(CellLuma(gx) - panelALuma), Is.LessThan(panelDelta * 0.5f),
                    $"plate {gx} (inside panel A) should read close to panel A's own tone, not drift toward panel B's " +
                    $"(plate {CellLuma(gx):F1}, panel A mean {panelALuma:F1}, panel delta {panelDelta:F1})");

            // A heavy, near-black seam must sit at the panel boundary (x = 3 cells in) — measured right
            // at the boundary column, not a plate interior.
            int boundaryX = Mathf.Min(3 * cellSize, size - 1);
            Color32 boundary = px[midRow * size + boundaryX];
            float boundaryLuma = 0.2126f * boundary.r + 0.7152f * boundary.g + 0.0722f * boundary.b;
            Assert.That(boundaryLuma, Is.LessThan(Mathf.Min(panelALuma, panelBLuma) * 0.6f),
                $"the 6 m panel seam ({boundaryLuma:F1}) must read markedly darker than either panel's own plate tone " +
                $"(A {panelALuma:F1}, B {panelBLuma:F1})");

            // --- Every reactor drum builds a bright orange rim and a glowing cyan core. ---
            var probeRoot = new GameObject("MV1339 Drum Probe");
            try
            {
                GameObject drum = ReefKit.BuildDrum(probeRoot.transform, Vector3.zero, new Vector3(3f, 2f, 3f));
                Assert.IsNotNull(drum, "BuildDrum must still build a drum");

                Transform rim = FindChildNamed(drum.transform, "Rim");
                Assert.IsNotNull(rim, "the drum must build a top-edge rim so it reads as lit, not a dark disc");
                Renderer rimRenderer = rim.GetComponent<Renderer>();
                Assert.IsNotNull(rimRenderer, "the rim must carry a renderer");
                Assert.AreSame(WorldMaterials.M_Hazard, rimRenderer.sharedMaterial,
                    "the drum's rim must wear the bright orange hazard material");

                Transform core = FindChildNamed(drum.transform, "Core");
                Assert.IsNotNull(core, "the drum must keep its glowing cyan core");
                Renderer coreRenderer = core.GetComponent<Renderer>();
                Assert.IsNotNull(coreRenderer, "the core must carry a renderer");
                Assert.AreSame(WorldMaterials.M_Circuit_Cyan, coreRenderer.sharedMaterial,
                    "the drum's core must wear the glowing cyan circuit material");
            }
            finally
            {
                Object.DestroyImmediate(probeRoot);
            }
        }

        private static Transform FindChildNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
