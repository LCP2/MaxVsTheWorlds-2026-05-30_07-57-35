using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// MV-259: the automated boot-and-play gate. `cc-verify` proves the build compiles, EditMode
    /// passes, and it holds 60fps — it says nothing about whether the opening is actually playable,
    /// which is exactly how MV-256 (robots camped in the lead-in room; an instant-death opening) sailed
    /// past a green gate. This boots the real shipped scene and plays through the opening beats in
    /// code, asserting the same invariants a human playtest was checking by hand: alive at spawn,
    /// movement works, the primary weapon can damage a robot, the lead-in stays empty throughout, and
    /// the menu is reachable mid-run. Any failure here must block the gate exactly like a compile error.
    ///
    /// The ticket's "~60 simulated seconds" is a human-playtest phrase; here it is stepped as frames
    /// rather than waited out in wall-clock time — a couple hundred <c>yield return null</c>s cover the
    /// same opening beat in a couple of real seconds under the test runner, which is what keeps this
    /// cheap enough to run on every push (build.yml's `testMode: all`) and in `cc-verify.bat`.
    ///
    /// Derives from <see cref="InputTestFixture"/> so simulated key presses actually stick: batchmode
    /// (`-nographics`, CI, and this project's local `cc-verify`) never gives the Game View real focus,
    /// and without this fixture the editor's native input backend treats every frame as a fresh
    /// "just regained focus" sync and hard-resets any device that isn't real hardware — silently
    /// eating every simulated press a frame after it was set. InputTestFixture severs that tie.
    ///
    /// Loads the real shipped scene, same as <see cref="QuitToMenuPlayTests"/>, because the thing under
    /// test IS the shipped opening — a hand-built fixture would be testing a different game.
    /// </summary>
    public sealed class BootAndPlaySmokeTests : InputTestFixture
    {
        private const int Slice = 0; // Backyard_Slice — scene 0 is the playable scene
        private const int BootAndPlayTimeoutMs = 90_000; // ticket bound: must terminate within ~90s
        private string _dir;
        private Keyboard _keyboard;
        private GameObject _testRobotGo;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            SaveSystem.ResetForTests();
            _dir = Path.Combine(Path.GetTempPath(), "mvtw-boot-and-play-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            HudController.SkipTouchControlsForTests = true;

            SceneManager.LoadScene(Slice);
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (_testRobotGo != null) Object.Destroy(_testRobotGo);
            Time.timeScale = 1f;
            SaveSystem.ResetForTests();
            MaxWorlds.Upgrades.UpgradeState.Reset();
            MaxWorlds.Pickups.PickupWallet.Reset();
            HudController.SkipTouchControlsForTests = false;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            yield return null;
        }

        private Key _heldKey = Key.None;

        private void HoldKey(Key key)
        {
            _heldKey = key;
            Press(_keyboard[key], queueEventOnly: true);
        }

        private void ReleaseKeys()
        {
            if (_heldKey != Key.None) Release(_keyboard[_heldKey], queueEventOnly: true);
            _heldKey = Key.None;
        }

        /// <summary>
        /// PlayerController builds its Move/Aim actions in Awake(), against whatever devices exist at
        /// that moment — which, for this test, is before the simulated Keyboard is added. A generic
        /// "&lt;Keyboard&gt;/d"-style binding does not retroactively re-resolve onto a device added
        /// afterwards (disabling/re-enabling the existing action instance does not force it either), so
        /// replace the field with a freshly-built action of the same shape, constructed now that the
        /// keyboard already exists.
        /// </summary>
        private static void RebuildWasdBoundAction(PlayerController controller, string fieldName,
            string upKey, string downKey, string leftKey, string rightKey, string gamepadStick)
        {
            var field = typeof(PlayerController).GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ((InputAction)field.GetValue(controller)).Disable();

            var fresh = new InputAction(fieldName, InputActionType.Value);
            fresh.AddCompositeBinding("2DVector")
                .With("Up", upKey).With("Down", downKey).With("Left", leftKey).With("Right", rightKey);
            fresh.AddBinding(gamepadStick, processors: "stickDeadzone(min=0.2)");
            fresh.Enable();
            field.SetValue(controller, fresh);
        }

        /// <summary>
        /// MV-260: MV-259's frame-counted loops only bound progress once the coroutine is actually
        /// resuming each frame — they cannot save it from a stall *inside* a single frame elsewhere in
        /// the boot path (headless CI hung 3.5h+ on run #252). <c>[Timeout]</c> is Unity Test
        /// Framework's own wall-clock watchdog for a <c>[UnityTest]</c> coroutine: independent of frame
        /// count, it force-fails and tears down the test if this method is still running past the
        /// budget, so the run always terminates pass or fail instead of hanging the gate.
        /// </summary>
        [UnityTest]
        [Timeout(BootAndPlayTimeoutMs)]
        public IEnumerator OpeningIsPlayable_SpawnMoveFireMenu_LeadInStaysEmpty()
        {
            // Added right before it's needed rather than in [UnitySetUp] — something between that
            // coroutine finishing and this method starting was dropping every InputSystem device
            // added earlier, which silently ate the whole simulated keyboard.
            _keyboard = InputSystem.AddDevice<Keyboard>();

            // --- Enter a run, same as every other scene-level PlayMode test (QuitToMenuPlayTests). ---
            var home = Object.FindFirstObjectByType<HomeScreen>();
            Assert.IsNotNull(home, "HomeScreen must be up on a fresh boot — nothing to play if it's missing");
            Button play = home.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.gameObject.name == "PLAY");
            Assert.IsNotNull(play, "no PLAY button found on the Home screen");
            play.onClick.Invoke();
            yield return null;
            yield return null;

            MapZone leadIn = ResolveLeadInZone();

            // --- 1. The player spawns and is alive. ---
            var health = Object.FindFirstObjectByType<PlayerHealth>();
            Assert.IsNotNull(health, "no PlayerHealth in the scene after entering a run — nothing spawned");
            var controller = health.GetComponent<PlayerController>();
            Assert.IsNotNull(controller, "the player has no PlayerController");
            Assert.IsTrue(health.IsAlive, "the player must be alive the instant a run starts");
            AssertLeadInEmpty(leadIn);

            RebuildWasdBoundAction(controller, "_move", "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d", "<Gamepad>/leftStick");
            RebuildWasdBoundAction(controller, "_aim", "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow", "<Gamepad>/rightStick");

            // --- 2. The player can move — hold Right for real frames and confirm it actually moved,
            // while continuously re-checking the lead-in stays empty through this opening window (5). ---
            Vector3 beforeMove = controller.transform.position;
            for (int i = 0; i < 30; i++)
            {
                HoldKey(Key.D);
                yield return null;
                if (i < 3)
                {
                    Debug.Log($"[MV-259 debug] frame={i} timeScale={Time.timeScale} deltaTime={Time.deltaTime} " +
                              $"dKeyPressed={_keyboard.dKey.isPressed} moveInput={controller.MoveInput} " +
                              $"pos={controller.transform.position}");
                }
                AssertLeadInEmpty(leadIn);
            }
            ReleaseKeys();
            yield return null;

            float moved = Vector3.Distance(beforeMove, controller.transform.position);
            Assert.Greater(moved, 0.05f, "holding Right for 30 frames should move the player; it didn't budge");
            Assert.IsTrue(health.IsAlive, "the player died just from walking");

            // --- 3. The primary weapon fires and can damage a robot. A hand-built robot dropped
            // directly in the blaster's cone/range (same pattern as BackyardLoopPlayTests' hand-built
            // MowerHutch) so the test doesn't depend on a factory having spawned one naturally yet. ---
            var blaster = health.GetComponent<WaterBlaster>();
            Assert.IsNotNull(blaster, "the player has no WaterBlaster — no primary weapon to fire");

            _testRobotGo = new GameObject("MV-259 smoke-test robot",
                typeof(CharacterController), typeof(RobotEnemy));
            var robot = _testRobotGo.GetComponent<RobotEnemy>();
            _testRobotGo.transform.position = controller.transform.position + Vector3.right * 2f;
            yield return null; // Awake/OnEnable — acquires the player as its sight target

            Assert.IsTrue(robot.IsAlive, "the test robot should start alive");
            float robotHealthBefore = robot.HealthCurrent;
            bool everAimed = false;
            bool everEmitted = false;

            for (int i = 0; i < 90 && robot.IsAlive; i++)
            {
                HoldKey(Key.RightArrow); // aims right, toward the robot at controller.position + right*2
                yield return null;
                if (controller.IsAiming) everAimed = true;
                if (blaster.IsEmitting) everEmitted = true;
            }
            ReleaseKeys();
            yield return null;

            Assert.IsTrue(everAimed, "aiming Right should have engaged IsAiming on the player");
            Assert.IsTrue(everEmitted, "the primary weapon never emitted while aiming at a robot in range");
            Assert.IsTrue(!robot.IsAlive || robot.HealthCurrent < robotHealthBefore,
                "the primary weapon fired but never damaged the robot standing in its cone");
            Assert.IsTrue(health.IsAlive, "the player must survive its own opening weapon check");

            Object.Destroy(_testRobotGo);
            _testRobotGo = null;
            yield return null;

            // --- 4. The main menu is reachable from within a run (MV-257's RunFlow.QuitToMenu). ---
            RunFlow.QuitToMenu();
            yield return null;
            yield return null;

            var homeAgain = Object.FindFirstObjectByType<HomeScreen>();
            Assert.IsNotNull(homeAgain, "quitting mid-run must reopen the Home screen");
            Assert.IsTrue(homeAgain.IsOpen, "the reopened Home screen should be open");
        }

        private static MapZone ResolveLeadInZone()
        {
            MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
            Assert.IsNotNull(map, "no shipped map data to check the lead-in zone against");
            MapZone leadIn = map.Zone("area1");
            Assert.IsNotNull(leadIn, "the map has no 'area1' zone to check");
            Assert.AreEqual(ZoneKind.Entry, leadIn.Kind,
                "area1 is no longer the entry/lead-in zone — this test's premise has moved");
            return leadIn;
        }

        private static void AssertLeadInEmpty(MapZone leadIn)
        {
            foreach (RobotEnemy robot in RobotEnemy.Active)
            {
                if (robot == null || !robot.gameObject.activeInHierarchy) continue;
                Vector3 p = robot.transform.position;
                Assert.IsFalse(leadIn.Contains(p.x, p.z),
                    $"'{robot.name}' is standing in the lead-in zone at ({p.x:0.0}, {p.z:0.0}) during the " +
                    "opening — a fresh run should stay safe to orient in (MV-256)");
            }
        }
    }
}
