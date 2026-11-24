using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;
using MaxWorlds.UI;
using Cue = MaxWorlds.Audio.SfxCueLibrary.Cue;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1437 (AC2-AC3, resolved values): variants never repeat back-to-back and use every clip, the
    /// 30 ms retrigger gate and the per-cue voice cap hold, and BossEngaged ducks the Music bus to
    /// -6 dB +-0.5 within 0.1 s and recovers to 0 dB within 1 s of the hold ending. Also checks that
    /// every AudioSource the directors create is routed to a mixer group.
    /// Fail-first: AudioBus/AudioDucking/SfxVariants/SfxVoiceTracker did not exist on fdceed8.
    /// </summary>
    public sealed class MV1437AudioMixTests
    {
        [TearDown]
        public void TearDown()
        {
            AudioBus.ApplyDuck(0f);
            foreach (var d in Object.FindObjectsByType<SfxDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
        }

        [Test]
        public void VariantsVoicesAndBossDuck_ResolveToTheTicketsNumbers()
        {
            // Variants: 30 plays over 3 clips, never the same twice running, all three used.
            int last = -1; var used = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 30; i++)
            {
                int pick = SfxVariants.Next(3, last);
                Assert.AreNotEqual(last, pick, $"play {i} repeated variant {pick}");
                used.Add(pick); last = pick;
            }
            Assert.AreEqual(3, used.Count);

            // 10 plays inside 20 ms: at most 1 starts.
            var gate = new SfxVoiceTracker(); int started = 0;
            for (int i = 0; i < 10; i++)
                if (gate.TryStart(Cue.DamageDealt, 100f + i * 0.002f, 1f, i, out _)) started++;
            Assert.LessOrEqual(started, 1);

            // 10 plays spread over 1 s on a 2 s clip: never more than 6 alive.
            var cap = new SfxVoiceTracker(); int maxAlive = 0;
            for (int i = 0; i < 10; i++)
            {
                float now = 200f + i * 0.1f;
                Assert.IsTrue(cap.TryStart(Cue.DamageDealt, now, 2f, i, out _));
                maxAlive = Mathf.Max(maxAlive, cap.Alive(Cue.DamageDealt, now));
            }
            Assert.AreEqual(6, maxAlive);

            // Routing: every AudioSource the director makes has a mixer group.
            var director = new GameObject("SfxDirector(Test)").AddComponent<SfxDirector>();
            // EditMode does not run Awake/OnEnable on a MonoBehaviour added in a test; invoke it as Unity would.
            foreach (var m in new[] { "Awake", "OnEnable" })
                typeof(SfxDirector).GetMethod(m, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(director, null);
            foreach (var src in director.GetComponentsInChildren<AudioSource>(true))
                Assert.IsNotNull(src.outputAudioMixerGroup, src.name);
            Assert.AreEqual("Sfx", director.GetComponentsInChildren<AudioSource>(true)[0].outputAudioMixerGroup.name);
            Assert.AreEqual("Music", AudioBus.Group(AudioBusId.Music).name);
            Assert.AreEqual("Ambience", AudioBus.Group(AudioBusId.Ambience).name);

            // Ducking: BossEngaged -> -6 dB +-0.5 within 0.1 s, back to 0 within 1 s of the hold ending.
            HudSignals.EmitBossEngaged("Test", 1);
            var duck = director.Ducking;
            Assert.Greater(duck.HoldRemaining, 0f, "BossEngaged did not trigger a duck");
            float t = 0f;
            while (t < 0.1f) { duck.Advance(0.01f); t += 0.01f; }
            Assert.AreEqual(-6f, duck.CurrentDb, 0.5f);
            // SetFloat only takes effect on a running mixer (not under EditMode), so what is asserted here is
            // that the Music bus exposes its duck parameter for the driver to write at runtime.
            Assert.IsTrue(AudioBus.TryGetMusicDuck(out _), "mixer exposes the MusicDuck parameter");

            float hold = duck.HoldRemaining;
            duck.Advance(hold + 0.001f);
            for (float r = 0f; r < 1f; r += 0.01f) duck.Advance(0.01f);
            Assert.AreEqual(0f, duck.CurrentDb, 0.01f);
        }
    }
}
