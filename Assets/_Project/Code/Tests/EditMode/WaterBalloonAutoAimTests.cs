using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-373: Water Balloon auto-fire has to choose its own landing point (a placed weapon's
    /// auto-fire is otherwise incoherent) — <see cref="WaterBalloonAutoAim.TryFindBestDirection"/> is
    /// the pure formula behind that choice, tested here against known layouts with no live scene.
    /// </summary>
    public sealed class WaterBalloonAutoAimTests
    {
        [Test]
        public void PicksThePointCoveringTheMostRobots_MV373()
        {
            // A tight 3-robot cluster on the throw-range circle (radius 10) around a bearing of 0°,
            // plus one lone robot on a completely different bearing. The splash (radius 2) at the
            // cluster's centre bearing catches all three cluster members; any other candidate — the
            // cluster's own edges, or the lone robot — catches at most two.
            var targets = new List<Vector3>
            {
                new Vector3(10f, 0f, 0f),            // cluster centre bearing
                new Vector3(9.90268f, 0f, 1.39173f),  // cluster +8°
                new Vector3(9.90268f, 0f, -1.39173f), // cluster -8°
                new Vector3(0f, 0f, 10f),             // lone robot, unrelated bearing
            };

            bool found = WaterBalloonAutoAim.TryFindBestDirection(
                Vector3.zero, throwDistance: 10f, splashRadius: 2f, targets, out Vector3 direction);

            Assert.IsTrue(found, "there are robots within reach — auto-fire must find a target");
            Assert.That(Vector3.Distance(direction, Vector3.right), Is.LessThan(0.01f),
                "the cluster's own centre bearing catches all three robots — a direct hit on the lone " +
                "robot, or either edge of the cluster, would catch fewer");
        }

        [Test]
        public void PrefersACatchOfThreeOverADirectHitOnOne_MV373()
        {
            // Same layout, aimed a different way: confirm the winning direction actually outscores
            // aiming straight at the lone robot (a "direct hit on one" the design note calls out by name).
            var targets = new List<Vector3>
            {
                new Vector3(10f, 0f, 0f),
                new Vector3(9.90268f, 0f, 1.39173f),
                new Vector3(9.90268f, 0f, -1.39173f),
                new Vector3(0f, 0f, 10f),
            };

            WaterBalloonAutoAim.TryFindBestDirection(
                Vector3.zero, throwDistance: 10f, splashRadius: 2f, targets, out Vector3 direction);

            Assert.That(Vector3.Distance(direction, Vector3.forward), Is.GreaterThan(0.01f),
                "must not settle for the lone robot's own bearing when a 3-robot cluster is reachable");
        }

        [Test]
        public void ReturnsFalseWhenNoRobotsAreActive_MV373()
        {
            bool found = WaterBalloonAutoAim.TryFindBestDirection(
                Vector3.zero, throwDistance: 10f, splashRadius: 2f, new List<Vector3>(), out _);

            Assert.IsFalse(found, "no robots at all — auto-fire must not fire or spend a cell");
        }

        [Test]
        public void ReturnsFalseWhenNothingIsWithinReachOfAnyCandidate_MV373()
        {
            // A robot standing right next to the thrower is far short of a 10m-throw-distance landing
            // point in every direction, and no candidate's splash (radius 1) reaches back to it.
            var targets = new List<Vector3> { new Vector3(1f, 0f, 0f) };

            bool found = WaterBalloonAutoAim.TryFindBestDirection(
                Vector3.zero, throwDistance: 10f, splashRadius: 1f, targets, out _);

            Assert.IsFalse(found, "the only robot is nowhere near any reachable landing point");
        }
    }
}
