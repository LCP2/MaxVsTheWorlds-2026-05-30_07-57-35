using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-744: World 3 was dressed with World 1's garden kit — foliage, fence planks, planters — on a
    /// ship deck. MV-750 already fenced <c>BackyardDressingSet</c> off from every world but World 1
    /// (<c>BackyardDressingTests.OnlyTheGardenWorldGetsTheGardenDressing</c> covers that half). What was
    /// still missing is the OTHER half of a real per-world kit: World 3's own cover category —
    /// "machinery", authored 424 times across <c>world3_config.json</c> — had nowhere to go.
    /// <see cref="ReefKit.BuildCoolantTurret"/> existed since MV-713 but nothing ever called it, so a
    /// machinery cover piece stayed a bare box wearing the ordinary swept Reef Prop material — a
    /// different <see cref="Material"/> instance from any of <see cref="WorldMaterials"/>' eight named
    /// Reef materials, even though it happens to share a colour with one of them.
    ///
    /// <see cref="CoverDressing.Machinery"/> and <see cref="ReefDressing"/> do not exist before this
    /// ticket, so this fails to COMPILE on 6845204 (the commit before this fix) — the same "doesn't
    /// exist there yet" failure MV713ReefKitTests documents for its own base commit.
    ///
    /// Asserts RESOLVED state only: the actual placed-object count, the cover block's own renderer
    /// state, and a same-reference (<c>Assert.AreSame</c>) check against the real
    /// <see cref="WorldMaterials.M_Circuit_Cyan"/> instance — not a name string, not a colour match.
    /// </summary>
    public sealed class MV744World3DressingKitTests
    {
        [Test]
        public void MachineryCoverBecomesAReefTurret_ByMaterialIdentity_NotAGenericSweptProp()
        {
            var host = new GameObject("MV744 host").transform;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var cover = new ArenaCover("a1_machinery", Vector2.zero, new Vector3(1.8f, 1.8f, 1.8f),
                    CoverShape.Box, CoverDressing.Machinery);
                var piece = new CoverPiece(cover, body);

                int placed = ReefDressing.DressCover(host, new List<CoverPiece> { piece });

                Assert.AreEqual(1, placed, "one machinery cover piece should place exactly one turret");

                // Collider stays (it is what stops Max/a robot/the boss); only the art swaps — the same
                // contract BackyardDressing.DressCover keeps for its own Tree/Hedge/Planter/Shed cases.
                Assert.IsNotNull(body.GetComponent<Collider>(),
                    "the cover block must keep its collider once it is dressed");
                Assert.IsFalse(body.GetComponent<Renderer>().enabled,
                    "the cover block's own renderer must be hidden once a turret stands in its place");

                MeshRenderer turret = host.GetComponentsInChildren<MeshRenderer>(true)
                    .FirstOrDefault(r => r.gameObject.name == "CoolantTurret");
                Assert.IsNotNull(turret, "no CoolantTurret was built for the machinery cover piece");

                // AC3, read literally: identity against the real named material, not its colour.
                Assert.AreSame(WorldMaterials.M_Circuit_Cyan, turret.sharedMaterial,
                    "the turret must wear one of WorldMaterials' own eight named Reef materials by " +
                    "identity — a generic swept Prop material of the same colour does not satisfy this");
            }
            finally
            {
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
