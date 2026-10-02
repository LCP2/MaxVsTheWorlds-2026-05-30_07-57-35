using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1064 — World 3's UNDERTOW stream must latch onto the robot it damages (bending to follow it
    /// rather than requiring it to stay inside the lance's own 3-degree acquire cone) and wrap it in
    /// bigger, orange/red coils and crackle instead of the old 3-strand violet/blue hairline look.
    ///
    /// Built off a player through the real entry point (<see cref="PlayerController.Awake"/>) with
    /// World 3's UNDERTOW active, ticked through <c>Undertow.Tick</c>'s own explicit-<c>dt</c> method —
    /// the same reflection-driven, real-entry-point idiom <c>MV1034UndertowStreamTests</c> and
    /// <c>MV1046UndertowBeamSnakesTests</c> already use. Max's own facing is driven through
    /// <see cref="PlayerController.Facing"/> itself (see <c>AimDegreesOffRobot</c>) to an EXACT angle
    /// off the live direction to the robot each tick, so the latch-hold angle check is exercised
    /// deterministically rather than through incidental geometry.
    ///
    /// Fails on caebad0 (the commit before this ticket): <c>Undertow</c> carries no latch at all, so
    /// aiming 20 degrees off a robot (over the lance's 3-degree cone) stops every tick from damaging
    /// it at all — the first damage assertion in the AC1 loop fails on tick 0 — and
    /// <c>Undertow.IsLatched</c>/<c>UndertowVfx.SetLatch</c> do not exist, so this test does not
    /// compile there. It also does not compile against the pre-fix strand/core geometry: there is no
    /// child named <c>UndertowCoil0</c> (no coils existed), and the pre-fix core/strand widths
    /// (0.12m/0.045m) and strand colours (violet/blue, <c>#B46BFF</c>/<c>#5F7BFF</c>) fail AC2's
    /// resolved-value width/colour-family assertions.
    /// </summary>
    public sealed class MV1064UndertowLatchTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick =
            typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo PlayerControllerFacingField =
            typeof(PlayerController).GetField("_facing", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyCcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyHealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);

        private GameObject _playerGo;
        private GameObject _robotGo;

        [SetUp]
        public void SetUp()
        {
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
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
        }

        private static void InvokeTick(Undertow undertow, float dt) =>
            UndertowTick.Invoke(undertow, new object[] { dt });

        /// <summary>Points Max <paramref name="degreesOff"/> away (around world up) from the live
        /// direction to <paramref name="robotGo"/>, through the REAL facing path
        /// (<see cref="PlayerController.Facing"/>) rather than poking <c>transform.rotation</c> directly
        /// — <c>Undertow.Tick</c>'s own aimSource block re-derives <c>transform.rotation</c> from
        /// <c>Facing</c> every single tick (defaulting to <see cref="Vector3.forward"/>, not zero, so it
        /// is never a no-op), which would silently clobber a direct transform write back to whatever
        /// <c>_facing</c> already held.</summary>
        private static void AimDegreesOffRobot(PlayerController player, GameObject robotGo, float degreesOff)
        {
            Vector3 toRobot = (robotGo.transform.position - player.transform.position).normalized;
            Vector3 aimDir = Quaternion.AngleAxis(degreesOff, Vector3.up) * toRobot;
            PlayerControllerFacingField.SetValue(player, aimDir);
        }

        [Test]
        public void StreamLatchesBendsToFollowAndWrapsInEmberCoils_DropsPastMaxAngleOrOnRelease_MV1064()
        {
            // --- Build Max through the real entry point with World 3's UNDERTOW active.
            _playerGo = new GameObject("MV1064 Player", typeof(CharacterController), typeof(PlayerController));
            var player = _playerGo.GetComponent<PlayerController>();
            PlayerControllerAwake.Invoke(player, null);

            var undertow = _playerGo.GetComponent<Undertow>();
            Assert.IsNotNull(undertow, "PlayerController.Awake must self-attach a live Undertow");
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode

            WeaponSystemState.ApplyWorldLoadout(2); // World 3 -> UNDERTOW is the active primary
            DevMode.Enabled = true;
            DevMode.AutoFire = true; // "request fire" without the real Input System (PlayMode is banned)

            Transform coreT = _playerGo.transform.Find("UndertowCore");
            Assert.IsNotNull(coreT, "UndertowVfx.Init must build a child named UndertowCore");
            var core = coreT.GetComponent<LineRenderer>();

            Transform coil0T = _playerGo.transform.Find("UndertowCoil0");
            Assert.IsNotNull(coil0T, "UndertowVfx.Init must build a wrap-coil child named UndertowCoil0 (MV-1064)");
            var coil0 = coil0T.GetComponent<LineRenderer>();

            // --- A robot 6m ahead, dead on-aim so the FIRST tick acquires the latch through the
            // ordinary acquire cone.
            _robotGo = new GameObject("MV1064 Target");
            var cc = _robotGo.AddComponent<CharacterController>();
            var robot = _robotGo.AddComponent<RobotEnemy>();
            RobotEnemyCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            _robotGo.transform.position = _playerGo.transform.position + _playerGo.transform.forward * 6f;
            RobotEnemyHealthField.SetValue(robot, 100000f); // survives every tick in this test
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

            InvokeTick(undertow, 0.1f);
            Assert.IsTrue(undertow.IsLatched, "a tick that damages a robot dead-ahead must latch onto it (MV-1064 spec #1)");
            Assert.AreSame(robot.gameObject, undertow.LatchedTransform.gameObject,
                "the latched target must be the robot this tick actually damaged");

            // --- AC2 (same test, resolved values -- never the authored constants themselves): 5
            // strands, each >= 0.12m wide and orange/red family; the core >= 0.22m wide; the coils are
            // also orange/red family. All read from the LineRenderers UpdateStream/SetLatch actually
            // wrote this tick, not from UndertowVfx's own constants.
            Assert.GreaterOrEqual(core.widthMultiplier, 0.22f, "the core must be >=0.22m wide (MV-1064 spec #7)");

            for (int s = 0; s < 5; s++)
            {
                Transform st = _playerGo.transform.Find($"UndertowStrand{s}");
                Assert.IsNotNull(st, $"UndertowVfx.Init must build strand child UndertowStrand{s} -- 5 strands (MV-1064 spec #8)");
                var strand = st.GetComponent<LineRenderer>();
                Assert.GreaterOrEqual(strand.widthMultiplier, 0.12f, $"strand {s} must be >=0.12m wide");
                AssertEmberFamily(strand.startColor, $"strand {s}");
            }
            Assert.IsNull(_playerGo.transform.Find("UndertowStrand5"), "exactly 5 strands must be built, not 6");

            for (int c = 0; c < 3; c++)
            {
                Transform ct = _playerGo.transform.Find($"UndertowCoil{c}");
                Assert.IsNotNull(ct, $"UndertowVfx.Init must build coil child UndertowCoil{c} -- 3 coils (MV-1064 spec #9)");
                AssertEmberFamily(ct.GetComponent<LineRenderer>().startColor, $"coil {c}");
            }

            // --- AC1: the latch holds for a full 2s hold (20 x 0.1s ticks) while Max's aim sits an
            // EXACT 20 degrees off the robot's own live direction (within the 30-degree latch budget,
            // well past the lance's own 3-degree acquire cone) and the robot walks 2m sideways.
            Vector3 walkStart = _robotGo.transform.position;
            for (int i = 0; i < 20; i++)
            {
                float frac = (i + 1) / 20f;
                _robotGo.transform.position = walkStart + Vector3.right * (2f * frac);
                AimDegreesOffRobot(player, _robotGo, 20f);
                Physics.SyncTransforms();

                float before = robot.HealthCurrent;
                InvokeTick(undertow, 0.1f);
                Assert.Less(robot.HealthCurrent, before,
                    $"tick {i} (t={((i + 1) * 0.1f):0.0}s): the latched robot must take a damage tick every " +
                    "0.1s even 20 degrees off Max's own aim (MV-1064 spec #2)");

                Vector3 lastPoint = core.GetPosition(core.positionCount - 1);
                float off = Vector3.Distance(lastPoint, _robotGo.transform.position);
                Assert.Less(off, 0.3f,
                    $"tick {i}: the beam's last point must stay within 0.3m of the latched robot, was {off:0.000}m off");

                Assert.IsTrue(coil0.enabled, $"tick {i}: the coil renderers must stay enabled while latched");
            }
            Assert.IsTrue(undertow.IsLatched, "the latch must still hold after 2s at 20 degrees off aim");

            // --- Past the 30-degree latch budget: within 0.2s (2 x 0.1s ticks) the latch drops, the
            // coils disable, and the robot takes no further damage (40 degrees is also outside the
            // 3-degree acquire cone, so normal aiming cannot pick it back up either).
            AimDegreesOffRobot(player, _robotGo, 40f);
            Physics.SyncTransforms();
            float healthBeforeDrop = robot.HealthCurrent;

            InvokeTick(undertow, 0.1f);
            InvokeTick(undertow, 0.1f);

            Assert.IsFalse(undertow.IsLatched, "the latch must drop within 0.2s once aim exceeds 30 degrees off the robot (MV-1064 spec #3)");
            Assert.AreEqual(healthBeforeDrop, robot.HealthCurrent, 0.01f,
                "the robot must take no further damage once the latch has dropped");
            Assert.IsFalse(coil0.enabled, "coil renderers must disable once the latch drops (MV-1064 spec #9)");

            // --- Releasing fire also drops the latch (MV-1064 AC1), proven from a fresh re-latch so
            // this assertion can't be mistaken for the angle-drop above.
            AimDegreesOffRobot(player, _robotGo, 0f);
            Physics.SyncTransforms();
            InvokeTick(undertow, 0.1f);
            Assert.IsTrue(undertow.IsLatched, "precondition: re-aiming dead-on must re-latch before testing the fire-release drop");

            DevMode.AutoFire = false;
            InvokeTick(undertow, 0.1f);
            Assert.IsFalse(undertow.IsLatched, "releasing fire must drop the latch");
        }

        /// <summary>MV-1064 AC2: "every strand and coil colour has R > G > B (orange/red family) and
        /// none has B > R".</summary>
        private static void AssertEmberFamily(Color c, string label)
        {
            Assert.Greater(c.r, c.g, $"{label} colour must be orange/red family (R>G), was {c}");
            Assert.Greater(c.g, c.b, $"{label} colour must be orange/red family (G>B), was {c}");
            Assert.LessOrEqual(c.b, c.r, $"{label} colour must not have B>R, was {c}");
        }
    }
}
