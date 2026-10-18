using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1231 AC1: the Breaker's finished look resolves to the ticket's own 2.4 m wide, 2.2 m tall body
    /// (MV-1228's own placeholder <c>BuildBreaker</c> was never tuned to this — it was flagged a
    /// deliberate greybox, "not the Breaker's final silhouette"), and its steel collar's chain actually
    /// releases from the wall anchor the instant <see cref="ChainSignals.ChainSnapped"/> fires, rather
    /// than staying a decoration frozen in its taut, dormant pose.
    ///
    /// Fails on d5e1fba (this ticket's own named base): <see cref="RobotBodies.Body"/> carries no
    /// <c>BreakerChainPivot</c>/<c>BreakerChainEnd</c>/<c>BreakerWallAnchor</c> fields there, and
    /// <see cref="RobotRig"/> has no <c>_breakerChainEnd</c>/<c>_breakerWallAnchor</c> fields either, so
    /// this test does not compile on that commit.
    ///
    /// EditMode only, reflection-driven (repo convention): <c>Awake</c>/<c>OnEnable</c> are not a
    /// reliable <c>AddComponent</c> side effect outside Play mode, so <c>RobotRig.EnsureBuilt</c> is
    /// invoked directly — the same idiom <c>RobotBodySizeTests.BuildRig</c> already established.
    /// </summary>
    public sealed class MV1231BreakerLookTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static RobotRig BuildRig(out RobotEnemy enemy)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // EnemyArchetype.Breaker.Shape is Box
            go.AddComponent<CharacterController>();

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(EnemyArchetype.Breaker);
            enemy = e;

            var rig = go.AddComponent<RobotRig>();

            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", NonPublicInstance).Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            return rig;
        }

        /// <summary>Combined world-space bounds of every built part — same encapsulate-every-MeshRenderer
        /// pattern <c>RobotBodySizeTests.CombinedBoundsHeight</c> already established, extended to report
        /// width too (the ticket's own AC asserts both).</summary>
        private static (float width, float height) CombinedBoundsSize(RobotRig rig)
        {
            Bounds? combined = null;
            foreach (var r in rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (combined == null) { combined = r.bounds; continue; }
                var b = combined.Value;
                b.Encapsulate(r.bounds);
                combined = b;
            }
            Assert.IsNotNull(combined, "the Breaker built no visible parts at all");
            return (combined.Value.size.x, combined.Value.size.y);
        }

        [Test]
        public void BreakerLook_ResolvesToItsAuthoredSize_AndReleasesItsChainOnChainSnapped()
        {
            RobotRig rig = null;
            try
            {
                rig = BuildRig(out RobotEnemy enemy);

                (float width, float height) = CombinedBoundsSize(rig);
                Assert.That(width, Is.EqualTo(2.4f).Within(0.1f),
                    $"the Breaker's resolved body must be 2.4m +-0.1 wide, got {width:0.00}m");
                Assert.That(height, Is.EqualTo(2.2f).Within(0.1f),
                    $"the Breaker's resolved body must be 2.2m +-0.1 tall, got {height:0.00}m");

                var chainEnd = (Transform)typeof(RobotRig).GetField("_breakerChainEnd", NonPublicInstance).GetValue(rig);
                var wallAnchor = (Transform)typeof(RobotRig).GetField("_breakerWallAnchor", NonPublicInstance).GetValue(rig);
                Assert.IsNotNull(chainEnd, "a Breaker must build a chain-end transform");
                Assert.IsNotNull(wallAnchor, "a Breaker must build a wall-anchor transform");

                float dormantGap = Vector3.Distance(chainEnd.position, wallAnchor.position);
                Assert.LessOrEqual(dormantGap, 0.1f,
                    $"dormant, the chain must run taut to the wall anchor, got {dormantGap:0.00}m apart");

                ChainSignals.EmitChainSnapped(enemy);

                float releasedGap = Vector3.Distance(chainEnd.position, wallAnchor.position);
                Assert.Greater(releasedGap, 0.1f,
                    "once ChainSnapped fires the chain's anchor joint must release — the end must no " +
                    $"longer sit within 0.1m of the wall anchor, got {releasedGap:0.00}m apart");
            }
            finally
            {
                if (rig != null) Object.DestroyImmediate(rig.gameObject);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
