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
    /// every time (MV-806's own gap). Every bolt of a given kind must share one cached
    /// <see cref="Mesh"/> instance, built once and reused.
    ///
    /// MV-815 update: Max's own bolt became a bowed crescent and the Sentinel's own straight bolt got
    /// its own separate cached mesh (<see cref="SentinelBolt.GetBoltMesh"/>) rather than sharing Max's
    /// -- so the two kinds no longer share ONE mesh between them; each kind now shares its own. This
    /// test's own "no per-shot rebuild" story is otherwise unchanged and still holds for both.
    ///
    /// Fails on base commit 9751529: firing a second LPPE pulse builds a distinct Mesh object from
    /// the first (no cache), and the Sentinel's own bolt is a different mesh entirely (a capsule
    /// primitive, not the lathe).
    /// </summary>
    public sealed class MV810BoltMeshCacheTests
    {
        [Test]
        public void TwentyPulsesShareOneCachedMesh_AndSoDoTwoSentinelBolts_ButTheTwoKindsDiffer()
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
                    "SeekerPulse's own crescent builder runs fresh per shot instead of reusing one " +
                    "cached instance");
            }

            var distinctSentinelMeshes = new HashSet<Mesh>();
            Mesh firstSentinelMesh = null;
            for (int i = 0; i < 2; i++)
            {
                SentinelBolt sentinel = SentinelBolt.Fire(Vector3.zero, Vector3.forward * 5f, speed: 20f);
                Transform sentinelBoltTransform = sentinel.transform.Find("Bolt");
                Assert.IsNotNull(sentinelBoltTransform,
                    "test precondition: SentinelBolt must build a child named 'Bolt'");
                Mesh sentinelMesh = sentinelBoltTransform.GetComponent<MeshFilter>().sharedMesh;
                if (firstSentinelMesh == null) firstSentinelMesh = sentinelMesh;
                distinctSentinelMeshes.Add(sentinelMesh);
                sentinel.Tick(1f);

                Assert.AreEqual(1, distinctSentinelMeshes.Count,
                    $"sentinel bolt #{i + 1} raised the count of distinct sharedMesh instances -- " +
                    "SentinelBolt's own cache rebuilds a fresh mesh per shot instead of reusing one");
            }

            Assert.AreNotSame(firstMesh, firstSentinelMesh,
                "MV-815: the Sentinel's own bolt mesh must be its own cached instance, not the LPPE " +
                "bolts' shared crescent -- MV-806's whole point is that the two can't be confused");

            Assert.Greater(firstMesh.vertexCount, 0,
                "the cached LPPE mesh has no vertices -- the cache must not be satisfiable by an empty stand-in");
            Assert.Greater(firstSentinelMesh.vertexCount, 0,
                "the cached Sentinel mesh has no vertices -- the cache must not be satisfiable by an empty stand-in");

            // MV-815: Max's own bolt's chord now spans local X (across the travel axis), not Z -- see
            // SeekerPulse.BuildCrescentBoltMesh's own doc comment. The Sentinel's own bolt is unchanged
            // (still a lathe of revolution along local Y).
            float expectedLength = CombatVfxTuning.LppeBolt().Length;
            Assert.That(firstMesh.bounds.size.x, Is.EqualTo(expectedLength).Within(0.03f),
                $"cached LPPE mesh bounds along the chord ({firstMesh.bounds.size.x:0.000}m) don't " +
                $"match BoltTuning.Length ({expectedLength:0.000}m)");
            Assert.That(firstSentinelMesh.bounds.size.y, Is.EqualTo(expectedLength).Within(0.02f),
                $"cached Sentinel mesh bounds along the travel axis ({firstSentinelMesh.bounds.size.y:0.000}m) " +
                $"don't match BoltTuning.Length ({expectedLength:0.000}m)");
        }
    }
}
