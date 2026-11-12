using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1344 AC1 — ORBITAL's semi-spiral light-missile battery, driven with its public
    /// <see cref="OrbitalBattery.Tick"/>/<see cref="OrbitalMissile.Tick"/> seams against a stationary Max.
    /// Fails on the base commit (none of OrbitalBattery/OrbitalMissile/OrbitalTuning exist there).
    /// </summary>
    public sealed class MV1344OrbitalMissileTests
    {
        private sealed class StubMax : MonoBehaviour, IDamageable
        {
            public int Hits;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => Hits++;
        }

        private GameObject _max;

        [TearDown]
        public void TearDown()
        {
            DevTuning.Reset();
            OrbitalMissile.DestroyPoolForTests();
            if (_max != null) Object.DestroyImmediate(_max);
        }

        [Test]
        public void DefaultOrbital_AgainstAStationaryMax_KeepsThreeToFourMissilesInTheAir_NeverFiresStraightAtHim_AndEveryMissileEndsByItsLife()
        {
            DevTuning.Reset();
            _max = new GameObject("Max (stub)");
            _max.AddComponent<StubMax>();
            _max.transform.position = new Vector3(9f, 0f, 0f);

            var battery = new OrbitalBattery();
            var seen = new HashSet<OrbitalMissile>();
            var maxAge = new Dictionary<OrbitalMissile, float>();
            int maxInFlight = 0;
            int inFlightAt7 = -1;
            float worstLaunchOffsetDeg = 180f;

            const float dt = 0.02f;
            int steps = Mathf.RoundToInt(12f / dt);
            for (int i = 1; i <= steps; i++)
            {
                float t = i * dt;
                battery.Tick(dt, Vector3.zero, _max.transform, 0);

                var flying = new List<OrbitalMissile>(battery.Live);
                foreach (OrbitalMissile m in flying)
                {
                    if (seen.Add(m))
                    {
                        float off = Vector3.Angle(m.LaunchHeading, Vector3.right);   // Max sits along +X
                        if (off < worstLaunchOffsetDeg) worstLaunchOffsetDeg = off;
                    }
                    m.Tick(dt);
                    maxAge[m] = Mathf.Max(maxAge.TryGetValue(m, out float a) ? a : 0f, m.Age);
                }

                maxInFlight = Mathf.Max(maxInFlight, battery.InFlight);
                if (Mathf.Abs(t - 7f) < dt * 0.5f) inFlightAt7 = battery.InFlight;
            }

            Assert.That(seen.Count, Is.GreaterThan(4), "it kept firing across the 12 s");
            Assert.That(maxInFlight, Is.LessThanOrEqualTo(4), "never more than 4 in flight");
            Assert.That(inFlightAt7, Is.GreaterThanOrEqualTo(3), "at least 3 in flight at t = 7 s");
            Assert.That(worstLaunchOffsetDeg, Is.GreaterThan(60f), "no launch heading within 60 deg of the line to Max");
            foreach (var kv in maxAge)
                Assert.That(kv.Value, Is.LessThanOrEqualTo(5.05f), "every missile is gone by 5.05 s of age");
        }
    }
}
