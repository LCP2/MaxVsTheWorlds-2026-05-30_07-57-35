using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-702 (the one new test, per the testing policy): the LPPE gadget submesh <see cref="MaxBody.Build"/>
    /// hands back must actually swap with <c>WeaponSystemState.ActivePrimary</c> — resolved
    /// <c>GameObject.activeSelf</c> state after running the real toggle logic
    /// (<see cref="MaxRig.ApplyPrimaryVisual(GameObject, GameObject, WeaponCatalog.PrimaryKind)"/>),
    /// never an authored constant.
    ///
    /// Fails on the MV-693 merge commit (55bc034): <c>MaxBodyResult</c> has no <c>RcdaGadget</c>/
    /// <c>LppeGadget</c> members and <c>MaxRig</c> has no <c>ApplyPrimaryVisual</c> overload there — this
    /// does not compile against that commit (CS1729/CS0117, "MaxBodyResult does not contain a
    /// constructor that takes 7 arguments" / "does not contain a definition for 'RcdaGadget'").
    /// </summary>
    public sealed class MV702LppeGadgetSwapTests
    {
        [Test]
        public void TheGadgetSubmeshSwapsWithActivePrimary_MV702()
        {
            var root = new GameObject("MV702TestRoot");
            try
            {
                var palette = new MaxPalette(null, null, null, null, null, null, null, null, null, null, null, null, null);
                MaxBodyResult body = MaxBody.Build(root.transform, palette, hipY: 0.74f);

                // Built with the RCDA showing and the LPPE hidden (RCDA is Max's run-start primary).
                Assert.That(body.RcdaGadget.activeSelf, Is.True, "the RCDA gadget must be visible by default");
                Assert.That(body.LppeGadget.activeSelf, Is.False, "the LPPE gadget must start hidden");

                // Flip to the LPPE (MV-689's World 2 morph) — the resolved visibility must swap.
                MaxRig.ApplyPrimaryVisual(body.RcdaGadget, body.LppeGadget, WeaponCatalog.PrimaryKind.Lppe);
                Assert.That(body.RcdaGadget.activeSelf, Is.False, "the RCDA gadget must hide once ActivePrimary is Lppe");
                Assert.That(body.LppeGadget.activeSelf, Is.True, "the LPPE gadget must show once ActivePrimary is Lppe");

                // And back — the swap is not one-directional.
                MaxRig.ApplyPrimaryVisual(body.RcdaGadget, body.LppeGadget, WeaponCatalog.PrimaryKind.Rcda);
                Assert.That(body.RcdaGadget.activeSelf, Is.True, "the RCDA gadget must return once ActivePrimary is Rcda again");
                Assert.That(body.LppeGadget.activeSelf, Is.False, "the LPPE gadget must hide once ActivePrimary is Rcda again");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
