using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-362's proximity-based aggro rule: "Robots attack a sentinel when it blocks them or is the
    /// nearest target — they must NOT always prefer sentinels over Max, or the player just builds a
    /// distraction and walks past every fight."
    /// </summary>
    public sealed class SentinelTargetingTests
    {
        [Test]
        public void EngagesTheSentinelWhenItIsCloserThanThePlayerAndInRange()
        {
            Assert.That(SentinelTargeting.ShouldEngageSentinel(
                distanceToPlayer: 8f, distanceToSentinel: 2f, aggroRadius: 10f), Is.True);
        }

        [Test]
        public void NeverPrefersASentinelThatIsFartherThanThePlayer()
        {
            Assert.That(SentinelTargeting.ShouldEngageSentinel(
                distanceToPlayer: 3f, distanceToSentinel: 6f, aggroRadius: 10f), Is.False,
                "must not always prefer sentinels over Max — only when strictly closer");
        }

        [Test]
        public void IgnoresASentinelOutsideTheAggroRadiusEvenIfCloserThanThePlayer()
        {
            Assert.That(SentinelTargeting.ShouldEngageSentinel(
                distanceToPlayer: 40f, distanceToSentinel: 15f, aggroRadius: 10f), Is.False,
                "a distant, off-fight sentinel must never steal aggro from a robot chasing Max elsewhere");
        }

        [Test]
        public void NearestReturnsNullWhenNoSentinelsAreDeployed()
        {
            Sentinel.ResetRegistry();
            Assert.IsNull(SentinelTargeting.Nearest(Vector3.zero));
        }

        [Test]
        public void NearestPicksTheClosestLivingSentinel()
        {
            Sentinel.ResetRegistry();
            var near = new GameObject("Near").AddComponent<WallSentinel>();
            var far = new GameObject("Far").AddComponent<WallSentinel>();
            try
            {
                near.Init(new Vector3(1f, 0f, 0f), Quaternion.identity, 200f);
                far.Init(new Vector3(20f, 0f, 0f), Quaternion.identity, 200f);

                Assert.That(SentinelTargeting.Nearest(Vector3.zero), Is.SameAs(near));
            }
            finally
            {
                Object.DestroyImmediate(near.gameObject);
                Object.DestroyImmediate(far.gameObject);
            }
        }

        [Test]
        public void NearestSkipsADeadSentinel()
        {
            Sentinel.ResetRegistry();
            var near = new GameObject("Near").AddComponent<WallSentinel>();
            var far = new GameObject("Far").AddComponent<WallSentinel>();
            try
            {
                near.Init(new Vector3(1f, 0f, 0f), Quaternion.identity, 10f);
                far.Init(new Vector3(20f, 0f, 0f), Quaternion.identity, 200f);

                near.TakeDamage(new MaxWorlds.Core.DamageInfo(10f, Vector3.zero, Vector3.forward, MaxWorlds.Core.Team.Enemy));
                Assert.That(near.IsAlive, Is.False);

                Assert.That(SentinelTargeting.Nearest(Vector3.zero), Is.SameAs(far));
            }
            finally
            {
                // near destroyed itself via Die(); only far needs manual cleanup.
                Object.DestroyImmediate(far.gameObject);
            }
        }
    }
}
