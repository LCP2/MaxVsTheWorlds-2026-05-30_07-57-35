using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests
{
    /// <summary>
    /// MV-1186: AudioListener.volume must drop to 0 for batchmode runs (so the dev PC's speakers stay
    /// silent for every worker verification run) but must be left untouched otherwise, so the existing
    /// music/SFX timing tests keep behaving exactly as they do today.
    /// </summary>
    public class MV1186HeadlessAudioMuteTests
    {
        private float _originalVolume;

        [SetUp]
        public void SetUp() => _originalVolume = AudioListener.volume;

        [TearDown]
        public void TearDown() => AudioListener.volume = _originalVolume;

        [Test]
        public void ApplyMutesOnlyWhenBatchMode()
        {
            AudioListener.volume = 1f;
            HeadlessAudioMute.Apply(true);
            Assert.AreEqual(0f, AudioListener.volume, "batchmode=true must mute AudioListener.volume to 0");

            AudioListener.volume = 1f;
            HeadlessAudioMute.Apply(false);
            Assert.AreEqual(1f, AudioListener.volume, "batchmode=false must leave AudioListener.volume untouched");
        }
    }
}
