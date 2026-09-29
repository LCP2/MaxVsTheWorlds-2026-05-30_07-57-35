using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1012 — World 3's UNDERTOW was never actually attached to Max: <see cref="PlayerController.Awake"/>
    /// self-attached <see cref="PlayerAbilities"/>, <see cref="MaxWorlds.Upgrades.ShoulderRack"/> and
    /// <see cref="PulseLaser"/> but never <see cref="Undertow"/>, so <see cref="WaterBlaster"/> (baked once
    /// into the scene, gated only on "not Lppe") kept firing the RCDA stream straight through World 3's
    /// morph. This proves the RESOLVED firing behaviour off a player built through the real entry point —
    /// <see cref="PlayerController.Awake"/>, invoked the same reflection-idiom every other EditMode test in
    /// this project uses to trigger Unity lifecycle methods that don't run for AddComponent outside Play
    /// mode (see <c>MV503StuckDiagnosticTests</c>) — never a hand-set field standing in for the self-attach
    /// or the aim-source fallback this ticket also adds.
    ///
    /// Also covers <see cref="CavitationImplosion.Apply"/>'s new line-of-sight gate (ticket item 4): a
    /// robot behind solid cover takes no damage from the implosion; an unobstructed robot at the same
    /// distance does.
    ///
    /// Fails on base commit 4436916 (today, before this ticket): (1) <c>player.GetComponent&lt;Undertow&gt;()</c>
    /// is null — <c>PlayerController.Awake</c> never attaches one; (2) even attached directly, <c>Undertow</c>
    /// has no <c>ActivePrimary</c> gate, so it keeps emitting under World 2's LPPE morph; (3) <c>WaterBlaster</c>
    /// still emits under World 3's UNDERTOW morph (its carve-out comment says so explicitly); (4)
    /// <c>CavitationImplosion.Apply</c> has no <c>LineOfSight.Clear</c> check, so the cover-blocked robot
    /// takes full implosion damage exactly like the unobstructed one.
    /// </summary>
    public sealed class MV1012UndertowAttachAndCoverGateTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowUpdate =
            typeof(Undertow).GetMethod("Update", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterUpdate =
            typeof(WaterBlaster).GetMethod("Update", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyHealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);

        private GameObject _playerGo;
        private GameObject _wbGo;
        private System.Collections.Generic.List<GameObject> _spawned;

        [SetUp]
        public void SetUp()
        {
            _spawned = new System.Collections.Generic.List<GameObject>();
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_wbGo != null) Object.DestroyImmediate(_wbGo);
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private RobotEnemy NewRobot(string name, Vector3 position)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance).SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            RobotEnemyHealthField.SetValue(robot, 100f); // full HP, well clear of the Override conversion threshold
            return robot;
        }

        [Test]
        public void UndertowSelfAttachesAndGatesOnActivePrimary_AndCavitationImplosionRespectsCover()
        {
            // --- AC1/AC3: PlayerController.Awake -- the real entry point -- attaches a live Undertow.
            _playerGo = new GameObject("MV1012 Player", typeof(CharacterController), typeof(PlayerController));
            var player = _playerGo.GetComponent<PlayerController>();
            PlayerControllerAwake.Invoke(player, null);

            var undertow = _playerGo.GetComponent<Undertow>();
            Assert.IsNotNull(undertow,
                "PlayerController.Awake must self-attach a live Undertow the same way it self-attaches PulseLaser");

            // Undertow's own Awake doesn't run for AddComponent outside Play mode either -- invoke it
            // directly, same as every other self-attached weapon's test in this project. This also
            // resolves its aimSource fallback to the real PlayerController just attached above (ticket
            // item 2) -- never hand-set.
            UndertowAwake.Invoke(undertow, null);

            // --- AC1: World 3's primary is UNDERTOW -- Undertow emits when firing is requested, RCDA doesn't.
            WeaponSystemState.ApplyWeaponCoreMorph(2);

            // DevMode.AutoFire forces IsFiring true the same way PressKitDirector's filming rig does,
            // sidestepping the real Input System (this project bans PlayMode) while still exercising the
            // real self-attached aimSource resolved just above.
            DevMode.Enabled = true;
            DevMode.AutoFire = true;
            UndertowUpdate.Invoke(undertow, null);
            Assert.IsTrue(undertow.IsEmitting,
                "Undertow must be emitting once World 3's primary is active and firing is requested");

            _wbGo = new GameObject("MV1012 WaterBlaster");
            var blaster = _wbGo.AddComponent<WaterBlaster>();
            WaterBlasterAwake.Invoke(blaster, null);
            blaster.SetFiring(true);
            WaterBlasterUpdate.Invoke(blaster, null);
            Assert.IsFalse(blaster.IsEmitting,
                "the RCDA hose must stand down once World 3's UNDERTOW is the active primary -- the old " +
                "carve-out gated WaterBlaster on \"not Lppe\" only, so it kept streaming under Undertow too");

            // --- AC1 continued: World 2's primary is the LPPE -- Undertow must emit nothing.
            WeaponSystemState.ApplyWeaponCoreMorph(1);
            UndertowUpdate.Invoke(undertow, null);
            Assert.IsFalse(undertow.IsEmitting,
                "Undertow must emit nothing once World 2's LPPE is the active primary instead");

            // --- AC2/item 4: CavitationImplosion.Apply skips a robot with no line of sight from the impact.
            Vector3 impact = new Vector3(0f, 0f, 400f); // clear of every other fixture's coordinates
            RobotEnemy blocked = NewRobot("MV1012-Blocked", impact + new Vector3(-2f, 0f, 0f));
            RobotEnemy clear = NewRobot("MV1012-Clear", impact + new Vector3(2f, 0f, 0f));

            var coverGo = new GameObject("MV1012-Cover");
            _spawned.Add(coverGo);
            coverGo.transform.position = impact + new Vector3(-1f, 0f, 0f); // squarely between impact and "blocked"
            var coverCollider = coverGo.AddComponent<BoxCollider>();
            coverCollider.size = new Vector3(0.5f, 2f, 2f);
            CoverLayer.Assign(coverGo);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

            CavitationImplosion.Apply(impact, damage: 12f,
                pullRadius: Undertow.DefaultPullRadius, pullDistance: Undertow.DefaultPullDistance,
                staggerSeconds: Undertow.DefaultStaggerSeconds);

            Assert.AreEqual(100f, blocked.HealthCurrent, 0.01f,
                "a robot behind solid cover from the implosion point must take no damage");
            Assert.IsFalse(blocked.IsConverted, "a robot the implosion can't even see must not convert either");
            Assert.Less(clear.HealthCurrent, 100f,
                "an unobstructed robot at the same distance from the implosion point must take damage");
        }
    }
}
