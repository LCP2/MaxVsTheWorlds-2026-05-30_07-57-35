using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Launcher missile's obstruction check (MV-364), plus MV-349's colour and pure fuel-exhausted
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

        // ---------------------------------------------------------------- MV-405: tip colour

        [Test]
        public void TheMissileTipColour_HasNoChannelPastTheSunlitCeiling()
        {
            Color c = HomingMissile.TipColorForTests;
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));

            Assert.LessOrEqual(peak, SunlitAlbedo.Ceiling,
                $"the missile's nose-tip colour peaks at {peak:0.00}, past the " +
                $"{SunlitAlbedo.Ceiling:0.00} sunlit ceiling — it will clip under the yard's 1.8x key " +
                "and wash out instead of reading as a clean red tip.");
        }

        /// <summary>AC3: "a clearly visible red tip". Red, not just warm — R must clearly dominate
        /// both other channels, or this reads as the same orange every other wind-up tell in the game
        /// already uses instead of a distinct ordnance marking.</summary>
        [Test]
        public void TheMissileTipColour_ReadsAsRedNotOrange()
        {
            Color c = HomingMissile.TipColorForTests;

            Assert.Greater(c.r, c.g + 0.2f, "the tip's red channel doesn't clearly dominate green — it will read as orange/brown, not red.");
            Assert.Greater(c.r, c.b + 0.2f, "the tip's red channel doesn't clearly dominate blue — it will read as orange/brown, not red.");
        }

        // ---------------------------------------------------------------- MV-405: body == Launcher's body

        /// <summary>AC2: "the missile body is the same blue as the Launcher robot's own body colour".
        /// Reading both sides through the same live accessors (rather than pinning a literal) is what
        /// actually proves they can never drift apart — see <see cref="HomingMissile.ShaftColorForTests"/>'s
        /// doc comment.</summary>
        [Test]
        public void TheMissileBody_MatchesTheLaunchersOwnBodyColourExactly()
        {
            Color missileBody = HomingMissile.ShaftColorForTests;
            Color launcherBody = CharacterSkin.BaseColorFor(CharacterSkin.RoleFor(EnemyKind.Launcher));

            Assert.AreEqual(launcherBody, missileBody,
                "the missile body no longer matches the Launcher's own body colour exactly.");
        }

        /// <summary>Same separation method as <c>ActorReadabilityTests.NoEnemyIsTheColourOfTheScenery</c>
        /// uses for every other archetype's body colour — hue distance against the yard's grass. The
        /// missile body is now read straight off the Launcher's own archetype colour (MV-405), so it is
        /// held to the same bar the rest of the cast is, not a bespoke "must also be darker" rule that
        /// only ever applied to the old, one-off rust-copper paint job this missile used to wear.</summary>
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
            }
        }

        // ---------------------------------------------------------------- MV-432: doubled thickness

        /// <summary>AC1/AC2: the shaft and warhead band are doubled in cross-section, and each fin's
        /// inner face sits exactly on the shaft surface rather than floating in a gap (the same
        /// detached-geometry class as the MV-430 gear teeth).</summary>
        [Test]
        public void TheMissileGeometry_MatchesTheDoubledThicknessSpec()
        {
            HomingMissile missile = HomingMissile.Fire(Vector3.zero, null, speed: 1f, damage: 1f,
                splashRadius: 1f);
            try
            {
                Transform shaft = missile.transform.Find("Shaft");
                Transform band = missile.transform.Find("WarheadBand");

                Assert.AreEqual(new Vector3(0.22f, 0.30f, 0.22f), shaft.localScale,
                    "the shaft did not double to (0.22, 0.30, 0.22) — it will still read as a needle at the 72° camera.");
                Assert.AreEqual(new Vector3(0.26f, 0.05f, 0.26f), band.localScale,
                    "the warhead band did not double to (0.26, 0.05, 0.26) alongside the shaft.");

                foreach (Transform child in missile.transform)
                {
                    if (child.name != "Fin") continue;

                    float expectedOffset = 0.5f * (shaft.localScale.x + child.localScale.x);
                    Assert.AreEqual(expectedOffset, Mathf.Abs(child.localPosition.x), 1e-4f,
                        "a fin's inner face does not sit on the shaft surface — it floats in a gap instead of being seated.");
                }
            }
            finally
            {
                Object.DestroyImmediate(missile.gameObject);
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
