using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1443: while the RIG holds Time.timeScale at 0 a weapon loop must be silent (resolved source
    /// volume 0, not playing) even though IsEmitting is latched true, and a stopped music pass must be
    /// restarted. Guards MV-1443.
    /// </summary>
    public sealed class MV1443RigCloseRestoresAudioTests
    {
        private static void Invoke(object o, string m) =>
            typeof(SfxDirector).GetMethod(m, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null);

        [Test]
        public void PausedGame_SilencesLatchedWeaponLoop_AndStoppedMusicPassRestarts()
        {
            float prev = Time.timeScale;
            var d = new GameObject("SfxDirector(MV1443)").AddComponent<SfxDirector>();
            Invoke(d, "Awake");
            try
            {
                var loopsField = typeof(SfxDirector).GetField("_loops", BindingFlags.Instance | BindingFlags.NonPublic);
                var loops = (Array)loopsField.GetValue(d);
                object undertow = null;
                foreach (var l in loops)
                    if (l.GetType().GetField("Cue").GetValue(l).ToString() == "UndertowLoop") undertow = l;
                Assert.NotNull(undertow);
                var t = undertow.GetType();
                t.GetField("IsEmitting").SetValue(undertow, (Func<bool>)(() => true));   // latched firing
                t.GetField("Volume").SetValue(undertow, 1f);
                var src = (AudioSource)t.GetField("Source").GetValue(undertow);
                src.volume = 0.8f;

                Time.timeScale = 0f;   // RIG open
                Invoke(d, "Update");

                Assert.AreEqual(0f, (float)t.GetField("Volume").GetValue(undertow), 0.0001f, "loop volume while paused");
                Assert.AreEqual(0f, src.volume, 0.0001f, "resolved source volume while paused");

                Assert.IsTrue(MusicDirector.NeedsRestart(false, false, 0.5f), "ended pass must restart");
                Assert.IsFalse(MusicDirector.NeedsRestart(false, true, 0.5f));
                Assert.IsFalse(MusicDirector.NeedsRestart(true, false, 0.5f), "mid-crossfade is left alone");
            }
            finally
            {
                Time.timeScale = prev;
                Invoke(d, "OnDisable");
                UnityEngine.Object.DestroyImmediate(d.gameObject);
            }
        }
    }
}
