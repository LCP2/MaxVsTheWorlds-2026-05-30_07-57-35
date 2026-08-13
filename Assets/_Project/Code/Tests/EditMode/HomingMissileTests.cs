using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Bomber missile's obstruction check (MV-364), plus MV-349's colour and pure fuel-exhausted
    /// state transition.
    ///
    /// MV-364: before <see cref="HomingMissile.BlockedByGeometry"/> existed, the missile had no
    /// obstruction logic at all — it homed straight through solid geometry on its way to the target,
    /// which is the one gap the ticket calls out by name ("a fence is cover for both sides"). The
    /// missile is a free-flying MonoBehaviour with its own colliders stripped (manual proximity check,
    /// not physics), so the obstruction query is exercised directly here rather than by driving a live
    /// instance's per-frame Update.
    ///
    /// MV-349: the sputter/bounce/boom sequence's own timing/physics runs on <c>Time.deltaTime</c>
    /// inside <c>Update</c> and isn't covered here — per the 11 Aug ruling, PlayMode is CI's problem,
    /// not the worker's, and this project doesn't author PlayMode tests.
    /// </summary>
    public sealed class HomingMissileTests
    {
        // ---------------------------------------------------------------- MV-364: obstruction

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

        // ---------------------------------------------------------------- MV-349: colour + fuel

        [Test]
        public void TheMissileColour_HasNoChannelPastTheSunlitCeiling()
        {
            Color c = HomingMissile.WarnColorForTests;
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));

            Assert.LessOrEqual(peak, SunlitAlbedo.Ceiling,
                $"the missile's warhead/fin colour peaks at {peak:0.00}, past the " +
                $"{SunlitAlbedo.Ceiling:0.00} sunlit ceiling — it will clip under the yard's 1.8x key " +
                "and wash toward the drab brown/tan Lee reported instead of reading as a hot, hostile " +
                "projectile.");
        }

        /// <summary>Pins the boundary at the fuel budget itself, and that it is an ordinary, reachable
        /// number rather than 0/negative/absurd (AC3: "reachable in normal play").</summary>
        [TestCase(0f, false)]
        [TestCase(5.99f, false)]
        [TestCase(6f, true)]
        [TestCase(10f, true)]
        public void HasRunDry_TransitionsAtTheFuelBudget(float age, bool expectRunDry)
        {
            Assert.AreEqual(expectRunDry, HomingMissile.HasRunDry(age, fuelBudget: 6f),
                $"age {age}s against a 6s fuel budget should give HasRunDry = {expectRunDry}");
        }
    }
}
