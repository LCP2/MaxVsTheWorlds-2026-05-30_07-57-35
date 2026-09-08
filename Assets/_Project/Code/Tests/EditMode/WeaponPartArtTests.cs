using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The weapon-upgrade props and the power cell (YT-134, MV-304).
    ///
    /// MV-464: absorbed <c>WeaponPartArtPlayTests</c> from PlayMode. That file's own doc comment said
    /// it had to stay in PlayMode because the builders strip colliders with <c>Object.Destroy</c>,
    /// which only takes effect a frame later — true when it was written, but <c>WeaponPartArt.Strip</c>
    /// is now <c>Application.isPlaying</c>-gated (MV-304) to call <c>DestroyImmediate</c> outside play
    /// mode instead, which is exactly what an EditMode test is. The comment was never updated after
    /// the fix landed; every test below ran green on the first EditMode pass with no code changes
    /// beyond dropping the now-pointless <c>yield return null</c>s and switching the leftover
    /// <c>Object.Destroy</c> calls in test bodies to <c>DestroyImmediate</c>.
    ///
    /// Every prop in this catalog is a runtime pile of primitives, which is precisely the shape that
    /// ships MAGENTA in a build (a primitive keeps Unity's default material, no URP subshader) and the
    /// shape the surface director repaints as stone. These pin both.
    /// </summary>
    public sealed class WeaponPartArtTests
    {
        [Test]
        public void PowerCellNutWalls_AreTintedTheCellsOwnCyan()
        {
            // MV-725: the battery capsule's "Casing" is gone — the currency is now a hex nut built from
            // six wall panels (WeaponPartArt.BuildPowerCell's Wall0..Wall5).
            var root = WeaponPartArt.BuildPowerCell();
            var expected = MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.CellCyan);

            for (int i = 0; i < 6; i++)
            {
                var wall = root.transform.Find($"Wall{i}");
                Assert.IsNotNull(wall, $"the power cell nut has no Wall{i}.");
                var mr = wall.GetComponent<MeshRenderer>();
                Assert.AreSame(expected, mr.sharedMaterial,
                    $"Wall{i} isn't tinted the cell's own cyan — the currency colour must stay cyan (MV-725).");
            }

            Object.DestroyImmediate(root);
        }

        private static readonly string[] AllKeys = new[]
        {
            WeaponPartArt.Keys.BeamNozzle, WeaponPartArt.Keys.PowerNozzle,
            WeaponPartArt.Keys.AugmentationHarness, WeaponPartArt.Keys.AccelerationEngine,
            WeaponPartArt.Keys.HydroDevice, WeaponPartArt.Keys.PowerCell,
        }.Concat(WeaponPartArt.MachineInternalsKeys).ToArray();

        private GameObject _built;

        [TearDown]
        public void TearDown() { if (_built != null) Object.DestroyImmediate(_built); }

        [Test]
        public void EveryPropBuilds_WithRealMaterials_AndNoColliders()
        {
            foreach (string key in AllKeys)
            {
                _built = WeaponPartArt.Build(key);
                Assert.IsNotNull(_built, $"'{key}' built nothing.");

                var renderers = _built.GetComponentsInChildren<MeshRenderer>();
                Assert.Greater(renderers.Length, 1, $"'{key}' is barely a prop — one box does not read.");

                foreach (var r in renderers)
                {
                    Assert.IsNotNull(r.sharedMaterial, $"'{key}/{r.name}' has no material — it draws nothing.");
                    string shader = r.sharedMaterial.shader.name;
                    Assert.That(shader,
                        Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds").Or.StartWith("Sprites"),
                        $"'{key}/{r.name}' wears '{shader}' — a default-material primitive is magenta in the build.");

                    // MV-725 AC2 — every renderer needs a real mesh too, not just a material; a
                    // MeshRenderer with no MeshFilter mesh draws nothing regardless of material.
                    var mf = r.GetComponent<MeshFilter>();
                    Assert.IsNotNull(mf?.sharedMesh, $"'{key}/{r.name}' has no mesh — it draws nothing.");
                }

                Assert.IsEmpty(_built.GetComponentsInChildren<Collider>(),
                    $"'{key}' kept a collider — it would fight the Pickup's own walk-over trigger.");

                Object.DestroyImmediate(_built);
                _built = null;
            }
        }

        [Test]
        public void TheFiveParts_AreDistinctSilhouettes()
        {
            // The whole point of the five is that a player tells them apart on the lawn. Proxy that with
            // "no two share the same part-count and bounding shape" — a cheap guard that catches the
            // failure mode of five near-identical boxes.
            string[] parts =
            {
                WeaponPartArt.Keys.BeamNozzle, WeaponPartArt.Keys.PowerNozzle,
                WeaponPartArt.Keys.AugmentationHarness, WeaponPartArt.Keys.AccelerationEngine,
                WeaponPartArt.Keys.HydroDevice,
            };
            var signatures = new System.Collections.Generic.List<string>();

            foreach (string key in parts)
            {
                var go = WeaponPartArt.Build(key);
                var rs = go.GetComponentsInChildren<MeshRenderer>();
                var b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                // count + coarse aspect ratio — enough to separate a slim nozzle from a fat backpack.
                string sig = $"{rs.Length}:{Mathf.Round(b.size.x / Mathf.Max(b.size.y, 0.01f) * 4f)}";
                signatures.Add(sig);
                Object.DestroyImmediate(go);
            }

            Assert.AreEqual(signatures.Count, signatures.Distinct().Count(),
                "two of the five parts read as the same shape — they have to be tellable apart at game zoom.");
        }

        [Test]
        public void TheMachineInternalsPool_HoldsAllTenDesigns()
        {
            // WV-237 originally shipped "~10 distinct part designs"; MV-430 collapsed the pool to one,
            // citing the fixed 72° camera reading nine of the ten as noise rather than variety. MV-454
            // restores the full ten now that every design also carries its own Brass/Copper colour
            // accent — colour survives the 72° projection far better than silhouette does, which was the
            // actual axis MV-430 measured.
            Assert.AreEqual(10, WeaponPartArt.MachineInternalsKeys.Length,
                "the machine-internals pool should hold all ten designs (MV-454).");
            CollectionAssert.Contains(WeaponPartArt.MachineInternalsKeys, WeaponPartArt.Keys.Gear);
        }

        [Test]
        public void TheMachineInternalsDesigns_AllShimmer_WithAConsistentPickupSilhouette()
        {
            // WV-237: every design has to read as "machine internals" (many parts, not a plain box)
            // and shimmer on the ground, while still sharing roughly the same pickup silhouette so a
            // player doesn't have to re-learn "that's a part" for each random design.
            float? refHeight = null;
            foreach (string key in WeaponPartArt.MachineInternalsKeys)
            {
                var go = WeaponPartArt.Build(key);
                var renderers = go.GetComponentsInChildren<MeshRenderer>();
                Assert.Greater(renderers.Length, 2, $"'{key}' is barely a prop — it won't read as machine internals.");

                bool hasGlisten = go.GetComponentsInChildren<Transform>()
                    .Any(t => t.name.StartsWith(WeaponPartArt.GlistenPrefix));
                Assert.IsTrue(hasGlisten, $"'{key}' has no glisten dot — it won't shimmer on the ground.");

                var b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                refHeight ??= b.size.y;
                Assert.That(b.size.y, Is.EqualTo(refHeight.Value).Within(0.4f),
                    $"'{key}' stands {b.size.y:F2} m tall against the pool's {refHeight.Value:F2} m — too far off for a consistent pickup silhouette.");

                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheTapPresentsItsSpoutAtTheCouplingHeight()
        {
            _built = GardenTapArt.Build();
            var spout = _built.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Spout");
            Assert.IsNotNull(spout, "the tap has no spout for the hose to meet.");
            Assert.That(spout.localPosition.y, Is.EqualTo(GardenTapArt.SpoutHeight).Within(0.2f),
                "the spout is not at the hose-coupling height — the tether would meet the tap in mid-air.");
            Assert.IsEmpty(_built.GetComponentsInChildren<Collider>(), "the tap prop kept a collider.");
        }

        [Test]
        public void ThePowerCell_CarriesSpecularGlintsOnItsNutWalls()
        {
            // YT-167, extended WV-236: the soft additive Core band (YT-145) is the aura, not the glisten —
            // Lee's playtest still read the shipped cell as flat because a halo isn't a specular
            // highlight, and "shine and glisten like DIAMONDS" (WV-236) means several facets, not one
            // pair. Pin that the cell wears four distinct glint dots, separate from the Core, so this
            // can't regress back to "just the aura" — or back to two facets — quietly. MV-725 moved these
            // from the old battery casing onto the new hex-nut walls; the count/positions-differ contract
            // is unchanged.
            _built = WeaponPartArt.Build(WeaponPartArt.Keys.PowerCell);

            var glints = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                glints[i] = _built.transform.Find(WeaponPartArt.GlistenPrefix + i);
                Assert.IsNotNull(glints[i], $"the power cell has no glint dot #{i}.");
            }
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    Assert.AreNotEqual(glints[i].localPosition, glints[j].localPosition,
                        $"glints #{i} and #{j} sit in the same spot — one of the four facets would never sparkle.");

            var core = _built.transform.Find("Core");
            Assert.IsNotNull(core, "the cell lost its YT-145 aura core.");
            Assert.AreNotEqual(core.localPosition, glints[0].localPosition,
                "a glint sits exactly on the aura core — it would read as one glow, not a distinct sparkle.");
        }

        [Test]
        public void TheHydroDevice_ShimmersLikeTheCellOnItsCoils()
        {
            // WV-236: "the shed device shimmers like a cell" — same glint language as the power cell,
            // riding the coil rings instead of the casing. MV-431 deliberately trimmed this to two
            // glints, not three (WeaponPartArt.BuildHydroDevice: "this prop already reads busier than
            // the cell, so it needs fewer, bolder catches of light") — this test still asserted a
            // third glint that MV-431 removed, and the assertion had gone stale silently because the
            // PlayMode suite it lived in (>20 min, routinely cancelled) was never actually watched to
            // green (MV-464). Pinning the current, deliberate count instead.
            _built = WeaponPartArt.Build(WeaponPartArt.Keys.HydroDevice);

            var glint0 = _built.transform.Find(WeaponPartArt.GlistenPrefix + "0");
            var glint1 = _built.transform.Find(WeaponPartArt.GlistenPrefix + "1");
            Assert.IsNotNull(glint0, "the shed device has no first glint dot.");
            Assert.IsNotNull(glint1, "the shed device has no second glint dot.");
            Assert.IsNull(_built.transform.Find(WeaponPartArt.GlistenPrefix + "2"),
                "a third glint appeared — MV-431 deliberately trimmed this prop to two.");
            Assert.AreNotEqual(glint0.localPosition, glint1.localPosition,
                "two of the shed device's glints sit in the same spot.");

            var core = _built.transform.Find("Core");
            Assert.IsNotNull(core, "the shed device lost its condensation core glow.");
            Assert.AreNotEqual(core.localPosition, glint0.localPosition,
                "a glint sits exactly on the core — it would read as one glow, not a distinct shimmer.");
        }

        [Test]
        public void ThePowerCellHudIcon_IsARealSprite()
        {
            var sprite = WeaponHudIcons.PowerCell();
            Assert.IsNotNull(sprite, "the power-cell HUD icon generated nothing.");
            Assert.Greater(sprite.texture.width, 0, "the icon has no pixels.");
            // Cached: a second call hands back the same sprite, not a fresh texture every HUD tick.
            Assert.AreSame(sprite, WeaponHudIcons.PowerCell(), "the icon is not cached — it rebuilds every call.");

            // MV-725 AC1 — HudController.BuildPowerCellCounter asks for 64; WeaponsScreen's cost icon
            // asks for RigBoardLayout.CostIconSize/CostIconSizePhone (22/40). Every call site's size must
            // resolve to a real sprite too, not just the default-argument size above.
            Assert.IsNotNull(WeaponHudIcons.PowerCell(64), "the HUD counter's 64px icon failed to resolve.");
            Assert.IsNotNull(WeaponHudIcons.PowerCell(Mathf.RoundToInt(RigBoardLayout.CostIconSize)),
                "WeaponsScreen's desktop cost-icon size failed to resolve.");
            Assert.IsNotNull(WeaponHudIcons.PowerCell(Mathf.RoundToInt(RigBoardLayout.CostIconSizePhone)),
                "WeaponsScreen's phone cost-icon size failed to resolve.");
        }

        [Test]
        public void ThePowerCellSecondary_BodyIsAFacetedHexagonalGem_NotARoundCylinder()
        {
            // MV-682: the two body facets were built from PrimitiveType.Cylinder, which stays circular
            // under any scale — it can only ever read as a smooth round taper, never the flat hexagonal
            // facets the MV-672 reference design calls for. Fails on the pre-fix build: the body was
            // two separate Cylinder GameObjects named UpperFacet/LowerFacet (no "Facets" mesh child at
            // all), and a Cylinder's own built-in mesh has far more than six distinct side directions.
            _built = WeaponPartArt.Build(WeaponPartArt.Keys.PowerCellSecondary);

            Assert.IsNull(_built.transform.Find("UpperFacet"),
                "UpperFacet is still a separate PrimitiveType.Cylinder body piece.");
            Assert.IsNull(_built.transform.Find("LowerFacet"),
                "LowerFacet is still a separate PrimitiveType.Cylinder body piece.");

            var facets = _built.transform.Find("Facets");
            Assert.IsNotNull(facets, "the power cell has no faceted body mesh.");

            var mesh = facets.GetComponent<MeshFilter>()?.sharedMesh;
            Assert.IsNotNull(mesh, "the Facets part has no mesh assigned.");

            int[] sideDirections = mesh.vertices
                .Where(v => Mathf.Abs(v.x) > 0.001f || Mathf.Abs(v.z) > 0.001f)   // skip the axis-aligned apex verts
                .Select(v => Mathf.RoundToInt(Mathf.Atan2(v.z, v.x) * Mathf.Rad2Deg))
                .Distinct()
                .ToArray();

            Assert.AreEqual(6, sideDirections.Length,
                $"the facet mesh has {sideDirections.Length} distinct side directions, not six — it still reads round, not hexagonal.");
        }

        [Test]
        public void PowerCellReadsAsAHexNutAndBolt_DistinctFromTheGearCog_WithPickupKindUnchanged()
        {
            // MV-725 — the currency renamed "Cells" to "Parts" (MV-671) but the art still drew a
            // battery. Pins the fix (a hex nut with a bolt actually running through it) and its two
            // guardrails: the new shape must not collide with WeaponPartArt.Keys.Gear's cosmetic cog
            // silhouette, and the gameplay-facing PickupKind enum must be untouched by an art-only ticket.
            var cell = WeaponPartArt.Build(WeaponPartArt.Keys.PowerCell);
            var gear = WeaponPartArt.Build(WeaponPartArt.Keys.Gear);

            // The bolt has to actually go THROUGH the nut: its resolved world bounds must clear the nut
            // walls' bounds on both the top and the bottom, not just sit flush inside them.
            var shaft = cell.transform.Find("BoltShaft");
            Assert.IsNotNull(shaft, "the power cell has no BoltShaft — it doesn't read as a bolt through a nut.");
            // Seeded from Wall0's own bounds, not a zero-size Bounds at the root's (0,0,0) position — the
            // latter would silently union in the origin point and drag wallBounds.min.y down to 0
            // regardless of where the walls actually start.
            var wall0 = cell.transform.Find("Wall0");
            Assert.IsNotNull(wall0, "the power cell nut has no Wall0.");
            var wallBounds = wall0.GetComponent<MeshRenderer>().bounds;
            for (int i = 1; i < 6; i++)
            {
                var wall = cell.transform.Find($"Wall{i}");
                Assert.IsNotNull(wall, $"the power cell nut has no Wall{i}.");
                wallBounds.Encapsulate(wall.GetComponent<MeshRenderer>().bounds);
            }
            var shaftBounds = shaft.GetComponent<MeshRenderer>().bounds;
            Assert.Greater(shaftBounds.max.y, wallBounds.max.y,
                "the bolt shaft doesn't protrude above the nut — it doesn't read as passing through it.");
            Assert.Less(shaftBounds.min.y, wallBounds.min.y,
                "the bolt shaft doesn't protrude below the nut — it doesn't read as passing through it.");

            // AC3 — the Part pickup and the Keys.Gear cosmetic drop must not share a silhouette: compare
            // renderer count and overall bounds aspect, the same "signature" idiom
            // TheFiveParts_AreDistinctSilhouettes above already uses for exactly this kind of comparison.
            var cellRenderers = cell.GetComponentsInChildren<MeshRenderer>();
            var gearRenderers = gear.GetComponentsInChildren<MeshRenderer>();
            var cellBounds = cellRenderers[0].bounds;
            for (int i = 1; i < cellRenderers.Length; i++) cellBounds.Encapsulate(cellRenderers[i].bounds);
            var gearBounds = gearRenderers[0].bounds;
            for (int i = 1; i < gearRenderers.Length; i++) gearBounds.Encapsulate(gearRenderers[i].bounds);

            bool sameCount = cellRenderers.Length == gearRenderers.Length;
            bool sameAspect = Mathf.Approximately(
                Mathf.Round(cellBounds.size.x / Mathf.Max(cellBounds.size.y, 0.01f) * 4f),
                Mathf.Round(gearBounds.size.x / Mathf.Max(gearBounds.size.y, 0.01f) * 4f));
            Assert.IsFalse(sameCount && sameAspect,
                "the Part pickup and the Gear cosmetic drop share a renderer-count-and-aspect signature — " +
                $"cell: {cellRenderers.Length} renderers, {cellBounds.size:F2}; gear: {gearRenderers.Length} renderers, {gearBounds.size:F2}.");

            // AC4 — this is an art-only ticket; PickupKind must not have gained/lost/reordered a member.
            var expectedKinds = new[] { "PowerCell", "Supercell", "Device", "PowerCellSecondary", "WeaponCore" };
            CollectionAssert.AreEqual(expectedKinds, System.Enum.GetNames(typeof(MaxWorlds.Pickups.PickupKind)),
                "PickupKind's members or their order changed — MV-725 is art-only, the enum must be untouched.");

            Object.DestroyImmediate(cell);
            Object.DestroyImmediate(gear);
        }

        [Test]
        public void AnUnknownKeyBuildsNothing_RatherThanThrowing()
        {
            // A gameplay drop table with a typo should drop nothing, not error the run.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unknown part key"));
            Assert.IsNull(WeaponPartArt.Build("not_a_real_part"));
        }
    }
}
