using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1349 (Reef relook 1/4). Resolved-value checks (Tier 2), nothing re-read from its own authored
    /// constant: (a) the Reef ROW's look (<see cref="WorldDefinition.Look"/>) has an ambient sky Rec.709
    /// luminance in 0.15-0.35; (b) the real world config's area a1 gets at least one amber lamp on every
    /// room-facing wall longer than 4 m, as planned by <see cref="ReefAreaKit.PlanWallLamps"/> from the
    /// authored walls; (c) no Reef area plans more than 2 realtime point lights.
    /// Fails on base b1a619d: the row's ambient sky luminance is ~0.52 and no lamp planner exists.
    /// </summary>
    public sealed class MV1349ReefLampsTests
    {
        [Test]
        public void ReefLooksDarkWet_EveryLongA1WallHasALamp_AndRealtimeLightsAreCapped()
        {
            WorldDefinition reef = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));
            Color sky = reef.Look.AmbientSky;
            float luma = 0.2126f * sky.r + 0.7152f * sky.g + 0.0722f * sky.b;
            Assert.That(luma, Is.InRange(0.15f, 0.35f), $"Reef ambient sky luminance {luma:F3} must be 0.15-0.35");

            WorldConfig cfg = WorldLibrary.Load(reef.ConfigKey);
            Assert.IsNotNull(cfg, "Reef config must load");
            const float wallThickness = 0.5f;

            WorldArea a1 = null;
            foreach (WorldArea a in cfg.areas) if (a != null && a.id == "a1") a1 = a;
            Assert.IsNotNull(a1, "area a1 must exist");

            List<ReefAreaKit.WallLampSite> a1Sites = ReefAreaKit.PlanWallLamps(a1, cfg, wallThickness);
            foreach (Wall wall in new[] { Wall.N, Wall.E, Wall.S, Wall.W })
            {
                Span span = a1.WallSpan(wall);
                if (span.Max - span.Min <= 4f) continue;
                int lamps = a1Sites.FindAll(s => s.wall == wall).Count;
                Assert.GreaterOrEqual(lamps, 1, $"a1 wall {wall} ({span.Max - span.Min:F1} m) needs at least one lamp");
            }

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null) continue;
                int realtime = ReefAreaKit.PlanWallLamps(area, cfg, wallThickness).FindAll(s => s.realtime).Count;
                Assert.LessOrEqual(realtime, 2, $"area {area.id} plans {realtime} realtime lamps (max 2)");
            }
        }
    }
}
