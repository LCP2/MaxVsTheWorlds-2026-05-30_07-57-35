using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Audio;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1159: a Sentinel's 3s teleport-in arrival (MV-1113) had no sound. <see cref="Sentinel.BeginArrival"/>
    /// now raises <see cref="HudSignals.SentinelArrived"/> exactly once, at the arrival's start position, and
    /// the new <see cref="SfxCueLibrary.Cue.SentinelArrived"/> cue resolves to the generated ElevenLabs clip
    /// (not the synthesised fallback), at least 2.5s long (it must still cover the 3.0s arrival).
    ///
    /// UNVERIFIED (Unity Editor license lapsed on the dev PC — see Jira comment): this has NOT been run, so
    /// Rule 1's "proven to fail on base" quote cannot be given yet. Written now so only a run is needed once
    /// the license is restored, not a re-derivation of this test.
    /// </summary>
    public sealed class MV1159SentinelArrivedSfxTests
    {
        [TearDown]
        public void TearDown() => Sentinel.ResetRegistry();

        [Test]
        public void BeginArrival_RaisesSentinelArrivedOnceAtItsPosition_AndResolvesToTheGeneratedClip()
        {
            int fired = 0;
            Vector3 firedAt = Vector3.zero;
            void Handler(Vector3 pos) { fired++; firedAt = pos; }

            var go = new GameObject("MV1159-Sentinel");
            try
            {
                var sentinel = go.AddComponent<Sentinel>();
                var arrivalPoint = new Vector3(3f, 0f, 4f);
                sentinel.Init(arrivalPoint, maxHp: 100f, range: 5f, fireInterval: 1f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

                HudSignals.SentinelArrived += Handler;
                sentinel.BeginArrival();
                HudSignals.SentinelArrived -= Handler;

                Assert.AreEqual(1, fired, "BeginArrival must raise HudSignals.SentinelArrived exactly once");
                Assert.AreEqual(arrivalPoint, firedAt, "SentinelArrived must carry the arrival's own position");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            var clip = SfxCueLibrary.ResolveClip(SfxCueLibrary.Cue.SentinelArrived);
            Assert.AreEqual(SfxCueLibrary.Cue.SentinelArrived.ToString(), clip.name,
                "ResolveClip(SentinelArrived) did not return a clip named 'SentinelArrived'");
            Assert.GreaterOrEqual(clip.length, 2.5f,
                $"SentinelArrived resolved to a {clip.length}s clip - expected the generated clip (>= 2.5s), not the shorter synthesised fallback");
        }
    }
}
