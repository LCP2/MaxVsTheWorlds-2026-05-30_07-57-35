using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-101 — the coils a dead robot throws out.
    ///
    /// The reason the springs fly themselves instead of riding rigidbodies (see the note on
    /// <see cref="SpringGuts"/>) is that the motion is then a pure function, and a pure function can
    /// be proven. So the two claims the ticket actually makes — "they bounce" and "they always clean
    /// up" — are tested here rather than eyeballed on a build.
    ///
    /// The director's own lifecycle is not tested in edit mode: Awake/OnEnable do not run, so a test
    /// of "does it unsubscribe" would pass without it ever having subscribed. That lives in
    /// SpringGutsPlayTests.
    /// </summary>
    public sealed class SpringGutsTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void Step_Falls_UnderGravity()
        {
            var pos = new Vector3(0f, 3f, 0f);
            var vel = Vector3.zero;
            float spin = 400f;

            SpringGuts.Step(ref pos, ref vel, ref spin, Dt);

            Assert.Less(vel.y, 0f, "a spring in the air is being pulled down");
            Assert.Less(pos.y, 3f, "and is lower than it was");
        }

        [Test]
        public void Step_Bounces_OffTheLawn_LosingEnergy()
        {
            // Fast enough downward that the bounce is worth having (well over the settle floor).
            var pos = new Vector3(0f, 0.02f, 0f);
            var vel = new Vector3(2f, -6f, 0f);
            float spin = 500f;

            SpringGuts.Step(ref pos, ref vel, ref spin, Dt);

            Assert.GreaterOrEqual(pos.y, 0f, "never left under the lawn");
            Assert.Greater(vel.y, 0f, "it came back up");
            Assert.Less(vel.y, 6f, "but with less than it arrived with — bounces decay");
            Assert.Less(vel.x, 2f, "and it scrubbed sideways speed on the ground");
            Assert.Less(spin, 500f, "and slowed its spin, so it doesn't whirl after it lands");
        }

        [Test]
        public void Step_Settles_RatherThanJitteringForever()
        {
            // A slow arrival: below the settle floor, so it should stop dead instead of micro-bouncing.
            var pos = new Vector3(0f, 0.001f, 0f);
            var vel = new Vector3(1.2f, -0.4f, 0.5f);
            float spin = 300f;

            SpringGuts.Step(ref pos, ref vel, ref spin, Dt);

            Assert.AreEqual(0f, pos.y, 1e-5f, "resting on the lawn");
            Assert.AreEqual(Vector3.zero, vel, "fully at rest — no residual bounce or slide");
            Assert.AreEqual(0f, spin, "and not still spinning");
        }

        [Test]
        public void Step_AlwaysComesToRest_AndStaysOnTheLawn()
        {
            // The real claim: whatever you launch, two seconds later it is lying still on the grass.
            // Ten seeds so this isn't one lucky trajectory.
            for (int seed = 0; seed < 10; seed++)
            {
                Random.InitState(seed);
                var pos = new Vector3(0f, 0.9f, 0f);
                var vel = Random.onUnitSphere * 6f;
                vel.y = Mathf.Abs(vel.y);
                float spin = 700f;

                // Three seconds — comfortably past the ~1.8 s the fastest launch takes to stop
                // bouncing, and past the longest life a spring is given.
                for (int i = 0; i < 180; i++) SpringGuts.Step(ref pos, ref vel, ref spin, Dt);

                Assert.GreaterOrEqual(pos.y, -1e-4f, $"seed {seed}: never sank through the lawn");
                Assert.AreEqual(Vector3.zero, vel, $"seed {seed}: came to rest inside its lifetime");
            }
        }

        [Test]
        public void ShrinkAt_HoldsFullSize_ThenLeavesAtExactlyZero()
        {
            const float life = 2f;

            Assert.AreEqual(1f, SpringGuts.ShrinkAt(0f, life), 1e-4f, "full size on arrival");
            Assert.AreEqual(1f, SpringGuts.ShrinkAt(1.5f, life), 1e-4f, "still full size mid-life");
            Assert.Less(SpringGuts.ShrinkAt(1.85f, life), 1f, "shrinking as it runs out");
            Assert.AreEqual(0f, SpringGuts.ShrinkAt(life, life), 1e-4f, "gone, exactly, at the end");
            Assert.AreEqual(0f, SpringGuts.ShrinkAt(life + 5f, life), 1e-4f, "and stays gone past it");
        }

        [Test]
        public void ShrinkAt_NeverGoesNegative_SoASpringCantInvert()
        {
            for (float age = 0f; age <= 4f; age += 0.05f)
            {
                float k = SpringGuts.ShrinkAt(age, 2f);
                Assert.GreaterOrEqual(k, 0f, $"age {age}");
                Assert.LessOrEqual(k, 1f, $"age {age}");
            }
        }

        // ---------------------------------------------------------------- the mesh

        [Test]
        public void SpringMesh_IsAClosedCoil_AndCheapEnoughToLitterTheLawnWith()
        {
            var mesh = SpringMesh.Build();
            try
            {
                int rings = SpringMesh.PathSegments + 1;
                Assert.AreEqual(rings * SpringMesh.Sides, mesh.vertexCount, "one ring per path point");

                // Tube walls plus a cap at each end. The caps are the bit worth asserting: without
                // them the coil has two holes punched through it from directly above, which is the
                // only angle this game has.
                int expectedTris = SpringMesh.PathSegments * SpringMesh.Sides * 2 + (SpringMesh.Sides - 2) * 2;
                Assert.AreEqual(expectedTris * 3, mesh.triangles.Length, "walls + both caps");

                Assert.Less(expectedTris, 400, "cheap: dozens of these are on screen at once on iOS");

                // Every index has to point at a real vertex — a stray index is a corrupt mesh, and a
                // corrupt mesh is a crash on device rather than a wrong picture.
                foreach (int idx in mesh.triangles)
                    Assert.That(idx, Is.InRange(0, mesh.vertexCount - 1), "index in range");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SpringMesh_IsCentredOnItsOwnMiddle_SoItSpinsInPlace()
        {
            var mesh = SpringMesh.Build();
            try
            {
                // Authored around the origin: a coil pivoting about an end swings like a thrown
                // stick instead of tumbling like a thrown spring.
                Assert.AreEqual(0f, mesh.bounds.center.y, 0.02f, "centred vertically");

                Assert.Greater(mesh.bounds.size.y, 0.5f, "tall enough to read as a coil, not a washer");
                Assert.Less(mesh.bounds.size.y, 1.5f, "and within the ~1 m unit space the scales assume");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SpringMesh_Shared_IsBuiltOnce_AndHandedOutEveryTime()
        {
            // One mesh across every spring is what keeps them in a single batch.
            var a = SpringMesh.Shared;
            var b = SpringMesh.Shared;

            Assert.IsNotNull(a);
            Assert.AreSame(a, b, "the shared coil is not rebuilt per ask");
        }
    }
}
