using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1301 AC1, every sub-case in one test (MV-465 Rule 1): on the LOADED Reef config (the real
    /// <see cref="WorldMapLoader"/> conversion, not the raw JSON), (a) the current-lane count per area
    /// matches the ticket's own table, (b) no lane overlaps a solid cover/hull-fill piece, (c) no lane
    /// comes within 2 m of a gate mouth, and (d) every clam (shed) area has at least one lane within
    /// 4 m of its clam. Every assertion reads a RESOLVED <see cref="MapEntity"/> position out of the
    /// built <see cref="MapData"/> (MV-465 Tier 2) — never an authored constant.
    ///
    /// Fails on the commit before this ticket: <c>world3_config.json</c> authors zero
    /// <c>currents[]</c> anywhere, so every area's expected-count assertion below fails immediately.
    /// </summary>
    public sealed class MV1301ReefCurrentLanesTests
    {
        [Test]
        public void ReefConfig_CurrentLaneCountsOverlapGateClearanceAndClamProximity()
        {
            var asset = Resources.Load<TextAsset>($"{WorldLibrary.ResourceRoot}/{WorldLibrary.World3}");
            Assert.IsNotNull(asset, "world3_config.json missing from Resources");
            Assert.IsTrue(WorldMapLoader.TryLoadJson(asset.text, out MapData map, out string reason),
                $"world3_config.json failed to load: {reason}");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "world3_config.json failed to load through WorldLibrary");

            // --- expected lane counts per the ticket's own table ---
            var expectedCount = new Dictionary<string, int>();
            for (int idx = 1; idx <= 30; idx++)
            {
                string id = $"a{idx}";
                if (idx == 15 || idx == 26 || idx == 30) expectedCount[id] = 0;       // boss areas: none
                else if (idx <= 3) expectedCount[id] = 1;                             // a1-a3: one lane
                else expectedCount[id] = 2;                                           // a4-a14, a16-a25, a27-a29: two lanes
            }

            MapEntity[] currents = map.CurrentEntities();
            var byArea = new Dictionary<string, List<MapEntity>>();
            foreach (MapEntity c in currents)
            {
                int underscoreIdx = c.id.IndexOf('_');
                string areaId = underscoreIdx > 0 ? c.id.Substring(0, underscoreIdx) : c.id;
                if (!byArea.TryGetValue(areaId, out List<MapEntity> list)) byArea[areaId] = list = new List<MapEntity>();
                list.Add(c);
            }

            foreach (var kv in expectedCount)
            {
                int actual = byArea.TryGetValue(kv.Key, out List<MapEntity> list) ? list.Count : 0;
                Assert.AreEqual(kv.Value, actual, $"{kv.Key}: expected {kv.Value} current lane(s), found {actual}");
            }

            // --- no lane overlaps solid cover/hull fill, none within 2 m of a gate mouth ---
            var solidCover = new List<MapEntity>();
            var gates = new List<MapEntity>();
            foreach (MapEntity e in map.entities)
            {
                if (e.Kind == EntityKind.Cover && e.CoverKind == CoverKind.Solid) solidCover.Add(e);
                else if (e.Kind == EntityKind.AreaGate) gates.Add(e);
            }

            foreach (MapEntity lane in currents)
            {
                float lxMin = lane.x - lane.width * 0.5f, lxMax = lane.x + lane.width * 0.5f;
                float lzMin = lane.z - lane.depth * 0.5f, lzMax = lane.z + lane.depth * 0.5f;

                foreach (MapEntity cov in solidCover)
                {
                    float cxMin = cov.x - cov.width * 0.5f, cxMax = cov.x + cov.width * 0.5f;
                    float czMin = cov.z - cov.depth * 0.5f, czMax = cov.z + cov.depth * 0.5f;
                    bool overlaps = lxMax > cxMin && lxMin < cxMax && lzMax > czMin && lzMin < czMax;
                    Assert.IsFalse(overlaps, $"current lane '{lane.id}' overlaps solid cover '{cov.id}'");
                }

                foreach (MapEntity gate in gates)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(lxMin - gate.x, gate.x - lxMax));
                    float dz = Mathf.Max(0f, Mathf.Max(lzMin - gate.z, gate.z - lzMax));
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    Assert.GreaterOrEqual(dist, 2f - 0.01f,
                        $"current lane '{lane.id}' is only {dist:0.00} m from gate '{gate.id}' — needs 2 m clear");
                }
            }

            // --- every clam (shed) area has at least one lane within 4 m of it ---
            foreach (WorldArea area in cfg.areas)
            {
                if (area.id == "stub" || !area.hasShed) continue;
                if (!byArea.TryGetValue(area.id, out List<MapEntity> lanes)) continue; // count assertion above already failed this

                string shedId = area.ShedId(0, 1);
                MapEntity shedEntity = map.Entity(shedId);
                Assert.IsNotNull(shedEntity, $"{area.id}: hasShed but no resolved factory entity '{shedId}'");

                float best = float.MaxValue;
                foreach (MapEntity lane in lanes)
                {
                    float lxMin = lane.x - lane.width * 0.5f, lxMax = lane.x + lane.width * 0.5f;
                    float lzMin = lane.z - lane.depth * 0.5f, lzMax = lane.z + lane.depth * 0.5f;
                    float dx = Mathf.Max(0f, Mathf.Max(lxMin - shedEntity.x, shedEntity.x - lxMax));
                    float dz = Mathf.Max(0f, Mathf.Max(lzMin - shedEntity.z, shedEntity.z - lzMax));
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                }
                Assert.LessOrEqual(best, 4f + 0.01f,
                    $"{area.id}: clam '{shedEntity.id}' is {best:0.00} m from its nearest current lane — needs <= 4 m");
            }
        }
    }
}
