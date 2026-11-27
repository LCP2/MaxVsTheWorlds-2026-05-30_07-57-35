using NUnit.Framework;
using MaxWorlds.Audio;
using MaxWorlds.Editor.Audio;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1474: the RIG hum is Lee's hand-supplied 30 s loop. The generator must never select it (even
    /// when forced or when the file is missing), and the resolved RigHum clip is the 30.0 s supplied file.
    /// Fail-first: on the base commit the generator has no hand-supplied rule (RigHum is selected when
    /// forced) and the resolved clip is the 24 s ElevenLabs loop.
    /// </summary>
    public sealed class MV1474RigHumHandSuppliedTests
    {
        [Test]
        public void GeneratorSkipsRigHum_EvenWhenForced_AndResolvedClipIs30Seconds()
        {
            Assert.IsFalse(ElevenLabsAudioGenerator.ShouldGenerateSfx("RigHum", outputExists: true, forced: true), "forced");
            Assert.IsFalse(ElevenLabsAudioGenerator.ShouldGenerateSfx("RigHum", outputExists: false, forced: false), "missing");
            Assert.IsTrue(ElevenLabsAudioGenerator.ShouldGenerateSfx("UiClick", outputExists: false, forced: false), "control: a normal entry is still generated");

            var clip = SfxCueLibrary.ResolveClip(SfxCueLibrary.Cue.RigHum);
            Assert.AreEqual(30.0f, clip.length, 0.05f);
        }
    }
}
