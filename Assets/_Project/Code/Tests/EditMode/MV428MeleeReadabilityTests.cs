using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-428: too many robots lunging at once from every angle reads as noise, not a fight, even
    /// though any one telegraphed dash alone is a fair, dodgeable tell.
    ///
    /// Change 1 — Bruiser, Heavy and Brute lose the lunge entirely ("a wardrobe should not leap")
    /// and instead deal <see cref="EnemyArchetype.TouchDamage"/> on a per-robot cooldown
    /// (<see cref="RobotCompositionTuning.DefaultContactCooldown"/>) while standing in contact
    /// range — see <c>RobotEnemy.TickContactTouch</c>.
    ///
    /// Change 2 — Rusher and Blinker keep the lunge but must hold a <see cref="LungeTokenPool"/>
    /// token to commit to Telegraph/Lunge, capped at
    /// <see cref="RobotCompositionTuning.DefaultLungeTokenCap"/> (2) field-wide — which stands in
    /// for "per area" here because this game only ever has one arena's robots actively fighting at
    /// once (see <see cref="LungeTokenPool"/>'s own doc comment).
    ///
    /// EditMode only, reflection-driven (repo convention — this worker never authors PlayMode
    /// tests): <c>Update()</c> never runs outside Play mode, so the private Tick* methods are
    /// invoked directly and <c>_stateTimer</c> is force-advanced rather than looping real frames,
    /// the same idiom as <c>MV363DormantRobotTests</c>.
    /// </summary>
    public sealed class MV428MeleeReadabilityTests
    {
        private GameObject _playerGo;
        private FakePlayer _player;

        private sealed class FakePlayer : MonoBehaviour, IDamageable
        {
            public float Health = 200f; // Max's HP, per the ticket's own worked example
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

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _player = _playerGo.AddComponent<FakePlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();
            DevTuning.Reset();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        // ------------------------------------------------------------------ helpers

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewEnemy(EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"Enemy {archetype.Kind}");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake/OnEnable (same note as EnemyFriendlyFireTests.NewEnemy), so
            // _cc — normally seeded there — has to be stamped by hand before TickChase's movement can
            // call CharacterControllerMotion.SafeMove on it.
            CcField.SetValue(e, cc);
            e.Apply(archetype); // stamps stats and re-runs ResetState, which finds the tagged Player
            return e;
        }

        /// <summary>Gives a robot sight of the player right now — <see cref="RobotEnemy.ResetState"/>
        /// only seeds a memory trail (<c>Perception.Spawn</c>), never live sight, and nothing else
        /// calls <c>Perception.Tick</c> while <c>Update()</c> is never invoked in EditMode.</summary>
        private void GiveSight(RobotEnemy e) => e.Sight.Tick(true, _player.transform.position, 0.02f);

        private static void InvokeTickChase(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickTelegraph(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickTelegraph", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickLunge(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickLunge", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void SetStateTimer(RobotEnemy e, float value) =>
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(e, value);

        // ------------------------------------------------------------------ Change 1

        [Test]
        public void BruiserHeavyBrute_NeverEnterLunge_RusherAndBlinkerStillDo()
        {
            var bruiser = NewEnemy(EnemyArchetype.Bruiser, new Vector3(1f, 0f, 0f));
            var heavy = NewEnemy(EnemyArchetype.Heavy, new Vector3(1f, 0f, 0f));
            var brute = NewEnemy(EnemyArchetype.Brute, new Vector3(1f, 0f, 0f));
            var rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(1f, 0f, 0f));
            var blinker = NewEnemy(EnemyArchetype.Blinker, new Vector3(1f, 0f, 0f));
            var all = new[] { bruiser, heavy, brute, rusher, blinker };
            try
            {
                foreach (var e in all) GiveSight(e);
                foreach (var e in all) InvokeTickChase(e, 0.02f);

                // Only the two token-holding kinds may have wound up at all.
                Assert.AreEqual(RobotEnemy.State.Chase, bruiser.Current, "Bruiser must never telegraph (MV-428)");
                Assert.AreEqual(RobotEnemy.State.Chase, heavy.Current, "Heavy must never telegraph (MV-428)");
                Assert.AreEqual(RobotEnemy.State.Chase, brute.Current, "Brute must never telegraph (MV-428)");
                Assert.AreEqual(RobotEnemy.State.Telegraph, rusher.Current, "Rusher must still telegraph");
                Assert.AreEqual(RobotEnemy.State.Telegraph, blinker.Current, "Blinker must still telegraph");

                // Push the two lungers all the way through, and re-drive the non-lungers several more
                // ticks — Lunge must stay permanently unreachable for them, not just on frame one.
                SetStateTimer(rusher, 999f);
                InvokeTickTelegraph(rusher, 0.02f);
                SetStateTimer(blinker, 999f);
                InvokeTickTelegraph(blinker, 0.02f);

                for (int i = 0; i < 5; i++)
                {
                    InvokeTickChase(bruiser, 0.02f);
                    InvokeTickChase(heavy, 0.02f);
                    InvokeTickChase(brute, 0.02f);
                }

                Assert.AreNotEqual(RobotEnemy.State.Lunge, bruiser.Current, "Bruiser must never lunge (MV-428)");
                Assert.AreNotEqual(RobotEnemy.State.Lunge, heavy.Current, "Heavy must never lunge (MV-428)");
                Assert.AreNotEqual(RobotEnemy.State.Lunge, brute.Current, "Brute must never lunge (MV-428)");
                Assert.AreEqual(RobotEnemy.State.Lunge, rusher.Current, "Rusher must still reach Lunge");
                Assert.AreEqual(RobotEnemy.State.Lunge, blinker.Current, "Blinker must still reach Lunge");
            }
            finally
            {
                foreach (var e in all) Object.DestroyImmediate(e.gameObject);
            }
        }

        [Test]
        public void BruiserInContact_DamagesOnACooldown_NotEveryFrame()
        {
            // Well within the Bruiser's 1.4 m contact radius for the whole test.
            var bruiser = NewEnemy(EnemyArchetype.Bruiser, new Vector3(0.5f, 0f, 0f));
            try
            {
                GiveSight(bruiser);

                // Cooldown starts FULL (same "no free first hit" convention as the Blinker's teleport
                // timer) — the very first contact tick must not deal damage.
                InvokeTickChase(bruiser, 0.02f);
                Assert.AreEqual(0, _player.Hits, "must not land a free hit before the cooldown elapses");

                // Many small ticks, well under the 1 s default cooldown in total — still zero hits.
                for (int i = 0; i < 10; i++) InvokeTickChase(bruiser, 0.02f); // +0.20s (0.22s elapsed)
                Assert.AreEqual(0, _player.Hits, "the cooldown must gate every one of these ticks, not just the first");

                // Cross the cooldown boundary.
                InvokeTickChase(bruiser, 1.0f);
                Assert.AreEqual(1, _player.Hits, "exactly one hit once the cooldown elapses");
                Assert.AreEqual(200f - EnemyArchetype.Bruiser.TouchDamage, _player.Health, 1e-3f);

                // Immediately after: must not double-hit on the very next frame.
                InvokeTickChase(bruiser, 0.02f);
                Assert.AreEqual(1, _player.Hits, "must not fire twice for one cooldown elapse — not per frame");
            }
            finally
            {
                Object.DestroyImmediate(bruiser.gameObject);
            }
        }

        // ------------------------------------------------------------------ Change 2

        [Test]
        public void EightLungingRobots_AtMostTwoAreInTelegraphOrLunge_OnAnyFrame()
        {
            var rushers = new RobotEnemy[8];
            for (int i = 0; i < 8; i++)
                rushers[i] = NewEnemy(EnemyArchetype.Rusher, new Vector3(1f + i * 0.01f, 0f, 0f));
            try
            {
                foreach (var e in rushers) GiveSight(e);
                foreach (var e in rushers) InvokeTickChase(e, 0.02f);

                int committed = 0;
                foreach (var e in rushers)
                    if (e.Current == RobotEnemy.State.Telegraph || e.Current == RobotEnemy.State.Lunge)
                        committed++;

                Assert.LessOrEqual(committed, 2, "at most the token cap may be mid-attack on any frame");
                Assert.AreEqual(2, committed, "the default cap is 2, and 8 robots all wanting a token must fill it exactly");
                Assert.AreEqual(2, LungeTokenPool.Held);
            }
            finally
            {
                foreach (var e in rushers) Object.DestroyImmediate(e.gameObject);
            }
        }

        [Test]
        public void AttackToken_IsReleasedOnRecover_AndOnDeath_SoItCannotDeadlockOrLeak()
        {
            var a = NewEnemy(EnemyArchetype.Rusher, new Vector3(1f, 0f, 0f));
            var b = NewEnemy(EnemyArchetype.Rusher, new Vector3(1f, 0f, 0.01f));
            var c = NewEnemy(EnemyArchetype.Rusher, new Vector3(1f, 0f, 0.02f));
            try
            {
                GiveSight(a); GiveSight(b); GiveSight(c);

                InvokeTickChase(a, 0.02f);
                InvokeTickChase(b, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Telegraph, a.Current);
                Assert.AreEqual(RobotEnemy.State.Telegraph, b.Current);
                Assert.AreEqual(2, LungeTokenPool.Held, "both tokens taken");

                InvokeTickChase(c, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Chase, c.Current,
                    "no token free — c must keep closing/pressuring instead of committing");

                // Push A all the way to Recover — its token must come back.
                SetStateTimer(a, 999f);
                InvokeTickTelegraph(a, 0.02f); // -> Lunge
                SetStateTimer(a, 999f);
                InvokeTickLunge(a, 0.02f);     // -> Recover
                Assert.AreEqual(RobotEnemy.State.Recover, a.Current);
                Assert.AreEqual(1, LungeTokenPool.Held, "Recover must hand the token back");

                // C can now take the freed token.
                InvokeTickChase(c, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Telegraph, c.Current, "the freed token must reach another robot");
                Assert.AreEqual(2, LungeTokenPool.Held);

                // Kill B mid-Telegraph — death must ALSO hand its token back, not leak it, since B
                // never reaches Recover.
                b.TakeDamage(new DamageInfo(9999f, b.transform.position, Vector3.forward, Team.Player));
                Assert.IsFalse(b.IsAlive);
                Assert.AreEqual(1, LungeTokenPool.Held, "death mid-attack must not leak the token");
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
                Object.DestroyImmediate(c.gameObject);
            }
        }
    }
}
