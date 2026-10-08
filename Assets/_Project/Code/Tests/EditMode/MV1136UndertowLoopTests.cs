using NUnit.Framework;
using MaxWorlds.Audio;
using Cue = MaxWorlds.Audio.SfxCueLibrary.Cue;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1136 — the World 3 beam (Undertow) gets a looping sound, serving the same loop machinery
    /// the hose already uses. Covers the enum placement constraint (the mute bitmask is indexed by
    /// the enum's integer value, so a new cue must land LAST), the SOUND tab label, clip resolution,
    /// and the pure fade step <see cref="SfxDirector.NextLoopVolume"/>.
    /// </summary>
    public sealed class MV1136UndertowLoopTests
    {
        [Test]
        public void UndertowLoopIsLastCue_HasItsLabelAndClip_AndTheFadeStepIsCorrect()
        {
            var allCues = SfxCueLibrary.AllCues;
            Assert.AreEqual(Cue.UndertowLoop, allCues[allCues.Length - 1],
                "UndertowLoop must be the LAST member of the enum - inserting earlier would shift every existing player's mute switches");
            Assert.AreEqual(21, (int)Cue.UiClick, "UiClick's integer value must not have shifted");

            Assert.AreEqual("Beam loop", SfxCueLibrary.DisplayNames[Cue.UndertowLoop]);
            Assert.AreEqual("UndertowLoop", SfxCueLibrary.ResolveClip(Cue.UndertowLoop).name);

            Assert.AreEqual(0.5f, SfxDirector.NextLoopVolume(0f, true, 0.05f), 1e-5f);
            Assert.AreEqual(0f, SfxDirector.NextLoopVolume(1f, false, 0.2f), 1e-5f);
        }
    }
}
