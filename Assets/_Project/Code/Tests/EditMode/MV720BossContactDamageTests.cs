using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-720: Lee's 2026-09-04 reversal of MV-588's "boss body deals no contact damage" rule —
    /// standing pressed against Big Bermuda now costs Max (or a Sentinel)
    /// <see cref="BossTuning.ContactDamagePerTick"/> on a fixed <see cref="BossTuning.ContactCooldown"/>
    /// cadence, the same rate-limited shape <c>RobotEnemy.TickContactTouch</c> (MV-428) already uses.
    /// Pins that the damage is RATE-LIMITED — a resolved value read off <see cref="FakeDamageable.Hits"/>/
    /// <see cref="FakeDamageable.Health"/> — not merely present (Testing Policy Rule 3) and not an
    /// authored constant (Rule 2): many small ticks under the cooldown must land nothing, and crossing
    /// the cooldown boundary must land EXACTLY one tick's worth, not more.
    ///
    /// EditMode only, reflection-driven (repo convention — this worker never authors PlayMode tests):
    /// <c>Awake</c> and the private <c>TickContactDamage</c> never run from a live Update loop outside
    /// Play mode, same idiom as <c>MV590BossWallSteeringTests</c>/<c>MV428MeleeReadabilityTests</c>.
    /// </summary>
    public sealed class MV720BossContactDamageTests
    {
        private sealed class FakeDamageable : MonoBehaviour, IDamageable
        {
            public float Health = 500f; // Max's full HP, per the ticket's own worked example
            public int Hits;
            public bool IsAlive => Health > 0f;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info)
            {
                if (!DamageRules.Applies(info.Attacker, Team)) return;
                Hits++;
                Health -= info.Amount;
            }
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static readonly MethodInfo TickContactDamageMethod =
            typeof(BigBermudaBoss).GetMethod("TickContactDamage", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void InvokeTickContactDamage(BigBermudaBoss boss, float dt) =>
            TickContactDamageMethod.Invoke(boss, new object[] { dt });

        private GameObject _playerGo;
        private FakeDamageable _player;
        private GameObject _bossGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _player = _playerGo.AddComponent<FakeDamageable>();
        }

        [TearDown]
        public void TearDown()
        {
            DevTuning.Reset();
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        [Test]
        public void BossInContactWithMax_DamagesOnACooldown_NotEveryFrame()
        {
            // Well within the boss's contact reach (its own collider radius + Max's) for the whole test.
            _playerGo.transform.position = Vector3.zero;
            _bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = _bossGo.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            _bossGo.transform.position = new Vector3(0.3f, 0f, 0f);
            var boss = _bossGo.AddComponent<BigBermudaBoss>();
            InvokeAwake(boss);

            // Cooldown starts FULL (MV-720's own "no free first hit" convention, same as
            // RobotEnemy's) -- the very first contact tick must not deal damage.
            InvokeTickContactDamage(boss, 0.02f);
            Assert.AreEqual(0, _player.Hits, "must not land a free hit before the cooldown elapses");

            // Many small ticks, well under BossTuning.ContactCooldown (1.0s) in total -- still zero hits.
            for (int i = 0; i < 10; i++) InvokeTickContactDamage(boss, 0.02f); // +0.20s (0.22s elapsed)
            Assert.AreEqual(0, _player.Hits, "the cooldown must gate every one of these ticks, not just the first");

            // Cross the cooldown boundary.
            InvokeTickContactDamage(boss, 1.0f);
            Assert.AreEqual(1, _player.Hits, "exactly one hit once the cooldown elapses");
            Assert.AreEqual(500f - BossTuning.ContactDamagePerTick, _player.Health, 1e-3f);

            // Immediately after: must not double-hit on the very next frame.
            InvokeTickContactDamage(boss, 0.02f);
            Assert.AreEqual(1, _player.Hits, "must not fire twice for one cooldown elapse -- not per frame");
        }
    }
}
