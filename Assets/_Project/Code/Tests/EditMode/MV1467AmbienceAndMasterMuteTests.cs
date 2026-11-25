using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1467: an Ambience on/off switch (source volume 0 and stopped when OFF, restarts when ON) and an
    /// "All sound" master switch (AudioListener.volume resolves to 0 when OFF, 1 when ON). Reflection-free
    /// of new members except through <see cref="Call"/>, so this compiles and fails by assertion on the
    /// pre-change base.
    /// </summary>
    public sealed class MV1467AmbienceAndMasterMuteTests
    {
        private static MethodInfo Find(string type, string method)
        {
            var t = typeof(AmbienceDirector).Assembly.GetType("MaxWorlds.Audio." + type);
            var m = t?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(m, type + "." + method + " must exist");
            return m;
        }

        [Test]
        public void AmbienceOffSilencesAndStopsBed_OnRestarts_AllSoundOffMutesListener()
        {
            float listener = AudioListener.volume;
            var amb = new GameObject("AmbienceDirector(MV1467)").AddComponent<AmbienceDirector>();
            try
            {
                typeof(AmbienceDirector).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(amb, null);
                var update = typeof(AmbienceDirector).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                amb.Source.clip = AudioClip.Create("mv1467-amb", 44100, 1, 44100, false);

                amb.Source.Play();
                update.Invoke(amb, null);
                Assert.Greater(amb.Source.volume, 0f, "ambience audible while ON");

                Find("AmbienceDirector", "SetAmbienceOn").Invoke(null, new object[] { false });
                update.Invoke(amb, null);
                Assert.AreEqual(0f, amb.Source.volume, 0f, "ambience volume with switch OFF");
                Assert.IsFalse(amb.Source.isPlaying, "ambience stopped with switch OFF");

                Find("AmbienceDirector", "SetAmbienceOn").Invoke(null, new object[] { true });
                update.Invoke(amb, null);
                Assert.IsTrue(amb.Source.isPlaying, "ambience restarted with switch ON");
                Assert.AreEqual(AmbienceDirector.DefaultAmbienceVolume, amb.Source.volume, 0.0001f, "ambience at slider volume");

                var setAll = Find("AudioMaster", "SetAllSoundOn");
                var apply = Find("AudioMaster", "Apply");
                setAll.Invoke(null, new object[] { false });
                apply.Invoke(null, new object[] { false });   // false = pretend not batchmode
                Assert.AreEqual(0f, AudioListener.volume, 0f, "listener volume with All sound OFF");
                setAll.Invoke(null, new object[] { true });
                apply.Invoke(null, new object[] { false });
                Assert.AreEqual(1f, AudioListener.volume, 0f, "listener volume with All sound ON");
            }
            finally
            {
                DevTuning.Reset();
                AudioListener.volume = listener;
                UnityEngine.Object.DestroyImmediate(amb.gameObject);
            }
        }
    }
}
