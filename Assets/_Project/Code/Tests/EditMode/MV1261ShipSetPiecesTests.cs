using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1261 AC1 (one new EditMode test, MV-465 Rule 1): <c>ShipSetPieces</c> does not exist before
    /// this ticket, so <see cref="ShipDressing.Dress"/> never builds a "Ship Set Pieces" host and the 5
    /// real hero areas (a1, a8, a15, a22, a30) carry no hero set-piece root at all — this fails to
    /// COMPILE on the base commit (the "doesn't exist there yet" fail-first shape
    /// <c>MV1258ReefSetPiecesTests</c>/<c>MV1257ReefAmbientLifeTests</c> already document for their own
    /// base commits), and would fail to find any of the 5 named roots below even if the test were
    /// written against a stub. See Logs/MV1261-failfirst.log, quoted in the fix comment.
    ///
    /// Loads the REAL shipped Mothership config through the production path (<see cref="WorldLibrary.Load"/>
    /// -> <see cref="WorldMapLoader.TryLoad"/> -> <see cref="MapRuntime.Build"/>), then runs the exact
    /// same <see cref="ShipDressing.Dress"/> call the real game runs — so the areas and cover pieces it
    /// reads back are the real ones a built Mothership map actually has, not a hand-rolled fixture.
    /// Asserts RESOLVED values only (testing policy Tier 2): each hero area's own set-piece root
    /// resolves, its renderer count, that it carries no <see cref="Collider"/>, and that no
    /// <see cref="MeshFilter"/> under it uses Unity's built-in "Cube" primitive mesh — never an authored
    /// constant, never a rendered pixel.
    /// </summary>
    public sealed class MV1261ShipSetPiecesTests
    {
        private static readonly (int areaIndex, string heroRootName)[] HeroAreas =
        {
            (1, "Hangar Bay Hero"),
            (8, "Assembly Line Hero"),
            (15, "Specimen Vault Hero"),
            (22, "Reactor Ring Hero"),
            (30, "Bridge Hero"),
        };

        [Test]
        public void RealMothershipMap_EveryHeroAreaResolvesASetPieceRoot_With10PlusRenderers_NoColliders_NoCubePrimitive()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV-1261 Mothership Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, map, cfg, built.Cover);

                Transform setPieces = root.transform.Find("Ship Set Pieces");
                Assert.IsNotNull(setPieces, "ShipDressing must build a 'Ship Set Pieces' host (MV-1261)");

                foreach ((int areaIndex, string heroRootName) in HeroAreas)
                {
                    Transform heroRoot = setPieces.Find(heroRootName);
                    Assert.IsNotNull(heroRoot, $"area {areaIndex} must carry its own '{heroRootName}' set-piece root");

                    Renderer[] renderers = heroRoot.GetComponentsInChildren<Renderer>(true);
                    Assert.GreaterOrEqual(renderers.Length, 10,
                        $"'{heroRootName}' (area {areaIndex}) must carry at least 10 renderers, found {renderers.Length}");

                    Collider[] colliders = heroRoot.GetComponentsInChildren<Collider>(true);
                    Assert.AreEqual(0, colliders.Length,
                        $"'{heroRootName}' (area {areaIndex}) must add no Collider, found {colliders.Length}");

                    foreach (MeshFilter mf in heroRoot.GetComponentsInChildren<MeshFilter>(true))
                        Assert.AreNotEqual("Cube", mf.sharedMesh != null ? mf.sharedMesh.name : null,
                            $"'{mf.name}' under '{heroRootName}' uses the built-in Cube primitive mesh — hero set pieces must use generated meshes only");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
