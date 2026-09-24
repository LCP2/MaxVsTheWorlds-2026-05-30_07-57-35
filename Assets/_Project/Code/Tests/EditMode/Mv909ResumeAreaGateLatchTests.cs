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
    /// MV-909 — a REGRESSION exposed by MV-904+MV-907 (both correct, both stay): Lee reported World 2's
    /// a14 (Replicator Nest) rendering with no east wall, no upper deck, no ramp and no internal pipe
    /// dressing — the area gate switching off essentially everything the player was standing in. Root
    /// cause, per Lee's own triage comment (not re-derived here): <see cref="AreaAccumulationDirector.SetCurrentArea"/>
    /// "only ever rewound". A fresh <see cref="AreaAccumulationDirector.Configure"/> leaves both trackers
    /// at 1, so <see cref="MaxWorlds.Arena.WorldRunner.ResumeCheckpoint"/> landing straight into a
    /// mid-world checkpoint (a14, via MV-776) called <c>SetCurrentArea(13)</c> — never lower than the
    /// cold-boot trackers, so a silent no-op. The very next physical-position check then read Max's real
    /// position (area13) as an unlinked jump FROM area1 (nothing links them directly), refused to
    /// advance, and permanently pinned <see cref="MapStaticBatchRoot"/>'s active set at area1's own
    /// neighbours for the rest of the session — every renderer tagged to any other zone (walls, decks,
    /// ramps, pipe dressing) went dark, leaving only what the gate never touches (the floor slab,
    /// Replicators, and area gates) — exactly the reported screenshot.
    ///
    /// Drives the real entry point (<see cref="MaxWorlds.Arena.WorldRunner.ResumeCheckpoint"/>) against
    /// the real shipped World 2 config, exactly as <see cref="MV890AreaGateDressingPairingTests"/> builds
    /// the world (<see cref="MapRuntime.Build"/> then <see cref="StormdrainDressing.Dress"/>, before the
    /// gate's own <c>Start()</c> ever runs) — never a direct <c>ApplyAreaGate("area14")</c> shortcut,
    /// since the bug is entirely in whether the director ever calls that method again after a cold-boot
    /// resume, not in the gate's own attribution (Lee's own measured report on this ticket already cleared
    /// attribution).
    ///
    /// Fails on bfa897c0 (current main at pickup — includes MV-904 9dcb0ad and MV-907 c2500a2): before
    /// this fix, <c>PhysicalArea</c> reads back 1 (not 13) after <c>ResumeCheckpoint(14)</c>, a
    /// "blocked an area-tracker jump from area1 to area13" warning is logged, and a14's own ramp/deck/
    /// east-wall-panel/cover-dressing renderers all read disabled because the render gate was never
    /// re-applied for the area Max actually landed in.
    /// </summary>
    public sealed class Mv909ResumeAreaGateLatchTests
    {
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
        public void ResumeCheckpointIntoA14_RaisesTheLatchedTracker_AndReappliesTheRenderGate()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV909 Host");
            GameObject areaGo = null, playerGo = null, runnerGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                // The exact BackyardPath.Awake order for World 2: Build, THEN Dress, both inside the same
                // Awake, before MapStaticBatchRoot.Start() (and its first ApplyAreaGate call) has run.
                StormdrainDressing.Dress(host.transform, map, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                areaGo = new GameObject("Area Accumulation");
                var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg);
                areaDirector.Configure(map, built.Cover);   // cold boot: CurrentArea == PhysicalArea == 1

                // MapStaticBatchRoot.Start() reads AreaAccumulationDirector.PhysicalArea and subscribes to
                // PlayerCrossedIntoArea — must run AFTER the director exists in the scene (same ordering
                // MV890AreaGateDressingPairingTests/MV887AreaRendererGateTests already rely on).
                InvokePrivate(batchRoot, "Start");
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(1), "precondition: a fresh Configure() cold-boots at area1");

                runnerGo = new GameObject("WorldRunner Test Root");
                var runner = runnerGo.AddComponent<WorldRunner>();
                runner.Configure(cfg, map, built, areaDirector);

                MapZone area1 = map.Zone("area1");
                Assert.IsNotNull(area1, "setup failure: World 2 must have an area1 zone to place the cold-boot player in");
                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.transform.position = area1.Center;

                // The RESUME entry point itself (HomeScreen.OnResume -> WorldRunner.ResumeCheckpoint) — a
                // cold boot straight into a mid-world checkpoint at a14, never having walked there this
                // session (MV-776 is what made a checkpoint this deep worth using).
                runner.ResumeCheckpoint(14);

                // ---- the ticket's own smoking gun: the tracker must not still read area1 ---------------
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(13),
                    "MV-909: ResumeCheckpoint(14) must land the physical tracker at area13 (RespawnPlanner's " +
                    "RespawnAreaIndex — the far end of the previous arena) — a tracker still latched at area1 " +
                    "is exactly the 'blocked an area-tracker jump' bug that pins the render gate's active set " +
                    "at area1's own neighbours for the rest of the session");
                Assert.That(areaDirector.CurrentArea, Is.EqualTo(13),
                    "MV-909: CurrentArea must raise alongside the physical tracker, or the very next ordinary " +
                    "forward crossing (a13 -> a14) reads as an unlinked jump FROM area1 and is blocked all over again");

                // ---- invariant 2: a14 must not be substantially dark, with Max standing right next to it
                var rendererZones = RendererZones(batchRoot);
                int a14Tagged = 0, a14Enabled = 0;
                foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
                {
                    if (!pair.Value.Contains("area14")) continue;
                    a14Tagged++;
                    if (pair.Key != null && pair.Key.enabled) a14Enabled++;
                }
                Assert.That(a14Tagged, Is.GreaterThan(0), "setup failure: a14 must carry at least one tagged renderer");
                float a14Proportion = (float)a14Enabled / a14Tagged;
                Debug.Log($"[MV-909] area14 own-tag enabled proportion after resume: {a14Enabled}/{a14Tagged} ({a14Proportion:P1})");
                Assert.That(a14Proportion, Is.GreaterThan(0.5f),
                    $"MV-909 invariant 2: a14 is only {a14Proportion:P1} enabled after the resume the player " +
                    "landed right beside it for — this is the reported 'whole area is blanked' defect");

                // ---- AC4: a14's own ramp, deck, east wall panel and an internal pipe/cover barrier, ----
                // ---- each named individually, not counted ----------------------------------------------
                Renderer ramp = FindRenderer(mapRoot, "a14_ramp1");
                Assert.IsNotNull(ramp, "setup failure: World 2 must build its own a14_ramp1");
                Assert.IsTrue(ramp.enabled, "MV-909 AC4: a14's ramp ('a14_ramp1') must be enabled after the resume");

                Renderer deck = FindRenderer(mapRoot, "a14_deck1");
                Assert.IsNotNull(deck, "setup failure: World 2 must build its own a14_deck1");
                Assert.IsTrue(deck.enabled, "MV-909 AC4: a14's deck ('a14_deck1') must be enabled after the resume");

                MapZone area14 = map.Zone("area14");
                Assert.IsNotNull(area14, "setup failure: World 2 must have an area14 zone");

                Transform wallPanels = host.transform.Find("Stormdrain Dressing")?.Find("Wall Panels");
                Assert.IsNotNull(wallPanels, "setup failure: StormdrainDressing.Dress must build its own Wall Panels host");
                Renderer eastWallPanel = FindFarthestInZone(wallPanels, map, area14, maximizeX: true);
                Assert.IsNotNull(eastWallPanel, "setup failure: a14 must carry at least one east-side wall panel replacement");
                Assert.IsTrue(IsRepresentedAndEnabled(eastWallPanel, "area14", rendererZones),
                    $"MV-909 AC4: a14's east wall panel replacement ('{eastWallPanel.name}') must be enabled after the resume");

                Transform coverDressingRoot = host.transform.Find("Stormdrain Dressing")?.Find("Cover");
                Assert.IsNotNull(coverDressingRoot, "setup failure: StormdrainDressing.Dress must build its own Cover host");
                Renderer pipeBarrier = FindFarthestInZone(coverDressingRoot, map, area14, maximizeX: false);
                Assert.IsNotNull(pipeBarrier, "setup failure: a14 must carry at least one cover-dressing (pipe/machinery) replacement");
                Assert.IsTrue(IsRepresentedAndEnabled(pipeBarrier, "area14", rendererZones),
                    $"MV-909 AC4: a14's internal pipe barrier dressing ('{pipeBarrier.name}') must be enabled after the resume");

                // ---- AC5: the MV-890 guard still holds — a dressed-away cover box never comes back, ----
                // ---- regardless of which area the resume left current ---------------------------------
                foreach (CoverPiece piece in built.Cover)
                {
                    if (piece.Body == null || piece.Cover.Dressing == CoverDressing.None) continue;
                    Renderer boxRenderer = piece.Body.GetComponent<Renderer>();
                    if (boxRenderer == null) continue;
                    Assert.IsFalse(boxRenderer.enabled,
                        $"MV-909/MV-890 guard: dressed cover box '{piece.Body.name}' must never be re-enabled " +
                        "by the area gate, resume or not");
                }

                // ---- invariant 1: no enabled non-trigger-collider object left with every one of its own
                // ---- tagged renderers disabled while its own zone is active (excluding _dressedHidden) --
                foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
                {
                    Renderer r = pair.Key;
                    if (r == null || r.enabled) continue;
                    Collider col = r.GetComponent<Collider>();
                    if (col == null || col.isTrigger || !col.enabled) continue;
                    if (IsDressedHidden(batchRoot, r)) continue;

                    bool anyOwnZoneActive = false;
                    foreach (string zoneId in pair.Value)
                    {
                        if (zoneId != "area13" && zoneId != "area14" && !IsFootprintNeighbour(map, zoneId, "area13")) continue;
                        anyOwnZoneActive = true;
                        break;
                    }
                    Assert.IsFalse(anyOwnZoneActive,
                        $"MV-909 invariant 1: '{r.name}' carries an enabled non-trigger collider but its own " +
                        "renderer is disabled while its zone is active — invisible-but-solid geometry");
                }
            }
            finally
            {
                if (runnerGo != null) Object.DestroyImmediate(runnerGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
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
        /// <paramref name="zoneId"/> is enabled in its place.</summary>
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

        private static bool IsFootprintNeighbour(MapData map, string zoneId, string activeZoneId)
        {
            MapZone z = map.Zone(zoneId);
            MapZone active = map.Zone(activeZoneId);
            return z != null && active != null && MapZone.ShareFootprint(z, active);
        }

        private static Renderer FindRenderer(Transform root, string name)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name == name) return r;
            return null;
        }

        /// <summary>The single renderer under <paramref name="root"/> resolving into <paramref name="zone"/>
        /// that sits farthest along X — the largest x for the zone's own east wall, or, with
        /// <paramref name="maximizeX"/> false, farthest from the zone's own centre (an interior prop
        /// rather than a boundary one). One specific, named instance — never a count.</summary>
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
