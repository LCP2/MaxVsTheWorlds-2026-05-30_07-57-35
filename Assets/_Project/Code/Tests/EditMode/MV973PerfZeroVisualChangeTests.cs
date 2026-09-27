using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-973 — "improve fps/heat with no difference to the quality of the game" (Lee's rule, 2026-09-26).
    /// Of the ticket's three sub-changes, the URP cascade count/soft-shadow flag is the one an EditMode
    /// test can assert without a graphics device (Tier 2 — a resolved asset property). MV-973's third
    /// sub-change — switching physics to <c>SimulationMode.Script</c> with a manual per-frame
    /// <c>Physics.SyncTransforms()</c> driver — was reverted by MV-984 (it left every
    /// <see cref="UnityEngine.CharacterController"/>'s scene-query position stuck at spawn, so no
    /// physics query could ever hit an awake robot); see <c>MV984AwakeRobotsTakeDamageTests</c> for that
    /// regression's coverage. The physics-mode comparison this test used to make here was culled with it
    /// (MV-465 culling policy: ticket's own changes make it redundant — there is no longer a physics-mode
    /// switch for it to prove equivalent to auto-simulation).
    ///
    /// The ticket's own AC1(a) asks for a further assertion this project's Testing policy (CLAUDE.md,
    /// MV-465 Rule 2) forbids an EditMode test from making at all: "renders ... to a RenderTexture ...
    /// and asserts the per-pixel difference" is a rendered-pixel assertion, and "no EditMode test may
    /// assert a rendered pixel ... EditMode asserts resolved values; the conformance harness asserts
    /// rendered ones" is that policy's own wording, written after a real defect (WeaponsScreen's
    /// fontSize/resizeTextMaxSize mismatch) shipped past exactly this kind of test. It is also not
    /// computable under this project's own `-batchmode -nographics` EditMode run regardless — see
    /// ShaderKitTests.EveryPassThatPositionsAPlant_BendsItTheSameWay's own comment ("pass enumeration
    /// needs a graphics device and the whole verify runs headless") for the same limitation hit before.
    /// Left unasserted here by policy, not by oversight; recorded in this ticket's Jira comment.
    /// </summary>
    public sealed class MV973PerfZeroVisualChangeTests
    {
        private const string MobileAssetPath = "Assets/Settings/Mobile_RPAsset.asset";

        [Test]
        public void UrpHasOneCascadeWithSoftShadowsUnchanged()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAssetPath);
            Assert.IsNotNull(urp, $"Mobile URP asset missing: {MobileAssetPath}");

            Assert.AreEqual(1, urp.shadowCascadeCount,
                "MV-973 asks for one shadow cascade at the MV-969 distance — near-field shadow texel " +
                "density should be equal or better than today's first cascade with three fewer cascade " +
                "splits to evaluate per pixel.");
            Assert.IsTrue(urp.supportsSoftShadows,
                "MV-973 keeps soft shadows as they are — only the cascade count changes.");
        }
    }
}
