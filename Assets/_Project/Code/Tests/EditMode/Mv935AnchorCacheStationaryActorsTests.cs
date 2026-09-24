using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-935 (Lee, 2026-09-24, live WebGL build b4bef27-0924-0806, World 2 a10): the anch bucket read
    /// 16.9 ms against a &lt;1.5 ms target with 24 awake robots, 75 Dormant robots and 3 Sentinels
    /// within <see cref="GroundAnchorVfx"/>'s scan radius. <c>LateUpdate</c> re-ran <c>Ground</c> (a
    /// <c>MapData</c> lookup) and both <c>GroundRing.Show</c> calls (a transform write plus a
    /// MaterialPropertyBlock round trip) for EVERY actor in range, every single frame, whether or not
    /// that actor had moved since the last one — true for most of a garrisoned area's population, since
    /// a Dormant robot (or an idle Sentinel) does not move at all until something wakes it.
    ///
    /// Fails against the pre-fix always-recompute shape (reproduced here by forcing the cache's
    /// "unchanged" short-circuit permanently false — the exact behaviour of the code before this
    /// ticket, which had no such check at all):
    ///
    ///   Assert.AreEqual failed. Expected: 1 But was: 3
    ///   MV-935: an actor that has not moved must not be re-anchored on the next frame —
    ///   AnchorRecomputeCount rose from 1 to 3 across 2 more unmoved frames, which is the exact
    ///   per-frame repaint cost (Ground() + two GroundRing.Show() calls per stationary actor) that read
    ///   16.9ms live against a ~100-actor World 2 garrison
    ///
    /// (Measured directly: the "unchanged" guard in <c>GroundAnchorVfx.LateUpdate</c> was stashed back
    /// to always evaluate false, this test re-run, then the guard restored.)
    ///
    /// Tier 2 (resolved values): reads <see cref="GroundAnchorVfx.AnchorRecomputeCount"/> — a resolved
    /// call count the engine itself drives, incremented only where the real repaint work happens — and
    /// the ring's own placed world position off the real pooled <see cref="GroundRing"/>. Never an
    /// authored constant, never a rendered pixel.
    /// </summary>
    public sealed class Mv935AnchorCacheStationaryActorsTests
    {
        private sealed class TestActor : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Enemy;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static readonly MethodInfo LateUpdateMethod =
            typeof(GroundAnchorVfx).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        private static GameObject SpawnActor(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.5f;
            go.AddComponent<TestActor>();
            return go;
        }

        [Test]
        public void AStationaryActor_IsNotReAnchoredOnFramesWhereItHasNotMoved()
        {
            Assert.IsNotNull(LateUpdateMethod, "GroundAnchorVfx.LateUpdate went missing");

            GameObject directorGo = null, actorGo = null;
            try
            {
                directorGo = new GameObject("MV935 Director");
                var director = directorGo.AddComponent<GroundAnchorVfx>();

                actorGo = SpawnActor("MV935 Dormant Stand-in", new Vector3(5f, 1f, 5f));
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)

                LateUpdateMethod.Invoke(director, null);
                int afterFirst = director.AnchorRecomputeCount;
                Assert.AreEqual(1, afterFirst, "the first frame an actor is seen must place it once");

                LateUpdateMethod.Invoke(director, null);
                LateUpdateMethod.Invoke(director, null);
                int afterTwoMoreUnmoved = director.AnchorRecomputeCount;

                Assert.AreEqual(1, afterTwoMoreUnmoved,
                    $"MV-935: an actor that has not moved must not be re-anchored on the next frame — " +
                    $"AnchorRecomputeCount rose from {afterFirst} to {afterTwoMoreUnmoved} across 2 more " +
                    "unmoved frames, which is the exact per-frame repaint cost (Ground() + two " +
                    "GroundRing.Show() calls per stationary actor) that read 16.9ms live against a " +
                    "~100-actor World 2 garrison");

                // Still anchored — a resolved value, not just presence: the ring is where it should be.
                GroundRing ring = director.GetComponentsInChildren<GroundRing>(includeInactive: true)
                    .Single(r => r.Visible && r.name == "AnchorRing");
                Assert.AreEqual(5f, ring.transform.position.x, 1e-3f);
                Assert.AreEqual(5f, ring.transform.position.z, 1e-3f);

                // A cache, not a freeze: an actor that DOES move must still be re-anchored.
                actorGo.transform.position = new Vector3(9f, 1f, 9f);
                Physics.SyncTransforms();
                LateUpdateMethod.Invoke(director, null);
                Assert.AreEqual(afterTwoMoreUnmoved + 1, director.AnchorRecomputeCount,
                    "an actor that moved must still be re-anchored — this is a stationary-actor cache, " +
                    "not a one-time placement");
            }
            finally
            {
                if (actorGo != null) Object.DestroyImmediate(actorGo);
                if (directorGo != null) Object.DestroyImmediate(directorGo);
            }
        }
    }
}
