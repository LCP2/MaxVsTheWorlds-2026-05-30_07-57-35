using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1324 AC1, every sub-case in one test (MV-465 Rule 1): over the LOADED Mothership config via
    /// the real <see cref="MaxWorlds.Arena.WorldMapLoader"/>/<see cref="MaxWorlds.Arena.WorldLibrary"/>
    /// conversion. Carries every MV-1323 sub-case (6 secret cover pieces, all eligible, no two in one
    /// area, >= 1 per third; 2 vaults with a carriesKey garrison entry and the 1.5 m clearance; 2
    /// challenge pads with the 3 m solid-cover clearance; 1 repair bay in areas 18-22; <= 2 extras per
    /// area; none in a boss/entry/exit area; the six flagged pieces' other fields unchanged) PLUS this
    /// ticket's own Mothership-only additions: no vault or repair bay inside any airlock's 12 m pull
    /// zone (<see cref="MaxWorlds.Factories.AirlockPanel"/>'s own <c>PullRadius</c>), at least one of
    /// the two challenge pads sits 5-8 m directly in front of an airlock (along its resolved
    /// <see cref="MaxWorlds.Arena.MapEntity.facing"/>), and no extra (vault/pad/repair bay) sits within
    /// 6 m of an Assembly Cradle (<see cref="MaxWorlds.Arena.EntityKind.Cradle"/>). Every assertion
    /// reads a RESOLVED <see cref="MaxWorlds.Arena.MapEntity"/> off the real loaded map (MV-465 Tier 2)
    /// — the one exception is a vault's carriesKey garrison entry, read off the authored
    /// <see cref="MaxWorlds.Arena.WorldConfig"/> the same way <see cref="MaxWorlds.Arena.Garrison"/>
    /// itself does, same as MV1323CityReefExtrasTests.
    ///
    /// Fails on the commit before this ticket: mothership_config.json authors no secret/vault/
    /// challenge/repairBay anywhere, so every per-world count assertion below fails immediately
    /// (0 != 6 secrets, 0 != 2 vaults, 0 != 2 pads, 0 != 1 repair bay).
    /// </summary>
    public sealed class MV1324MothershipExtrasTests
    {
        private const float GateClearance = 2f;
        private const float VaultMargin = 1.5f;
        private const float PadClearance = 3f;
        private const float AirlockPullRadius = 12f;
        private const float CradleClearance = 6f;
        private static readonly string[] EliteExcludedKinds = { "turret", "dreadnought", "lurker", "skitter" };

        private static readonly Dictionary<string, Vector2> FacingVectors = new Dictionary<string, Vector2>
        {
            ["N"] = new Vector2(0f, 1f),
            ["S"] = new Vector2(0f, -1f),
            ["E"] = new Vector2(1f, 0f),
            ["W"] = new Vector2(-1f, 0f),
        };

        [Test]
        public void MothershipConfig_SecretsVaultsPadsAndRepairBayMeetSpec()
        {
            string worldKey = MaxWorlds.Arena.WorldLibrary.Mothership;
            var asset = Resources.Load<TextAsset>($"{MaxWorlds.Arena.WorldLibrary.ResourceRoot}/{worldKey}");
            Assert.IsNotNull(asset, $"{worldKey}.json missing from Resources");
            Assert.IsTrue(
                MaxWorlds.Arena.WorldMapLoader.TryLoadJson(asset.text, out MaxWorlds.Arena.MapData map, out string reason),
                $"failed to load: {reason}");

            MaxWorlds.Arena.WorldConfig cfg = MaxWorlds.Arena.WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"WorldLibrary failed to load {worldKey}");

            var areaById = new Dictionary<string, MaxWorlds.Arena.WorldArea>();
            foreach (MaxWorlds.Arena.WorldArea a in cfg.areas) areaById[a.id] = a;

            bool IsExcludedArea(string areaId) =>
                areaById.TryGetValue(areaId, out MaxWorlds.Arena.WorldArea a) &&
                (a.IsEntryRole || a.IsBossRole || a.IsExitRole);

            var gates = new List<MaxWorlds.Arena.MapEntity>();
            var allCover = new List<MaxWorlds.Arena.MapEntity>();
            var secrets = new List<MaxWorlds.Arena.MapEntity>();
            var vaults = new List<MaxWorlds.Arena.MapEntity>();
            var pads = new List<MaxWorlds.Arena.MapEntity>();
            var bays = new List<MaxWorlds.Arena.MapEntity>();
            var airlocks = new List<MaxWorlds.Arena.MapEntity>();
            var cradles = new List<MaxWorlds.Arena.MapEntity>();

            foreach (MaxWorlds.Arena.MapEntity e in map.entities)
            {
                switch (e.Kind)
                {
                    case MaxWorlds.Arena.EntityKind.AreaGate: gates.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Cover:
                        allCover.Add(e);
                        if (e.secret) secrets.Add(e);
                        break;
                    case MaxWorlds.Arena.EntityKind.Vault: vaults.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Challenge: pads.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.RepairBay: bays.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Airlock: airlocks.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Cradle: cradles.Add(e); break;
                }
            }

            // --- Secrets: exactly 6, all eligible, no two in one area, >= 1 per third ---
            Assert.AreEqual(6, secrets.Count, $"expected 6 secret cover pieces, found {secrets.Count}");

            Dictionary<string, PreExistingCoverFields> expectedFields = ExpectedPreExistingSecretFields();

            var secretAreas = new HashSet<string>();
            foreach (MaxWorlds.Arena.MapEntity s in secrets)
            {
                string areaId = AreaOf(s.id);
                Assert.IsTrue(secretAreas.Add(areaId), $"secret '{s.id}' shares area '{areaId}' with another secret");
                Assert.IsFalse(IsExcludedArea(areaId), $"secret '{s.id}' is in a boss/entry/exit area");

                Assert.AreEqual(MaxWorlds.Arena.CoverKind.Solid, s.CoverKind, $"secret '{s.id}' is not solid cover");
                float footprint = s.width * s.depth;
                Assert.LessOrEqual(footprint, 10f, $"secret '{s.id}' footprint {footprint} exceeds the 10 sqm destructible-cover cap");
                bool isHull = s.dressing.IndexOf("bulkhead", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isHull) Assert.LessOrEqual(footprint, 6f, $"secret '{s.id}' is a hull piece over the 6 sqm carve-out cap");
                Assert.Greater(s.hp, 0f, $"secret '{s.id}' has hp <= 0");

                foreach (MaxWorlds.Arena.MapEntity g in gates)
                {
                    float d = Vector2.Distance(new Vector2(s.x, s.z), new Vector2(g.x, g.z));
                    Assert.GreaterOrEqual(d, GateClearance - 0.01f, $"secret '{s.id}' is only {d:0.00} m from gate '{g.id}'");
                }

                Assert.IsTrue(expectedFields.TryGetValue(s.id, out PreExistingCoverFields expected),
                    $"secret '{s.id}' is not one of the six pieces this ticket authored as secret");
                Assert.AreEqual(expected.X, s.x, 0.001f, $"secret '{s.id}' x moved from its pre-existing value");
                Assert.AreEqual(expected.Z, s.z, 0.001f, $"secret '{s.id}' z moved from its pre-existing value");
                Assert.AreEqual(expected.Width, s.width, 0.001f, $"secret '{s.id}' width changed from its pre-existing value");
                Assert.AreEqual(expected.Height, s.height, 0.001f, $"secret '{s.id}' height changed from its pre-existing value");
                Assert.AreEqual(expected.Depth, s.depth, 0.001f, $"secret '{s.id}' depth changed from its pre-existing value");
                Assert.AreEqual(expected.Shape, s.shape, $"secret '{s.id}' shape changed from its pre-existing value");
                Assert.AreEqual(expected.Dressing, s.dressing, $"secret '{s.id}' dressing changed from its pre-existing value");
            }

            int[][] thirds = { new[] { 2, 10 }, new[] { 11, 20 }, new[] { 21, 29 } };
            foreach (int[] third in thirds)
            {
                bool any = false;
                foreach (string areaId in secretAreas)
                {
                    int idx = ParseAreaIndex(areaId);
                    if (idx >= third[0] && idx <= third[1]) { any = true; break; }
                }
                Assert.IsTrue(any, $"no secret in third [{third[0]},{third[1]}]");
            }

            // --- Vaults: area >= 5, not excluded, 1.5 m clear of cover/gate, carriesKey robot in the
            // same area, PLUS this ticket's own: >= 12 m from every airlock's pull zone, >= 6 m from
            // every Assembly Cradle. ---
            Assert.AreEqual(2, vaults.Count, $"expected 2 vaults, found {vaults.Count}");
            foreach (MaxWorlds.Arena.MapEntity v in vaults)
            {
                string areaId = AreaOf(v.id);
                Assert.IsTrue(areaById.ContainsKey(areaId), $"vault '{v.id}' area '{areaId}' not found");
                Assert.GreaterOrEqual(ParseAreaIndex(areaId), 5, $"vault '{v.id}' area index < 5");
                Assert.IsFalse(IsExcludedArea(areaId), $"vault '{v.id}' is in a boss/entry/exit area");

                Rect box = RectOf(v);
                foreach (MaxWorlds.Arena.MapEntity c in allCover) AssertClear(box, RectOf(c), VaultMargin, v.id, c.id);
                foreach (MaxWorlds.Arena.MapEntity g in gates) AssertClear(box, PointRect(g), VaultMargin, v.id, g.id);

                foreach (MaxWorlds.Arena.MapEntity al in airlocks)
                {
                    float d = Vector2.Distance(new Vector2(v.x, v.z), new Vector2(al.x, al.z));
                    Assert.GreaterOrEqual(d, AirlockPullRadius - 0.01f,
                        $"vault '{v.id}' is only {d:0.00} m from airlock '{al.id}' (inside its {AirlockPullRadius} m pull zone)");
                }
                foreach (MaxWorlds.Arena.MapEntity cr in cradles)
                {
                    float d = Vector2.Distance(new Vector2(v.x, v.z), new Vector2(cr.x, cr.z));
                    Assert.GreaterOrEqual(d, CradleClearance - 0.01f,
                        $"vault '{v.id}' is only {d:0.00} m from cradle '{cr.id}'");
                }

                MaxWorlds.Arena.WorldArea area = areaById[areaId];
                bool hasKeyHolder = false;
                foreach (MaxWorlds.Arena.WorldGarrisonEntry entry in area.garrison)
                {
                    if (entry == null || !entry.carriesKey) continue;
                    hasKeyHolder = true;
                    string kind = (entry.kind ?? "").ToLowerInvariant();
                    Assert.IsFalse(Array.IndexOf(EliteExcludedKinds, kind) >= 0,
                        $"area '{areaId}' carriesKey entry has excluded kind '{entry.kind}'");
                }
                Assert.IsTrue(hasKeyHolder, $"vault '{v.id}' area '{areaId}' has no carriesKey garrison entry");
            }

            // --- Challenge pads: area >= 4, not excluded, 3 m clear of solid cover at the centre,
            // >= 6 m from every cradle, and at least one of the two sits 5-8 m directly in front of an
            // airlock (this ticket's own rule). ---
            Assert.AreEqual(2, pads.Count, $"expected 2 challenge pads, found {pads.Count}");
            bool anyPadFrontsAnAirlock = false;
            foreach (MaxWorlds.Arena.MapEntity p in pads)
            {
                string areaId = AreaOf(p.id);
                Assert.GreaterOrEqual(ParseAreaIndex(areaId), 4, $"pad '{p.id}' area index < 4");
                Assert.IsFalse(IsExcludedArea(areaId), $"pad '{p.id}' is in a boss/entry/exit area");

                foreach (MaxWorlds.Arena.MapEntity c in allCover)
                {
                    if (c.CoverKind != MaxWorlds.Arena.CoverKind.Solid) continue;
                    float d = DistancePointToRect(new Vector2(p.x, p.z), RectOf(c));
                    Assert.GreaterOrEqual(d, PadClearance - 0.01f,
                        $"pad '{p.id}' is only {d:0.00} m from solid cover '{c.id}'");
                }
                foreach (MaxWorlds.Arena.MapEntity cr in cradles)
                {
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(cr.x, cr.z));
                    Assert.GreaterOrEqual(d, CradleClearance - 0.01f,
                        $"pad '{p.id}' is only {d:0.00} m from cradle '{cr.id}'");
                }

                foreach (MaxWorlds.Arena.MapEntity al in airlocks)
                {
                    if (AreaOf(al.id) != areaId) continue;
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(al.x, al.z));
                    if (d < 5f - 0.01f || d > 8f + 0.01f) continue;
                    if (!FacingVectors.TryGetValue(al.facing, out Vector2 dir)) continue;
                    Vector2 expected = new Vector2(al.x, al.z) + dir * d;
                    if (Vector2.Distance(expected, new Vector2(p.x, p.z)) < 0.01f) anyPadFrontsAnAirlock = true;
                }
            }
            Assert.IsTrue(anyPadFrontsAnAirlock, "no challenge pad sits 5-8 m directly in front of an airlock");

            // --- Repair bay: exactly 1, area index 18-22, not excluded, PLUS this ticket's own: >= 12 m
            // from every airlock's pull zone, >= 6 m from every cradle. ---
            Assert.AreEqual(1, bays.Count, $"expected 1 repair bay, found {bays.Count}");
            foreach (MaxWorlds.Arena.MapEntity b in bays)
            {
                string areaId = AreaOf(b.id);
                int idx = ParseAreaIndex(areaId);
                Assert.IsTrue(idx >= 18 && idx <= 22, $"repair bay '{b.id}' area index {idx} not in [18,22]");
                Assert.IsFalse(IsExcludedArea(areaId), $"repair bay '{b.id}' is in a boss/entry/exit area");

                foreach (MaxWorlds.Arena.MapEntity al in airlocks)
                {
                    float d = Vector2.Distance(new Vector2(b.x, b.z), new Vector2(al.x, al.z));
                    Assert.GreaterOrEqual(d, AirlockPullRadius - 0.01f,
                        $"repair bay '{b.id}' is only {d:0.00} m from airlock '{al.id}' (inside its {AirlockPullRadius} m pull zone)");
                }
                foreach (MaxWorlds.Arena.MapEntity cr in cradles)
                {
                    float d = Vector2.Distance(new Vector2(b.x, b.z), new Vector2(cr.x, cr.z));
                    Assert.GreaterOrEqual(d, CradleClearance - 0.01f,
                        $"repair bay '{b.id}' is only {d:0.00} m from cradle '{cr.id}'");
                }
            }

            // --- No area carries more than 2 extras (vault + pad + repair bay) ---
            var extrasPerArea = new Dictionary<string, int>();
            void Bump(string areaId) => extrasPerArea[areaId] = extrasPerArea.TryGetValue(areaId, out int n) ? n + 1 : 1;
            foreach (MaxWorlds.Arena.MapEntity v in vaults) Bump(AreaOf(v.id));
            foreach (MaxWorlds.Arena.MapEntity p in pads) Bump(AreaOf(p.id));
            foreach (MaxWorlds.Arena.MapEntity b in bays) Bump(AreaOf(b.id));
            foreach (KeyValuePair<string, int> kv in extrasPerArea)
                Assert.LessOrEqual(kv.Value, 2, $"area '{kv.Key}' has {kv.Value} extras (> 2)");
        }

        private static string AreaOf(string entityId)
        {
            int u = entityId.IndexOf('_');
            return u > 0 ? entityId.Substring(0, u) : entityId;
        }

        private static int ParseAreaIndex(string areaId)
        {
            return areaId == "stub" ? 0 : int.Parse(areaId.Substring(1));
        }

        private static Rect RectOf(MaxWorlds.Arena.MapEntity e) =>
            new Rect(e.x - e.width / 2f, e.z - e.depth / 2f, e.width, e.depth);

        private static Rect PointRect(MaxWorlds.Arena.MapEntity e) => new Rect(e.x, e.z, 0f, 0f);

        private static void AssertClear(Rect box, Rect obstacle, float margin, string boxId, string obstacleId)
        {
            Rect inflated = new Rect(obstacle.x - margin, obstacle.y - margin,
                obstacle.width + margin * 2f, obstacle.height + margin * 2f);
            Assert.IsFalse(box.Overlaps(inflated), $"'{boxId}' is within {margin} m of '{obstacleId}'");
        }

        private static float DistancePointToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, p.x - r.xMax, 0f);
            float dy = Mathf.Max(r.yMin - p.y, p.y - r.yMax, 0f);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private readonly struct PreExistingCoverFields
        {
            public readonly float X, Z, Width, Height, Depth;
            public readonly string Shape, Dressing;

            public PreExistingCoverFields(float x, float z, float width, float height, float depth, string shape, string dressing)
            {
                X = x; Z = z; Width = width; Height = height; Depth = depth; Shape = shape; Dressing = dressing;
            }
        }

        /// <summary>The six pieces this ticket flags secret, with every field the WorldCover->MapEntity
        /// passthrough carries EXCEPT the two this ticket is allowed to touch (<c>secret</c>, and
        /// <c>hp</c> only if it was 0).</summary>
        private static Dictionary<string, PreExistingCoverFields> ExpectedPreExistingSecretFields()
        {
            return new Dictionary<string, PreExistingCoverFields>
            {
                ["a3_cover1"] = new PreExistingCoverFields(80.0f, 45.0f, 1.6f, 1.6f, 1.6f, "box", "ship_bulkhead_stub"),
                ["a7_cover1"] = new PreExistingCoverFields(164.6f, 117.0f, 1.6f, 1.6f, 1.6f, "box", "ship_bulkhead_stub"),
                ["a14_cover1"] = new PreExistingCoverFields(300.2f, 238.0f, 1.6f, 1.6f, 1.6f, "box", "ship_bulkhead_stub"),
                ["a20_cover1"] = new PreExistingCoverFields(438.2f, 342.7f, 1.6f, 1.6f, 1.6f, "box", "ship_bulkhead_stub"),
                ["a21_cover2"] = new PreExistingCoverFields(486.5f, 337.33f, 3.0f, 1.6f, 3.0f, "box", "ship_crate"),
                ["a27_cover2"] = new PreExistingCoverFields(623.25f, 459.67f, 3.0f, 1.6f, 3.0f, "box", "ship_crate"),
            };
        }
    }
}
