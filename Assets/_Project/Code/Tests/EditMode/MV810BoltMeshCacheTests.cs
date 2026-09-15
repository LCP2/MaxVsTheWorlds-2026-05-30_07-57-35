using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-810 -- Lee's build reported 34fps against a target of 60 on the tip of `main` carrying
    /// MV-805, which replaced the LPPE bolt's capsule primitive with a generated lathe mesh built
    /// PER SHOT (<see cref="SeekerPulse.Fire"/> -&gt; <c>BuildVisual</c> -&gt; the old
    /// <c>BuildBoltMesh</c>), and <see cref="SentinelBolt"/> was still its own
    /// <c>GameObject.CreatePrimitive(PrimitiveType.Capsule)</c> per shot with its collider destroyed
    /// every time (MV-806's own gap). Every bolt of both kinds must now share one cached
    /// <see cref="Mesh"/> instance, built once and reused (<see cref="SeekerPulse.GetBoltMesh"/>).
    ///
    /// Fails on base commit 9751529: firing a second LPPE pulse builds a distinct Mesh object from
    /// the first (no cache), and the Sentinel's own bolt is a different mesh entirely (a capsule
    /// primitive, not the lathe).
    /// </summary>
    public sealed class MV810BoltMeshCacheTests
    {
        [Test]
        public void TwentyPulsesAndASentinelBoltAllShareOneCachedMesh()
        {
            var distinctMeshes = new HashSet<Mesh>();
            Mesh firstMesh = null;

            for (int i = 0; i < 20; i++)
            {
                SeekerPulse pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f,
                    turnRateDegPerSec: 360f, lifetime: 0.01f, damage: 9f, lockRange: 14f,
                    lockHalfAngleDeg: 35f);

                Transform boltTransform = pulse.transform.Find("Bolt");
                Assert.IsNotNull(boltTransform,
                    "test precondition: SeekerPulse must build a child named 'Bolt'");
                Mesh mesh = boltTransform.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh, "test precondition: the bolt must carry a mesh");
                if (firstMesh == null) firstMesh = mesh;
                distinctMeshes.Add(mesh);

                pulse.Tick(1f); // dt > lifetime -- forces Retire(), destroying bolt+trail+glow

                Assert.AreEqual(1, distinctMeshes.Count,
                    $"pulse #{i + 1}'s bolt raised the count of distinct sharedMesh instances -- " +
                    "SeekerPulse's BuildBoltMesh lathes a fresh ~384-vertex mesh per shot instead of " +
                    "reusing one cached instance");
            }

            SentinelBolt sentinel = SentinelBolt.Fire(Vector3.zero, Vector3.forward * 5f, speed: 20f);
            Transform sentinelBoltTransform = sentinel.transform.Find("Bolt");
            Assert.IsNotNull(sentinelBoltTransform,
                "test precondition: SentinelBolt must build a child named 'Bolt'");
            Mesh sentinelMesh = sentinelBoltTransform.GetComponent<MeshFilter>().sharedMesh;
            distinctMeshes.Add(sentinelMesh);
            sentinel.Tick(1f);

            Assert.AreEqual(1, distinctMeshes.Count,
                "the Sentinel's own bolt does not share the LPPE's cached mesh -- it is still its own " +
                "GameObject.CreatePrimitive(PrimitiveType.Capsule) built fresh per shot");
            Assert.AreSame(firstMesh, sentinelMesh,
                "the Sentinel's bolt mesh is not reference-equal to the LPPE bolts' shared mesh");

            Assert.Greater(firstMesh.vertexCount, 0,
                "the cached mesh has no vertices -- the cache must not be satisfiable by an empty stand-in");
            float expectedLength = CombatVfxTuning.LppeBolt().Length;
            Assert.That(firstMesh.bounds.size.y, Is.EqualTo(expectedLength).Within(0.02f),
                $"cached mesh bounds along the travel axis ({firstMesh.bounds.size.y:0.000}m) don't match " +
                $"BoltTuning.Length ({expectedLength:0.000}m)");
        }
    }
}
