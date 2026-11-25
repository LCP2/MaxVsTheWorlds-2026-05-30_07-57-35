using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1444: with THE RIG open, RigHum plays at its target volume while music and ambience are silent
    /// (but not stopped, so they keep their place); shortly after it closes the hum is stopped and the
    /// music and ambience are back at their targets.
    /// </summary>
    public sealed class MV1444RigHumTests
    {
        private static void Invoke(object o, string m) =>
            o.GetType().GetMethod(m, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null);

        private static AudioSource Field(object o, string f) =>
            (AudioSource)o.GetType().GetField(f, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);

        [Test]
        public void RigOpen_PlaysHumAndSilencesMusicAndAmbience_CloseRestoresThemWithoutRestarting()
        {
            RigAudioMix.Reset();
            var sfx = new GameObject("SfxDirector(MV1444)").AddComponent<SfxDirector>();
            var music = new GameObject("MusicDirector(MV1444)").AddComponent<MusicDirector>();
            var amb = new GameObject("AmbienceDirector(MV1444)").AddComponent<AmbienceDirector>();
            Invoke(sfx, "Awake");
            Invoke(music, "Awake");
            Invoke(amb, "Awake");
            try
            {
                var musicSrc = Field(music, "_mainSource");
                musicSrc.clip = AudioClip.Create("mv1444-music", 44100 * 10, 1, 44100, false);
                musicSrc.Play();
                musicSrc.time = 3f;

                float dt = 1f / 30f;
                void Frame(bool open)
                {
                    sfx.TickRigAudio(open, dt);
                    Invoke(music, "TickVolumes");
                    Invoke(amb, "Update");
                }

                for (int i = 0; i < 30; i++) Frame(false);   // 1 s of normal play
                float musicTarget = musicSrc.volume;
                float ambTarget = amb.Source.volume;
                Assert.Greater(musicTarget, 0f, "music target before the RIG");
                Assert.Greater(ambTarget, 0f, "ambience target before the RIG");

                float timeAtOpen = musicSrc.time;
                for (int i = 0; i < 60; i++) Frame(true);   // RIG open for 2 s

                Assert.AreEqual(RigAudioMix.HumTargetVolume, sfx.RigHumSource.volume, 0.0001f, "hum volume with the RIG open");
                Assert.IsTrue(sfx.RigHumSource.isPlaying, "hum playing with the RIG open");
                Assert.AreEqual(0f, musicSrc.volume, 0.0001f, "music volume with the RIG open");
                Assert.AreEqual(0f, amb.Source.volume, 0.0001f, "ambience volume with the RIG open");

                for (int i = 0; i < 30; i++) Frame(false);   // closed for 1 s

                Assert.AreEqual(0f, sfx.RigHumSource.volume, 0.0001f, "hum volume after close");
                Assert.IsFalse(sfx.RigHumSource.isPlaying, "hum stopped after close");
                Assert.AreEqual(musicTarget, musicSrc.volume, 0.0001f, "music back at target");
                Assert.AreEqual(ambTarget, amb.Source.volume, 0.0001f, "ambience back at target");
                Assert.GreaterOrEqual(musicSrc.time, timeAtOpen, "music continues rather than restarting");
            }
            finally
            {
                RigAudioMix.Reset();
                Invoke(sfx, "OnDisable");
                Object.DestroyImmediate(sfx.gameObject);
                Object.DestroyImmediate(music.gameObject);
                Object.DestroyImmediate(amb.gameObject);
            }
        }
    }
}
