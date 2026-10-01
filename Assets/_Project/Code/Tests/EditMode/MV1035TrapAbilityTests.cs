using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1035 — World 3's TRAP ability: a HUD button (shown only once <c>p_trp</c> is owned) drops a
    /// trap at Max's feet; any eligible enemy robot that walks inside its radius is caught, frozen,
    /// untargetable and undamageable, then converted into a permanent ally once the trap is full or 8s
    /// after its first catch. One consolidated test method (MV-465 Rule 1: at most one new test per
    /// ticket) covers the whole loop (AC1) plus the radius boundary and the two-robot fill case (AC2),
    /// entirely through real entry points: <see cref="RigState.AcquireCap"/>/<see cref="RigState.RaiseLevel"/>
    /// (the RIG's own reached/owned gating, not a snapshot shortcut), <see cref="PlayerAbilities.TryDropTrap"/>,
    /// <see cref="RobotTrap.Tick"/> (public for exactly the reason <see cref="RobotEnemy.Tick"/> already
    /// is — so a test can drive an exact, controlled sequence of frames), and <see cref="RobotEnemy.TakeDamage"/>.
    ///
    /// Fails on baa509c (MV-1033, the commit before MV-1034/MV-1035): none of the RIG nodes p_trp/p_tcap/
    /// p_trad exist in <c>rig_board.world3.json</c>, and none of <see cref="PlayerAbilities.TryDropTrap"/>,
    /// <see cref="RobotTrap"/>, <see cref="RobotEnemy.IsTrapCatchable"/>, <see cref="RobotEnemy.BeginTrapHold"/>,
    /// <see cref="RobotEnemy.BeginTrapConversion"/>, <see cref="RobotEnemy.TickTrapConversion"/>, or
    /// <see cref="CharacterSkin.SetAllyOverrideColor"/> exist, so this file does not compile there.
    /// </summary>
    public sealed class MV1035TrapAbilityTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private const float Dt = 1f / 60f;
        private static readonly Color TrapAllyColor = new Color(0.357f, 0.890f, 0.353f); // #5BE35A

        [SetUp]
        [TearDown]
        public void Clear()
        {
            RobotEnemy.ResetRegistry();
            RobotEnemy.ConversionCap = 1;
            RigBoard.UseWorld(0);
            RigState.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
        }

        /// <summary>Same construction idiom as <c>MV1015...Tests.NewRobot</c>: AddComponent doesn't
        /// reliably run Awake/OnEnable outside Play mode, so <c>_cc</c> is stamped by hand and OnEnable
        /// is invoked directly — reflection used only for spawn WIRING, never for driving the ability
        /// logic under test. Also gives the robot a real body renderer + bound CharacterSkin so the
        /// conversion colour assertion reads an actual resolved render colour, not a bare field.</summary>
        private static RobotEnemy NewRobot(string name, Vector3 position, in EnemyArchetype archetype)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(archetype);
            RobotOnEnableMethod.Invoke(e, null);
            go.transform.position = position;

            go.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            go.AddComponent<MeshRenderer>();
            go.AddComponent<CharacterSkin>().Bind(CharacterRole.Robot);
            return e;
        }

        private static RectTransform FindRect(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, NonPublicInstance).Invoke(component, null);

        [Test]
        public void TrapCatchesHoldsFreezesAndConvertsToPermanentAlly_AndGatesByRadius_MV1035()
        {
            RigBoard.UseWorld(2); // World 3 — the only board carrying p_trp/p_tcap/p_trad
            RigState.Reset();

            GameObject maxGo = null, hudGo = null, max2Go = null, floorGo = null;
            RobotEnemy robotA = null, enemyTarget = null, nearRobot = null, farRobot = null,
                fillRobot1 = null, fillRobot2 = null;
            try
            {
                // MV-1061: a real physical floor under the ally's walk, same idiom as
                // MV952RobotFallRecoveryTests — without it, robotA's post-conversion CharacterController
                // (ungrounded in the full-suite's shared EditMode scene, whose ground residue depends on
                // whichever fixture ran immediately before this one) falls instead of walking over to
                // contact-damage enemyTarget, and the "must damage an enemy robot" assertion below goes
                // flaky depending on run order. Covers robotA (2,0,0) through enemyTarget (3,0,0) with
                // margin for the 1.5s walk.
                floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floorGo.transform.position = new Vector3(2.5f, -0.05f, 0f);
                floorGo.transform.localScale = new Vector3(10f, 0.1f, 10f);
                Physics.SyncTransforms();

                maxGo = new GameObject("MV-1035 test Max", typeof(CharacterController)) { tag = "Player" };
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();
                var abilities = maxGo.AddComponent<PlayerAbilities>();
                maxGo.transform.position = Vector3.zero;

                hudGo = new GameObject("HUD");
                var hud = hudGo.AddComponent<HudController>();
                InvokeLifecycle(hud, "Awake");
                InvokeLifecycle(hud, "OnEnable");

                RectTransform trapButton = FindRect(hudGo, "Trap Button");
                Assert.IsNotNull(trapButton, "fixture: the TRAP button must exist in the HUD tree");
                Assert.IsFalse(trapButton.gameObject.activeInHierarchy,
                    "the TRAP button must be hidden while p_trp = 0");

                // Level p_trp = 1, p_tcap = 2 via the RIG (ticket's own test spec) — real draft/spend
                // entry points, not a RestoreSnapshot shortcut.
                Assert.IsTrue(RigState.AcquireCap("p_trp"),
                    "test setup: p_trp must be reachable — its parent p_dmg starts at level 1 in World 3");
                Assert.IsTrue(RigState.RaiseLevel("p_tcap"), "test setup: p_tcap (startLevel 1) must raise to 2");
                Assert.AreEqual(2, RigState.Level("p_tcap"));
                WeaponSystemState.RebuildAcquiredFromRigState(); // fires the same Changed HudController.OnAbilitiesChanged listens to

                Assert.IsTrue(trapButton.gameObject.activeInHierarchy, "the TRAP button must appear once p_trp >= 1");

                // === AC1: tap it, walk one robot in -> held/frozen/undamageable, 8s no-fill timeout
                // starts conversion (capacity is 2, only 1 robot present), +4.5s completes it as a
                // permanent ally that then damages an enemy robot.
                Assert.IsTrue(abilities.TryDropTrap(), "TryDropTrap must succeed once owned, off cooldown, with none down");
                RobotTrap trap = abilities.ActiveTrap;
                Assert.IsNotNull(trap, "fixture: a trap must now be active");

                robotA = NewRobot("Robot A", new Vector3(2f, 0f, 0f), EnemyArchetype.Bruiser); // inside the default 3.0m radius (p_trad level 0)
                Physics.SyncTransforms();

                trap.Tick(Dt);
                Assert.IsTrue(robotA.IsTrapHeld, "a robot inside the radius must be caught on the very next tick");
                Assert.IsFalse(robotA.IsEngageable, "a held robot must not be engageable/targetable");

                float healthBeforeHits = robotA.HealthCurrent;
                robotA.TakeDamage(new DamageInfo(50f, robotA.transform.position, Vector3.up, Team.Player, source: DamageSource.SecondaryWeapon)); // "a rocket"
                robotA.TakeDamage(new DamageInfo(50f, robotA.transform.position, Vector3.up, Team.Player, soak: true, source: DamageSource.PrimaryWeapon)); // "a stream tick"
                Assert.AreEqual(healthBeforeHits, robotA.HealthCurrent, 0f, "a held robot must take 0 damage from anything");

                // Tick to just past the 8s fill timeout (measured from the catch above). Guarded on
                // trap != null throughout (Unity's overloaded null check): the trap despawns itself
                // (DestroyImmediate, in this EditMode test) the instant conversion completes, and
                // ticking a destroyed trap afterward throws — Unity's own Update loop would simply stop
                // calling it, but a raw test loop has to check for itself.
                int framesTo8s = Mathf.CeilToInt(8.05f / Dt);
                for (int i = 0; i < framesTo8s && trap != null; i++) trap.Tick(Dt);
                Assert.IsTrue(trap.IsConverting, "the trap must start converting 8s after its first catch, still short of capacity");
                Assert.IsTrue(robotA.IsTrapConverting);
                Assert.IsFalse(robotA.IsConverted, "must not be converted yet — the 4.5s colour lerp hasn't run");

                // Tick through the 4.5s conversion lerp.
                int framesTo4_5s = Mathf.CeilToInt(4.55f / Dt);
                for (int i = 0; i < framesTo4_5s && trap != null; i++) trap.Tick(Dt);

                Assert.IsTrue(robotA.IsConverted, "the held robot must be a permanent ally once the lerp completes");
                Assert.AreEqual(Team.Player, robotA.Team);
                Assert.IsFalse(robotA.IsTrapHeld, "a converted robot is released, not still held");
                Assert.IsNull(abilities.ActiveTrap, "the trap must despawn once everything it holds has converted");
                Assert.Greater(abilities.TrapCooldownRemaining, 0f, "a successful conversion must start the 20s cooldown");

                Color renderedColor = robotA.GetComponent<CharacterSkin>().BodyColor;
                Assert.AreEqual(TrapAllyColor.r, renderedColor.r, 0.02f, "converted body colour red channel must match #5BE35A within 2%");
                Assert.AreEqual(TrapAllyColor.g, renderedColor.g, 0.02f, "converted body colour green channel must match #5BE35A within 2%");
                Assert.AreEqual(TrapAllyColor.b, renderedColor.b, 0.02f, "converted body colour blue channel must match #5BE35A within 2%");

                enemyTarget = NewRobot("Enemy Target", robotA.transform.position + new Vector3(1f, 0f, 0f), EnemyArchetype.Bruiser);
                Physics.SyncTransforms();
                float enemyHealthBefore = enemyTarget.HealthCurrent;
                for (int i = 0; i < 90; i++) robotA.Tick(Dt); // 1.5s — past contact-damage cooldown, same cadence MV1015 uses
                Assert.Less(enemyTarget.HealthCurrent, enemyHealthBefore, "a converted ally must damage an enemy robot");

                // === AC2 (radius): with p_trad at 4 (radius 3.0 + 0.75*4 = 6.0m), a robot 5.8m away is
                // caught and one at 6.3m is not. Uses a SEPARATE Max/PlayerAbilities so this trap's own
                // 20s cooldown from the conversion above can't block a fresh drop.
                Assert.IsTrue(RigState.AcquireCap("p_trad"));
                RigState.RaiseLevel("p_trad"); RigState.RaiseLevel("p_trad"); RigState.RaiseLevel("p_trad"); // 1 -> 4
                Assert.AreEqual(4, RigState.Level("p_trad"));

                max2Go = new GameObject("MV-1035 test Max 2", typeof(CharacterController)) { tag = "Player" };
                max2Go.transform.position = new Vector3(0f, 0f, 200f); // far from everything above
                var abilities2 = max2Go.AddComponent<PlayerAbilities>();

                Assert.IsTrue(abilities2.TryDropTrap());
                RobotTrap radiusTrap = abilities2.ActiveTrap;

                nearRobot = NewRobot("Near5.8m", max2Go.transform.position + new Vector3(5.8f, 0f, 0f), EnemyArchetype.Rusher);
                farRobot = NewRobot("Far6.3m", max2Go.transform.position + new Vector3(-6.3f, 0f, 0f), EnemyArchetype.Rusher);
                Physics.SyncTransforms();

                radiusTrap.Tick(Dt);
                Assert.IsTrue(nearRobot.IsTrapHeld, "a robot 5.8m away must be caught when the radius is 6.0m");
                Assert.IsFalse(farRobot.IsTrapHeld, "a robot 6.3m away must not be caught when the radius is 6.0m");
                Object.DestroyImmediate(radiusTrap.gameObject); // not carried further — avoid leaking it into later sections/tests

                // === AC1 (fill case): a trap with capacity 2 converts both only once the second robot
                // arrives — driven directly against RobotTrap so it isn't entangled with AC2's own
                // still-active radiusTrap (one trap at a time is PlayerAbilities' own rule, not RobotTrap's).
                // +2 free slots on top of whatever is already converted (robotA, above) — conversions
                // are permanent now (MV-1035), so that slot stays occupied for the rest of this test.
                RobotEnemy.ConversionCap = RobotEnemy.Converted.Count + 2;
                bool fillTrapConverted = false;
                RobotTrap fillTrap = RobotTrap.Spawn(new Vector3(0f, 0f, 400f), capacity: 2, radius: 3f, onDespawn: c => fillTrapConverted = c);

                fillRobot1 = NewRobot("Fill1", new Vector3(1f, 0f, 400f), EnemyArchetype.Rusher);
                Physics.SyncTransforms();
                fillTrap.Tick(Dt);
                Assert.IsTrue(fillRobot1.IsTrapHeld);

                // Well under 8s — the trap must NOT start converting with only 1 of 2 caught.
                for (int i = 0; i < Mathf.CeilToInt(3f / Dt); i++) fillTrap.Tick(Dt);
                Assert.IsFalse(fillTrap.IsConverting, "must not convert before capacity or the 8s timeout, whichever first");

                fillRobot2 = NewRobot("Fill2", new Vector3(-1f, 0f, 400f), EnemyArchetype.Rusher);
                Physics.SyncTransforms();
                fillTrap.Tick(Dt); // the second catch fills it to capacity immediately
                Assert.IsTrue(fillRobot2.IsTrapHeld);
                Assert.IsTrue(fillTrap.IsConverting, "reaching capacity must start conversion immediately, not wait for the 8s timeout");

                for (int i = 0; i < framesTo4_5s && fillTrap != null; i++) fillTrap.Tick(Dt);
                Assert.IsTrue(fillRobot1.IsConverted, "both caught robots must convert together");
                Assert.IsTrue(fillRobot2.IsConverted, "both caught robots must convert together");
                Assert.IsTrue(fillTrapConverted, "the fill trap's despawn callback must report a successful conversion");
            }
            finally
            {
                // MV645HudLeftColumnTests' own precedent: OnEnable subscribed HudController to
                // WeaponSystemState.Changed by reflection, so OnDisable must unsubscribe the same way
                // before destroying it — otherwise the leaked subscriber fires (and can log an
                // edit-mode Destroy() error) on the very next unrelated test's WeaponSystemState.Reset().
                if (hudGo != null) InvokeLifecycle(hudGo.GetComponent<HudController>(), "OnDisable");
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (max2Go != null) Object.DestroyImmediate(max2Go);
                if (hudGo != null) Object.DestroyImmediate(hudGo);
                if (floorGo != null) Object.DestroyImmediate(floorGo);
                if (robotA != null) Object.DestroyImmediate(robotA.gameObject);
                if (enemyTarget != null) Object.DestroyImmediate(enemyTarget.gameObject);
                if (nearRobot != null) Object.DestroyImmediate(nearRobot.gameObject);
                if (farRobot != null) Object.DestroyImmediate(farRobot.gameObject);
                if (fillRobot1 != null) Object.DestroyImmediate(fillRobot1.gameObject);
                if (fillRobot2 != null) Object.DestroyImmediate(fillRobot2.gameObject);
            }
        }
    }
}
