using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The weapon-upgrade props and the power cell (YT-134). PlayMode because the builders strip
    /// colliders with <c>Object.Destroy</c>, which only takes effect after a frame — an EditMode test
    /// would see the colliders still attached and pass a prop that fights the Pickup's own trigger.
    ///
    /// Every prop in this catalog is a runtime pile of primitives, which is precisely the shape that
    /// ships MAGENTA in a build (a primitive keeps Unity's default material, no URP subshader) and the
    /// shape the surface director repaints as stone. These pin both.
    /// </summary>
    public sealed class WeaponPartArtPlayTests
    {
        private static readonly string[] AllKeys =
        {
            WeaponPartArt.Keys.BeamNozzle, WeaponPartArt.Keys.PowerNozzle,
            WeaponPartArt.Keys.AugmentationHarness, WeaponPartArt.Keys.AccelerationEngine,
            WeaponPartArt.Keys.HydroDevice, WeaponPartArt.Keys.PowerCell,
        };

        private GameObject _built;

        [TearDown]
        public void TearDown() { if (_built != null) Object.Destroy(_built); }

        [UnityTest]
        public IEnumerator EveryPropBuilds_WithRealMaterials_AndNoColliders()
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

                yield return null;   // let the collider-strip Destroy() land
                Assert.IsEmpty(_built.GetComponentsInChildren<Collider>(),
                    $"'{key}' kept a collider — it would fight the Pickup's own walk-over trigger.");

                Object.Destroy(_built);
                _built = null;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TheFiveParts_AreDistinctSilhouettes()
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
                Object.Destroy(go);
                yield return null;
            }

            Assert.AreEqual(signatures.Count, signatures.Distinct().Count(),
                "two of the five parts read as the same shape — they have to be tellable apart at game zoom.");
        }

        [UnityTest]
        public IEnumerator TheTapPresentsItsSpoutAtTheCouplingHeight()
        {
            _built = GardenTapArt.Build();
            var spout = _built.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Spout");
            Assert.IsNotNull(spout, "the tap has no spout for the hose to meet.");
            Assert.That(spout.localPosition.y, Is.EqualTo(GardenTapArt.SpoutHeight).Within(0.2f),
                "the spout is not at the hose-coupling height — the tether would meet the tap in mid-air.");
            yield return null;
            Assert.IsEmpty(_built.GetComponentsInChildren<Collider>(), "the tap prop kept a collider.");
        }

        [UnityTest]
        public IEnumerator ThePowerCell_CarriesSpecularGlintsOnItsCasing()
        {
            // YT-167: the soft additive Core band (YT-145) is the aura, not the glisten — Lee's playtest
            // still read the shipped cell as flat because a halo isn't a specular highlight. Pin that the
            // cell wears its own glint dots, separate from that Core, so this can't regress back to "just
            // the aura" quietly.
            _built = WeaponPartArt.Build(WeaponPartArt.Keys.PowerCell);

            var glint0 = _built.transform.Find(WeaponPartArt.GlistenPrefix + "0");
            var glint1 = _built.transform.Find(WeaponPartArt.GlistenPrefix + "1");
            Assert.IsNotNull(glint0, "the power cell has no first glint dot.");
            Assert.IsNotNull(glint1, "the power cell has no second glint dot.");
            Assert.AreNotEqual(glint0.localPosition, glint1.localPosition,
                "the two glints sit in the same spot — only one point on the casing would ever sparkle.");

            var core = _built.transform.Find("Core");
            Assert.IsNotNull(core, "the cell lost its YT-145 aura core.");
            Assert.AreNotEqual(core.localPosition, glint0.localPosition,
                "a glint sits exactly on the aura core — it would read as one glow, not a distinct sparkle.");

            yield return null;   // let the collider-strip Destroy() land before TearDown
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

        [UnityTest]
        public IEnumerator AnUnknownKeyBuildsNothing_RatherThanThrowing()
        {
            // A gameplay drop table with a typo should drop nothing, not error the run.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unknown part key"));
            Assert.IsNull(WeaponPartArt.Build("not_a_real_part"));
            yield return null;
        }
    }
}
