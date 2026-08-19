using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-447 cause 4: <c>RobotEnemy.TickChase</c>'s old ranged-standoff check
    /// (<c>dist &lt; standoffRange</c>) inverted the movement direction by a full 180 degrees the
    /// instant <c>dist</c> crossed a single number. A Gunner/Bomber sitting exactly at
    /// <c>standoffRange</c> — which is exactly where it settles, since that's the distance it is
    /// steering to hold — alternated advance/retreat every single tick by construction. Replaced with
    /// a band (<see cref="RobotEnemy"/>'s <c>StandoffBackOffFraction</c>/<c>StandoffCloseInFraction</c>):
    /// back off below the inner edge, close in above the outer edge, hold inside it.
    ///
    /// EditMode only, reflection-driven — same idiom as <c>MV428MeleeReadabilityTests</c>.
    /// </summary>
    public sealed class RobotStandoffBandTests
    {
        private GameObject _playerGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = Vector3.zero;
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewEnemy(EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"Enemy {archetype.Kind}");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(archetype);
            return e;
        }

        private void GiveSight(RobotEnemy e) =>
            e.Sight.Tick(true, _playerGo.transform.position, 0.02f);

        private static void InvokeTickChase(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        [Test]
        public void SittingExactlyAtStandoffRange_HoldsInsteadOfOscillating()
        {
            // The exact repro: a Gunner steering to hold standoffRange settles AT it, tick after tick.
            float standoff = EnemyArchetype.Gunner.StandoffRange;
            var gunner = NewEnemy(EnemyArchetype.Gunner, new Vector3(standoff, 0f, 0f));
            try
            {
                GiveSight(gunner);
                Vector3 start = gunner.transform.position;

                for (int i = 0; i < 30; i++) InvokeTickChase(gunner, 0.016f);

                float drift = Vector3.Distance(start, gunner.transform.position);
                Assert.Less(drift, 0.05f,
                    $"a Gunner sitting exactly at standoffRange drifted {drift:F3} m over 30 ticks — " +
                    "the old hard threshold bang-bangs advance/retreat here every single tick");
            }
            finally
            {
                Object.DestroyImmediate(gunner.gameObject);
            }
        }

        [Test]
        public void SweepingDistAcrossStandoffRange_FlipsMovementSignAtMostOnce()
        {
            float standoff = EnemyArchetype.Gunner.StandoffRange;
            var gunner = NewEnemy(EnemyArchetype.Gunner, new Vector3(standoff * 2f, 0f, 0f));
            try
            {
                GiveSight(gunner);

                int signFlips = 0;
                int? lastSign = null;

                // Sweep distance down from well outside the band to well inside melee range, one
                // steering decision at a time — "slowly", the ticket's own wording.
                for (float dist = standoff * 2f; dist >= 0.5f; dist -= 0.02f)
                {
                    gunner.transform.position = new Vector3(dist, 0f, 0f);
                    Vector3 before = gunner.transform.position;

                    InvokeTickChase(gunner, 0.016f);

                    Vector3 moved = gunner.transform.position - before;
                    if (moved.sqrMagnitude < 1e-8f) continue; // held this tick — no sign to compare

                    int sign = moved.x < 0f ? -1 : 1; // negative x = toward the player at the origin
                    if (lastSign.HasValue && sign != lastSign.Value) signFlips++;
                    lastSign = sign;
                }

                Assert.LessOrEqual(signFlips, 1,
                    $"movement direction flipped sign {signFlips} times sweeping across standoffRange — " +
                    "a hard threshold flips every tick it straddles; a band flips at most once each way");
            }
            finally
            {
                Object.DestroyImmediate(gunner.gameObject);
            }
        }

        [Test]
        public void InsideTheBand_FacesThePlayerWithoutMoving()
        {
            float standoff = EnemyArchetype.Bomber.StandoffRange;
            var bomber = NewEnemy(EnemyArchetype.Bomber, new Vector3(0f, 0f, standoff));
            try
            {
                GiveSight(bomber);
                Vector3 before = bomber.transform.position;

                // RotateToward caps the turn rate (MV-434), so facing the player from a standing start
                // takes several ticks — the position must stay put across every one of them, not just
                // the first, while the facing catches up.
                for (int i = 0; i < 60; i++)
                {
                    InvokeTickChase(bomber, 0.016f);
                    Assert.AreEqual(before, bomber.transform.position, $"tick {i}: moved while holding in the band");
                }

                Vector3 toPlayer = (_playerGo.transform.position - bomber.transform.position).normalized;
                Assert.Greater(Vector3.Dot(bomber.transform.forward, toPlayer), 0.9f,
                    "must end up facing the player while holding, not frozen at whatever angle it arrived at");
            }
            finally
            {
                Object.DestroyImmediate(bomber.gameObject);
            }
        }
    }
}
