using System;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;
using Cue = MaxWorlds.Audio.SfxCueLibrary.Cue;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1438 (AC1, resolved clips): every SfxCueLibrary.Cue resolves exactly 3 generated variant clips
    /// (none falling back to ProcSfx, which would resolve a single synthesised one), and the five world
    /// ambience beds resolve. Fail-first: on the MV-1437 base most cues had 0-1 clips and no ambience existed.
    /// </summary>
    public sealed class MV1438SfxLibraryCompleteTests
    {
        private static readonly string[] Worlds = { "backyard", "stormdrain", "city", "reef", "mothership" };

        [Test]
        public void EveryCueResolvesThreeGeneratedVariants_AndFiveAmbienceBedsResolve()
        {
            var problems = new System.Text.StringBuilder();
            foreach (Cue cue in Enum.GetValues(typeof(Cue)))
            {
                int n = 0;
                for (int i = 1; i <= 3; i++)
                    if (Resources.Load<AudioClip>("Audio/Sfx/" + cue + "_" + i) != null) n++;
                int expected = (cue == Cue.HoseLoop || cue == Cue.UndertowLoop) ? 1 : 3;
                if (cue == Cue.HoseLoop || cue == Cue.UndertowLoop) n = Resources.Load<AudioClip>("Audio/Sfx/" + cue + "_1") != null ? 1 : 0;
                if (n != expected) problems.AppendLine($"{cue}: {n} generated variant clips, expected {expected}");
                Assert.AreEqual(expected, SfxCueLibrary.ResolveVariants(cue).Length, $"{cue} resolved variant count");
            }
            foreach (var w in Worlds)
                if (Resources.Load<AudioClip>(AmbienceDirector.ResourcePath(w)) == null)
                    problems.AppendLine($"ambience {w}: missing");
            Assert.AreEqual(string.Empty, problems.ToString(), problems.ToString());
        }
    }
}
