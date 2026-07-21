using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Hose;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The "walk up to a tap and it swaps" selection (YT-130): <see cref="Tap.NearestWithin"/> picks
    /// the nearest tap in plug range, or none. Tested pure, without a scene.
    /// </summary>
    public sealed class TapNearestWithinTests
    {
        private static readonly List<Vector3> Taps = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 16f),
            new Vector3(0f, 0f, 32f),
        };

        [Test]
        public void StandingOnATapSelectsIt()
        {
            Assert.That(Tap.NearestWithin(new Vector3(0f, 1f, 16f), Taps, 2.5f), Is.EqualTo(1),
                "standing on the middle tap must select it");
        }

        [Test]
        public void OutOfRangeOfEveryTapSelectsNone()
        {
            // Halfway between two taps (8 m from each), plug range 2.5 — nothing is close enough.
            Assert.That(Tap.NearestWithin(new Vector3(0f, 1f, 8f), Taps, 2.5f), Is.EqualTo(-1),
                "between taps, out of plug range, the hose must stay on whatever it's on");
        }

        [Test]
        public void ItIsPlanar_HeightDoesNotCount()
        {
            Assert.That(Tap.NearestWithin(new Vector3(0f, 50f, 0f), Taps, 2.5f), Is.EqualTo(0),
                "the plug check is on the ground plane; Max's Y must not push a tap out of range");
        }

        [Test]
        public void WithTwoInRangeItPicksTheNearer()
        {
            var close = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 3f) };
            Assert.That(Tap.NearestWithin(new Vector3(0f, 0f, 2f), close, 5f), Is.EqualTo(1),
                "with two taps in range, the nearer one wins");
        }
    }
}
