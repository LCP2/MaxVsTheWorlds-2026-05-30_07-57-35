using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1371 (the one new test): Max stands still 6 m from the Helmsman in phase 2. Once the slam's tell
    /// has fixed his spot, he steps away; the boss still leaps onto the spot AS IT WAS AT THE TELL (a
    /// resolved position, Tier 2). The shockwave then crosses a Max standing in one of its two 40 degree
    /// gaps (0 damage) and a Max standing 90 degrees round from it, outside both gaps (15 damage).
    ///
    /// Fails on the base commit (HEAD of MV-1370, 64bc655 + this test only): phase 2 has no slam clock, so
    /// the boss never lands on the marked spot.
    /// </summary>
    public sealed class MV1371HelmsmanLeapSlamTests
    {
        private sealed class MaxStub : MonoBehaviour, IDamageable
        {
            public float Taken;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => Taken += info.Amount;
        }

        private const float Dt = 0.05f;

        private static FieldInfo Field(string name) =>
            typeof(HelmsmanBoss).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private static Vector3 Dir(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>One slam against a Max who, after the landing, stands 10 m from it at
        /// <paramref name="bearingFromGap"/> degrees round from a gap. Returns the shockwave damage he took.</summary>
        private static float ShockwaveDamage(float bearingFromGap)
        {
            GameObject bossGo = null, playerGo = null;
            try
            {
                var spot = new Vector3(6f, 0f, 0f);
                playerGo = new GameObject("MV1371 Max") { tag = "Player" };
                playerGo.transform.position = spot;
                var max = playerGo.AddComponent<MaxStub>();

                bossGo = new GameObject("MV1371 Helmsman");
                bossGo.AddComponent<CharacterController>();
                var boss = bossGo.AddComponent<HelmsmanBoss>();
                typeof(HelmsmanBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(boss, null);
                Field("_phase").SetValue(boss, System.Enum.Parse(
                    typeof(HelmsmanBoss).GetNestedType("Phase", BindingFlags.NonPublic), "Walking"));

                FieldInfo cycle = Field("_slamCycle"), gapField = Field("_waveGapYaw");
                Assert.IsNotNull(cycle, "phase 2 has no slam clock");
                Assert.IsNotNull(gapField, "the shockwave has no gaps");
                cycle.SetValue(boss, 5.99f);   // the ticket's 6 s cadence, one tick before the tell

                boss.Tick(Dt);                                           // the tell fixes Max's spot...
                playerGo.transform.position = new Vector3(-30f, 0f, 0f); // ...and he steps away from it

                bool landed = false;
                for (int i = 0; i < 80 && !landed; i++)
                {
                    boss.Tick(Dt);
                    Vector3 to = boss.transform.position - spot; to.y = 0f;
                    landed = to.magnitude < 0.1f;
                }
                Assert.IsTrue(landed, "the boss must leap onto the spot Max stood on at the tell (6,0,0); it is at "
                    + boss.transform.position);

                float gap = (float)gapField.GetValue(boss);
                playerGo.transform.position = spot + Dir(gap + bearingFromGap) * 10f;
                max.Taken = 0f;
                for (int i = 0; i < 40; i++) boss.Tick(Dt);              // the 1.2 s shockwave (12 m) runs out
                return max.Taken;
            }
            finally
            {
                if (bossGo != null) Object.DestroyImmediate(bossGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                BossCensus.Reset();
            }
        }

        [Test]
        public void Slam_LandsOnTheTellSpot_AndShockwaveSparesTheGapsButHitsOutsideThem()
        {
            Assert.AreEqual(0f, ShockwaveDamage(0f), 0.001f, "Max standing in a gap must take no shockwave damage");
            Assert.AreEqual(15f, ShockwaveDamage(90f), 0.001f,
                "Max standing outside both gaps takes the shockwave's 15, once");
        }
    }
}
