using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Factory-health slider takes effect on a live factory (YT-126): RefreshMax re-reads the
    /// override and retunes the real DestructibleHealth, exactly like the other sliders.
    /// </summary>
    public sealed class DurabilityPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            DevTuning.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MovingTheFactorySliderRetunesALiveHutch()
        {
            DevTuning.Reset();
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var hutch = _go.AddComponent<MowerHutch>();
            yield return null;   // Awake builds _health at the authored 350

            Assert.That(hutch.Normalized, Is.EqualTo(1f).Within(0.001f), "a fresh hutch starts full");

            // Raise the ceiling: the dented amount stays, so the same 350 HP now reads as half.
            DevTuning.FactoryHealth = 700f;
            hutch.RefreshMax();
            yield return null;
            Assert.That(hutch.Normalized, Is.EqualTo(0.5f).Within(0.001f),
                "raising factory health mid-session must give headroom, not top it up");

            // Lower it below current: clamps, so the bar never reads past full.
            DevTuning.FactoryHealth = 200f;
            hutch.RefreshMax();
            yield return null;
            Assert.That(hutch.Normalized, Is.EqualTo(1f).Within(0.001f),
                "lowering the ceiling below current HP must clamp");
        }
    }
}
