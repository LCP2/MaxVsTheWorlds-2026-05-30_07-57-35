using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-373: Water Balloon auto-fire has to choose its own landing point (a placed weapon's
    /// auto-fire is otherwise incoherent) — <see cref="WaterBalloonAutoAim.TryFindBestLanding"/> is the
    /// pure formula behind that choice, tested here against known layouts with no live scene.
    ///
    /// MV-992 replaced the old <c>TryFindBestDirection</c> (which only ever scored a landing point at
    /// the full throw distance, a thin ring) with this method — every candidate lands at its OWN
    /// position, so a robot standing close to Max is a real candidate too, not just one on the LOB's
    /// outer edge. The tests below that predate MV-992 are updated for the new signature/semantics
    /// rather than kept as new tests; MV-992 adds exactly one net-new test,
    /// <see cref="FindsBestLandingWithinTheRadius_MV992"/>.
    /// </summary>
    public sealed class WaterBalloonAutoAimTests
    {
        [Test]
        public void PicksTheLandingCoveringTheMostRobots_MV373()
        {
            // Three robots on a line 1m apart (9m, 10m, 11m out) plus one lone robot far off on an
            // unrelated bearing. Splash 1.05m: landing on the MIDDLE robot (10m) catches all three
            // (its neighbours are exactly 1m away); landing on either end robot only catches two.
            var targets = new List<Vector3>
            {
                new Vector3(9f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
                new Vector3(11f, 0f, 0f),
                new Vector3(0f, 0f, 20f), // lone robot, unrelated bearing
            };

            bool found = WaterBalloonAutoAim.TryFindBestLanding(
                Vector3.zero, maxDistance: 25f, splashRadius: 1.05f, targets,
                out Vector3 direction, out float distance);

            Assert.IsTrue(found, "there are robots within reach — auto-fire must find a target");
            Assert.That(Vector3.Distance(direction, Vector3.right), Is.LessThan(0.01f),
                "the middle robot's own landing catches all three in-line robots — either end robot, " +
                "or the lone robot, would catch fewer");
            Assert.That(distance, Is.EqualTo(10f).Within(0.01f),
                "the landing must be at the winning robot's own position");
        }

        [Test]
        public void ReturnsFalseWhenNoRobotsAreActive_MV373()
        {
            bool found = WaterBalloonAutoAim.TryFindBestLanding(
                Vector3.zero, maxDistance: 10f, splashRadius: 2f, new List<Vector3>(), out _, out _);

            Assert.IsFalse(found, "no robots at all — auto-fire must not fire or spend a cell");
        }

        [Test]
        public void ReturnsFalseWhenNoTargetIsWithinMaxDistance_MV373()
        {
            // Only robot in the scene sits well outside the LOB radius entirely.
            var targets = new List<Vector3> { new Vector3(20f, 0f, 0f) };

            bool found = WaterBalloonAutoAim.TryFindBestLanding(
                Vector3.zero, maxDistance: 10f, splashRadius: 2f, targets, out _, out _);

            Assert.IsFalse(found, "the only robot is outside the LOB radius entirely");
        }

        [Test]
        public void FindsBestLandingWithinTheRadius_MV992()
        {
            // MV-992: LOB is a RADIUS — a robot well inside it (3m, with an 8m maxDistance) must be
            // targetable, landing at its own position, not ignored the way the old ring-only scan did
            // (see the fail-first proof quoted in this ticket's fix comment). A robot outside the
            // radius (9m) must not be. Two non-overlapping robots (2m and 6m, splash 1.2m so they can't
            // both be caught by one landing) must resolve to the NEAREST one.
            var closeRobot = new List<Vector3> { new Vector3(3f, 0f, 0f) };
            bool foundClose = WaterBalloonAutoAim.TryFindBestLanding(
                Vector3.zero, maxDistance: 8f, splashRadius: 1.2f, closeRobot,
                out Vector3 closeDirection, out float closeDistance);
            Assert.IsTrue(foundClose, "a robot well inside the 8m LOB radius must be targetable");
            Assert.That(closeDistance, Is.EqualTo(3f).Within(0.01f),
                "the landing must be at the robot's own position, not the full LOB distance");
            Assert.That(Vector3.Distance(closeDirection, Vector3.right), Is.LessThan(0.01f));

            var farRobot = new List<Vector3> { new Vector3(9f, 0f, 0f) };
            bool foundFar = WaterBalloonAutoAim.TryFindBestLanding(
                Vector3.zero, maxDistance: 8f, splashRadius: 1.2f, farRobot, out _, out _);
            Assert.IsFalse(foundFar, "a robot outside the 8m LOB radius must not be targeted");

            var nonOverlapping = new List<Vector3> { new Vector3(6f, 0f, 0f), new Vector3(2f, 0f, 0f) };
            bool foundNearest = WaterBalloonAutoAim.TryFindBestLanding(
                Vector3.zero, maxDistance: 8f, splashRadius: 1.2f, nonOverlapping,
                out Vector3 nearestDirection, out float nearestDistance);
            Assert.IsTrue(foundNearest);
            Assert.That(nearestDistance, Is.EqualTo(2f).Within(0.01f),
                "non-overlapping candidates each catch only themselves (a tie) — ties go to the nearest");
        }
    }
}
