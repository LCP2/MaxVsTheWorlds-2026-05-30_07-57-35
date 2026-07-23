using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Intro;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The opening cinematic's DIRECTOR (YT-156) — the timeline. PlayMode, because every claim here is
    /// about time: it walks its five beats in order, borrows the screen and gives it back, and hands off
    /// to gameplay when it is done. <see cref="IntroCinematic.Tick"/> is public precisely so a test can
    /// fast-forward the ~24-second sequence without waiting that long in real time.
    /// </summary>
    public sealed class IntroCinematicPlayTests
    {
        private GameObject _camGo;
        private IntroCinematic _intro;

        [SetUp]
        public void SetUp()
        {
            // A clean slate: the AfterSceneLoad Install may have stood one up at play-mode entry. This
            // test drives its own instance directly, so clear any stray one first.
            foreach (var stray in Object.FindObjectsByType<IntroCinematic>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            // Something for Camera.main to resolve to — the intro draws OVER it, and hands back to it.
            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();

            RenderSettings.fog = true;   // a known state the intro must restore
        }

        [TearDown]
        public void TearDown()
        {
            if (_intro != null) Object.Destroy(_intro.gameObject);
            if (_camGo != null) Object.Destroy(_camGo);
        }

        private IntroCinematic Build()
        {
            var go = new GameObject("IntroCinematic");
            _intro = go.AddComponent<IntroCinematic>();   // Awake takes over the screen and builds the set
            return _intro;
        }

        [UnityTest]
        public IEnumerator ItBuildsAllThreeActsAndBorrowsTheScreen()
        {
            var intro = Build();
            yield return null;

            Assert.IsTrue(intro.IsPlaying, "the cinematic is not running the frame after it is built.");
            Assert.Greater(intro.TotalDuration, 15f, "the whole sequence is suspiciously short.");
            Assert.IsNotNull(intro.Space, "the space act was not built.");
            Assert.IsNotNull(intro.Descent, "the descent act was not built.");
            Assert.IsNotNull(intro.Shed, "the shed act was not built.");

            Assert.IsNotNull(intro.IntroCamera, "the intro has no camera of its own.");
            Assert.IsFalse(RenderSettings.fog, "fog was not switched off for the space act.");
        }

        [UnityTest]
        public IEnumerator ItWalksEveryBeatInOrderThenHandsOff()
        {
            var intro = Build();
            yield return null;

            var seen = new List<string>();

            // Fast-forward in small steps so every beat's OnEnter fires and the door actually scrubs open.
            float maxDoor = 0f;
            for (int i = 0; i < 400 && intro.IsPlaying; i++)
            {
                intro.Tick(0.1f);
                if (seen.Count == 0 || seen[seen.Count - 1] != intro.BeatName)
                {
                    if (!intro.IsPlaying) break;
                    seen.Add(intro.BeatName);
                }
                if (intro.Shed != null) maxDoor = Mathf.Max(maxDoor, intro.Shed.DoorOpen01);
            }

            foreach (var beat in new[] { "space-open", "comet", "split-land", "descent", "shed" })
                Assert.Contains(beat, seen, $"the cinematic never reached the '{beat}' beat.");

            // The beats appear once each, in storyboard order.
            Assert.AreEqual(seen.Count, seen.Distinct().Count(), "a beat was entered more than once.");
            Assert.Less(seen.IndexOf("space-open"), seen.IndexOf("shed"),
                "the beats are out of order — space must come before the shed.");

            Assert.Greater(maxDoor, 0.9f, "the door never reached wide open across the run.");
            Assert.IsFalse(intro.IsPlaying, "the cinematic never handed off.");
        }

        [UnityTest]
        public IEnumerator ItGivesTheScreenBackAtHandoff()
        {
            var intro = Build();
            yield return null;

            while (intro.IsPlaying) intro.Tick(0.2f);

            Assert.IsFalse(intro.IsPlaying, "still playing after being ticked past the end.");
            Assert.IsTrue(RenderSettings.fog, "fog was not restored to the yard at handoff.");
        }

        // ------------------------------------------------------------------ YT-155: skip + handoff

        [UnityTest]
        public IEnumerator TapToSkipEndsItImmediatelyAndGivesTheScreenBack()
        {
            var intro = Build();
            yield return null;

            intro.Tick(0.5f);   // a little way into the first beat
            Assert.IsTrue(intro.IsPlaying, "the cinematic ended before it was skipped.");

            intro.Skip();

            Assert.IsFalse(intro.IsPlaying, "Skip did not end the cinematic.");
            Assert.IsTrue(RenderSettings.fog, "Skip did not hand the yard's fog back.");
        }

        [UnityTest]
        public IEnumerator ItSuspendsPlayerControlThenReturnsItAtHandoff()
        {
            // A real PlayerController for the intro to find and suspend. It builds all its input in code
            // (no serialized refs), so it stands up cleanly in a test.
            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<PlayerController>();
            Assert.IsTrue(player.enabled, "sanity: the player starts in control.");

            var intro = Build();   // Awake finds + suspends the player
            yield return null;

            Assert.IsFalse(player.enabled, "player control was not suspended for the cinematic.");
            Assert.IsTrue(intro.PlayerControlSuspended, "the intro does not report holding control.");

            // Play it through to the natural handoff.
            while (intro.IsPlaying) intro.Tick(0.2f);

            Assert.IsTrue(player.enabled, "control was not returned to the player at handoff.");
            Assert.IsFalse(intro.PlayerControlSuspended, "the intro still reports holding control after handoff.");

            Object.Destroy(playerGo);
        }

        [UnityTest]
        public IEnumerator SkipReturnsControlToThePlayer()
        {
            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<PlayerController>();

            var intro = Build();
            yield return null;
            Assert.IsFalse(player.enabled, "player control was not suspended for the cinematic.");

            intro.Skip();

            Assert.IsTrue(player.enabled, "skipping did not return control to the player.");

            Object.Destroy(playerGo);
        }

        // ------------------------------------------------------------------ YT-161: beats 3 & 4 read

        [UnityTest]
        public IEnumerator SplitLand_SeveralInvadersStrikeWithHangTimeBeforeTheDive()
        {
            var intro = Build();
            yield return null;
            while (intro.IsPlaying && intro.BeatName != "split-land") intro.Tick(0.1f);
            Assert.AreEqual("split-land", intro.BeatName, "never reached the split-land beat.");

            int ScorchesLit() => intro.Space.Root.GetComponentsInChildren<Transform>(true)
                                       .Count(t => t.name == "Scorch" && t.gameObject.activeSelf);

            // Tick through most (not all) of the beat: several invaders must already be down and
            // visible with time still left in the beat, not all landing in the final instant before
            // the cut to the dive.
            float beatStart = intro.Elapsed;
            while (intro.IsPlaying && intro.BeatName == "split-land" && intro.Elapsed - beatStart < 3.2f)
                intro.Tick(0.05f);

            Assert.AreEqual("split-land", intro.BeatName,
                "the beat moved on before there was hang time to see the impacts.");
            Assert.GreaterOrEqual(ScorchesLit(), 3,
                "fewer than 3 invaders have struck with time left in the beat — the impacts are still rushed.");
        }

        [UnityTest]
        public IEnumerator Descent_CameraSettlesOnTheRoofBeforeTheFlashCut()
        {
            var intro = Build();
            yield return null;
            while (intro.IsPlaying && intro.BeatName != "descent") intro.Tick(0.05f);
            Assert.AreEqual("descent", intro.BeatName, "never reached the descent beat.");

            const float descentDuration = 4.8f;   // mirrors the "descent" beat's Duration in BuildBeats
            float beatStart = intro.Elapsed;
            Vector3? pos90 = null, pos99 = null;

            while (intro.IsPlaying && intro.BeatName == "descent")
            {
                intro.Tick(0.02f);
                if (intro.BeatName != "descent") break;   // the next beat's OnEnter can move the camera
                float frac = (intro.Elapsed - beatStart) / descentDuration;
                if (pos90 == null && frac >= 0.9f) pos90 = intro.IntroCamera.transform.position;
                if (pos99 == null && frac >= 0.99f) pos99 = intro.IntroCamera.transform.position;
            }

            Assert.IsTrue(pos90.HasValue, "never reached 90% through the descent beat.");
            Assert.IsTrue(pos99.HasValue, "never reached 99% through the descent beat.");
            Assert.Less(Vector3.Distance(pos90.Value, pos99.Value), 25f,
                "the camera is still travelling fast in the last stretch of the dive — it arrives too " +
                "late on the roof to read before the flash-cut.");
        }

        [UnityTest]
        public IEnumerator NoPartOfTheSetShipsMagenta()
        {
            var intro = Build();
            yield return null;
            intro.Tick(0.1f);   // let the first act come on screen
            yield return null;

            foreach (var r in intro.gameObject.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.IsNotNull(r.sharedMaterial, $"'{r.name}' has no material — it draws nothing.");
                string shader = r.sharedMaterial.shader.name;
                Assert.That(shader,
                    Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds")
                        .Or.StartWith("Sprites").Or.StartWith("Standard"),
                    $"'{r.name}' wears '{shader}' — a default material is magenta in the build.");
            }
        }
    }
}
