using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1050: World 2's finale area (a21, The Wet Well) grows 3x by area — 44x44 (1936 m²) to 76x76
    /// (5776 m², ~2.98x) — growing east and south only, since a5 sits immediately north of its old N wall
    /// (z=64). g24 (a20 -> a21) is re-expressed so its door keeps the same absolute world position it
    /// held before the resize, even though a21's W-wall span (and so the door's own fractional <c>pos</c>
    /// along it) changed length.
    ///
    /// One new test (testing policy MV-465, Rule 1), asserting RESOLVED values (Tier 2) off the real,
    /// loaded <see cref="MapData"/> — never the authored JSON numbers re-read against themselves: the
    /// built zone's own XZ bounds (<see cref="MapZone.XMin"/>/XMax/ZMin/ZMax, derived from
    /// <see cref="WorldArea.CenterXz"/> at load time) and the built <c>g24</c> <see cref="MapEntity"/>'s
    /// own resolved door-mouth position (<see cref="WorldMapLoader"/>'s <c>ResolveDoorPosition</c>).
    ///
    /// Fails on base commit 12419fa: a21 there is still 44x44 (ZMin 20, ZMax 64, XMax 136), so the size/
    /// bounds assertions below fail outright ("Expected: 76 But was: 44").
    /// </summary>
    public sealed class MV1050A21FinaleResizeTests
    {
        [Test]
        public void A21_Is76x76_AndG24DoorHoldsItsOldAbsolutePosition()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "world2_config.json failed to load");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string mapReason), mapReason);

            WorldArea a21 = cfg.Area("a21");
            Assert.IsNotNull(a21, "area 'a21' not found");
            Assert.AreEqual(21, a21.index, "a21 must still be World 2's final combat area");

            // The BUILT zone's own resolved bounds (centre + half-extents), not the raw origin/size JSON.
            MapZone zone = map.Zone($"area{a21.index}");
            Assert.IsNotNull(zone, "a21's own zone ('area21') was never built");

            Assert.AreEqual(76f, zone.width, 0.01f, "a21's resolved zone width must be 76 m (3x its old 44 m area)");
            Assert.AreEqual(76f, zone.depth, 0.01f, "a21's resolved zone depth must be 76 m (3x its old 44 m area)");
            Assert.AreEqual(92f, zone.XMin, 0.01f, "a21's W wall must stay at its old X (growth is east/south only)");
            Assert.AreEqual(168f, zone.XMax, 0.01f, "a21's E wall must move 32 m east for the 3x-area resize");
            Assert.AreEqual(64f, zone.ZMax, 0.01f, "a21's N wall must stay at its old Z — a5 sits immediately north of it");
            Assert.AreEqual(-12f, zone.ZMin, 0.01f, "a21's S wall must move 32 m south for the 3x-area resize");

            // g24's own resolved door mouth (WorldMapLoader.ResolveDoorPosition) must sit at the exact
            // same absolute world position (92, 43) it held before the resize, even though its authored
            // 'pos' fraction along a21's own (now longer) W-wall span had to change to land there.
            MapEntity g24 = map.Entity("g24");
            Assert.IsNotNull(g24, "gate 'g24' was never built");
            Assert.AreEqual(EntityKind.AreaGate, g24.Kind, "g24 must build as an AreaGate");
            Assert.AreEqual(92f, g24.x, 0.01f, "g24's resolved door mouth X must be unchanged");
            Assert.AreEqual(43f, g24.z, 0.01f, "g24's resolved door mouth Z must be unchanged by the resize");
        }
    }
}
