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
        /// <summary>The post-MV-276 distance — a historical baseline, kept as its own named number
        /// so the "10% closer" claim stays checkable arithmetic even after MV-315 re-baked the
        /// actually-committed default (<see cref="CommittedDistance"/>) further.</summary>
        private const float PostMV276Distance = 25.1f / FixedAngleCameraRig.ZoomFactor;

        [Test]
        public void ThePullBackShowsHalfAgainAsMuchArena_BeforeMV276TightenedItFurther()
        {
            // The ticket asked for ~1.5x the visible AREA. Area goes as distance squared, so the
            // move is √1.5 ≈ 1.22x, not 1.5x — pulling back 1.5x would have shown 2.25x the ground
            // and left Max an ant in a wide shot. MV-276 then dialled 10% closer/tighter from that
            // 25.1 m YT-82 pull-back, so the post-MV-276 distance is this / ZoomFactor.
            float yt82Distance = CameraFraming.DistanceForAreaScale(
                CameraFraming.PreviousDistance, CameraFraming.TargetAreaScale);

            Assert.AreEqual(25.1f, yt82Distance, 0.1f,
                "the YT-82 area-scale target no longer lands on the historical 25.1m baseline");
            Assert.AreEqual(yt82Distance / FixedAngleCameraRig.ZoomFactor, PostMV276Distance, 0.1f,
                "the post-MV-276 distance no longer sits 10% closer than the YT-82 baseline");
        }

        [Test]
        public void TheMV276ZoomBumpIsExactlyOneTenthCloser()
        {
            Assert.AreEqual(1.1f, 25.1f / PostMV276Distance, 0.01f,
                "MV-276: the camera should render at 1.1x the pre-MV-276 zoom");
        }

        [Test]
        public void ItIsAPullBack_NotAPushIn()
        {
            Assert.Greater(PostMV276Distance, CameraFraming.PreviousDistance,
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
            var after = FixedAngleCameraRig.ComputeOffset(CommittedDistance(), 72f);

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
            float d = CommittedDistance();
            Assert.Less(FixedAngleCameraRig.MinDistance, d, "no room to zoom back in");
            Assert.Greater(FixedAngleCameraRig.MaxDistance, d, "no room to pull further out");
        }

        // --- The live knob (dev-mode [ / ]) -------------------------------------------------------

        private static FixedAngleCameraRig NewRig(out GameObject go)
        {
            go = new GameObject("Rig Test");
            return go.AddComponent<FixedAngleCameraRig>();
        }

        /// <summary>The actually-committed desktop default, read live off a fresh rig rather than a
        /// literal kept in this file — the literal is exactly what drifted out of step with the code
        /// when MV-315 re-baked the desktop distance.</summary>
        private static float CommittedDistance()
        {
            var rig = NewRig(out var go);
            try { return rig.Distance; }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheKnobStartsWhereTheCommittedFramingIs()
        {
            float committed = CommittedDistance();
            var rig = NewRig(out var go);
            try { Assert.AreEqual(committed, rig.Distance, 1e-3); }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NudgingMovesTheCameraInAndOut()
        {
            float start = CommittedDistance();
            var rig = NewRig(out var go);
            try
            {
                rig.Nudge(3f);
                Assert.AreEqual(start + 3f, rig.Distance, 1e-3);
                rig.Nudge(-5f);
                Assert.AreEqual(start - 2f, rig.Distance, 1e-3);
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

        // --- The dev-only pitch knob (MV-450) -----------------------------------------------------

        [Test]
        public void ThePitchKnobStartsAtTheShipped72DegreeDefault()
        {
            var rig = NewRig(out var go);
            try { Assert.AreEqual(72f, rig.Pitch, 1e-3); }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NudgingPitchMovesItByTheGivenDelta()
        {
            var rig = NewRig(out var go);
            try
            {
                rig.NudgePitch(-7f);
                Assert.AreEqual(65f, rig.Pitch, 1e-3);
                rig.NudgePitch(3f);
                Assert.AreEqual(68f, rig.Pitch, 1e-3);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ThePitchKnobCannotBeSweptPastItsBounds()
        {
            var rig = NewRig(out var go);
            try
            {
                rig.NudgePitch(-999f);
                Assert.AreEqual(FixedAngleCameraRig.MinPitch, rig.Pitch, 1e-3,
                    "held ; long enough and the camera should stop at the sanity floor, not go past it");
                rig.NudgePitch(999f);
                Assert.AreEqual(FixedAngleCameraRig.MaxPitch, rig.Pitch, 1e-3,
                    "held ' long enough and the camera should stop at the sanity ceiling");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SetPitchClampsDirectlyToo()
        {
            // Same contract as SetDistance vs Nudge: the slider (SetPitch) and the held keys
            // (NudgePitch) have to agree on the clamp, or the two ways of dialling this in disagree.
            var rig = NewRig(out var go);
            try
            {
                rig.SetPitch(10f);
                Assert.AreEqual(FixedAngleCameraRig.MinPitch, rig.Pitch, 1e-3);
                rig.SetPitch(200f);
                Assert.AreEqual(FixedAngleCameraRig.MaxPitch, rig.Pitch, 1e-3);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheNudgeRangeBracketsTheShippedPitch_SoThereIsRoomToTuneBothWays()
        {
            Assert.Less(FixedAngleCameraRig.MinPitch, 72f, "no room to tilt toward side-on");
            Assert.Greater(FixedAngleCameraRig.MaxPitch, 72f, "no room to go more overhead");
        }

        /// <summary>
        /// AC2: changing pitch must not silently zoom the shot too — holding the visible ground area
        /// constant is the whole point of MV-450's distance recompute, so Lee judges pitch and zoom one
        /// at a time instead of two variables moving together. Pure maths, no live camera needed — the
        /// same FOV/aspect <see cref="TeleportZoomController"/> already trusts <c>Camera.main</c> for.
        /// Fails without the MV-450 area-preserving recompute (a same-distance-different-pitch shot
        /// visibly shows less ground at 55° than at 72°).
        /// </summary>
        [Test]
        public void PitchChangeHoldsTheVisibleGroundAreaWithin2Percent()
        {
            const float fov = 40f;              // FixedAngleCameraRig's shipped Cinemachine lens (GroundAnchorPlayTests)
            const float aspect = 16f / 9f;

            float baselineDistance = CommittedDistance();
            float baselineRadius = TeleportZoomFraming.SafeVisibleRadius(baselineDistance, 72f, fov, aspect);

            foreach (float pitch in new[] { 55f, 60f, 65f, 72f })
            {
                float distance = FixedAngleCameraRig.DistanceHoldingVisibleArea(
                    baselineDistance, 72f, pitch, fov, aspect);
                float achievedRadius = TeleportZoomFraming.SafeVisibleRadius(distance, pitch, fov, aspect);

                float pctError = Mathf.Abs(achievedRadius - baselineRadius) / baselineRadius;
                Assert.Less(pctError, 0.02f,
                    $"pitch {pitch}° drifted the visible ground area {pctError:P1} from the 72° baseline");
            }
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
