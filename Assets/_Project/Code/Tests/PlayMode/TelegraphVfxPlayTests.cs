using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-53 — the boss AoE danger indicator. PlayMode because a DamageZone builds itself at
    /// runtime (and arms on a real clock), which edit mode can't do.
    /// </summary>
    public sealed class TelegraphVfxPlayTests
    {
        // The zone's arm delay is driven by scaled game time (Time.deltaTime), so this test needs
        // a running clock — and the ambient Time.timeScale can't be trusted at test start in this
        // headless runner (see the other PlayMode fixtures that pin it in SetUp for the same
        // reason). Root cause of YT-173: Time.timeScale sat at 0 for the whole run, so the zone
        // never armed and the test raced a warning that would never come.
        [SetUp] public void SetUp() => Time.timeScale = 1f;
        [TearDown] public void TearDown() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator ArmingZone_ShowsADangerRingThatMatchesItsRadius_ThenClearsIt()
        {
            yield return null;   // let TelegraphVfx install itself

            var telegraph = Object.FindFirstObjectByType<TelegraphVfx>();
            Assert.IsNotNull(telegraph, "TelegraphVfx should install itself with no scene wiring");

            var zone = DamageZone.Spawn(
                new Vector3(5f, 0f, 5f), radius: 3f, damage: 5f, life: 1.2f,
                armDelay: 0.6f, color: Color.red);

            yield return null;
            yield return null;

            Assert.IsTrue(zone.IsArming, "the zone should still be arming");
            var ring = FindVisibleRing();
            Assert.IsNotNull(ring, "an arming AoE must show a danger ring — otherwise it is unfair");
            Assert.AreEqual(6f, ring.transform.localScale.x, 0.01f,
                "the indicator must match the real damage radius exactly, or it teaches the player " +
                "the wrong safe distance");

            // Once it arms, the warning has served its purpose and must get out of the way.
            float armDeadline = Time.realtimeSinceStartup + 3f;
            while (zone != null && zone.IsArming && Time.realtimeSinceStartup < armDeadline) yield return null;
            Assert.IsTrue(zone != null && !zone.IsArming,
                "the zone never finished arming within the timeout — can't test the clear");

            // Wait for the actual clear signal rather than assuming a fixed frame count is enough:
            // TelegraphVfx only retires the ring on the LateUpdate pass after it notices the zone
            // stopped arming, so poll for it instead of guessing exactly how many frames that takes.
            GroundRing lingering = FindVisibleRing();
            float clearDeadline = Time.realtimeSinceStartup + 3f;
            while (lingering != null && Time.realtimeSinceStartup < clearDeadline)
            {
                yield return null;
                lingering = FindVisibleRing();
            }

            Assert.IsNull(lingering, "the danger ring must clear once the zone is live");

            if (zone != null) Object.Destroy(zone.gameObject);
        }

        /// <summary>Only TelegraphVfx's own rings. Other systems (the boss shockwave) also use
        /// GroundRing, and picking one of those up would make this test lie about what it's
        /// measuring.</summary>
        private static GroundRing FindVisibleRing()
        {
            var telegraph = Object.FindFirstObjectByType<TelegraphVfx>();
            if (telegraph == null) return null;

            foreach (var r in telegraph.GetComponentsInChildren<GroundRing>(includeInactive: true))
            {
                if (r.Visible) return r;
            }
            return null;
        }
    }
}
