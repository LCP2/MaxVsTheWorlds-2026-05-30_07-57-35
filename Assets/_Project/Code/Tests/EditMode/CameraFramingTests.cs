using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The camera pull-back (YT-82). The framing itself is a feel call and Lee sets the final number
    /// by eye, so what's pinned here is everything that ISN'T taste: that the pull-back delivers the
    /// arena it claims to, and — the one that matters — that it does not touch the angle.
    /// </summary>
    public sealed class CameraFramingTests
    {
        /// <summary>The committed starting distance. Kept in step with the scene by
        /// <see cref="TheSceneAndTheCodeAgreeOnTheZoom"/>.</summary>
        private const float Distance = 25.1f;

        [Test]
        public void ThePullBackShowsHalfAgainAsMuchArena()
        {
            // The ticket asked for ~1.5x the visible AREA. Area goes as distance squared, so the
            // move is √1.5 ≈ 1.22x, not 1.5x — pulling back 1.5x would have shown 2.25x the ground
            // and left Max an ant in a wide shot.
            float wanted = CameraFraming.DistanceForAreaScale(
                CameraFraming.PreviousDistance, CameraFraming.TargetAreaScale);

            Assert.AreEqual(wanted, Distance, 0.1f,
                "the committed distance no longer delivers the 1.5x arena it claims");
            Assert.AreEqual(CameraFraming.TargetAreaScale,
                CameraFraming.AreaScaleForDistance(CameraFraming.PreviousDistance, Distance), 0.02f);
        }

        [Test]
        public void ItIsAPullBack_NotAPushIn()
        {
            Assert.Greater(Distance, CameraFraming.PreviousDistance,
                "YT-82 is a pull-back; this is closer than the build Lee complained about");
        }

        [Test]
        public void AreaGoesAsTheSquareOfDistance_NotLinearly()
        {
            // The trap this whole file exists to avoid. Double the distance and you see FOUR times
            // the ground, not twice — get this wrong and "1.5x more arena" silently ships as 2.25x.
            Assert.AreEqual(4f, CameraFraming.AreaScaleForDistance(10f, 20f), 1e-3);
            Assert.AreEqual(2f, CameraFraming.DistanceForAreaScale(1f, 4f), 1e-3);
        }

        // --- The angle is load-bearing and must not move -----------------------------------------

        [Test]
        public void PullingBackDoesNotTiltTheCamera()
        {
            // The ticket's hard constraint: ZOOM ONLY, the ~72° pitch stays exactly as it is. The
            // height:back ratio IS the pitch, so if it survives the move, the angle did.
            float tan = Mathf.Tan(72f * Mathf.Deg2Rad);
            var before = FixedAngleCameraRig.ComputeOffset(CameraFraming.PreviousDistance, 72f);
            var after = FixedAngleCameraRig.ComputeOffset(Distance, 72f);

            Assert.AreEqual(tan, before.y / -before.z, 1e-3);
            Assert.AreEqual(tan, after.y / -after.z, 1e-3, "the pull-back tilted the camera");
            Assert.AreEqual(before.y / -before.z, after.y / -after.z, 1e-4,
                "the angle changed between the old framing and the new one");
        }

        [Test]
        public void NoDistanceInTheNudgeRangeCanTiltTheCamera()
        {
            // Lee will sweep this knob live. Every stop along it has to keep the angle.
            float tan = Mathf.Tan(72f * Mathf.Deg2Rad);
            for (float d = FixedAngleCameraRig.MinDistance; d <= FixedAngleCameraRig.MaxDistance; d += 1f)
            {
                var o = FixedAngleCameraRig.ComputeOffset(d, 72f);
                Assert.AreEqual(tan, o.y / -o.z, 1e-3, $"pitch drifted at {d} m");
            }
        }

        [Test]
        public void TheNudgeRangeBracketsTheCommittedFraming_SoThereIsRoomToTuneBothWays()
        {
            Assert.Less(FixedAngleCameraRig.MinDistance, Distance, "no room to zoom back in");
            Assert.Greater(FixedAngleCameraRig.MaxDistance, Distance, "no room to pull further out");
        }

        // --- The live knob (dev-mode [ / ]) -------------------------------------------------------

        private static FixedAngleCameraRig NewRig(out GameObject go)
        {
            go = new GameObject("Rig Test");
            return go.AddComponent<FixedAngleCameraRig>();
        }

        [Test]
        public void TheKnobStartsWhereTheCommittedFramingIs()
        {
            var rig = NewRig(out var go);
            try { Assert.AreEqual(Distance, rig.Distance, 1e-3); }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NudgingMovesTheCameraInAndOut()
        {
            var rig = NewRig(out var go);
            try
            {
                rig.Nudge(3f);
                Assert.AreEqual(Distance + 3f, rig.Distance, 1e-3);
                rig.Nudge(-5f);
                Assert.AreEqual(Distance - 2f, rig.Distance, 1e-3);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheKnobCannotBeSweptPastItsBounds()
        {
            var rig = NewRig(out var go);
            try
            {
                rig.Nudge(-999f);
                Assert.AreEqual(FixedAngleCameraRig.MinDistance, rig.Distance, 1e-3,
                    "held [ long enough and the camera ends up inside Max's head");
                rig.Nudge(999f);
                Assert.AreEqual(FixedAngleCameraRig.MaxDistance, rig.Distance, 1e-3,
                    "held ] long enough and Max is a speck");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheKnobCannotTiltTheCamera_NoMatterHowFarItIsSwept()
        {
            // The one thing the zoom knob must never be able to do. The pitch is load-bearing for
            // the AI-art pipeline, and a "zoom" control that quietly tilts is how it would go.
            var rig = NewRig(out var go);
            try
            {
                foreach (float delta in new[] { -999f, 4f, 12f, 999f, -7f })
                {
                    rig.Nudge(delta);
                    Assert.AreEqual(72f, rig.Pitch, 1e-3, "the zoom knob changed the camera ANGLE");
                    Assert.AreEqual(72f, go.transform.rotation.eulerAngles.x, 1e-2,
                        "the rig's actual rotation drifted off 72°");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // --- The scene must not silently outrank the code ----------------------------------------

        /// <summary>
        /// cameraDistance has to stay a [SerializeField] — the ticket wants an inspector knob — so
        /// it cannot be moved into a static the way the blaster's energy was (YT-80). That leaves it
        /// exposed to the same trap: Unity bakes a copy into Backyard_Slice.unity, and from then on
        /// editing the C# default changes nothing at all, silently. The blaster shipped for weeks
        /// draining at 25/s while the source said 15/s for exactly this reason.
        ///
        /// So if the value must live in two places, the two places have to agree, and something has
        /// to fail when they don't. This is that something.
        /// </summary>
        [Test]
        public void TheSceneAndTheCodeAgreeOnTheZoom()
        {
            string scenePath = Path.Combine(
                Application.dataPath, "_Project", "Scenes", "Backyard_Slice.unity");
            Assert.IsTrue(File.Exists(scenePath), $"the shipping scene has moved: {scenePath}");

            string yaml = File.ReadAllText(scenePath);
            var match = Regex.Match(yaml, @"FixedAngleCameraRig\s*\n\s*pitchDegrees:\s*([\d.]+)\s*\n\s*cameraDistance:\s*([\d.]+)");
            Assert.IsTrue(match.Success,
                "couldn't find the camera rig's serialized values in the scene — if the fields were " +
                "renamed or reordered, update this guard rather than deleting it");

            float scenePitch = float.Parse(match.Groups[1].Value);
            float sceneDistance = float.Parse(match.Groups[2].Value);

            // Read what the CODE authors straight off a fresh rig, rather than trusting a constant
            // in this file to have been kept up to date — a guard that can itself drift out of step
            // with the thing it guards is not a guard.
            var rig = NewRig(out var go);
            float authoredDistance = rig.Distance;
            float authoredPitch = rig.Pitch;
            Object.DestroyImmediate(go);

            Assert.AreEqual(72f, authoredPitch, 1e-3,
                "the code's camera pitch is not 72° — the angle is load-bearing (YT-33/YT-82)");
            Assert.AreEqual(72f, scenePitch, 1e-3,
                "the scene's camera pitch is not 72° — the angle is load-bearing (YT-33/YT-82)");
            Assert.AreEqual(authoredDistance, sceneDistance, 1e-3,
                $"the scene ships cameraDistance={sceneDistance} but the code authors " +
                $"{authoredDistance}. The SCENE wins at runtime, so the committed default is a " +
                "decoration and whatever you just changed in C# will do nothing. Change both.");
        }
    }
}
