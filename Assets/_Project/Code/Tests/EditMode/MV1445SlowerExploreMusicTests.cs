using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1445 AC2: each world's explore track is authored at 96-104 BPM in audio_manifest.json, the loop
    /// end the game actually uses (<see cref="MusicDirector.LoopEndSeconds"/>) is a whole number of 4/4
    /// bars at that BPM, and for all ten tracks the loop end sits inside the resolved clip.
    ///
    /// Fail-first: on the base commit every explore track is authored at 120-128 BPM.
    /// </summary>
    public sealed class MV1445SlowerExploreMusicTests
    {
        [Serializable] private class Entry { public string id; public int bpm; public float loopEndSeconds; }
        [Serializable] private class Manifest { public Entry[] music; }

        [Test]
        public void ExploreTracksAre96To104Bpm_WithWholeBarLoopEnds_AndAllTenLoopEndsInsideTheirClips()
        {
            string path = Path.Combine(Application.dataPath, "_Project/Audio/audio_manifest.json");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            Assert.AreEqual(5, WorldCatalog.Count);

            for (int i = 0; i < WorldCatalog.Count; i++)
            {
                MusicWorld world = MusicDirector.ResolveWorld(i);
                Entry entry = Array.Find(manifest.music, e => e.id == world + "_explore");
                Assert.IsNotNull(entry, $"{world}_explore missing from the manifest");
                Assert.That(entry.bpm, Is.InRange(96, 104), $"{world} explore authored BPM");

                float barSeconds = 4f * 60f / entry.bpm;
                float exploreEnd = MusicDirector.LoopEndSeconds(world, boss: false);
                float bars = exploreEnd / barSeconds;
                Assert.AreEqual(Mathf.Round(bars), bars, 0.001f, $"{world} explore loop end {exploreEnd}s is {bars} bars at {entry.bpm} BPM");
                Assert.AreEqual(exploreEnd, entry.loopEndSeconds, 0.001f, $"{world} manifest loopEndSeconds vs MusicDirector");

                foreach (bool boss in new[] { false, true })
                {
                    string suffix = boss ? "_boss" : "_explore";
                    var clip = Resources.Load<AudioClip>("Audio/Music/" + world + suffix);
                    Assert.IsNotNull(clip, world + suffix);
                    float end = MusicDirector.LoopEndSeconds(world, boss);
                    Assert.Less(end, clip.length, $"{world}{suffix} loop end {end}s vs clip {clip.length}s");
                }
            }
        }
    }
}
