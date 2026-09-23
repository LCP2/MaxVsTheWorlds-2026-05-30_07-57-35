using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-917: the world-config schema gets an explicit cover behaviour kind
    /// (<see cref="CoverKind"/>) — <c>coverKind: "solid"</c> (default) or <c>"see_through"</c> — so
    /// World 2's pipe barriers and collapsed gratings can say "blocks movement only" as data, not as
    /// an inference from <see cref="CoverDressing"/>. Both pieces here are authored with dressing
    /// "none" deliberately, so passing depends only on the new <c>coverKind</c> field, never on the
    /// existing Hedge/Pipe dressing shortcut (<see cref="MapRuntime.BuildCover"/>, MV-400/MV-863) that
    /// this ticket leaves untouched.
    ///
    /// Fails on 18e1ef0 (the commit this ticket started from): <see cref="MapEntity"/> carries no
    /// <c>coverKind</c> field, so both pieces resolve <see cref="CoverKind.Solid"/> unconditionally —
    /// the see-through piece built here blocks the line-of-sight/shot raycast exactly like the solid
    /// one, and <c>Assert.IsTrue(LineOfSight.Clear(...))</c> for it fails.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): builds a solid and a see-through synthetic cover piece via the real
    /// <see cref="MapRuntime.Build"/>, then reads back <see cref="LineOfSight.Clear"/> (AC2/AC4 — the
    /// same helper WaterBlaster/HomingMissile/HomingSteering already cast projectiles and sight-lines
    /// against, reused here rather than inventing a second rule) and a live
    /// <see cref="CharacterController.Move"/> sweep (AC3).
    /// </summary>
    public sealed class MV917CoverKindTests
    {
        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset —
        // a distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry
        // (same reasoning as MV863PipeCoverTests.RigOrigin).
        private static readonly Vector3 RigOrigin = new Vector3(-58210f, 0f, 71940f);
        private const float PieceHeight = 1.5f;

        private static Vector3 SolidCenterXz => RigOrigin + new Vector3(-10f, 0f, 0f);
        private static Vector3 SeeThroughCenterXz => RigOrigin + new Vector3(10f, 0f, 0f);

        private static MapData Probe()
        {
            return new MapData
            {
                name = "Cover Kind Probe",
                zones = new[]
                {
                    new MapZone
                    {
                        id = "room", type = "open",
                        x = RigOrigin.x, z = RigOrigin.z, width = 40f, depth = 20f,
                    },
                },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "solid", kind = "cover",
                        x = SolidCenterXz.x, z = SolidCenterXz.z,
                        width = 2f, height = PieceHeight, depth = 1f, shape = "box",
                        coverKind = "solid",
                    },
                    new MapEntity
                    {
                        id = "seeThrough", kind = "cover",
                        x = SeeThroughCenterXz.x, z = SeeThroughCenterXz.z,
                        width = 2f, height = PieceHeight, depth = 1f, shape = "box",
                        coverKind = "see_through",
                    },
                },
            };
        }

        [Test]
        public void ASeeThroughCoverPiece_LetsAShotReachATargetBehindItAndStillBlocksMovement_WhileSolidStaysOpaque()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            MapData map = Probe();
            var root = new GameObject("MV-917 Cover Kind Probe Root");
            var target = new GameObject("MV-917 Target");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                CoverPiece solid = built.Cover.Find(p => p.Cover.Name == "solid");
                CoverPiece seeThrough = built.Cover.Find(p => p.Cover.Name == "seeThrough");
                Assert.IsNotNull(solid.Body, "the probe's solid cover piece was never built");
                Assert.IsNotNull(seeThrough.Body, "the probe's see-through cover piece was never built");

                // AC1 (indirectly): the solid piece authors no dressing at all, so it can only be
                // opaque here because of its explicit coverKind, not a Hedge/Pipe dressing shortcut.
                Assert.AreEqual(CoverKind.Solid, solid.Cover.Kind);
                Assert.AreEqual(CoverKind.SeeThrough, seeThrough.Cover.Kind);

                float eyeY = PieceHeight * 0.5f; // mid-height, the pieces' own collider centre

                // AC2/AC4: a shot/sight-line at the solid piece is stopped before it ever reaches
                // the target standing behind it — reusing LineOfSight.Clear, the same primitive
                // WaterBlaster/HomingMissile/HomingSteering already cast projectiles against.
                Vector3 solidFrom = SolidCenterXz + new Vector3(0f, eyeY, -3f);
                Vector3 solidBehind = SolidCenterXz + new Vector3(0f, eyeY, 3f);
                target.transform.position = solidBehind;
                Assert.IsFalse(LineOfSight.Clear(solidFrom, solidBehind, target.transform),
                    "a solid cover piece let a sight-line/shot pass straight through it to the target behind");

                // AC2/AC4: the same shot through the see-through piece reaches the target behind it.
                Vector3 stFrom = SeeThroughCenterXz + new Vector3(0f, eyeY, -3f);
                Vector3 stBehind = SeeThroughCenterXz + new Vector3(0f, eyeY, 3f);
                target.transform.position = stBehind;
                Assert.IsTrue(LineOfSight.Clear(stFrom, stBehind, target.transform),
                    "a see-through cover piece blocked a sight-line/shot that should have passed over it " +
                    "and reached the target behind");

                // AC3: the see-through piece still stops a CharacterController cold — a raw oversized
                // Move() straight at its near face, grounded at y=0 with cc.center offsetting up to the
                // piece's own mid-height, the same tunneling-proof idiom MV863PipeCoverTests uses.
                var character = new GameObject("MV-917 Character", typeof(CharacterController));
                CharacterController cc = character.GetComponent<CharacterController>();
                cc.center = Vector3.up * eyeY;
                cc.height = PieceHeight;
                cc.radius = 0.4f;
                character.transform.position = SeeThroughCenterXz + new Vector3(0f, 0f, -3f);
                try
                {
                    cc.Move(Vector3.forward * 6f);
                    Assert.Less(character.transform.position.z, SeeThroughCenterXz.z,
                        "a CharacterController moved straight at a see-through cover piece ended up on " +
                        "the far side of it — the piece did not block movement");
                }
                finally
                {
                    Object.DestroyImmediate(character);
                }
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(root);
            }
        }
    }
}
