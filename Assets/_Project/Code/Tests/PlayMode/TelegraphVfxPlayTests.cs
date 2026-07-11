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
            float deadline = Time.realtimeSinceStartup + 3f;
            while (zone != null && zone.IsArming && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            yield return null;

            Assert.IsNull(FindVisibleRing(), "the danger ring must clear once the zone is live");

            if (zone != null) Object.Destroy(zone.gameObject);
        }

        private static GroundRing FindVisibleRing()
        {
            foreach (var r in Object.FindObjectsByType<GroundRing>(FindObjectsSortMode.None))
            {
                if (r.Visible) return r;
            }
            return null;
        }
    }
}
