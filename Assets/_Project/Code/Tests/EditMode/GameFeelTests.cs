using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Feel;
using MaxWorlds.UI;

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

        /// <summary>MV-637: the crowd/"earthquake" shake stacks additively — many simultaneous
        /// non-crit hits in the same tick pile trauma on top of each other. Drives GameFeel's real
        /// OnDamage path (via HudSignals.EmitDamage) rather than reading the hitTrauma field, so this
        /// asserts the resolved trauma ScreenShake actually ends up with, not the authored constant.</summary>
        [Test]
        public void SimultaneousDamageEvents_AccumulateSixtyPercentLessTrauma_ThanTheOldConstant()
        {
            const int n = 5;
            const float oldHitTrauma = 0.055f;

            Camera[] suppressed = CameraTestUtil.SuppressAmbientMainCameras();
            var camGo = new GameObject("MV637 Main Camera Probe", typeof(Camera)) { tag = "MainCamera" };
            var feelGo = new GameObject("MV637 GameFeel Probe");
            GameFeel feel = null;
            try
            {
                // EditMode tests never enter Play Mode, so Unity does not pump Awake/OnEnable for a
                // plain (non-ExecuteAlways) MonoBehaviour added via AddComponent — invoke them
                // directly so GameFeel's real HudSignals.DamageDealt subscription is actually wired,
                // the same way this file's sibling EditMode tests reach into private lifecycle
                // methods (see MV574MobileHeatAndModalIdleTests) rather than relying on ExecuteAlways.
                feel = feelGo.AddComponent<GameFeel>();
                Assert.IsNotNull(feel, "GameFeel must install onto the probe object");
                const System.Reflection.BindingFlags nonPublicInstance =
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(GameFeel).GetMethod("Awake", nonPublicInstance).Invoke(feel, null);
                typeof(GameFeel).GetMethod("OnEnable", nonPublicInstance).Invoke(feel, null);

                for (int i = 0; i < n; i++) HudSignals.EmitDamage(Vector3.zero, 10f, crit: false);

                float actualTrauma = camGo.GetComponent<ScreenShake>().Trauma;

                float oldReferenceTrauma = 0f;
                for (int i = 0; i < n; i++) oldReferenceTrauma = GameFeelTuning.AddTrauma(oldReferenceTrauma, oldHitTrauma);

                Assert.AreEqual(oldReferenceTrauma * 0.4f, actualTrauma, 1e-4f,
                    "N simultaneous hits must accumulate 60% less trauma than the old 0.055f constant produced");
            }
            finally
            {
                // Undo the manual OnEnable() above — without it, GameFeel.OnDamage stays subscribed
                // to the static HudSignals.DamageDealt event after feelGo is destroyed, and the next
                // test's EmitDamage call throws MissingReferenceException on the dangling instance.
                if (feel != null)
                {
                    typeof(GameFeel).GetMethod("OnDisable",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(feel, null);
                }
                Object.DestroyImmediate(feelGo);
                Object.DestroyImmediate(camGo);
                CameraTestUtil.RestoreAmbientMainCameras(suppressed);
            }
        }
    }
}
