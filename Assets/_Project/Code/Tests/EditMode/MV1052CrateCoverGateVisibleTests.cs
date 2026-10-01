using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1052: Lee stood in World 3 area 10 (Drowned Mess Hall, iOS v0.11.3) and saw open floor —
    /// none of a10's 19 authored "crate" cover pieces (all <see cref="CoverDressing.None"/>) read as
    /// drawn, even though the map screen (which reads authored data directly, never a built renderer)
    /// shows them fine.
    ///
    /// Root cause (proven by this test's own diagnostic run against 12419fa, logged in the fix comment):
    /// NOT the three candidates the ticket listed to check first. <see cref="MV1051MachineryWallVisibleTests"/>
    /// and <see cref="MV1019ReefFloorContrastTests"/> already prove the dressing pass's material/mesh
    /// output is correct, and driving <see cref="MapStaticBatchRoot"/>'s own gate (Start(), then either a
    /// direct <c>ApplyAreaGate("area10", ...)</c> or the MV-925 self-heal path) through the exact real
    /// load path confirms a crate BODY — combined by <see cref="MapStaticBatchRoot"/>'s own
    /// <c>CombineZoneGeometry</c> alongside every other a10 cover piece — correctly enables and disables
    /// with the gate.
    ///
    /// What is actually broken: <see cref="ReefKit.ApplyCrateSkin"/>'s own four "CrateCap" corner
    /// accents (built under <c>crateBody.transform</c> by <see cref="ReefDressing.DressCover"/>, called
    /// from <c>BackyardPath.Awake</c> AFTER <see cref="MapRuntime.Build"/> has already returned) are
    /// created too late to ever be tagged: <c>MapRuntime.BuildProps</c>'s own <c>AddStatic</c> ->
    /// <c>TagStatic</c> walk (which recurses into every child renderer under a cover piece's body, and
    /// is the ONLY place a crate's own caps could ever join <see cref="MapStaticBatchRoot"/>'s zone map
    /// or its combinable <c>_statics</c> list) has already finished by the time the caps exist. They are
    /// never in <c>_rendererZones</c>, so <c>ApplyAreaGate</c>'s toggle loop never touches them (it skips
    /// any renderer it never tagged, by design — see that method's own doc) and they sit permanently
    /// enabled, orphaned from the gate that correctly governs their own parent crate body — breaking the
    /// "collider stays, art swaps" / "renderer visibility tracks the gate" contract MV-887/890/932/972
    /// all establish for every other per-world dressing kit. With the crate BODY correctly hidden while
    /// Max stands elsewhere, the caps are all that is left drawing — four thin orange slivers where a
    /// full crate should be, exactly Lee's own "one slab ... part-drawn".
    ///
    /// Asserts RESOLVED state only (Rule 2, Tier 2) through the real load path: every a10 crate's body
    /// AND its own corner caps are enabled with Max standing in a10 (the ticket's own AC, which already
    /// passed before this fix — the combine/gate pipeline for the body itself was never broken), and
    /// every one of them — caps included — is disabled once the gate moves off area10 entirely. Fails on
    /// 12419fa (the commit before this fix) on the SECOND half: a10_c11's own corner caps stay enabled
    /// with Max standing in an unrelated zone.
    /// </summary>
    public sealed class MV1052CrateCoverGateVisibleTests
    {
        private const float MinCoverageFraction = 0.90f;
        private const float MinLuminanceDifference = 0.1f;

        [Test]
        public void A10CrateCoverAndItsCornerCaps_TrackTheAreaGateTogether()
        {
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            WorldArea a10 = cfg.areas.FirstOrDefault(a => a.id == "a10");
            Assert.IsNotNull(a10, "World 3 must still author area 'a10' (Drowned Mess Hall) for this test to mean anything");
            Vector2 a10Center = a10.CenterXz;
            var maxInA10 = new Vector3(a10Center.x, 0f, a10Center.y);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV1052 World3 Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order: geometry, then the Reef-only cover pass
                // (which is what actually builds each crate's own corner caps, under its body).
                MapBuild built = MapRuntime.Build(map, host.transform);
                ReefDressing.DressCover(host.transform, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                // MapStaticBatchRoot.Start() — where CombineZoneGeometry and the initial area gate run —
                // never fires on its own outside Play mode. Same reflection trick MV887AreaRendererGateTests
                // already uses.
                InvokePrivate(batchRoot, "Start");

                List<CoverPiece> a10Crates = built.Cover
                    .Where(p => p.Cover.Name.StartsWith("a10_") && p.Cover.Dressing == CoverDressing.None)
                    .ToList();
                Assert.AreEqual(19, a10Crates.Count,
                    "World 3 area 10 should still author exactly its 19 crate cover pieces for this test to mean anything");

                Renderer floor = FindNamed(host.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 3's built map to include its floor slab");
                float floorLuma = ResolvedLuminance(ResolvedBaseColor(floor.sharedMaterial));

                var rendererZones = RendererZones(batchRoot);
                var failures = new List<string>();

                // --- Max placed in a10 (the ticket's own AC): every crate, body and caps alike, visible. ---
                batchRoot.ApplyAreaGate("area10", maxInA10);
                foreach (CoverPiece piece in a10Crates)
                {
                    Renderer liveBody = LiveRendererCovering(rendererZones, "area10", piece.Cover.Footprint, MinCoverageFraction);
                    if (liveBody == null)
                    {
                        failures.Add($"{piece.Cover.Name}: no enabled renderer tagged 'area10' covers >= " +
                                     $"{MinCoverageFraction:P0} of its footprint with Max standing in a10");
                    }
                    else
                    {
                        float crateLuma = ResolvedLuminance(ResolvedBaseColor(liveBody.sharedMaterial));
                        if (Mathf.Abs(crateLuma - floorLuma) < MinLuminanceDifference)
                            failures.Add($"{piece.Cover.Name}: material luminance ({crateLuma:F3}) is within " +
                                         $"{MinLuminanceDifference:F2} of the floor's ({floorLuma:F3}) — not distinguishable");
                    }

                    foreach (Renderer cap in CornerCapRenderers(piece))
                        if (!cap.enabled)
                            failures.Add($"{piece.Cover.Name}: a corner cap is disabled with Max standing in a10");
                }

                // --- Max moved to an unrelated zone: every crate, body and caps alike, must go dark. ---
                batchRoot.ApplyAreaGate("area-unrelated-to-a10", new Vector3(-1000f, 0f, -1000f));
                foreach (CoverPiece piece in a10Crates)
                {
                    Renderer stillLive = LiveRendererCovering(rendererZones, "area10", piece.Cover.Footprint, MinCoverageFraction);
                    if (stillLive != null && stillLive.enabled)
                        failures.Add($"{piece.Cover.Name}: a renderer covering its footprint is still enabled " +
                                     "with Max standing far outside a10");

                    foreach (Renderer cap in CornerCapRenderers(piece))
                        if (cap.enabled)
                            failures.Add($"{piece.Cover.Name}: a corner cap is still enabled with Max standing " +
                                         "far outside a10 — it was never registered with the area gate");
                }

                Assert.IsEmpty(failures, "MV-1052 a10 crate cover gate-tracking violations:\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        /// <summary>Every "CrateCap" renderer <see cref="ReefKit.ApplyCrateSkin"/> built under this
        /// piece's own body (empty for a piece whose shape never got corner caps in the first place).</summary>
        private static IEnumerable<Renderer> CornerCapRenderers(CoverPiece piece)
        {
            Transform caps = piece.Body.transform.Find("Corner Caps");
            if (caps == null) yield break;
            foreach (Renderer r in caps.GetComponentsInChildren<Renderer>(true))
                yield return r;
        }

        /// <summary>The same "read the gate's own live tag map, not a renderer's authored identity" idiom
        /// <see cref="MV887AreaRendererGateTests.FindGatedRenderer"/> uses — a combined cover piece's own
        /// original renderer is permanently disabled and removed from this map once folded into a shared
        /// mesh, so only the LIVE map can say what, if anything, is actually drawing this footprint right
        /// now. Among every renderer tagged <paramref name="zoneId"/> whose world-space bounds cover at
        /// least <paramref name="minCoverage"/> of <paramref name="footprint"/>, returns the one with the
        /// SMALLEST own bounds — the tightest fit, i.e. the renderer that actually IS this piece's own
        /// geometry (now folded into a per-material combined mesh alongside every other a10 crate) rather
        /// than some unrelated, far larger combined bucket (the floor/walls) that merely happens to
        /// enclose this small a footprint too. Enabled or not — the caller decides what "enabled" should
        /// mean for its own assertion.</summary>
        private static Renderer LiveRendererCovering(Dictionary<Renderer, List<string>> rendererZones,
            string zoneId, Rect footprint, float minCoverage)
        {
            float area = footprint.width * footprint.height;
            Renderer best = null;
            float bestVolume = float.MaxValue;

            foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
            {
                Renderer r = pair.Key;
                if (r == null || !pair.Value.Contains(zoneId)) continue;

                Bounds b = r.bounds;
                float ix = Mathf.Max(0f, Mathf.Min(footprint.xMax, b.max.x) - Mathf.Max(footprint.xMin, b.min.x));
                float iz = Mathf.Max(0f, Mathf.Min(footprint.yMax, b.max.z) - Mathf.Max(footprint.yMin, b.min.z));
                if ((ix * iz) / area < minCoverage) continue;

                float volume = b.size.x * b.size.y * b.size.z;
                if (volume < bestVolume) { bestVolume = volume; best = r; }
            }
            return best;
        }

        private static Color ResolvedBaseColor(Material m) =>
            m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;

        private static float ResolvedLuminance(Color authoredSRgb)
        {
            Color lin = authoredSRgb.linear;
            return 0.2126f * lin.r + 0.7152f * lin.g + 0.0722f * lin.b;
        }

        private static Renderer FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Renderer>();
            return null;
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(target, null);

        private static Dictionary<Renderer, List<string>> RendererZones(MapStaticBatchRoot batchRoot) =>
            (Dictionary<Renderer, List<string>>)typeof(MapStaticBatchRoot)
                .GetField("_rendererZones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(batchRoot);
    }
}
