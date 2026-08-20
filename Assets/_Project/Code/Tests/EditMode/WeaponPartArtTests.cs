using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Rendering;
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
        public void PowerCellCasing_IsTintedTheCellsOwnCyan()
        {
            var root = WeaponPartArt.BuildPowerCell();
            var casing = root.transform.Find("Casing");
            Assert.IsNotNull(casing, "the power cell prop has no Casing.");

            var mr = casing.GetComponent<MeshRenderer>();
            var expected = MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.CellCyan);
            Assert.AreSame(expected, mr.sharedMaterial,
                "the power cell casing isn't tinted the cell's own cyan — it'll read drab next to the core band.");

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
        public void ThePowerCell_CarriesSpecularGlintsOnItsCasing()
        {
            // YT-167, extended WV-236: the soft additive Core band (YT-145) is the aura, not the glisten —
            // Lee's playtest still read the shipped cell as flat because a halo isn't a specular
            // highlight, and "shine and glisten like DIAMONDS" (WV-236) means several facets, not one
            // pair. Pin that the cell wears four distinct glint dots, separate from the Core, so this
            // can't regress back to "just the aura" — or back to two facets — quietly.
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
