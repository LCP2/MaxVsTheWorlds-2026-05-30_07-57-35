using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-871 — <c>GroundAnchorVfx.LateUpdate</c> ran <c>Physics.OverlapSphereNonAlloc(Vector3.zero,
    /// 1000f, ...)</c> every frame: a 1 km sphere centred on the world ORIGIN, not on the player, so it
    /// touched every actor in World 2 regardless of what the fixed camera could show. World 2's standing
    /// robot population (194 by a11, 280 by a13, 412 by the boss — nothing despawns one behind the
    /// player) then silently overflowed the 256-entry buffer before a single wall or prop was counted.
    ///
    /// Fails on base commit 68d962b: with the query still centred on the origin at a 1000 m radius, an
    /// actor 300 m away is well inside range and gets anchored exactly like the actor 10 m away, so the
    /// "nothing at the far actor" assertions below fail. Actually run against that commit (not just
    /// reasoned about — <c>GroundAnchorVfx.cs</c> stashed back to it, test re-run, then restored):
    ///
    ///   Assert.AreEqual failed. Expected: 1 But was: 2
    ///   exactly one ring should be placed - the near actor inside the 40 m player scan radius; the far
    ///   actor at 300 m must get none
    ///
    /// Tier 2 (resolved values): reads <c>activeSelf</c> and the world position off the actual pooled
    /// <c>GroundRing</c> objects <c>LateUpdate</c> placed — the same <c>GetComponentsInChildren</c> path
    /// <c>GroundAnchorPlayTests</c> already uses — never a constant.
    /// </summary>
    public sealed class MV871PlayerScanRadiusTests
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
        public void OnlyTheActorInsideThePlayerScanRadius_GetsMarks_TheFarOneGetsNone()
        {
            Assert.IsNotNull(LateUpdateMethod, "GroundAnchorVfx.LateUpdate went missing");

            GameObject player = null, near = null, far = null, directorGo = null;
            try
            {
                player = new GameObject("MV871 Player", typeof(CharacterController), typeof(PlayerController));
                player.transform.position = Vector3.zero;

                near = SpawnActor("MV871 Near Actor", new Vector3(10f, 1f, 0f));
                far = SpawnActor("MV871 Far Actor", new Vector3(300f, 1f, 0f));
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)

                directorGo = new GameObject("MV871 Director");
                var director = directorGo.AddComponent<GroundAnchorVfx>();

                LateUpdateMethod.Invoke(director, null);

                var marks = director.GetComponentsInChildren<GroundRing>(includeInactive: true);
                var rings = marks.Where(r => r.gameObject.activeSelf && r.name == "AnchorRing").ToArray();
                var shadows = marks.Where(r => r.gameObject.activeSelf && r.name == "ContactShadow").ToArray();

                Assert.AreEqual(1, rings.Length,
                    "exactly one ring should be placed - the near actor inside the 40 m player scan " +
                    "radius; the far actor at 300 m must get none");
                Assert.AreEqual(1, shadows.Length,
                    "exactly one contact shadow should be placed - the far actor at 300 m must get none");

                Assert.AreEqual(10f, rings[0].transform.position.x, 1e-3,
                    "the ring is not sitting at the near actor's ground position");
                Assert.AreEqual(0f, rings[0].transform.position.z, 1e-3);
                Assert.AreEqual(10f, shadows[0].transform.position.x, 1e-3,
                    "the shadow is not sitting at the near actor's ground position");
                Assert.AreEqual(0f, shadows[0].transform.position.z, 1e-3);
            }
            finally
            {
                if (directorGo != null) Object.DestroyImmediate(directorGo);
                if (player != null) Object.DestroyImmediate(player);
                if (near != null) Object.DestroyImmediate(near);
                if (far != null) Object.DestroyImmediate(far);
            }
        }
    }
}
