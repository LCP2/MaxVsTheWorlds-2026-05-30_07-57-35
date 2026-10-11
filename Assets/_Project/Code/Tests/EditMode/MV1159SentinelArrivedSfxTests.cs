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
    /// Rule 1 fail-first proof (base = current main, test-only): CS0117 'HudSignals' / 'SfxCueLibrary.Cue'
    /// does not contain a definition for 'SentinelArrived' (no production code yet). With the production
    /// code and the generated clip in place, this still FAILS on the clip-length half: the generated
    /// SentinelArrived.wav retains 2.063673s after ElevenLabs's silence-trim (two requests tried, per the
    /// ticket's one-regeneration allowance; see the fix comment) — short of the >= 2.5s this asserts. Left
    /// failing on purpose: the ticket says not to weaken this assertion to fit the asset.
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
