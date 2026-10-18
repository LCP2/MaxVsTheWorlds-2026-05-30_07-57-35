using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1238 AC1: a Tractor 8 m from Max in open floor, clear sight, pulls him 3.0 m ± 0.2 toward
    /// itself over one pull (2.5 m/s for 1.2 s, via <see cref="PlayerController.ApplyForcedMove"/>,
    /// MV-1222) — re-applied every tick (<see cref="RobotEnemy.TickPull"/>), so a solid cover piece
    /// placed on <see cref="CoverLayer"/> between them before the pull ever ticks snaps it at once:
    /// Max is not moved at all. Both assertions read RESOLVED values (MV-465 Tier 2): Max's real
    /// position, driven through <see cref="PlayerController"/>'s own private per-tick forced-move
    /// helper, same reflection idiom <c>MV1222ForcedMoveTests</c>/<c>MV1228BreakerRamTests</c> already
    /// established — never an authored constant read directly.
    ///
    /// Fails on 697a4f0 (this ticket's own named base): neither <see cref="EnemyKind.Tractor"/> nor
    /// <see cref="EnemyArchetype.Tractor"/> nor <c>RobotEnemy.TickPull</c> exist there, so this test
    /// does not compile on that commit.
    ///
    /// EditMode only, reflection-driven (repo convention): <c>Update()</c> never runs outside Play
    /// mode, so the private Tick* methods and <c>PlayerController</c>'s own private
    /// <c>Awake</c>/<c>TickForcedMove</c> are invoked directly — the same idiom
    /// <c>MV1228BreakerRamTests</c> already established.
    /// </summary>
    public sealed class MV1238TractorPullTests
    {
        private const float Dt = 0.02f;
        private const int PullTicks = 60; // 1.2 s lungeTime / 0.02 s dt — one full pull

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo StateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", NonPublicInstance);
        private static readonly MethodInfo TickChaseMethod =
            typeof(RobotEnemy).GetMethod("TickChase", NonPublicInstance);
        private static readonly MethodInfo TickTelegraphMethod =
            typeof(RobotEnemy).GetMethod("TickTelegraph", NonPublicInstance);
        private static readonly MethodInfo TickLungeMethod =
            typeof(RobotEnemy).GetMethod("TickLunge", NonPublicInstance);
        private static readonly MethodInfo PlayerAwakeMethod =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo TickForcedMoveMethod =
            typeof(PlayerController).GetMethod("TickForcedMove", NonPublicInstance);

        private static RobotEnemy NewTractor(Vector3 position)
        {
            var go = new GameObject("Tractor");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake/OnEnable, so _cc (normally seeded there) has to be stamped by
            // hand before any Tick* movement — same idiom as MV1228BreakerRamTests.NewBreaker.
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Tractor); // stamps stats and re-runs ResetState, which finds the tagged Player
            Physics.SyncTransforms(); // see NewMax's own comment -- same stale-physics-position guard
            return e;
        }

        private static GameObject NewMax(Vector3 position, out PlayerController player)
        {
            var go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController)) { tag = "Player" };
            go.transform.position = position;
            player = go.GetComponent<PlayerController>();
            PlayerAwakeMethod.Invoke(player, null); // Awake is not a reliable AddComponent side effect outside Play mode
            // autoSyncTransforms is off project-wide: a CharacterController's first-ever Move() uses
            // the physics engine's own stale (creation-time) position until a sync runs, not the
            // managed transform.position set just above -- without this, that first SafeMove silently
            // teleports Max from his physics-side origin instead of displacing him from `position`.
            Physics.SyncTransforms();
            return go;
        }

        private static void CommitTelegraphToLunge(RobotEnemy e)
        {
            StateTimerField.SetValue(e, 999f);
            TickTelegraphMethod.Invoke(e, new object[] { Dt });
        }

        /// <summary>Both halves of AC1 in one test (MV-465 Rule 1: at most one new test per ticket) —
        /// the ticket's own AC wording is already one scenario "repeated with cover between them".</summary>
        [Test]
        public void Pull_MovesMaxInTheOpen_AndNeverMovesHimWithCoverBetweenThem()
        {
            PullMovesMaxTowardTheTractorInTheOpen();
            PullNeverMovesMaxWithSolidCoverBetweenThem();
        }

        private void PullMovesMaxTowardTheTractorInTheOpen()
        {
            GameObject maxGo = null, tractorGo = null;
            try
            {
                maxGo = NewMax(new Vector3(8f, 0f, 0f), out PlayerController player);
                RobotEnemy tractor = NewTractor(Vector3.zero);
                tractorGo = tractor.gameObject;

                tractor.Sight.Tick(LineOfSight.Between(tractor.transform, maxGo.transform), maxGo.transform.position, Dt);
                TickChaseMethod.Invoke(tractor, new object[] { Dt });
                Assert.AreEqual(RobotEnemy.State.Telegraph, tractor.Current,
                    "8 m, clear sight, inside the 10 m range — the Tractor must commit to the telegraph");

                CommitTelegraphToLunge(tractor);
                Assert.AreEqual(RobotEnemy.State.Lunge, tractor.Current, "telegraph must commit to the pull");

                Vector3 before = maxGo.transform.position;
                for (int i = 0; i < PullTicks; i++)
                {
                    tractor.Sight.Tick(LineOfSight.Between(tractor.transform, maxGo.transform), maxGo.transform.position, Dt);
                    TickLungeMethod.Invoke(tractor, new object[] { Dt });
                    TickForcedMoveMethod.Invoke(player, new object[] { Dt });
                    Physics.SyncTransforms();
                }

                float moved = before.x - maxGo.transform.position.x; // the Tractor sits at -X from Max
                Assert.AreEqual(3.0f, moved, 0.2f,
                    $"one pull (2.5 m/s for 1.2 s) must move Max 3.0 m +/- 0.2 toward the Tractor, got {moved:0.00} m");
            }
            finally
            {
                if (tractorGo != null) Object.DestroyImmediate(tractorGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                RobotEnemy.ResetRegistry();
            }
        }

        private void PullNeverMovesMaxWithSolidCoverBetweenThem()
        {
            GameObject maxGo = null, tractorGo = null, coverGo = null;
            try
            {
                maxGo = NewMax(new Vector3(8f, 0f, 0f), out PlayerController player);
                RobotEnemy tractor = NewTractor(Vector3.zero);
                tractorGo = tractor.gameObject;

                // Commit to the pull with a clear sight-line first, exactly like the open-floor
                // scenario, then drop a solid cover piece on CoverLayer between them BEFORE the pull
                // ever ticks — the ticket's own "a wall or solid cover ... snaps the line at once"
                // means the very first tick must refuse to move him, not merely slow or cap it.
                tractor.Sight.Tick(LineOfSight.Between(tractor.transform, maxGo.transform), maxGo.transform.position, Dt);
                TickChaseMethod.Invoke(tractor, new object[] { Dt });
                CommitTelegraphToLunge(tractor);
                Assert.AreEqual(RobotEnemy.State.Lunge, tractor.Current, "telegraph must commit to the pull");

                coverGo = new GameObject("Cover", typeof(BoxCollider));
                coverGo.transform.position = new Vector3(4f, 0f, 0f);
                coverGo.transform.localScale = new Vector3(1f, 3f, 6f);
                CoverLayer.Assign(coverGo);
                Physics.SyncTransforms();

                Vector3 before = maxGo.transform.position;
                for (int i = 0; i < PullTicks; i++)
                {
                    tractor.Sight.Tick(LineOfSight.Between(tractor.transform, maxGo.transform), maxGo.transform.position, Dt);
                    TickLungeMethod.Invoke(tractor, new object[] { Dt });
                    TickForcedMoveMethod.Invoke(player, new object[] { Dt });
                    Physics.SyncTransforms();
                }

                float moved = Vector3.Distance(before, maxGo.transform.position);
                Assert.Less(moved, 0.01f,
                    $"a solid cover piece between them must snap the pull at once, but Max moved {moved:0.000} m");
            }
            finally
            {
                if (coverGo != null) Object.DestroyImmediate(coverGo);
                if (tractorGo != null) Object.DestroyImmediate(tractorGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
