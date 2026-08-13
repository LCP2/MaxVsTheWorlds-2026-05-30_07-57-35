using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Bomber missile's obstruction check (MV-364). Before this, <see cref="HomingMissile"/> had no
    /// obstruction logic at all — it homed straight through solid geometry on its way to the target,
    /// which is the one gap the ticket calls out by name ("a fence is cover for both sides"). The
    /// missile is a free-flying MonoBehaviour with its own colliders stripped (manual proximity check,
    /// not physics — see <see cref="HomingMissile.BlockedByGeometry"/>'s own doc), so the obstruction
    /// query is exercised directly here rather than by driving a live instance's per-frame Update.
    /// </summary>
    public sealed class HomingMissileTests
    {
        [Test]
        public void ASolidPieceOfCoverBetweenTwoPoints_BlocksTheFlightPath()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = new Vector3(0f, 0f, 5f);
                wall.transform.localScale = new Vector3(4f, 3f, 0.6f);
                CoverLayer.Assign(wall);
                Physics.SyncTransforms();

                bool blocked = HomingMissile.BlockedByGeometry(
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f), out RaycastHit hit);

                Assert.IsTrue(blocked,
                    "a missile flying straight at a fence on the Cover layer did not detect it — it " +
                    "would fly straight through instead of detonating at the wall");
                Assert.Greater(hit.point.z, 4.5f, "the hit point should sit at the near face of the wall");
                Assert.Less(hit.point.z, 5.01f, "the hit point should not read as past the wall");
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void NothingInTheWay_DoesNotBlock()
        {
            bool blocked = HomingMissile.BlockedByGeometry(
                Vector3.zero, new Vector3(0f, 0f, 10f), out _);

            Assert.IsFalse(blocked, "an open flight path with no cover in it read as blocked");
        }

        [Test]
        public void ZeroLengthStep_NeverBlocks()
        {
            // A missile that hasn't moved this frame (dt == 0, or the very first Update) must not
            // false-positive a raycast of zero length.
            bool blocked = HomingMissile.BlockedByGeometry(
                new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f), out _);

            Assert.IsFalse(blocked);
        }
    }
}
