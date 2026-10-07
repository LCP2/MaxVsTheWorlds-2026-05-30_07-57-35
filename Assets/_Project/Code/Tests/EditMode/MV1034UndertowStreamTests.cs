using System.Collections.Generic;
using System.IO;
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
    /// MV-1034 — World 3's UNDERTOW loses its charge/cavitation shot: holding fire now streams the
    /// pressure lance continuously for as long as it's held, with no charge phase pausing it and no
    /// release shot. Proves the RESOLVED firing/visual behaviour off a player built through the real
    /// entry point (<see cref="PlayerController.Awake"/>) with World 3's primary active, ticked through
    /// <c>Undertow</c>'s own real per-frame method with an explicit <c>dt</c> (the same reflection-driven,
    /// real-entry-point idiom <c>MV1012UndertowAttachAndCoverGateTests</c> already uses) — never a
    /// hand-set field standing in for the fire gate or the stream's endpoint.
    ///
    /// Fails on baa509c (the commit before this ticket): <c>Undertow</c> stops ticking the lance once a
    /// continuous hold reaches 0.9s (charges instead), so a robot held under fire for 2s does NOT take
    /// damage on every 0.1s tick; releasing spawns a <c>CavitationBubble</c> rather than nothing; and
    /// there is no stream VFX at all — <c>Undertow.IsStreamVisible</c>/<c>StreamEndPoint</c> do not exist,
    /// so this test does not compile there.
    ///
    /// Updated for MV-1070: the stream now only damages a robot once the tip's own seeking spring has
    /// physically closed on it (<see cref="Undertow.IsLatched"/>), which takes a handful of ticks even
    /// dead-ahead (spec'd up to ~0.45s) rather than landing on tick 0 — AC1's "every tick across a 2s
    /// hold" window now starts counting from the first latched tick, not from the very first tick fired.
    /// </summary>
    public sealed class MV1034UndertowStreamTests
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
        private static readonly FieldInfo RobotEnemyCcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyHealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);

        private GameObject _playerGo;
        private readonly List<GameObject> _spawned = new List<GameObject>();

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
            RobotEnemyCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            // MV-1106 doubled Undertow's base damage to 8/tick (was 4); 20 ticks at the new rate is up
            // to 160 damage, so 100 no longer survives the full hold -- bumped with headroom to spare.
            RobotEnemyHealthField.SetValue(robot, 300f); // survives every tick in this test
            return robot;
        }

        private static void InvokeTick(Undertow undertow, float dt) =>
            UndertowTick.Invoke(undertow, new object[] { dt });

        [Test]
        public void StreamFiresEveryTickWithNoChargeAndStopsAtCoverOrTheFarthestPiercedRobot_MV1034()
        {
            // --- Build Max through the real entry point with World 3's primary active.
            _playerGo = new GameObject("MV1034 Player", typeof(CharacterController), typeof(PlayerController));
            var player = _playerGo.GetComponent<PlayerController>();
            PlayerControllerAwake.Invoke(player, null);

            var undertow = _playerGo.GetComponent<Undertow>();
            Assert.IsNotNull(undertow, "PlayerController.Awake must self-attach a live Undertow");
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode

            WeaponSystemState.ApplyWeaponCoreMorph(2); // World 3 -> UNDERTOW is the active primary
            DevMode.Enabled = true;
            DevMode.InfiniteEnergy = true; // isolate the 2s hold from tank depletion
            DevMode.AutoFire = true; // "request fire" without the real Input System (PlayMode is banned)

            // --- AC1: a robot 6m ahead takes damage on every 0.1s tick across a full 2s hold -- no
            // charge phase ever stops it, unlike the pre-fix behaviour past ChargeSeconds (0.9s).
            // MV-1070: acquiring the latch is no longer instantaneous even dead-ahead (the tip's seeking
            // spring takes a handful of ticks to physically close within Undertow.LatchDistance, spec'd
            // up to ~0.45s), so tick until latched first, THEN assert the per-tick damage cadence the
            // remaining ticks of the 2s hold.
            RobotEnemy target = NewRobot("Target", _playerGo.transform.position + _playerGo.transform.forward * 6f);
            Physics.SyncTransforms();

            const int maxAcquireTicks = 10; // 1s budget -- generous over the ~0.45s settle spec
            int acquireTick = 0;
            for (; acquireTick < maxAcquireTicks && !undertow.IsLatched; acquireTick++)
                InvokeTick(undertow, 0.1f);
            Assert.IsTrue(undertow.IsLatched, $"the tip never latched onto the dead-ahead target within {maxAcquireTicks * 0.1f:0.0}s");

            for (int i = acquireTick; i < 20; i++)
            {
                float before = target.HealthCurrent;
                InvokeTick(undertow, 0.1f);
                Assert.Less(target.HealthCurrent, before,
                    $"tick {i} (t={((i + 1) * 0.1f):0.0}s): the lance must damage the target every 0.1s tick " +
                    "once latched, across the full 2s hold -- no charge phase may ever pause it (MV-1034)");
            }

            Assert.IsTrue(undertow.IsStreamVisible, "the stream renderers must be enabled while firing");
            float endDistance = Vector3.Distance(undertow.StreamEndPoint, target.transform.position);
            Assert.Less(endDistance, 0.3f,
                $"the stream's end point must land within 0.3m of the 6m-ahead target it's damaging, was {endDistance:0.000}m off");

            // --- AC1 continued: releasing fire stops the stream and spawns no cavitation shot -- the
            // charge/release mechanic (and CavitationBubble/CavitationImplosion themselves) are gone.
            DevMode.AutoFire = false;
            InvokeTick(undertow, 0.1f);
            Assert.IsFalse(undertow.IsStreamVisible, "the stream renderers must be disabled once fire is released");

            string runtimeRoot = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot), $"Runtime root not found: {runtimeRoot}");
            var offenders = new List<string>();
            foreach (string path in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(path).Contains("Cavitation")) offenders.Add(Path.GetFileName(path));
            }
            Assert.IsEmpty(offenders,
                "no reference to Cavitation may remain under Assets/_Project/Code/Runtime -- MV-1034 removed " +
                $"the charge/cavitation shot entirely. Offenders: {string.Join(", ", offenders)}");

            // --- AC2: with a cover block between Max and a second robot, the stream's end point lands
            // on the cover face, not the robot behind it. The AC1 target is cleared first so it can't
            // sit coincident with the new robot at the same 6m mark.
            Object.DestroyImmediate(target.gameObject);
            RobotEnemy behindCover = NewRobot("BehindCover", _playerGo.transform.position + _playerGo.transform.forward * 6f);

            var coverGo = new GameObject("MV1034-Cover");
            _spawned.Add(coverGo);
            coverGo.transform.position = _playerGo.transform.position + _playerGo.transform.forward * 3f;
            var coverCollider = coverGo.AddComponent<BoxCollider>();
            coverCollider.size = new Vector3(2f, 2f, 0.5f); // thin along the beam axis (forward, +Z)
            CoverLayer.Assign(coverGo);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

            DevMode.AutoFire = true;
            // MV-1070: the tip is a seeking point now, not an instant raycast hit -- with no candidate
            // (behindCover is sight-blocked) it eases toward the rest point over a handful of ticks
            // rather than snapping there in one, so tick until it settles before reading its position.
            for (int i = 0; i < 10; i++) InvokeTick(undertow, 0.1f);

            float coverFaceZ = coverGo.transform.position.z - coverCollider.size.z * 0.5f;
            Assert.AreEqual(coverFaceZ, undertow.StreamEndPoint.z, 0.05f,
                "the stream's end point must land on the cover's near face, not pass through it");
            float distanceToRobotBehindCover = Vector3.Distance(undertow.StreamEndPoint, behindCover.transform.position);
            Assert.Greater(distanceToRobotBehindCover, 0.3f,
                "the stream's end point must NOT be at the robot behind the cover");
        }
    }
}
