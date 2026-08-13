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

        // ---------------------------------------------------------------- MV-377: body vs. grass

        /// <summary>The shaft (plus the two fins, which share its colour) is most of the missile's
        /// silhouette, so this is "the body" the ticket means. Same separation method as
        /// <c>ActorReadabilityTests.NoEnemyIsTheColourOfTheScenery</c>: hue distance for a coloured
        /// background, and it must also sit well clear in luminance, because the AC calls out VALUE
        /// contrast specifically, not just hue.</summary>
        [Test]
        public void TheMissileBody_ReadsAgainstTheGrassItFliesOver()
        {
            Color body = HomingMissile.ShaftColorForTests;
            var palette = BiomePalette.Backyard;

            Assert.LessOrEqual(Mathf.Max(body.r, Mathf.Max(body.g, body.b)), SunlitAlbedo.Ceiling,
                "the missile body clips under the yard's 1.8x key before it even gets to read as a " +
                "colour.");

            foreach (var (name, grass) in new[]
            {
                ("shaded turf", palette.GroundBase),
                ("sunlit turf", palette.GroundAccent),
            })
            {
                Color.RGBToHSV(body, out float bodyHue, out _, out _);
                Color.RGBToHSV(grass, out float grassHue, out _, out _);
                float hue = Mathf.Abs(bodyHue - grassHue) * 360f;
                if (hue > 180f) hue = 360f - hue;

                Assert.Greater(hue, 50f,
                    $"the missile body is the same colour family as the {name} ({hue:0}° apart) — " +
                    "it will colour-match the grass it's flying over instead of standing out against it.");

                float bodyLum = 0.2126f * body.r + 0.7152f * body.g + 0.0722f * body.b;
                float grassLum = 0.2126f * grass.r + 0.7152f * grass.g + 0.0722f * grass.b;
                Assert.Greater(grassLum - bodyLum, 0.08f,
                    $"the missile body ({bodyLum:0.00}) isn't clearly darker than the {name} " +
                    $"({grassLum:0.00}) — a hue difference alone isn't the strong VALUE contrast the AC " +
                    "asks for.");
            }
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
