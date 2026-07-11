using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Feel;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-52 — the parts that can only be told the truth by a real clock and a real frame:
    /// that hit-stop always gives the timescale back, that it never steals it from a pause,
    /// and that the shake leaves the camera exactly where it found it.
    /// </summary>
    public sealed class GameFeelPlayTests
    {
        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator HitStop_FreezesThenRestoresTime()
        {
            var go = new GameObject("hitstop");
            var stop = go.AddComponent<HitStop>();

            stop.Request(0.1f, 0.05f);
            yield return null;

            Assert.AreEqual(0.05f, Time.timeScale, 1e-3f, "time should be slowed during the stop");

            yield return new WaitForSecondsRealtime(0.25f);

            Assert.AreEqual(1f, Time.timeScale, 1e-3f, "time must always be handed back");
            Assert.IsFalse(stop.IsStopping);

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator HitStop_RefusesToRunWhileTheGameIsPaused()
        {
            var go = new GameObject("hitstop-paused");
            var stop = go.AddComponent<HitStop>();

            Time.timeScale = 0f;              // ResultScreen pauses like this
            stop.Request(0.1f, 0.05f);
            yield return null;

            Assert.AreEqual(0f, Time.timeScale, "a hit-stop must not run over the top of a pause");
            Assert.IsFalse(stop.IsStopping);

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator HitStop_DoesNotUnpauseAGameThatPausedMidStop()
        {
            var go = new GameObject("hitstop-race");
            var stop = go.AddComponent<HitStop>();

            stop.Request(0.08f, 0.05f);
            yield return null;
            Assert.IsTrue(stop.IsStopping);

            // The run ends mid-hit-stop and the result screen pauses. A naive restore-to-1 here
            // would silently un-pause the result screen and let the game play on behind the card.
            Time.timeScale = 0f;

            yield return new WaitForSecondsRealtime(0.25f);

            Assert.AreEqual(0f, Time.timeScale,
                "hit-stop must only restore time if it still owns the timescale");

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Shake_OffsetsTheCameraThenReturnsItExactly()
        {
            var go = new GameObject("cam");
            var start = new Vector3(3f, 13f, -4.22f);
            go.transform.position = start;
            go.transform.rotation = Quaternion.Euler(72f, 0f, 0f);   // the fixed rig angle

            var shake = go.AddComponent<ScreenShake>();
            shake.AddTrauma(1f);
            yield return null;
            yield return null;

            Assert.AreNotEqual(start, go.transform.position, "full trauma should visibly move the camera");

            yield return SettleShake(shake);

            Assert.That(shake.Trauma, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(Vector3.Distance(go.transform.position, start), Is.LessThan(1e-3f),
                "the camera must come back to exactly where the rig put it — no drift");
            Assert.That(Quaternion.Angle(go.transform.rotation, Quaternion.Euler(72f, 0f, 0f)),
                Is.LessThan(0.05f), "the fixed ~72 degree framing must survive the shake");

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Shake_DoesNotDriftWhenTheRigNeverRewritesTheTransform()
        {
            // Nothing else is writing this transform (no Cinemachine brain in the test scene), so
            // if the shake failed to undo its own offset each frame it would walk the camera away.
            var go = new GameObject("cam-drift");
            var start = Vector3.zero;
            go.transform.position = start;

            var shake = go.AddComponent<ScreenShake>();
            for (int i = 0; i < 30; i++)
            {
                shake.AddTrauma(0.5f);
                yield return null;
            }
            yield return SettleShake(shake);

            Assert.That(Vector3.Distance(go.transform.position, start), Is.LessThan(1e-3f),
                "repeated shakes must not accumulate into permanent camera displacement");

            Object.Destroy(go);
        }

        /// <summary>Wait (in real time) for the trauma to bleed off.
        ///
        /// Deliberately NOT a fixed number of frames: in batch mode a frame is a fraction of a
        /// millisecond, so hundreds of frames pass almost no unscaled time and the shake — which
        /// decays on unscaled time — has barely moved. A frame-count wait here fails against
        /// perfectly good code.</summary>
        private static IEnumerator SettleShake(ScreenShake shake)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (shake.Trauma > 0f && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;   // one more frame for the final zero-offset LateUpdate to land
        }
    }
}
