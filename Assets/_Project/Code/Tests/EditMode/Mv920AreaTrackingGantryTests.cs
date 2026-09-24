using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-920 — a regression MV-909's own fix did not cover: <see cref="AreaAccumulationDirector.Update"/>'s
    /// live physical-crossing check only ever advanced <c>_physicalArea</c> when the new area's number was
    /// GREATER than the tracker's current value (<c>if (area &gt; _physicalArea)</c>). That is true for a
    /// player walking areas in authored numeric order, but World 2's own a14->a15->a12->a11->a10->a16 deck
    /// gantry (measured on this ticket's own Jira comment) counts DOWN before going back up — g33 through
    /// g36 are real, authored, primary-opened deck gates. The moment Max stepped from a15 (15) onto a12
    /// (12), the guard silently no-opped: <c>_physicalArea</c> latched at 15 for the rest of the session,
    /// <see cref="MapStaticBatchRoot.ApplyAreaGate"/> was never called again, and every area from a12
    /// onward kept whatever enabled/disabled state its renderers already had — Max walking on a deck the
    /// gate had switched off, exactly the reported "suspended in midair" defect.
    ///
    /// Walks the REAL shipped World 2 gate graph in the exact order World 2's own gates make it walkable:
    /// straight up the floor chain 1-2-...-13-14 (every gate here raises the area number), then the
    /// reported gantry itself, area-by-area, exactly as authored — 14-15 (g32, +1), 15-12 (g33, -3),
    /// 12-11 (g34, -1), 11-10 (g35, -1), 10-16 (g36, +6), 16-17 (g37, +1) — then back onto the floor chain
    /// 17-18-19-20-21. Every consecutive pair is checked against <see cref="MapData.AreLinked"/> before the
    /// walk runs, so a future relayout that changes this graph fails loudly at that assertion instead of
    /// silently stopping this test from meaning anything. <see cref="AreaAccumulationDirector.Update"/>
    /// drives every step with the player's own position exactly as the live game does — never
    /// <see cref="AreaAccumulationDirector.SetCurrentArea"/>, which is the authoritative reset MV-909 fixed
    /// and does not exercise this ticket's bug at all. Asserts, after EVERY linked crossing (raised or
    /// lowered), that <see cref="AreaAccumulationDirector.PhysicalArea"/> actually reads the area Max is
    /// standing in — the direct, decisive proof of the fix, since a frozen tracker is exactly what silently
    /// stops the render gate from ever re-applying.
    ///
    /// Fails on 421c2eb (current main at pickup): the assertion after the area15->area12 step reads
    /// <c>PhysicalArea == 15</c> (not 12), and every subsequent per-area invariant/proportion check for
    /// a12, a11, a10, a16 and everything beyond them fails too, since the gate was never re-applied for
    /// any of them.
    /// </summary>
    public sealed class Mv920AreaTrackingGantryTests
    {
        /// <summary>MV-909's own precedent for this exact "is an area substantially dark" invariant.</summary>
        private const float MinOwnTagEnabledProportion = 0.5f;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void WalkingTheRealGateGraph_KeepsTheAreaTrackerAndRenderGateCorrect_EvenWhenAreaNumbersGoDown()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV920 Host");
            GameObject areaGo = null, playerGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);
                StormdrainDressing.Dress(host.transform, map, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();

                areaGo = new GameObject("Area Accumulation");
                var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg);
                areaDirector.Configure(map, built.Cover);

                InvokePrivate(batchRoot, "Start");
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(1), "precondition: a fresh Configure() cold-boots at area1");

                playerGo = new GameObject("Player") { tag = "Player" };
                var rendererZones = RendererZones(batchRoot);

                // AC1/6 (this ticket's own required report): zone "stub" (the entry stub, never a numbered
                // combat area) is one of World 2's 22 zones but is never something the physical tracker
                // resolves to (AreaAccumulationDirector.AreaIndexOf("stub") == 0) — checked directly here,
                // against the gate's own attribution, rather than through the director at all.
                batchRoot.ApplyAreaGate("stub");
                AssertInvariant1(map, rendererZones, batchRoot, ComputeActiveSet(map, "stub"), "stub");

                List<int> walk = RealWorldGantryWalk();

                // setup failure, not a ticket assertion: every consecutive pair in this walk must be a real
                // authored MapLink, and the walk must cover all 21 numbered areas, or it isn't the real graph.
                var covered = new HashSet<int>(walk);
                Assert.That(covered.Count, Is.EqualTo(21),
                    $"setup failure: this walk covers {covered.Count}/21 numbered areas — World 2's own gate " +
                    "graph changed under it, not a MV-920 regression");
                for (int i = 1; i < walk.Count; i++)
                    Assert.IsTrue(map.AreLinked($"area{walk[i - 1]}", $"area{walk[i]}"),
                        $"setup failure: area{walk[i - 1]} and area{walk[i]} are no longer linked by an authored " +
                        "MapLink — World 2's own gate graph changed under this walk, not a MV-920 regression");

                var proportions = new Dictionary<int, float>();
                var firstVisited = new HashSet<int> { 1 };

                playerGo.transform.position = ProbePositionFor(map, map.Zone("area1"));
                InvokePrivate(areaDirector, "Update");
                RecordAreaCheckpoint(map, rendererZones, batchRoot, areaDirector, 1, proportions);

                for (int i = 1; i < walk.Count; i++)
                {
                    int fromIndex = walk[i - 1];
                    int toIndex = walk[i];

                    playerGo.transform.position = ProbePositionFor(map, map.Zone($"area{toIndex}"));
                    InvokePrivate(areaDirector, "Update");

                    // ---- the ticket's own smoking gun: the tracker must follow Max's real position, up or
                    // ---- down alike, whenever the two areas are actually linked (they are here by construction)
                    Assert.That(areaDirector.PhysicalArea, Is.EqualTo(toIndex),
                        $"MV-920: a live crossing from area{fromIndex} to area{toIndex} must move the physical " +
                        $"tracker to {toIndex} (it read {areaDirector.PhysicalArea}) — a tracker that only " +
                        "raises on a bigger area number freezes the render gate the moment World 2's own " +
                        "gantry (g33-g36) steps to a LOWER one");

                    if (firstVisited.Add(toIndex))
                    {
                        RecordAreaCheckpoint(map, rendererZones, batchRoot, areaDirector, toIndex, proportions);

                        // ---- AC4/AC5, checked at the exact instant each area first becomes the live gate
                        // ---- (a re-entry afterward would need a direct, ungated jump back to it, which is
                        // ---- exactly the kind of teleport this test's own walk is built to avoid) ----------
                        if (toIndex == 14)
                        {
                            Renderer ramp = FindRenderer(mapRoot, "a14_ramp1");
                            Assert.IsNotNull(ramp, "setup failure: World 2 must build its own a14_ramp1");
                            Assert.IsTrue(ramp.enabled, "MV-920 AC5: a14's ramp ('a14_ramp1') must be enabled with area14 current");

                            Renderer deck14 = FindRenderer(mapRoot, "a14_deck1");
                            Assert.IsNotNull(deck14, "setup failure: World 2 must build its own a14_deck1");
                            Assert.IsTrue(deck14.enabled, "MV-920 AC4: every deck slab renderer on 'a14_deck1' must be enabled with area14 current");

                            MapZone area14 = map.Zone("area14");
                            Assert.IsNotNull(area14, "setup failure: World 2 must have an area14 zone");

                            Transform wallPanels = host.transform.Find("Stormdrain Dressing")?.Find("Wall Panels");
                            Assert.IsNotNull(wallPanels, "setup failure: StormdrainDressing.Dress must build its own Wall Panels host");
                            Renderer eastWallPanel = FindFarthestInZone(wallPanels, map, area14, maximizeX: true);
                            Assert.IsNotNull(eastWallPanel, "setup failure: a14 must carry at least one east-side wall panel replacement");
                            Assert.IsTrue(IsRepresentedAndEnabled(eastWallPanel, "area14", rendererZones),
                                $"MV-920 AC5: a14's east wall panel replacement ('{eastWallPanel.name}') must be enabled with area14 current");

                            Transform coverDressingRoot = host.transform.Find("Stormdrain Dressing")?.Find("Cover");
                            Assert.IsNotNull(coverDressingRoot, "setup failure: StormdrainDressing.Dress must build its own Cover host");
                            Renderer pipeBarrier = FindFarthestInZone(coverDressingRoot, map, area14, maximizeX: false);
                            Assert.IsNotNull(pipeBarrier, "setup failure: a14 must carry at least one cover-dressing (pipe/machinery) replacement");
                            Assert.IsTrue(IsRepresentedAndEnabled(pipeBarrier, "area14", rendererZones),
                                $"MV-920 AC5: a14's internal pipe barrier dressing ('{pipeBarrier.name}') must be enabled with area14 current");
                        }
                        else if (toIndex == 15)
                        {
                            Renderer deck15 = FindRenderer(mapRoot, "a15_deck1");
                            Assert.IsNotNull(deck15, "setup failure: World 2 must build its own a15_deck1");
                            Assert.IsTrue(deck15.enabled, "MV-920 AC4: every deck slab renderer on 'a15_deck1' must be enabled with area15 current");
                        }
                    }
                }

                Assert.That(proportions.Count, Is.EqualTo(21), "every numbered area must have been recorded exactly once");
                var report = new System.Text.StringBuilder("[MV-920] per-area own-tag enabled proportion after the real gate-graph walk:\n");
                foreach (KeyValuePair<int, float> kv in proportions)
                    report.AppendLine($"  area{kv.Key}: {kv.Value:P1}");
                Debug.Log(report.ToString());

                foreach (KeyValuePair<int, float> kv in proportions)
                    Assert.That(kv.Value, Is.GreaterThan(MinOwnTagEnabledProportion),
                        $"MV-920 invariant 2: area{kv.Key} is only {kv.Value:P1} enabled with itself current — " +
                        "the 'walking on an invisible area' defect");
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        /// <summary>Records this area's own-tag enabled proportion (invariant 2) and checks invariant 1
        /// world-wide (AC2/AC3) at the exact moment it first becomes the live gate.</summary>
        private static void RecordAreaCheckpoint(MapData map, Dictionary<Renderer, List<string>> rendererZones,
            MapStaticBatchRoot batchRoot, AreaAccumulationDirector areaDirector, int areaIndex,
            Dictionary<int, float> proportions)
        {
            string zoneId = $"area{areaIndex}";
            HashSet<string> active = ComputeActiveSet(map, zoneId);
            AssertInvariant1(map, rendererZones, batchRoot, active, zoneId);

            int tagged = 0, enabled = 0;
            foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
            {
                if (!pair.Value.Contains(zoneId)) continue;
                tagged++;
                if (pair.Key != null && pair.Key.enabled) enabled++;
            }
            Assert.That(tagged, Is.GreaterThan(0), $"setup failure: {zoneId} must carry at least one tagged renderer");
            proportions[areaIndex] = (float)enabled / tagged;
        }

        /// <summary>Invariant 1 (AC2): no object carrying an enabled non-trigger collider may have its own
        /// renderer disabled while its own zone (or a zone sharing its footprint, or a linked neighbour of
        /// <paramref name="currentZoneId"/>) is in <paramref name="active"/> — invisible-but-solid geometry.
        /// Excludes <c>_dressedHidden</c> renderers exactly as <see cref="MapStaticBatchRoot.ApplyAreaGate"/>
        /// itself does (MV-890: a dressed-away cover box is never re-enabled by the gate, on purpose).</summary>
        private static void AssertInvariant1(MapData map, Dictionary<Renderer, List<string>> rendererZones,
            MapStaticBatchRoot batchRoot, HashSet<string> active, string currentZoneId)
        {
            foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
            {
                Renderer r = pair.Key;
                if (r == null || r.enabled) continue;
                Collider col = r.GetComponent<Collider>();
                if (col == null || col.isTrigger || !col.enabled) continue;
                if (IsDressedHidden(batchRoot, r)) continue;

                bool ownZoneActive = pair.Value.Exists(active.Contains);
                Assert.IsFalse(ownZoneActive,
                    $"MV-920 invariant 1 (current={currentZoneId}): '{r.name}' carries an enabled non-trigger " +
                    "collider but its own renderer is disabled while its zone is active — invisible-but-solid geometry");
            }
        }

        /// <summary>Reimplementation of <see cref="MapStaticBatchRoot.ApplyAreaGate"/>'s own active-set
        /// computation, for assertion purposes only — that method keeps its local <c>active</c> set
        /// private and exposes no way to read it back.</summary>
        private static HashSet<string> ComputeActiveSet(MapData map, string currentZoneId)
        {
            var active = new HashSet<string> { currentZoneId };
            if (map?.links != null)
                foreach (MapLink link in map.links)
                {
                    if (link == null) continue;
                    if (link.from == currentZoneId) active.Add(link.to);
                    else if (link.to == currentZoneId) active.Add(link.from);
                }

            if (map?.zones != null)
            {
                var activeZones = new List<MapZone>();
                foreach (MapZone z in map.zones)
                    if (z != null && active.Contains(z.id)) activeZones.Add(z);

                foreach (MapZone z in map.zones)
                {
                    if (z == null || active.Contains(z.id)) continue;
                    foreach (MapZone match in activeZones)
                    {
                        if (!MapZone.ShareFootprint(z, match)) continue;
                        active.Add(z.id);
                        break;
                    }
                }
            }
            return active;
        }

        /// <summary>The exact area-index sequence a player physically produces walking World 2's own real
        /// gate graph from the entry stub to the boss approach: straight up the floor chain (every gate
        /// here raises the number), then the reported gantry itself exactly as authored (g32 a14-a15 +1,
        /// g33 a15-a12 -3, g34 a12-a11 -1, g35 a11-a10 -1, g36 a10-a16 +6, g37 a16-a17 +1), then back onto
        /// the floor chain to the end. Hand-authored, not derived, because the whole point is to walk the
        /// SAME non-monotonic path Lee actually played (measured on this ticket's own Jira comment) rather
        /// than whichever path a generic graph search happens to prefer — a naive DFS from area1 reaches
        /// a12/a11/a10 via their OWN direct floor gates (g28-g30) before ever taking the deck route, which
        /// hides the reported defect entirely. Verified against <see cref="MapData.AreLinked"/> at the call
        /// site before the walk runs.</summary>
        private static List<int> RealWorldGantryWalk() => new List<int>
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,   // floor chain, monotonic
            15, 12, 11, 10, 16, 17,                          // the reported gantry — g32 through g37
            18, 19, 20, 21,                                  // floor chain, monotonic
        };

        /// <summary>A world position that resolves (via <see cref="MapData.ZoneAt(float,float,float)"/>) to
        /// <paramref name="zone"/> — its own centre at a low floor height for a level-0 room, or the centre
        /// of the Deck/Hatch entity built for it (never a ramp — decks and floors share id prefixes with
        /// their own area) at deck height for a level&gt;0 overlay, matching the exact height/footprint test
        /// <see cref="MapData.ZoneAt(float,float,float)"/> itself applies.</summary>
        private static Vector3 ProbePositionFor(MapData map, MapZone zone)
        {
            Assert.IsNotNull(zone, "setup failure: ProbePositionFor given a null zone");
            if (zone.level == 0) return new Vector3(zone.x, 0.5f, zone.z);

            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Deck && e.Kind != EntityKind.Hatch) continue;
                if (!zone.Contains(e.x, e.z)) continue;
                return new Vector3(e.x, map.deckHeight, e.z);
            }

            Assert.Fail($"setup failure: no Deck/Hatch entity found inside level>0 zone '{zone.id}'");
            return default;
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static Dictionary<Renderer, List<string>> RendererZones(MapStaticBatchRoot batchRoot) =>
            (Dictionary<Renderer, List<string>>)typeof(MapStaticBatchRoot)
                .GetField("_rendererZones", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(batchRoot);

        /// <summary>MV-934: <paramref name="original"/> may have been folded into a combined static mesh
        /// for its own zone (<c>MapStaticBatchRoot.CombineZoneGeometry</c>) — its own renderer is then
        /// permanently disabled and dropped from <paramref name="rendererZones"/> by design, with its
        /// geometry drawn through that zone's own "Combined ..." mesh instead. True if EITHER the
        /// original is still individually tracked and enabled, OR some combined mesh tagged to
        /// <paramref name="zoneId"/> is enabled in its place. Copied from Mv909ResumeAreaGateLatchTests
        /// (MV-909's own precedent for this exact idiom).</summary>
        private static bool IsRepresentedAndEnabled(Renderer original, string zoneId, Dictionary<Renderer, List<string>> rendererZones)
        {
            if (original == null) return false;
            if (rendererZones.TryGetValue(original, out List<string> zones) && zones.Contains(zoneId))
                return original.enabled;

            foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
            {
                if (pair.Key == null || !pair.Key.name.StartsWith("Combined ")) continue;
                if (pair.Value.Contains(zoneId)) return pair.Key.enabled;
            }
            return false;
        }

        private static bool IsDressedHidden(MapStaticBatchRoot batchRoot, Renderer r)
        {
            var hidden = (HashSet<Renderer>)typeof(MapStaticBatchRoot)
                .GetField("_dressedHidden", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(batchRoot);
            return hidden != null && hidden.Contains(r);
        }

        private static Renderer FindRenderer(Transform root, string name)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name == name) return r;
            return null;
        }

        /// <summary>The single renderer under <paramref name="root"/> resolving into <paramref name="zone"/>
        /// that sits farthest along X — the zone's own east wall — or, with <paramref name="maximizeX"/>
        /// false, farthest from the zone's own centre (an interior prop rather than a boundary one). One
        /// specific, named instance — never a count. Copied from Mv909ResumeAreaGateLatchTests (MV-909's
        /// own precedent for this exact named-instance idiom).</summary>
        private static Renderer FindFarthestInZone(Transform root, MapData map, MapZone zone, bool maximizeX)
        {
            Renderer best = null;
            float bestScore = float.NegativeInfinity;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 p = r.transform.position;
                MapZone at = map.ZoneAt(p.x, p.y, p.z);
                if (at == null || at.id != zone.id) continue;

                float score = maximizeX ? p.x : Mathf.Abs(p.x - zone.Center.x) + Mathf.Abs(p.z - zone.Center.z);
                if (score <= bestScore) continue;
                bestScore = score;
                best = r;
            }
            return best;
        }
    }
}
