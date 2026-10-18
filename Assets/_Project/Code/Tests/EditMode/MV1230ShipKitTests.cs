using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1230: the Mothership's own ship kit — dropship, drop pod, crate (full/low), glass case,
    /// conveyor, console bank, bulkhead stub. Fails to compile on the base commit (75808da): none of
    /// <see cref="CoverDressing.ShipDropship"/> through <see cref="CoverDressing.ShipBulkheadStub"/>,
    /// <see cref="ShipKit"/> or <see cref="ShipDressing"/> exist yet — the same fail-first shape
    /// CityKitTests/MV1215ReefStructureKitTests already use for a brand-new vocabulary.
    ///
    /// Builds a fixture Mothership map with one cover piece of each new dressing through the production
    /// <see cref="MapRuntime.Build"/> cover-build path, then dresses it through the production
    /// <see cref="ShipDressing.DressCover"/> routing (not a hand-rolled substitute), and asserts RESOLVED
    /// values only (testing policy Rule 2, Tier 2): each piece's own authored string actually resolves
    /// to the real <see cref="CoverDressing"/> member; the engine-built <see cref="Collider"/>'s own
    /// height (<see cref="Collider.bounds"/>, not the authored float) matches the authored height within
    /// 1 cm (AC1); and the kit's built renderer union covers at least 90% of that SAME resolved
    /// collider's own XZ footprint, at most +0.15 m over it (AC1) — CityKitTests' own fit window, read
    /// off the real collider rather than a second, hand-typed copy of the authored size.
    /// </summary>
    public sealed class MV1230ShipKitTests
    {
        // Distinctive, far-off origin — same reasoning MV745/MV863/MV917/MV1215's own fixtures give:
        // EditMode tests share one physics scene for the whole cc-verify run, with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-91200f, 0f, 77300f);

        private static MapData Probe()
        {
            return new MapData
            {
                name = "Ship Kit Probe",
                zones = new[]
                {
                    new MapZone
                    {
                        id = "room", type = "open",
                        x = RigOrigin.x, z = RigOrigin.z, width = 320f, depth = 20f,
                    },
                },
                entities = new[]
                {
                    Piece("dropship", 0, "ship_dropship", "solid", 10f, 1.6f, 4f),
                    Piece("droppod", 20, "ship_drop_pod", "solid", 1.4f, 1.2f, 1.4f),
                    Piece("crate", 40, "ship_crate", "solid", 3f, 1.6f, 3f),
                    Piece("cratelow", 60, "ship_crate_low", "solid", 3f, 1.0f, 3f),
                    Piece("glasscase", 80, "ship_glass_case", "see_through", 2.2f, 1.6f, 2.2f),
                    Piece("conveyor", 100, "ship_conveyor", "see_through", 2f, 0.6f, 3f),
                    Piece("consolebank", 120, "ship_console_bank", "solid", 2f, 1.0f, 1f),
                    Piece("bulkheadstub", 140, "ship_bulkhead_stub", "solid", 1.4f, 1.6f, 0.4f),
                },
            };
        }

        private static MapEntity Piece(string id, float xOffset, string dressing, string coverKind,
            float width, float height, float depth)
        {
            return new MapEntity
            {
                id = id, kind = "cover",
                x = RigOrigin.x + xOffset, z = RigOrigin.z,
                width = width, height = height, depth = depth,
                shape = "box", dressing = dressing, coverKind = coverKind,
            };
        }

        [Test]
        public void EveryNewShipDressing_FitsItsResolvedColliderAndParsesItsDressing()
        {
            MapData map = Probe();
            var root = new GameObject("MV-1230 Ship Kit Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);

                CoverPiece dropship = built.Cover.Find(p => p.Cover.Name == "dropship");
                CoverPiece dropPod = built.Cover.Find(p => p.Cover.Name == "droppod");
                CoverPiece crate = built.Cover.Find(p => p.Cover.Name == "crate");
                CoverPiece crateLow = built.Cover.Find(p => p.Cover.Name == "cratelow");
                CoverPiece glassCase = built.Cover.Find(p => p.Cover.Name == "glasscase");
                CoverPiece conveyor = built.Cover.Find(p => p.Cover.Name == "conveyor");
                CoverPiece consoleBank = built.Cover.Find(p => p.Cover.Name == "consolebank");
                CoverPiece bulkheadStub = built.Cover.Find(p => p.Cover.Name == "bulkheadstub");

                Assert.IsNotNull(dropship.Body, "the probe's dropship cover piece was never built");
                Assert.IsNotNull(dropPod.Body, "the probe's drop pod cover piece was never built");
                Assert.IsNotNull(crate.Body, "the probe's crate cover piece was never built");
                Assert.IsNotNull(crateLow.Body, "the probe's low crate cover piece was never built");
                Assert.IsNotNull(glassCase.Body, "the probe's glass case cover piece was never built");
                Assert.IsNotNull(conveyor.Body, "the probe's conveyor cover piece was never built");
                Assert.IsNotNull(consoleBank.Body, "the probe's console bank cover piece was never built");
                Assert.IsNotNull(bulkheadStub.Body, "the probe's bulkhead stub cover piece was never built");

                // The authored string actually resolves to the real member, not a silent fallback to
                // CoverDressing.None.
                Assert.AreEqual(CoverDressing.ShipDropship, dropship.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ShipDropPod, dropPod.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ShipCrate, crate.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ShipCrateLow, crateLow.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ShipGlassCase, glassCase.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ShipConveyor, conveyor.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ShipConsoleBank, consoleBank.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ShipBulkheadStub, bulkheadStub.Cover.Dressing);

                // Ticket's own coverKind table: glass case/conveyor see-through, everything else solid.
                Assert.AreEqual(CoverKind.SeeThrough, glassCase.Cover.Kind);
                Assert.AreEqual(CoverKind.SeeThrough, conveyor.Cover.Kind);
                Assert.AreEqual(CoverKind.Solid, dropship.Cover.Kind);
                Assert.AreEqual(CoverKind.Solid, crate.Cover.Kind);

                // Production dressing pass — builds the real kit art and hides every cover box, exactly
                // the path BackyardPath.ApplyWorldMaterials takes for WorldKit.Ship.
                int dressed = ShipDressing.DressCover(root.transform, built.Cover);
                Assert.AreEqual(8, dressed, "ShipDressing must dress every one of the probe's 8 new pieces");

                Transform kitHost = root.transform.Find("Ship Kit");
                Assert.IsNotNull(kitHost, "ShipDressing must build a 'Ship Kit' host");

                // Nothing ShipKit builds carries a collider — colliders come from the map's own cover
                // entries, never from dressing.
                Assert.IsEmpty(kitHost.GetComponentsInChildren<Collider>(true),
                    "ShipKit must never build a collider");

                AssertFit(FindBuiltPiece(kitHost, dropship.Cover.Center), dropship.Body, 1.6f, "Wing A", "Wing B");
                AssertFit(FindBuiltPiece(kitHost, dropPod.Cover.Center), dropPod.Body, 1.2f);
                AssertFit(FindBuiltPiece(kitHost, crate.Cover.Center), crate.Body, 1.6f);
                AssertFit(FindBuiltPiece(kitHost, crateLow.Cover.Center), crateLow.Body, 1.0f);
                AssertFit(FindBuiltPiece(kitHost, glassCase.Cover.Center), glassCase.Body, 1.6f);
                AssertFit(FindBuiltPiece(kitHost, conveyor.Cover.Center), conveyor.Body, 0.6f);
                AssertFit(FindBuiltPiece(kitHost, consoleBank.Cover.Center), consoleBank.Body, 1.0f);
                AssertFit(FindBuiltPiece(kitHost, bulkheadStub.Cover.Center), bulkheadStub.Body, 1.6f);

                // No unlit/emissive material (the gold lamps) wears a reserved colour, and none of the
                // kit's own lit panel tones trip the guard either.
                foreach (Renderer r in kitHost.GetComponentsInChildren<Renderer>(true))
                {
                    Material mat = r.sharedMaterial;
                    if (mat == null || mat.shader == null || !mat.shader.name.Contains("Unlit")) continue;

                    Color c = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor")
                            : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                            : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;

                    Assert.IsFalse(ShipKit.IsReservedColour(c),
                        $"{r.name} (material '{mat.name}', colour {c}) wears a reserved colour");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The direct child of <paramref name="kitHost"/> whose own renderer union sits
        /// closest, in XZ, to <paramref name="expectedCenter"/> — the probe spaces every piece 20 m
        /// apart along X, so nearest-centre matching is unambiguous even between two pieces built by
        /// the same method name (crate/crate_low both root a GameObject named "Crate").</summary>
        private static Transform FindBuiltPiece(Transform kitHost, Vector3 expectedCenter)
        {
            Transform best = null;
            float bestDist = float.MaxValue;
            var expected2D = new Vector2(expectedCenter.x, expectedCenter.z);

            foreach (Transform child in kitHost)
            {
                Renderer[] rends = child.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) continue;

                Bounds b = rends[0].bounds;
                foreach (Renderer r in rends) b.Encapsulate(r.bounds);

                float dist = Vector2.Distance(new Vector2(b.center.x, b.center.z), expected2D);
                if (dist < bestDist) { bestDist = dist; best = child; }
            }

            return best;
        }

        /// <summary>AC1's own fit window, read off <paramref name="coverBody"/>'s REAL, engine-resolved
        /// <see cref="Collider"/> rather than a second hand-typed copy of the authored size: the union
        /// of <paramref name="pieceRoot"/>'s own (non-skipped) renderers must span at least 90% and at
        /// most the collider's own X/Z extent + 0.15 m, and the collider's own height must match the
        /// authored height within 1 cm (trivially true by construction — <c>Spawn()</c> scales a unit
        /// <c>BoxCollider</c> by the authored size — asserted directly off the resolved bounds anyway,
        /// since testing policy Rule 2 asks for the resolved value, not the authored constant).</summary>
        private static void AssertFit(Transform pieceRoot, GameObject coverBody, float authoredHeight, params string[] skipNames)
        {
            Assert.IsNotNull(pieceRoot, "no built kit piece matched this cover entry's own centre");

            var collider = coverBody.GetComponent<Collider>();
            Assert.IsNotNull(collider, $"{coverBody.name} has no resolved collider");
            Bounds cb = collider.bounds;

            // AC1: the resolved collider's own height matches the authored height within 1 cm.
            Assert.LessOrEqual(Mathf.Abs(cb.size.y - authoredHeight), 0.01f,
                $"{coverBody.name}'s resolved collider height {cb.size.y:F3} does not match the authored {authoredHeight:F3} within 1 cm");

            Renderer[] renderers = pieceRoot.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && System.Array.IndexOf(skipNames, r.name) < 0).ToArray();
            Assert.IsNotEmpty(renderers, $"{pieceRoot.name} built no renderers");

            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

            // AC1: the renderer union covers at least 90% of the resolved collider's own XZ footprint.
            Assert.GreaterOrEqual(b.size.x, cb.size.x * 0.90f,
                $"{pieceRoot.name} X extent {b.size.x:F2} is below 90% of its resolved collider's {cb.size.x:F2}");
            Assert.LessOrEqual(b.size.x, cb.size.x + 0.15f,
                $"{pieceRoot.name} X extent {b.size.x:F2} exceeds its resolved collider's {cb.size.x:F2} + 0.15");
            Assert.GreaterOrEqual(b.size.z, cb.size.z * 0.90f,
                $"{pieceRoot.name} Z extent {b.size.z:F2} is below 90% of its resolved collider's {cb.size.z:F2}");
            Assert.LessOrEqual(b.size.z, cb.size.z + 0.15f,
                $"{pieceRoot.name} Z extent {b.size.z:F2} exceeds its resolved collider's {cb.size.z:F2} + 0.15");
        }
    }
}
