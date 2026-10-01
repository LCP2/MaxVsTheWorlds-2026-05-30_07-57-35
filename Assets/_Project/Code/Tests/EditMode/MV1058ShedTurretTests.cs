using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1058 (Lee, live build, 2026-10-01): shed corner turrets mostly read as "plain cubes sitting on
    /// the corners" — only Missile (MV-913) had gotten a generated-mesh rig; Spiker and Laser were still
    /// a bare <c>GameObject.CreatePrimitive(PrimitiveType.Cube)</c> at the authored 0.5 m FittingSize,
    /// tinted the shed's own Structure colour, so they blended into the roof. Fail-first on 12419fa: a
    /// Spiker/Laser fitting's only renderer carries a built-in "Cube" mesh sized to FittingSize — well
    /// under both thresholds this test asserts.
    ///
    /// Tier 2 (resolved values, not authored constants): reads the BUILT fitting's own combined renderer
    /// bounds and mesh names, never an authored field, so a renderer that draws something smaller (or
    /// still a cube) fails this even if some other field elsewhere claims otherwise. One test (per
    /// CC_AUTONOMY's one-new-test rule) covering all three kinds via TestCase, the same shape
    /// <c>MV547ShedFittingTests</c> and <c>MV911</c>'s own guard already use.
    /// </summary>
    public sealed class MV1058ShedTurretTests
    {
        [TestCase(ShedFittingKind.Spiker)]
        [TestCase(ShedFittingKind.Laser)]
        [TestCase(ShedFittingKind.Missile)]
        public void ShedTurret_ReadsAsALargeRedDomeWithALongBarrel_NoPrimitiveCubeAnywhere(ShedFittingKind kind)
        {
            var go = new GameObject();
            go.transform.localScale = Vector3.one * 0.5f; // MapRuntime.FittingSize, the corner mount scale
            try
            {
                var fitting = go.AddComponent<ShedFitting>();
                fitting.Bind(null, kind);

                Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
                Assert.Greater(renderers.Length, 0, $"{kind} fitting built no visible parts at all");

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

                Assert.GreaterOrEqual(bounds.size.x, 0.85f - 1e-3f,
                    $"{kind} fitting reads only {bounds.size.x:F2} m across in X, short of the ticket's 0.85 m dome");
                Assert.GreaterOrEqual(bounds.size.z, 0.85f - 1e-3f,
                    $"{kind} fitting reads only {bounds.size.z:F2} m across in Z, short of the ticket's 0.85 m dome");
                Assert.GreaterOrEqual(Mathf.Max(bounds.size.x, bounds.size.z), 1.5f,
                    $"{kind} fitting's longest horizontal extent is only {Mathf.Max(bounds.size.x, bounds.size.z):F2} m, " +
                    "short of the ticket's 1.5 m barrel-tip-to-back-of-dome reach");

                foreach (Renderer r in renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    Assert.IsNotNull(mf != null ? mf.sharedMesh : null, $"{r.name} carries no mesh");
                    StringAssert.DoesNotContain("Cube", mf.sharedMesh.name,
                        $"{r.name} ({kind}) still draws a primitive cube, not a generated mesh");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
