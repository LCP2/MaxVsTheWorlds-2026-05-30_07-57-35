using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-624 (Lee, 29 Aug 2026 playtest): "Sentinels pass through everything — pots, walls etc...
    /// At the moment they can fire through walls and pots though and I want to retain that ability."
    /// The sentinel already carried a solid <see cref="CapsuleCollider"/>, but every movement write was
    /// a direct <c>transform.position</c> assignment — a teleport with no swept collision test — so the
    /// collider made it an obstacle to everything else while leaving it immune to obstacles itself. The
    /// fix routes all three movement paths (sidestep, standoff-follow, MV-615 separation) through a new
    /// <see cref="CharacterController"/> via <see cref="CharacterControllerMotion.SafeMove"/>, the same
    /// swept-move helper <see cref="MaxWorlds.Player.PlayerController"/> already uses (MV-386), while the
    /// firing/targeting path stays exactly as it was — range-only, no visibility test of any kind.
    ///
    /// EditMode only, reflection-driven for <see cref="Sentinel"/>'s private <c>Update</c>/<c>TickMovement</c>
    /// — same idiom <see cref="SentinelBodyTests"/> and <see cref="GunnerSentinelBeamTests"/> already use.
    /// Multi-frame movement drives <c>TickMovement(dt)</c> directly with an explicit <c>dt</c> rather than
    /// <c>Update()</c>/<c>Time.deltaTime</c>, which is not reliably non-zero outside Play mode — the same
    /// reason <c>RobotEnemy.TickChase</c> (see <c>RobotStandoffBandTests</c>) takes <c>dt</c> as a
    /// parameter rather than reading <see cref="Time.deltaTime"/> itself.
    /// </summary>
    public sealed class MV624SentinelCollisionTests
    {
        // MapData.wallThickness's own default (see CharacterControllerMotion's class doc for the same number).
        private const float WallThickness = 0.4f;

        // EditMode tests share one scene across the whole run (1600+ tests) with no reset between
        // them -- a far, dedicated coordinate space rules out any leftover geometry from an unrelated
        // test as a cause of an unexpected block (this cost real debugging time on AC6 before the
        // offset was added: without it, Max's CharacterController was stopped early by something that
        // was never one of this test's own objects).
        private static readonly Vector3 Origin = new Vector3(5000f, 0f, 5000f);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
        }

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static Sentinel NewSentinel(GameObject go, Vector3 position, float moveSpeed,
            float standoffDistance, Transform followTarget, float range = 7f)
        {
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp: 999f, range: range, fireInterval: 0.6f,
                moveSpeed: moveSpeed, standoffDistance: standoffDistance, followTarget: followTarget);
            return sentinel;
        }

        private static void TickMovement(Sentinel sentinel, float dt) =>
            typeof(Sentinel).GetMethod("TickMovement", NonPublicInstance).Invoke(sentinel, new object[] { dt });

        private static void InvokeUpdate(Sentinel sentinel) =>
            typeof(Sentinel).GetMethod("Update", NonPublicInstance).Invoke(sentinel, null);

        private static Vector3? SidestepTarget(Sentinel sentinel) =>
            (Vector3?)typeof(Sentinel).GetField("_sidestepTarget", NonPublicInstance).GetValue(sentinel);

        private static float SidestepTimeoutSeconds() => (float)typeof(Sentinel)
            .GetField("SidestepTimeoutSeconds", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private static GameObject NewFollowTarget(Vector3 position, Vector3 forward)
        {
            var go = new GameObject("Follow Target");
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            return go;
        }

        private static GameObject NewBox(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            return go;
        }

        private static RobotEnemy NewTargetRobot(Vector3 position)
        {
            var go = new GameObject("Target Robot");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly
            return e;
        }

        // ---------------------------------------------------------------------------- AC1

        [Test]
        public void MovementIsBlockedByAWallTheSameThicknessAsAMapGateOrFence()
        {
            var sentinelGo = new GameObject("Sentinel");
            GameObject followGo = null, wallGo = null;
            try
            {
                followGo = NewFollowTarget(Origin + new Vector3(0f, 0f, 5f), Vector3.forward);
                // standoffDistance 0 -- the sentinel keeps trying to close the FULL gap every tick, so
                // sustained pressure against the wall is exercised, not just a single approach.
                var sentinel = NewSentinel(sentinelGo, Origin + new Vector3(0f, 0f, -3f),
                    moveSpeed: 3f, standoffDistance: 0f, followTarget: followGo.transform);
                wallGo = NewBox("Wall", Origin + new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, WallThickness));
                Physics.SyncTransforms();

                for (int i = 0; i < 200; i++) TickMovement(sentinel, 0.05f);

                float wallNearFaceZ = Origin.z - WallThickness * 0.5f;
                float signedDistanceFromWall = wallNearFaceZ - sentinel.transform.position.z;
                Assert.That(signedDistanceFromWall, Is.GreaterThan(0f),
                    $"the sentinel ended up at or past the wall's near face (z={sentinel.transform.position.z}, " +
                    $"wall near face z={wallNearFaceZ}) -- it tunnelled through, the exact MV-624 defect");
            }
            finally
            {
                Object.DestroyImmediate(sentinelGo);
                if (followGo != null) Object.DestroyImmediate(followGo);
                if (wallGo != null) Object.DestroyImmediate(wallGo);
            }
        }

        // ---------------------------------------------------------------------------- AC2

        [Test]
        public void MovementIsBlockedByAPotSizedPropWithoutClimbingOverIt()
        {
            var sentinelGo = new GameObject("Sentinel");
            GameObject followGo = null, propGo = null;
            try
            {
                followGo = NewFollowTarget(Origin + new Vector3(0f, 0f, 5f), Vector3.forward);
                var sentinel = NewSentinel(sentinelGo, Origin + new Vector3(0f, 0f, -3f),
                    moveSpeed: 3f, standoffDistance: 0f, followTarget: followGo.transform);
                float startY = sentinel.transform.position.y;
                // A pot/crate-sized prop, resting on the floor -- tall enough (0.5m) that Unity's
                // default 0.3 stepOffset would climb it; the sentinel's own 0.1 stepOffset must not.
                propGo = NewBox("Pot", Origin + new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.5f));
                Physics.SyncTransforms();

                for (int i = 0; i < 200; i++) TickMovement(sentinel, 0.05f);

                float propNearFaceZ = Origin.z - 0.25f;
                float signedDistanceFromProp = propNearFaceZ - sentinel.transform.position.z;
                Assert.That(signedDistanceFromProp, Is.GreaterThan(0f),
                    $"the sentinel ended up at or past the prop's near face (z={sentinel.transform.position.z})");
                Assert.That(sentinel.transform.position.y, Is.EqualTo(startY).Within(0.02f),
                    $"the sentinel's y rose from {startY} to {sentinel.transform.position.y} -- it climbed " +
                    "the prop instead of being stopped by it (stepOffset too high)");
            }
            finally
            {
                Object.DestroyImmediate(sentinelGo);
                if (followGo != null) Object.DestroyImmediate(followGo);
                if (propGo != null) Object.DestroyImmediate(propGo);
            }
        }

        // ---------------------------------------------------------------------------- AC3

        [Test]
        public void UnobstructedMovementStillClosesToStandoffDistanceWithinTheOldFrameBudget()
        {
            var sentinelGo = new GameObject("Sentinel");
            GameObject followGo = null;
            try
            {
                followGo = NewFollowTarget(Origin + new Vector3(0f, 0f, 8f), Vector3.forward);
                const float standoff = AbilityTuning.DefaultSentinelStandoffDistance;
                var sentinel = NewSentinel(sentinelGo, Origin,
                    moveSpeed: 3f, standoffDistance: standoff, followTarget: followGo.transform);
                Physics.SyncTransforms();

                for (int i = 0; i < 200; i++) TickMovement(sentinel, 0.05f); // 10s -- far more than an 8m/3m/s close needs

                float finalDistance = Vector3.Distance(sentinel.transform.position, followGo.transform.position);
                Assert.That(finalDistance, Is.EqualTo(standoff).Within(0.1f),
                    "routing movement through the CharacterController must not make an unobstructed " +
                    $"approach sluggish or inaccurate -- expected ~{standoff}m, got {finalDistance}m");
            }
            finally
            {
                Object.DestroyImmediate(sentinelGo);
                if (followGo != null) Object.DestroyImmediate(followGo);
            }
        }

        // ---------------------------------------------------------------------------- AC4

        [Test]
        public void ASidestepTargetInsideASolidClearsWithinTheTimeoutInsteadOfLatchingForever()
        {
            var sentinelGo = new GameObject("Sentinel");
            GameObject followGo = null, wallGo = null;
            try
            {
                // Close enough (1m < DefaultSentinelReactDistance's 1.5m) to trigger a sidestep on the
                // very first tick; moveSpeed 0 isolates the sidestep -- MV-579's reaction is independent
                // of the Move axis (see Sentinel.TickMovement's own comment).
                followGo = NewFollowTarget(Origin + new Vector3(0f, 0f, -1f), Vector3.forward);
                var sentinel = NewSentinel(sentinelGo, Origin,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: followGo.transform);

                // SentinelSidestepTarget steps toward local +X for this geometry (right of the
                // approacher's forward) -- a wall placed there blocks the sidestep well short of its
                // 1.2m target, so it can never arrive and must rely on the timeout instead.
                wallGo = NewBox("Wall", Origin + new Vector3(0.6f, 1.5f, 0f), new Vector3(0.3f, 3f, 3f));
                Physics.SyncTransforms();

                TickMovement(sentinel, 0.02f); // arm the sidestep
                Assert.That(SidestepTarget(sentinel), Is.Not.Null, "precondition: the sidestep must have armed");

                // The sentinel stays within react distance of the follow target throughout (it barely
                // moves before the wall stops it), so once cleared it is free to re-arm a fresh attempt
                // rather than being required to stay cleared forever -- the assertion is "clears within
                // the bound at least once", not "never sidesteps again".
                float timeout = SidestepTimeoutSeconds();
                float elapsed = 0.02f;
                bool everCleared = false;
                while (elapsed < timeout + 0.5f)
                {
                    TickMovement(sentinel, 0.04f);
                    elapsed += 0.04f;
                    if (SidestepTarget(sentinel) == null) { everCleared = true; break; }
                }

                Assert.That(everCleared, Is.True,
                    $"a sidestep target the sentinel can never physically reach must clear within the " +
                    $"{timeout}s timeout bound -- a sentinel frozen forever mid-sidestep is worse than " +
                    "the bug this ticket fixes");

                // Resumes normal behaviour afterwards: another tick must not throw.
                Assert.DoesNotThrow(() => TickMovement(sentinel, 0.02f));
            }
            finally
            {
                Object.DestroyImmediate(sentinelGo);
                if (followGo != null) Object.DestroyImmediate(followGo);
                if (wallGo != null) Object.DestroyImmediate(wallGo);
            }
        }

        // ---------------------------------------------------------------------------- AC5

        [Test]
        public void FiringStillIgnoresWallsCompletely()
        {
            var sentinelGo = new GameObject("Sentinel");
            RobotEnemy target = null;
            GameObject wallGo = null;
            try
            {
                var sentinel = NewSentinel(sentinelGo, Origin,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null, range: 7f);
                target = NewTargetRobot(Origin + new Vector3(3f, 0f, 0f)); // inside SentinelRange (7)
                wallGo = NewBox("Wall", Origin + new Vector3(1.5f, 1.5f, 0f), new Vector3(WallThickness, 3f, 6f));
                Physics.SyncTransforms();

                float healthBefore = target.HealthCurrent;
                InvokeUpdate(sentinel); // fireCooldown starts at 0 -- fires on the very first tick

                Assert.That(target.HealthCurrent, Is.LessThan(healthBefore),
                    "a wall fully between the sentinel and an in-range robot must not block the shot -- " +
                    "Lee's explicit ask ('I want to retain that ability')");
            }
            finally
            {
                Object.DestroyImmediate(sentinelGo);
                if (target != null) Object.DestroyImmediate(target.gameObject);
                if (wallGo != null) Object.DestroyImmediate(wallGo);
            }
        }

        /// <summary>Source-shape guard, not a behavioural one (same idiom as <c>SentinelBodyTests</c>'
        /// AC1) -- the half of AC5 "most likely to get broken by accident" per the ticket, so it is
        /// pinned by inspection as well as by the behavioural test above.</summary>
        [Test]
        public void SentinelCsHasNoLineOfSightCallAnywhereInIt()
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime", "Arena", "Sentinel.cs");
            Assert.IsTrue(File.Exists(path), $"Sentinel.cs not found at {path}");

            string code = string.Join("\n", File.ReadAllLines(path).Select(StripLineComment));
            Assert.IsFalse(code.Contains("Physics.Raycast"), "Sentinel.cs must never gain a raycast line-of-sight check");
            Assert.IsFalse(code.Contains("Physics.Linecast"), "Sentinel.cs must never gain a linecast line-of-sight check");
            Assert.IsFalse(code.Contains("Physics.SphereCast"), "Sentinel.cs must never gain a spherecast line-of-sight check");
        }

        private static string StripLineComment(string line)
        {
            int i = line.IndexOf("//", System.StringComparison.Ordinal);
            return i < 0 ? line : line.Substring(0, i);
        }

        // ---------------------------------------------------------------------------- AC6

        [Test]
        public void MaxIsNeverBlockedByADeployedSentinel_MV579()
        {
            var maxGo = new GameObject("Max", typeof(CharacterController));
            var sentinelGo = new GameObject("Sentinel");
            try
            {
                maxGo.transform.position = Origin + new Vector3(0f, 0f, -3f);
                var maxCc = maxGo.GetComponent<CharacterController>();

                // Deployed directly in Max's path -- IgnorePlayerCollision (run inside Init) must exempt
                // BOTH the sentinel's CapsuleCollider and its new CharacterController against Max's own.
                NewSentinel(sentinelGo, Origin, moveSpeed: 0f, standoffDistance: 2.5f,
                    followTarget: maxGo.transform);
                Physics.SyncTransforms();

                for (int i = 0; i < 60; i++)
                    CharacterControllerMotion.SafeMove(maxCc, Vector3.forward * 0.05f); // 3m total -- start to Origin.z

                Assert.That(maxGo.transform.position.z - Origin.z, Is.EqualTo(0f).Within(0.1f),
                    $"Max was blocked by the sentinel he deployed -- final relative z=" +
                    $"{maxGo.transform.position.z - Origin.z}, expected to have walked straight through to ~0");
            }
            finally
            {
                Object.DestroyImmediate(maxGo);
                Object.DestroyImmediate(sentinelGo);
            }
        }

        // ---------------------------------------------------------------------------- AC7

        [Test]
        public void SpawnPlacementIsExactEvenSomewhereAWalkingSentinelCouldNotHaveReached()
        {
            var sentinelGo = new GameObject("Sentinel");
            GameObject wallGo = null;
            try
            {
                // A wall sitting exactly on the deploy point -- if placement ever became a walked move
                // instead of a teleport, this would clip it short.
                var p = Origin + new Vector3(4f, 0f, 4f);
                wallGo = NewBox("Wall", p, new Vector3(4f, 3f, WallThickness));

                var sentinel = NewSentinel(sentinelGo, p, moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

                Assert.That(sentinel.transform.position, Is.EqualTo(p),
                    "the aimed placement joystick's whole point is landing exactly where the player aimed");
            }
            finally
            {
                Object.DestroyImmediate(sentinelGo);
                if (wallGo != null) Object.DestroyImmediate(wallGo);
            }
        }

        // ---------------------------------------------------------------------------- AC8

        [Test]
        public void MutualSeparationStillPushesTwoCoincidentSentinelsApart_MV615()
        {
            var aGo = new GameObject("Sentinel A");
            var bGo = new GameObject("Sentinel B");
            try
            {
                var a = NewSentinel(aGo, Origin, moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                var b = NewSentinel(bGo, Origin, moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                Physics.SyncTransforms();

                for (int i = 0; i < 90; i++)
                {
                    TickMovement(a, 1f / 60f);
                    TickMovement(b, 1f / 60f);
                }

                float finalDistance = Vector3.Distance(a.transform.position, b.transform.position);
                Assert.That(finalDistance, Is.GreaterThanOrEqualTo(PlayerAbilities.SentinelPlacementClearance - 0.05f),
                    $"two coincident sentinels must separate back out to at least the " +
                    $"{PlayerAbilities.SentinelPlacementClearance}m placement clearance, got {finalDistance}m");
            }
            finally
            {
                Object.DestroyImmediate(aGo);
                Object.DestroyImmediate(bGo);
            }
        }
    }
}
