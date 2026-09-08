using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-707 AC1: the Cart Charger's telegraphed straight-line charge locks its direction at the end
    /// of the telegraph and never re-aims — sidestepping, not out-running it, is the counterplay — and
    /// ramming a wall/cover/deck-column stuns it at a damage-taken penalty rather than just bouncing
    /// off like an ordinary wall contact. All assertions read RESOLVED values: the archetype's own
    /// <see cref="EnemyArchetype.Charger"/> stats aren't asserted directly, only what the state machine
    /// actually does with them — the committed direction (a private field, read via reflection, never a
    /// hardcoded Vector3), the travelled position, and <see cref="RobotEnemy.IsStunned"/>/
    /// <see cref="RobotEnemy.StunTimeRemaining"/>/<see cref="RobotEnemy.DamageTakenMultiplier"/> (MV-465
    /// Tier 2).
    ///
    /// Fails on 5314c85 (MV-701's merge commit, the ticket's own named base): neither
    /// <see cref="EnemyKind.Charger"/> nor <see cref="EnemyArchetype.Charger"/> exist there, so this
    /// test does not compile on that commit.
    ///
    /// EditMode only, reflection-driven (repo convention): <c>Update()</c> never runs outside Play
    /// mode, so the private Tick* methods and <c>HandleWallContact</c> are invoked directly — the same
    /// idiom <c>MV428MeleeReadabilityTests</c>/<c>MV586ForceFieldRamTests</c> already established
    /// (<c>ControllerColliderHit</c> has no public constructor, so a wall/cover hit is driven through
    /// <c>HandleWallContact</c> with a bare <see cref="Collider"/> instead of a real physics collision).
    /// </summary>
    public sealed class MV707CartChargerTests
    {
        private GameObject _playerGo;
        private GameObject _coverGo;
        private RobotEnemy _charger;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo StateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LungeDirField =
            typeof(RobotEnemy).GetField("_lungeDir", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickChaseMethod =
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickTelegraphMethod =
            typeof(RobotEnemy).GetMethod("TickTelegraph", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickLungeMethod =
            typeof(RobotEnemy).GetMethod("TickLunge", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo HandleWallContactMethod =
            typeof(RobotEnemy).GetMethod("HandleWallContact", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewEnemy(in EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"Enemy {archetype.Kind}");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake/OnEnable, so _cc (normally seeded there) has to be stamped by
            // hand before any Tick* movement can call CharacterControllerMotion.SafeMove on it — same
            // idiom as MV428MeleeReadabilityTests.NewEnemy.
            CcField.SetValue(e, cc);
            e.Apply(archetype); // stamps stats and re-runs ResetState, which finds the tagged Player
            return e;
        }

        private void GiveSight(RobotEnemy e, Vector3 playerPos) => e.Sight.Tick(true, playerPos, 0.02f);

        private static void SetStateTimer(RobotEnemy e, float value) =>
            StateTimerField.SetValue(e, value);

        private static Vector3 GetLungeDir(RobotEnemy e) => (Vector3)LungeDirField.GetValue(e);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(8f, 0f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_charger != null) Object.DestroyImmediate(_charger.gameObject);
            if (_coverGo != null) Object.DestroyImmediate(_coverGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void Charge_CommitsToTheTelegraphedDirection_NeverReaims_AndStunsOnAWallHit()
        {
            // Charger at (0,0), Max at (8,0) — 8 m, inside the Charger's own 12 m engage range.
            _charger = NewEnemy(EnemyArchetype.Charger, Vector3.zero);
            GiveSight(_charger, _playerGo.transform.position);

            TickChaseMethod.Invoke(_charger, new object[] { 0.02f });
            Assert.AreEqual(RobotEnemy.State.Telegraph, _charger.Current,
                "within 12 m and in clear sight, the Charger must commit to the telegraph");

            // TickChase's own FaceAndMove already turned AND stepped partway toward Max through the
            // formation approach-point fan every chasing kind steers through
            // (EnemyFormation.ApproachPoint, MV-449) — a real, deliberate spread this ticket's own
            // worked example (a lone Charger, dead ahead) abstracts away. Re-planted dead ahead of Max
            // here — both position and facing — so what TickTelegraph commits to below is the
            // telegraph's OWN "hold facing, lock at commit" behaviour, not an artefact of the approach
            // fan's lateral nudge or how many ticks the capped turn rate needed — both already
            // separately exercised elsewhere, not what this test is about.
            // Disabled around the reposition, same idiom RobotEnemy's own TickTeleport uses — the
            // CharacterController owns its own internal position state and fights a direct transform
            // set on the very next Move() otherwise.
            var cc = (CharacterController)CcField.GetValue(_charger);
            cc.enabled = false;
            _charger.transform.position = Vector3.zero;
            _charger.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            cc.enabled = true;
            // autoSyncTransforms is off project-wide -- the CC's own internal collider otherwise still
            // sees the pre-reset transform on the very next Move(), the same convention
            // WaterBlasterGateDamageTests/PulseLaserTests already follow after a manual reposition.
            Physics.SyncTransforms();

            // Push the telegraph to completion — state-timer forced directly, same idiom as
            // MV428MeleeReadabilityTests. RotateToward's own turn is a no-op here (already facing the
            // live target exactly), so this locks _lungeDir at precisely +X.
            SetStateTimer(_charger, 999f);
            TickTelegraphMethod.Invoke(_charger, new object[] { 0.02f });
            Assert.AreEqual(RobotEnemy.State.Lunge, _charger.Current, "telegraph must commit to the charge");

            Vector3 committedDir = GetLungeDir(_charger);
            Assert.Greater(committedDir.x, 0.999f,
                $"the Charger's committed direction must be +X (Max sat due +X at commit), got {committedDir}");

            // AC1: move Max well off the charge line WHILE the charge is under way.
            _playerGo.transform.position = new Vector3(8f, 0f, 4f);

            // One 0.6667 s tick at the archetype's 9 m/s covers ~6 m of +X travel — far short of Max's
            // new position (now 4 m off the line) so this cannot land a hit, and short of the 12 m cap
            // so the charge is still live afterward, standing at the cover box's own position.
            TickLungeMethod.Invoke(_charger, new object[] { 12f / 9f * 0.5f });

            Assert.AreEqual(RobotEnemy.State.Lunge, _charger.Current,
                "the charge must still be live after travelling only half its 12 m cap");
            Assert.AreEqual(committedDir, GetLungeDir(_charger),
                "moving Max mid-charge must never re-aim the committed direction");
            // Bounded, not exact: CharacterController.Move's own collision resolution can add a little
            // slack to the raw speed*dt distance even with nothing in the scene to hit. The property
            // this AC actually cares about is direction, not odometry — z staying pinned at 0 (tight
            // tolerance; there is nothing to perturb it on this axis) is what proves the charge never
            // re-aimed toward Max's new (8, 0, 4): a re-aim would have pulled z toward +4, not held it.
            Assert.Greater(_charger.transform.position.x, 3f,
                "the charge must have travelled well forward along +X since committing");
            Assert.Less(_charger.transform.position.x, 12f,
                "the charge must not yet have reached its 12 m cap");
            Assert.AreEqual(0f, _charger.transform.position.z, 0.05f,
                "the charge must stay pinned to its committed +X line, never drifting toward Max's new z");

            // Simulate reaching the cover box at (6, 1.5) and ramming it — ControllerColliderHit has no
            // public constructor, so the wall/cover hit is driven directly through HandleWallContact,
            // the same seam MV586ForceFieldRamTests already established for exactly this reason.
            _coverGo = new GameObject("Cover") { layer = 0 };
            var coverCollider = _coverGo.AddComponent<BoxCollider>();
            _coverGo.transform.position = new Vector3(6f, 0f, 1.5f);

            HandleWallContactMethod.Invoke(_charger, new object[] { coverCollider, Vector3.left });

            Assert.AreEqual(RobotEnemy.State.Recover, _charger.Current,
                "ramming a wall/cover must end the charge into Recover, not leave it mid-Lunge");
            Assert.IsTrue(_charger.IsStunned, "ramming a wall/cover must stun the Charger");
            Assert.AreEqual(1.2f, _charger.StunTimeRemaining, 0.05f,
                $"the wall-ram stun should read ~1.2s remaining, got {_charger.StunTimeRemaining:0.000}s");
            Assert.AreEqual(1.5f, _charger.DamageTakenMultiplier, 0.01f,
                $"a Charger stunned by a wall-ram should take 1.5x damage, got {_charger.DamageTakenMultiplier:0.000}x");
        }
    }
}
