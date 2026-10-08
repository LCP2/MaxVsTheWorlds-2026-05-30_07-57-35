using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1137 — proves the generated per-world music actually loads from <see cref="Resources"/> with
    /// each clip's RESOLVED length inside the ticket's range (Tier 2: a resolved value, never an
    /// authored constant), and that <see cref="MusicDirector.GeneratedVolumes"/> crossfades main/boss
    /// correctly at three fixed inputs.
    ///
    /// Fail-first: on the base commit, <c>Assets/_Project/Resources/Audio/Music/</c> doesn't exist, so
    /// every <see cref="Resources.Load{T}"/> call below returns null and the clip assertions fail; and
    /// <see cref="MusicDirector.GeneratedVolumes"/> doesn't exist, so the file doesn't compile without
    /// it either.
    /// </summary>
    public sealed class MV1137GeneratedMusicTests
    {
        private static readonly MusicWorld[] Worlds = { MusicWorld.Backyard, MusicWorld.Stormdrain, MusicWorld.Reef };

        [Test]
        public void GeneratedMusicClips_LoadWithExpectedLength_AndVolumesCrossfadeCorrectly()
        {
            foreach (var world in Worlds)
            {
                var explore = Resources.Load<AudioClip>("Audio/Music/" + world + "_explore");
                var boss = Resources.Load<AudioClip>("Audio/Music/" + world + "_boss");

                Assert.IsNotNull(explore, $"{world}: explore clip did not load from Resources");
                Assert.IsNotNull(boss, $"{world}: boss clip did not load from Resources");

                Assert.GreaterOrEqual(explore.length, 60f, $"{world}: explore clip shorter than 60s ({explore.length}s)");
                Assert.LessOrEqual(explore.length, 120f, $"{world}: explore clip longer than 120s ({explore.length}s)");

                Assert.GreaterOrEqual(boss.length, 40f, $"{world}: boss clip shorter than 40s ({boss.length}s)");
                Assert.LessOrEqual(boss.length, 90f, $"{world}: boss clip longer than 90s ({boss.length}s)");
            }

            var silent = MusicDirector.GeneratedVolumes(0.5f, 1f, 0f);
            Assert.AreEqual(0.5f, silent.main, 1e-5f, "master-only volumes: main");
            Assert.AreEqual(0f, silent.boss, 1e-5f, "master-only volumes: boss");

            var fullBoss = MusicDirector.GeneratedVolumes(0.5f, 1f, 1f);
            Assert.AreEqual(0f, fullBoss.main, 1e-5f, "full-intensity volumes: main");
            Assert.AreEqual(0.5f, fullBoss.boss, 1e-5f, "full-intensity volumes: boss");

            var halfway = MusicDirector.GeneratedVolumes(0.5f, 0.5f, 0.5f);
            Assert.AreEqual(0.125f, halfway.main, 1e-5f, "half-faded half-intensity volumes: main");
            Assert.AreEqual(0.125f, halfway.boss, 1e-5f, "half-faded half-intensity volumes: boss");
        }
    }
}
