using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Dressing;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-75 — the dressed yard, actually built.
    ///
    /// The plan is unit-tested on its own (DressingPlanTests). What this asserts is the part that
    /// only exists once the props are real objects in a real scene: that a yard full of set-dressing
    /// still has nothing in it to walk into, that the kit's models loaded at all, and that none of
    /// them ended up wearing a material the WebGL build can't draw — which is the failure mode this
    /// project has actually shipped before (YT-61, the magenta).
    /// </summary>
    public sealed class BackyardDressingPlayTests
    {
        private GameObject _path;
        private GameObject _dressing;

        [TearDown]
        public void TearDown()
        {
            if (_dressing != null) Object.Destroy(_dressing);
            if (_path != null) Object.Destroy(_path);
        }

        private IEnumerator BuildYard()
        {
            _path = new GameObject("Backyard Path");
            _path.AddComponent<BackyardPath>();      // builds the walls, floor and cover in Awake
            yield return null;

            _dressing = new GameObject("BackyardDressing");
            _dressing.AddComponent<BackyardDressing>();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheYard_IsActuallyDressed()
        {
            yield return BuildYard();

            var dressing = _dressing.GetComponent<BackyardDressing>();
            Assert.Greater(dressing.PropCount, 80,
                "the garden kit didn't load — the yard is still a grey box");

            var skins = Object.FindObjectsByType<DressingSkin>(FindObjectsSortMode.None);
            Assert.Greater(skins.Length, 80, "the props aren't marked as dressing");
        }

        [UnityTest]
        public IEnumerator NoPieceOfDressing_CanBeWalkedInto()
        {
            yield return BuildYard();

            // The whole reason this stream can fill the arena without touching the gameplay stream:
            // set-dressing is scenery, not level geometry. Not one collider, anywhere.
            var colliders = _dressing.GetComponentsInChildren<Collider>();

            Assert.IsEmpty(colliders.Select(c => c.transform.parent != null
                                                     ? $"{c.transform.parent.name}/{c.name}"
                                                     : c.name),
                "set-dressing must never obstruct movement, and a collider is how it would");
        }

        [UnityTest]
        public IEnumerator EveryProp_WearsAMaterialTheBuildCanDraw()
        {
            yield return BuildYard();
            yield return null;   // give RuntimeSurfaceDirector its sweep

            foreach (var r in _dressing.GetComponentsInChildren<MeshRenderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    Assert.IsNotNull(m, $"{r.name} has an empty material slot");
                    Assert.IsNotNull(m.shader, $"{r.name} has a material with no shader");
                    Assert.IsTrue(m.shader.isSupported, $"{r.name} wears an unsupported shader: {m.shader.name}");
                    Assert.That(m.shader.name, Does.Not.Contain("InternalErrorShader"),
                        $"{r.name} would render magenta in a player build");

                    // The tell-tale from YT-61: Unity's built-in default has no URP subshader, and
                    // renders fine in the editor while rendering magenta in the actual build.
                    Assert.That(m.name, Does.Not.StartWith("Default-Material"),
                        $"{r.name} kept Unity's built-in default material");
                }
            }
        }

        [UnityTest]
        public IEnumerator TheCoverBlocks_KeepTheirColliders_AndLoseTheirGreybox()
        {
            yield return BuildYard();

            foreach (var c in BackyardCover.Default)
            {
                var block = _path.GetComponentsInChildren<Transform>()
                                 .FirstOrDefault(t => t.name == c.Name);
                Assert.IsNotNull(block, $"{c.Name} wasn't built");

                Assert.IsNotNull(block.GetComponent<Collider>(),
                    $"{c.Name} lost the collider the fight is built around");

                var r = block.GetComponent<MeshRenderer>();
                Assert.IsNotNull(r, $"{c.Name} lost its renderer entirely");
                Assert.IsFalse(r.enabled,
                    $"{c.Name} is still showing its greybox through the prop standing on it");
            }
        }

        [UnityTest]
        public IEnumerator TheFence_IsTheHeightOfTheWallItHides()
        {
            yield return BuildYard();

            var panels = _dressing.GetComponentsInChildren<Transform>()
                                  .Where(t => t.name == KitModels.FencePanel)
                                  .ToList();
            Assert.Greater(panels.Count, 60, "the yard isn't fenced");

            // The plan asks for a 3.4 m panel; the kit is authored at some unit scale of its own.
            // This is the assertion that we fitted the model to the world instead of trusting it.
            foreach (var p in panels.Take(10))
            {
                Bounds b = default;
                bool any = false;
                foreach (var r in p.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }

                Assert.IsTrue(any, "a fence panel with no renderer");
                Assert.That(b.size.y, Is.EqualTo(3.4f).Within(0.15f),
                    $"a fence panel came out {b.size.y:0.00} m tall");
                Assert.That(b.min.y, Is.EqualTo(0f).Within(0.1f),
                    "a fence panel should stand on the ground, not float or sink");
            }
        }
    }
}
