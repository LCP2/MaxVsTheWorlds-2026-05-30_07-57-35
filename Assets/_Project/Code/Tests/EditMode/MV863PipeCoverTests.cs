using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-863, Lee's decision (2026-09-17): a pipe barrier (<c>dressing: "pipe"</c>) is see-through
    /// cover — it blocks Max and robots, but everyone sees and shoots over it. Fails on dd85276: a
    /// pipe-dressed cover piece was left on <see cref="CoverLayer"/> exactly like every other solid
    /// (only <see cref="CoverDressing.Hedge"/> was excluded in <c>MapRuntime.BuildCover</c>), so a
    /// sight-line/projectile raycast through it stopped dead, and the shipped World 2 pipe piece
    /// authored its collider 0.6 m taller than the art it resolves to (see the fix comment for the
    /// captured failure output).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): builds a synthetic 6 x 1 m pipe barrier via the real <see cref="MapRuntime.Build"/> and
    /// reads back a live raycast, a live <see cref="CharacterController.Move"/> sweep, and the built
    /// collider's own resolved height — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV863PipeCoverTests
    {
        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset —
        // a distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry
        // (same reasoning as MV590BossWallSteeringTests.RigOrigin / MV670's own probe origin).
        private static readonly Vector3 RigOrigin = new Vector3(83421f, 0f, 27650f);

        private static MapData PipeBarrierProbe()
        {
            return new MapData
            {
                name = "Pipe Barrier Probe",
                zones = new[]
                {
                    new MapZone
                    {
                        id = "room", type = "open",
                        x = RigOrigin.x, z = RigOrigin.z, width = 20f, depth = 20f,
                    },
                },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "pipe", kind = "cover", x = RigOrigin.x, z = RigOrigin.z,
                        width = 6f, height = 1f, depth = 1f, shape = "box", dressing = "pipe",
                    },
                },
            };
        }

        [Test]
        public void APipeBarrier_BlocksMovement_ButNotSightOrShots_AtItsAuthoredHeight()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            MapData map = PipeBarrierProbe();
            var root = new GameObject("MV-863 Pipe Barrier Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                CoverPiece pipe = built.Cover.Find(p => p.Cover.Name == "pipe");
                Assert.IsNotNull(pipe.Body, "the probe map's pipe barrier was never built");

                Collider pipeCollider = pipe.Body.GetComponent<Collider>();
                Assert.IsNotNull(pipeCollider, "a pipe barrier carries no collider — nothing would block a footstep");

                // AC1: "the resolved collider height is 1.0 m +/- 0.05".
                Assert.That(pipeCollider.bounds.size.y, Is.EqualTo(1f).Within(0.05f),
                    $"the pipe's resolved collider height is {pipeCollider.bounds.size.y:F2} m — the config " +
                    "authors height: 1.0 and the art already only ever reaches 1.0 m, so the collider must match");

                // AC1: "a raycast on the projectile/sight mask between them hits nothing" — LineOfSight,
                // WaterBlaster's spray and HomingMissile all cast against CoverLayer.Mask, so this one
                // raycast stands for all three.
                // Sampled at 0.5 m — the pipe's own collider mid-height (it spans y=[0, 1]), not the 1.0 m
                // eye-height SightlineTests uses for taller cover, which would graze this barrier's own
                // top face.
                Vector3 from = RigOrigin + new Vector3(0f, 0.5f, -3f);
                Vector3 to = RigOrigin + new Vector3(0f, 0.5f, 3f);
                bool blocked = Physics.Raycast(from, (to - from).normalized, out RaycastHit hit,
                    Vector3.Distance(from, to), CoverLayer.Mask, QueryTriggerInteraction.Ignore);
                Assert.IsFalse(blocked,
                    $"a pipe barrier still blocks a sight-line/shot straight through it" +
                    (blocked ? $" (hit '{hit.transform.name}')" : ""));

                // AC1: "a CharacterController moved into it is stopped" — a raw oversized Move() straight
                // at the barrier's near face, the same tunneling-proof idiom
                // CharacterControllerMotionTunnelingTests already uses against a wall.
                // Grounded at y=0 (not the 1m-up placement CharacterControllerMotionTunnelingTests uses
                // against its own 3m-tall wall) — the pipe box's own collider only spans y=[0, 1] (its
                // Y-center is half its own 1 m height, BackyardCover.ArenaCover.Center), so the capsule
                // must actually reach the ground to overlap it at all.
                var character = new GameObject("MV-863 Pipe Probe Character", typeof(CharacterController));
                CharacterController cc = character.GetComponent<CharacterController>();
                cc.center = Vector3.up * 1f;
                cc.height = 2f;
                cc.radius = 0.4f;
                character.transform.position = RigOrigin + new Vector3(0f, 0f, -3f);
                try
                {
                    cc.Move(Vector3.forward * 6f);
                    Assert.Less(character.transform.position.z, RigOrigin.z,
                        "a CharacterController moved straight at the pipe barrier ended up on the far side " +
                        "of it — the barrier did not block movement");
                }
                finally
                {
                    Object.DestroyImmediate(character);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
