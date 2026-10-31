using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1323 AC1, every sub-case in one test (MV-465 Rule 1): over the two LOADED configs (the City
    /// and the Reef) via the real <see cref="MaxWorlds.Arena.WorldMapLoader"/>/<see cref="MaxWorlds.Arena.WorldLibrary"/>
    /// conversion, each world has exactly 6 secret cover pieces (all eligible for destructible cover,
    /// no two in the same area, >= 1 per third of the world), 2 vaults (area index >= 5, never in a
    /// boss/entry/exit area, >= 1.5 m clear of every cover/relay/pylon/current-lane/gate, and a
    /// carriesKey garrison entry — never Turret, Dreadnought, Lurker or Skitter — authored in the same
    /// area), 2 challenge pads (area index >= 4, never in a boss/entry/exit area, >= 3 m clear of solid
    /// cover at the centre), and exactly 1 repair bay (area index 18-22, never in a boss/entry/exit
    /// area), with no area carrying more than 2 of these three "extra" kinds. Every assertion reads a
    /// RESOLVED <see cref="MaxWorlds.Arena.MapEntity"/> off the real loaded map (MV-465 Tier 2) — the
    /// one exception is a vault's carriesKey garrison entry, which the loader never resolves into
    /// MapData at all (Garrison seeding reads <see cref="MaxWorlds.Arena.WorldArea.garrison"/> directly
    /// at runtime), so that one piece reads the authored <see cref="MaxWorlds.Arena.WorldConfig"/> the
    /// same way <see cref="MaxWorlds.Arena.Garrison"/> itself does. Also covers AC1's "every
    /// pre-existing cover piece's other fields unchanged": the six secret pieces are the only cover
    /// entities this ticket's own diff ever writes to, so their position/footprint/shape/dressing are
    /// checked against their pre-existing values too (see <see cref="ExpectedPreExistingSecretFields"/>).
    ///
    /// Fails on the commit before this ticket: neither city_config.json nor world3_config.json authors
    /// a single secret/vault/challenge/repairBay anywhere, so every per-world count assertion below
    /// fails immediately (0 != 6, 0 != 2, 0 != 2, 0 != 1).
    /// </summary>
    public sealed class MV1323CityReefExtrasTests
    {
        private const float GateClearance = 2f;
        private const float VaultMargin = 1.5f;
        private const float PadClearance = 3f;
        private static readonly string[] EliteExcludedKinds = { "turret", "dreadnought", "lurker", "skitter" };

        [Test]
        public void CityAndReefConfigs_SecretsVaultsPadsAndRepairBayMeetSpec()
        {
            CheckWorld(MaxWorlds.Arena.WorldLibrary.City, "City");
            CheckWorld(MaxWorlds.Arena.WorldLibrary.World3, "Reef");
        }

        private static void CheckWorld(string worldKey, string label)
        {
            var asset = Resources.Load<TextAsset>($"{MaxWorlds.Arena.WorldLibrary.ResourceRoot}/{worldKey}");
            Assert.IsNotNull(asset, $"{label}: {worldKey}.json missing from Resources");
            Assert.IsTrue(
                MaxWorlds.Arena.WorldMapLoader.TryLoadJson(asset.text, out MaxWorlds.Arena.MapData map, out string reason),
                $"{label}: failed to load: {reason}");

            MaxWorlds.Arena.WorldConfig cfg = MaxWorlds.Arena.WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{label}: WorldLibrary failed to load {worldKey}");

            var areaById = new Dictionary<string, MaxWorlds.Arena.WorldArea>();
            foreach (MaxWorlds.Arena.WorldArea a in cfg.areas) areaById[a.id] = a;

            bool IsExcludedArea(string areaId) =>
                areaById.TryGetValue(areaId, out MaxWorlds.Arena.WorldArea a) &&
                (a.IsEntryRole || a.IsBossRole || a.IsExitRole);

            var gates = new List<MaxWorlds.Arena.MapEntity>();
            var allCover = new List<MaxWorlds.Arena.MapEntity>();
            var replicators = new List<MaxWorlds.Arena.MapEntity>();
            var pylons = new List<MaxWorlds.Arena.MapEntity>();
            var factories = new List<MaxWorlds.Arena.MapEntity>();
            var currents = new List<MaxWorlds.Arena.MapEntity>();
            var secrets = new List<MaxWorlds.Arena.MapEntity>();
            var vaults = new List<MaxWorlds.Arena.MapEntity>();
            var pads = new List<MaxWorlds.Arena.MapEntity>();
            var bays = new List<MaxWorlds.Arena.MapEntity>();

            foreach (MaxWorlds.Arena.MapEntity e in map.entities)
            {
                switch (e.Kind)
                {
                    case MaxWorlds.Arena.EntityKind.AreaGate: gates.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Cover:
                        allCover.Add(e);
                        if (e.secret) secrets.Add(e);
                        break;
                    case MaxWorlds.Arena.EntityKind.Replicator: replicators.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Pylon: pylons.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Factory: factories.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Current: currents.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Vault: vaults.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.Challenge: pads.Add(e); break;
                    case MaxWorlds.Arena.EntityKind.RepairBay: bays.Add(e); break;
                }
            }

            // --- Secrets: exactly 6, all eligible, no two in one area, >= 1 per third ---
            Assert.AreEqual(6, secrets.Count, $"{label}: expected 6 secret cover pieces, found {secrets.Count}");

            // AC1's own "every pre-existing cover piece's other fields unchanged" — the six flagged
            // pieces are the only cover entities this ticket's diff ever touches (every other cover
            // entity never had a field written to it at all), so this checks exactly the fields this
            // diff was NOT supposed to move: position, footprint, shape and dressing. These are a
            // straight authored-to-resolved passthrough in WorldMapLoader (no engine-side resolution
            // step exists for them, unlike a layout-resolved rect or an auto-sized font), so asserting
            // them here is not the Tier-1 "fontSize" trap — there is no resolved value that could ever
            // legitimately diverge from what was authored.
            Dictionary<string, PreExistingCoverFields> expectedFields = ExpectedPreExistingSecretFields(label);

            var secretAreas = new HashSet<string>();
            foreach (MaxWorlds.Arena.MapEntity s in secrets)
            {
                string areaId = AreaOf(s.id);
                Assert.IsTrue(secretAreas.Add(areaId), $"{label}: secret '{s.id}' shares area '{areaId}' with another secret");
                Assert.IsFalse(IsExcludedArea(areaId), $"{label}: secret '{s.id}' is in a boss/entry/exit area");

                Assert.AreEqual(MaxWorlds.Arena.CoverKind.Solid, s.CoverKind, $"{label}: secret '{s.id}' is not solid cover");
                float footprint = s.width * s.depth;
                Assert.LessOrEqual(footprint, 10f, $"{label}: secret '{s.id}' footprint {footprint} exceeds the 10 sqm destructible-cover cap");
                bool isHull = s.dressing.IndexOf("bulkhead", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isHull) Assert.LessOrEqual(footprint, 6f, $"{label}: secret '{s.id}' is a hull piece over the 6 sqm carve-out cap");
                Assert.Greater(s.hp, 0f, $"{label}: secret '{s.id}' has hp <= 0");

                foreach (MaxWorlds.Arena.MapEntity g in gates)
                {
                    float d = Vector2.Distance(new Vector2(s.x, s.z), new Vector2(g.x, g.z));
                    Assert.GreaterOrEqual(d, GateClearance - 0.01f, $"{label}: secret '{s.id}' is only {d:0.00} m from gate '{g.id}'");
                }

                Assert.IsTrue(expectedFields.TryGetValue(s.id, out PreExistingCoverFields expected),
                    $"{label}: secret '{s.id}' is not one of the six pieces this ticket authored as secret");
                Assert.AreEqual(expected.X, s.x, 0.001f, $"{label}: secret '{s.id}' x moved from its pre-existing value");
                Assert.AreEqual(expected.Z, s.z, 0.001f, $"{label}: secret '{s.id}' z moved from its pre-existing value");
                Assert.AreEqual(expected.Width, s.width, 0.001f, $"{label}: secret '{s.id}' width changed from its pre-existing value");
                Assert.AreEqual(expected.Height, s.height, 0.001f, $"{label}: secret '{s.id}' height changed from its pre-existing value");
                Assert.AreEqual(expected.Depth, s.depth, 0.001f, $"{label}: secret '{s.id}' depth changed from its pre-existing value");
                Assert.AreEqual(expected.Shape, s.shape, $"{label}: secret '{s.id}' shape changed from its pre-existing value");
                Assert.AreEqual(expected.Dressing, s.dressing, $"{label}: secret '{s.id}' dressing changed from its pre-existing value");
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
                Assert.IsTrue(any, $"{label}: no secret in third [{third[0]},{third[1]}]");
            }

            // --- Vaults: area >= 5, not excluded, 1.5 m clear, carriesKey robot in the same area ---
            Assert.AreEqual(2, vaults.Count, $"{label}: expected 2 vaults, found {vaults.Count}");
            foreach (MaxWorlds.Arena.MapEntity v in vaults)
            {
                string areaId = AreaOf(v.id);
                Assert.IsTrue(areaById.ContainsKey(areaId), $"{label}: vault '{v.id}' area '{areaId}' not found");
                Assert.GreaterOrEqual(ParseAreaIndex(areaId), 5, $"{label}: vault '{v.id}' area index < 5");
                Assert.IsFalse(IsExcludedArea(areaId), $"{label}: vault '{v.id}' is in a boss/entry/exit area");

                Rect box = RectOf(v);
                foreach (MaxWorlds.Arena.MapEntity c in allCover) AssertClear(box, RectOf(c), VaultMargin, label, v.id, c.id);
                foreach (MaxWorlds.Arena.MapEntity cur in currents) AssertClear(box, RectOf(cur), VaultMargin, label, v.id, cur.id);
                foreach (MaxWorlds.Arena.MapEntity f in factories) AssertClear(box, RectOf(f), VaultMargin, label, v.id, f.id);
                foreach (MaxWorlds.Arena.MapEntity r in replicators) AssertClear(box, PointRect(r), VaultMargin, label, v.id, r.id);
                foreach (MaxWorlds.Arena.MapEntity p in pylons) AssertClear(box, PointRect(p), VaultMargin, label, v.id, p.id);
                foreach (MaxWorlds.Arena.MapEntity g in gates) AssertClear(box, PointRect(g), VaultMargin, label, v.id, g.id);

                MaxWorlds.Arena.WorldArea area = areaById[areaId];
                bool hasKeyHolder = false;
                foreach (MaxWorlds.Arena.WorldGarrisonEntry entry in area.garrison)
                {
                    if (entry == null || !entry.carriesKey) continue;
                    hasKeyHolder = true;
                    string kind = (entry.kind ?? "").ToLowerInvariant();
                    Assert.IsFalse(Array.IndexOf(EliteExcludedKinds, kind) >= 0,
                        $"{label}: area '{areaId}' carriesKey entry has excluded kind '{entry.kind}'");
                }
                Assert.IsTrue(hasKeyHolder, $"{label}: vault '{v.id}' area '{areaId}' has no carriesKey garrison entry");
            }

            // --- Challenge pads: area >= 4, not excluded, 3 m clear of solid cover at the centre ---
            Assert.AreEqual(2, pads.Count, $"{label}: expected 2 challenge pads, found {pads.Count}");
            foreach (MaxWorlds.Arena.MapEntity p in pads)
            {
                string areaId = AreaOf(p.id);
                Assert.GreaterOrEqual(ParseAreaIndex(areaId), 4, $"{label}: pad '{p.id}' area index < 4");
                Assert.IsFalse(IsExcludedArea(areaId), $"{label}: pad '{p.id}' is in a boss/entry/exit area");

                foreach (MaxWorlds.Arena.MapEntity c in allCover)
                {
                    if (c.CoverKind != MaxWorlds.Arena.CoverKind.Solid) continue;
                    float d = DistancePointToRect(new Vector2(p.x, p.z), RectOf(c));
                    Assert.GreaterOrEqual(d, PadClearance - 0.01f,
                        $"{label}: pad '{p.id}' is only {d:0.00} m from solid cover '{c.id}'");
                }
            }

            // --- Repair bay: exactly 1, area index 18-22, not excluded ---
            Assert.AreEqual(1, bays.Count, $"{label}: expected 1 repair bay, found {bays.Count}");
            foreach (MaxWorlds.Arena.MapEntity b in bays)
            {
                string areaId = AreaOf(b.id);
                int idx = ParseAreaIndex(areaId);
                Assert.IsTrue(idx >= 18 && idx <= 22, $"{label}: repair bay '{b.id}' area index {idx} not in [18,22]");
                Assert.IsFalse(IsExcludedArea(areaId), $"{label}: repair bay '{b.id}' is in a boss/entry/exit area");
            }

            // --- No area carries more than 2 extras (vault + pad + repair bay) ---
            var extrasPerArea = new Dictionary<string, int>();
            void Bump(string areaId) => extrasPerArea[areaId] = extrasPerArea.TryGetValue(areaId, out int n) ? n + 1 : 1;
            foreach (MaxWorlds.Arena.MapEntity v in vaults) Bump(AreaOf(v.id));
            foreach (MaxWorlds.Arena.MapEntity p in pads) Bump(AreaOf(p.id));
            foreach (MaxWorlds.Arena.MapEntity b in bays) Bump(AreaOf(b.id));
            foreach (KeyValuePair<string, int> kv in extrasPerArea)
                Assert.LessOrEqual(kv.Value, 2, $"{label}: area '{kv.Key}' has {kv.Value} extras (> 2)");
        }

        private static string AreaOf(string entityId)
        {
            int u = entityId.IndexOf('_');
            return u > 0 ? entityId.Substring(0, u) : entityId;
        }

        private static int ParseAreaIndex(string areaId)
        {
            // Area ids are "a<N>" except the entry stub "stub" (index 0) — never a secret/vault/pad/bay area.
            return areaId == "stub" ? 0 : int.Parse(areaId.Substring(1));
        }

        private static Rect RectOf(MaxWorlds.Arena.MapEntity e) =>
            new Rect(e.x - e.width / 2f, e.z - e.depth / 2f, e.width, e.depth);

        private static Rect PointRect(MaxWorlds.Arena.MapEntity e) => new Rect(e.x, e.z, 0f, 0f);

        private static void AssertClear(Rect box, Rect obstacle, float margin, string label, string boxId, string obstacleId)
        {
            Rect inflated = new Rect(obstacle.x - margin, obstacle.y - margin,
                obstacle.width + margin * 2f, obstacle.height + margin * 2f);
            Assert.IsFalse(box.Overlaps(inflated), $"{label}: '{boxId}' is within {margin} m of '{obstacleId}'");
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

        /// <summary>The six pieces this ticket flags secret in each world, with every field the
        /// WorldCover->MapEntity passthrough carries EXCEPT the two this ticket is allowed to touch
        /// (<c>secret</c>, and <c>hp</c> only if it was 0) — all six already authored <c>hp: 150</c>, so
        /// neither world's set needed an hp change either.</summary>
        private static Dictionary<string, PreExistingCoverFields> ExpectedPreExistingSecretFields(string label)
        {
            if (label == "City")
            {
                return new Dictionary<string, PreExistingCoverFields>
                {
                    ["a3_c4"] = new PreExistingCoverFields(42.25f, 37.0f, 1.2f, 1.0f, 1.2f, "box", "pallet"),
                    ["a9_c17"] = new PreExistingCoverFields(85.57f, 136.2f, 1.9f, 1.4f, 0.5f, "box", "roadSign"),
                    ["a13_c28"] = new PreExistingCoverFields(115.15f, 208.53f, 0.6f, 0.9f, 0.6f, "cylinder", "bin"),
                    ["a17_c5"] = new PreExistingCoverFields(87.45f, 289.0f, 1.7f, 1.0f, 0.6f, "box", "barrier"),
                    ["a23_c10"] = new PreExistingCoverFields(219f, 393.0f, 0.65f, 1.0f, 0.6f, "box", "aBoard"),
                    ["a27_c16"] = new PreExistingCoverFields(200.0f, 470.5f, 2.0f, 1.0f, 0.6f, "box", "barrier"),
                };
            }

            return new Dictionary<string, PreExistingCoverFields>
            {
                ["a9_k1"] = new PreExistingCoverFields(175.0f, 101.0f, 2f, 3.0f, 2f, "box", "reef_pillar"),
                ["a10_c14"] = new PreExistingCoverFields(176.5f, 121.5f, 1f, 1.8f, 5f, "box", "reef_crate"),
                ["a14_k1"] = new PreExistingCoverFields(67.5f, 83.5f, 3f, 2.4f, 3f, "cylinder", "reef_drum"),
                ["a17_c3"] = new PreExistingCoverFields(144.0f, 49.0f, 2f, 1.8f, 2f, "box", "reef_manifold"),
                ["a24_c15"] = new PreExistingCoverFields(9.5f, 149.0f, 3f, 1.8f, 2f, "box", "reef_crate"),
                ["a28_c13"] = new PreExistingCoverFields(196.0f, 145.5f, 2f, 1.8f, 1f, "box", "reef_crate"),
            };
        }
    }
}
