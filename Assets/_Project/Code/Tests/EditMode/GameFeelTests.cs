using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Feel;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>YT-52 — the pure maths behind hit-stop, shake and kick.</summary>
    public sealed class GameFeelTests
    {
        [Test]
        public void ShakeAmount_IsTraumaSquared_SoSmallHitsStayQuiet()
        {
            // The squaring is the point: a half-trauma event should shake a quarter as hard,
            // not half as hard, or every chip hit rocks the camera.
            Assert.AreEqual(0.25f, GameFeelTuning.ShakeAmount(0.5f), 1e-4f);
            Assert.AreEqual(1f, GameFeelTuning.ShakeAmount(1f), 1e-4f);
            Assert.AreEqual(0f, GameFeelTuning.ShakeAmount(0f), 1e-4f);
        }

        [Test]
        public void Trauma_AddsButClamps_SoACrowdWipeCantPegTheCamera()
        {
            float t = 0f;
            for (int i = 0; i < 50; i++) t = GameFeelTuning.AddTrauma(t, 0.3f);
            Assert.AreEqual(1f, t, 1e-4f, "trauma must saturate, not run away");
        }

        [Test]
        public void Trauma_DecaysToZeroAndStopsThere()
        {
            float t = GameFeelTuning.DecayTrauma(1f, 10f, 1.9f);
            Assert.AreEqual(0f, t, 1e-4f);
            Assert.AreEqual(0f, GameFeelTuning.DecayTrauma(0f, 1f, 1.9f), 1e-4f, "must not go negative");
        }

        [Test]
        public void ShakeOffset_IsZeroWithoutTrauma()
        {
            Assert.AreEqual(Vector3.zero, GameFeelTuning.ShakeOffset(0f, 12.3f, 0.4f, 20f));
        }

        [Test]
        public void ShakeOffset_IsBounded_AndContinuousBetweenFrames()
        {
            var a = GameFeelTuning.ShakeOffset(1f, 5.00f, 0.4f, 20f);
            var b = GameFeelTuning.ShakeOffset(1f, 5.01f, 0.4f, 20f);

            Assert.That(a.magnitude, Is.LessThanOrEqualTo(0.4f * Mathf.Sqrt(3f) + 1e-3f),
                "the shake must stay inside its configured bound or it will break the framing");

            // Noise, not Random: consecutive frames must be correlated, or the camera reads as
            // static/jitter rather than as a shake.
            Assert.That(Vector3.Distance(a, b), Is.LessThan(0.25f),
                "consecutive frames should be close — this is Perlin noise, not white noise");
        }

        [Test]
        public void HitStop_IsRateLimited()
        {
            // A sustained stream lands a tick every 0.1s per enemy; without this the game stutters.
            Assert.IsTrue(GameFeelTuning.CanHitStop(now: 1.0f, lastStopAt: 0f, minInterval: 0.22f));
            Assert.IsFalse(GameFeelTuning.CanHitStop(now: 0.1f, lastStopAt: 0f, minInterval: 0.22f));
            Assert.IsTrue(GameFeelTuning.CanHitStop(now: 0.22f, lastStopAt: 0f, minInterval: 0.22f));
        }
    }
}
