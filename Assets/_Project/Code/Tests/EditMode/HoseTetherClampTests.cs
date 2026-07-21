using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Hose;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The hose leash maths (YT-129): the pure <see cref="HoseTether.Clamp"/> that decides where Max
    /// is allowed to stand relative to his tap, tested without a scene or a CharacterController.
    /// </summary>
    public sealed class HoseTetherClampTests
    {
        private static readonly Vector3 Tap = new Vector3(3f, 0f, -12f);

        [SetUp]
        [TearDown]
        public void ClearOverrides() => DevTuning.Reset();

        [Test]
        public void InsideTheTetherHeIsLeftAlone()
        {
            Vector3 max = Tap + new Vector3(5f, 0f, 0f);   // 5 m out, tether 20 m
            Assert.That(HoseTether.Clamp(max, Tap, 20f), Is.EqualTo(max),
                "inside the leash Max moves freely — the tether must not tug him at all");
        }

        [Test]
        public void PastTheTetherHeIsPinnedToTheCircle()
        {
            Vector3 max = Tap + new Vector3(50f, 0f, 0f);  // way past a 20 m leash
            Vector3 clamped = HoseTether.Clamp(max, Tap, 20f);

            float dist = new Vector2(clamped.x - Tap.x, clamped.z - Tap.z).magnitude;
            Assert.That(dist, Is.EqualTo(20f).Within(1e-3f),
                "past the leash he must sit exactly on the tether circle, not one metre further");
        }

        [Test]
        public void HeIsPinnedInHisOwnDirection_NotSnappedToAFixedSpot()
        {
            // Two players past the leash on opposite sides end up on opposite sides of the circle:
            // the clamp slides along the leash, it doesn't yank everyone to one anchor point.
            Vector3 east = HoseTether.Clamp(Tap + new Vector3(40f, 0f, 0f), Tap, 20f);
            Vector3 north = HoseTether.Clamp(Tap + new Vector3(0f, 0f, 40f), Tap, 20f);

            Assert.That(east.x, Is.GreaterThan(Tap.x), "the eastward player stays east of the tap");
            Assert.That(north.z, Is.GreaterThan(Tap.z), "the northward player stays north of the tap");
        }

        [Test]
        public void TheClampIsPlanar_ItNeverChangesHisHeight()
        {
            Vector3 max = Tap + new Vector3(50f, 1.37f, 0f);  // some Y off the ground plane
            Vector3 clamped = HoseTether.Clamp(max, Tap, 20f);
            Assert.That(clamped.y, Is.EqualTo(1.37f).Within(1e-4f),
                "the leash is a ground-plane radius; it must not lift or drop Max");
        }

        [Test]
        public void ShorteningTheTetherViaTheSliderPullsHimInFurther()
        {
            // The Settings slider writes DevTuning.HoseTetherLength; HoseTether.Length reads it.
            Vector3 max = Tap + new Vector3(50f, 0f, 0f);

            DevTuning.HoseTetherLength = 8f;
            Vector3 tight = HoseTether.Clamp(max, Tap, HoseTether.Length);
            float tightDist = new Vector2(tight.x - Tap.x, tight.z - Tap.z).magnitude;

            Assert.That(tightDist, Is.EqualTo(8f).Within(1e-3f),
                "moving the tether slider to 8 m must leash Max at 8 m, not the authored 20");
            Assert.That(HoseTether.Length, Is.EqualTo(8f),
                "HoseTether.Length must read the live override, so the slider applies with no push");
        }

        [Test]
        public void AnUntouchedSliderLeavesTheAuthoredLength()
        {
            Assert.That(HoseTether.Length, Is.EqualTo(HoseTether.AuthoredLength),
                "a fresh session leashes Max at the authored length until the slider is moved");
        }
    }
}
