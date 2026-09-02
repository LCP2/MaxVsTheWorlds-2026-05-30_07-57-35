using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-651: <c>cc-verify.bat</c>'s frame-time gate always failed "no robots ever spawned within 30s"
    /// — never a measured PASS or a measured p95 FAIL. Root cause: <see cref="BigBermudaRig"/>'s private
    /// <c>NewCharacterMaterial</c> called <c>new Material(MaterialLibrary.SurfaceShader)</c> whenever
    /// <see cref="MaterialLibrary.Character"/> returned null — but in the exact
    /// <c>-batchmode -nographics</c> standalone player <c>cc-verify</c> boots for this gate, EVERY shader
    /// in <c>MaterialLibrary.ShaderChain</c> also resolves to null (confirmed via temporary
    /// instrumentation: <c>Logs/perf-run.log</c> showed an uncaught <c>ArgumentNullException</c> from
    /// <c>new Material(null)</c> inside <c>BigBermudaRig.BuildMaterials</c>, thrown while
    /// <c>MapRuntime.Build</c> built a12's boss). That exception unwound <c>BackyardPath.Awake()</c>
    /// before it ever reached <c>WorldRunner.Configure()</c>, so no shed's <c>EnemySpawner</c> was ever
    /// wired to its area's composition — every shed's <c>AreaCadence</c> stayed the default
    /// "unauthored" one and emitted nothing, forever, which is exactly the persistent
    /// "EnemyMix.AreaCadence: shed's area authors an empty composition" warning (one per shed, 17 total)
    /// the ticket's <c>Logs/perf-run.log</c> showed and the PerfCaptureDirector's 30s warmup timeout
    /// against <c>RobotEnemy.ActiveCount</c> then correctly reported as a FAIL.
    ///
    /// This asserts the RESOLVED return value of the exact guard the fix added — a Tier 2 assertion, not
    /// an authored constant and not a rendered pixel: with no template AND no fallback shader (the
    /// starved condition this ticket's evidence showed actually occurs), the method must degrade to a
    /// null material rather than crash, the same contract every other <see cref="MaterialLibrary"/>
    /// shader-lookup path in this codebase already honours (MaterialLibrary.Character()/Build()).
    /// </summary>
    public sealed class MV651NoShaderMaterialTests
    {
        [Test]
        public void NewCharacterMaterial_NoTemplateAndNoFallbackShader_DegradesToNullInsteadOfThrowing()
        {
            MethodInfo method = typeof(BigBermudaRig).GetMethod(
                "NewCharacterMaterial",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Material), typeof(Shader), typeof(string), typeof(Color) },
                null);

            Assert.IsNotNull(method,
                "BigBermudaRig must still expose a NewCharacterMaterial(Material, Shader, string, Color) " +
                "overload for this test to drive the no-shader branch directly");

            object result = null;
            Assert.DoesNotThrow(() =>
            {
                result = method.Invoke(null, new object[] { null, null, "Test_NoShader", Color.white });
            }, "no template and no fallback shader must degrade to a null material, not throw " +
               "ArgumentNullException out of new Material(null) (MV-651)");

            Assert.IsNull(result,
                "with no template and no fallback shader available, the resolved material must be null");
        }
    }
}
