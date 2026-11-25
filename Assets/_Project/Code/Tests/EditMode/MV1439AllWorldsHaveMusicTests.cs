using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1439 AC1: every catalogued world resolves, through <see cref="MusicDirector.ResolveWorld"/>, to
    /// its OWN generated explore + boss clip (never ProcMusic), with resolved clip lengths in range and
    /// a loop-end point that sits inside the clip. Tier 2: resolved clip lengths, not authored constants.
    ///
    /// Fail-first: on the base commit the City row resolves to <c>MusicWorld.Stormdrain</c> (shares the
    /// Stormdrain track), there is no <c>MusicWorld.City</c> and no <c>MusicDirector.LoopEndSeconds</c>,
    /// and Resources/Audio/Music has no City_* or Mothership_* clip.
    /// </summary>
    public sealed class MV1439AllWorldsHaveMusicTests
    {
        [Test]
        public void EveryCatalogWorld_ResolvesItsOwnGeneratedExploreAndBossClip()
        {
            Assert.AreEqual(5, WorldCatalog.Count, "the catalogue is expected to hold five worlds");
            var seen = new System.Collections.Generic.HashSet<MusicWorld>();
            for (int i = 0; i < WorldCatalog.Count; i++)
            {
                MusicWorld world = MusicDirector.ResolveWorld(i);
                Assert.IsTrue(seen.Add(world), $"world index {i} resolves to {world}, already used by an earlier world");

                var explore = Resources.Load<AudioClip>("Audio/Music/" + world + "_explore");
                var boss = Resources.Load<AudioClip>("Audio/Music/" + world + "_boss");
                Assert.IsNotNull(explore, $"{world}: no generated explore clip (ProcMusic fallback would play)");
                Assert.IsNotNull(boss, $"{world}: no generated boss clip (ProcMusic fallback would play)");

                Assert.That(explore.length, Is.InRange(60f, 120f), $"{world} explore length {explore.length}s");
                Assert.That(boss.length, Is.InRange(40f, 120f), $"{world} boss length {boss.length}s");

                float exploreEnd = MusicDirector.LoopEndSeconds(world, boss: false);
                float bossEnd = MusicDirector.LoopEndSeconds(world, boss: true);
                Assert.That(exploreEnd, Is.GreaterThan(30f).And.LessThanOrEqualTo(explore.length), $"{world} explore loop end {exploreEnd}s vs clip {explore.length}s");
                Assert.That(bossEnd, Is.GreaterThan(30f).And.LessThanOrEqualTo(boss.length), $"{world} boss loop end {bossEnd}s vs clip {boss.length}s");
            }
        }
    }
}
