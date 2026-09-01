using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-622: a Bolter with an engaged Sentinel used to keep firing at Max's own position —
    /// <c>RobotEnemy.TickBolt</c> read <c>_playerTarget</c> rather than the MV-362 retargeting rule's
    /// own live answer (<c>target</c>) — and even a bolt that DID reach a Sentinel could never damage
    /// it, since <c>BolterBolt.Detonate</c> only ever resolved a <see cref="PlayerHealth"/> receiver.
    /// Both halves fixed together: <c>TickBolt</c> now fires at <c>target</c>, and <c>Detonate</c> now
    /// also resolves a <see cref="Sentinel"/> hit at a fraction of ITS OWN max health (7% as of Lee's
    /// V12c workbook, 2026-09-02, MV-642; was 10% per the V12 workbook, 2026-09-01, MV-638) — the same
    /// fraction-of-target design the player hit already used. Drives the real MV-362 retargeting
    /// (<c>RetargetIfNeeded</c>) rather than forcing the engagement field directly, so the test proves
    /// the actual production decision path, not a hand-picked state.
    /// </summary>
    public sealed class MV622BolterEngagesSentinelTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static readonly BindingFlags NonPublicInstance =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private static void InvokePrivate(object target, string methodName, params object[] args) =>
            typeof(RobotEnemy).GetMethod(methodName, NonPublicInstance).Invoke(target, args);

        private static object GetPrivateField(object target, string fieldName) =>
            typeof(RobotEnemy).GetField(fieldName, NonPublicInstance).GetValue(target);

        private static void SetPrivateField(object target, string fieldName, object value) =>
            typeof(RobotEnemy).GetField(fieldName, NonPublicInstance).SetValue(target, value);

        /// <summary>Damage resolution only — bypasses the flight/hit-radius check (already covered by
        /// <c>BolterBoltTests.WithinHitRadius_TransitionsAtTheHitRadius</c>) so this test isn't at the
        /// mercy of <c>Time.deltaTime</c> in edit mode. <c>Destroy(gameObject)</c> inside
        /// <c>Detonate</c> is edit-mode-illegal (same shape as <c>HomingMissile</c>'s own Detonate/
        /// Explode, per that class's test-file doc comment), hence the log suppression — same idiom
        /// <c>MV618HutchPaceAndDamageTests</c> uses for <c>MowerHutch.BuildCore</c>'s own Object.Destroy.</summary>
        private static void InvokeDetonate(BolterBolt bolt)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(BolterBolt).GetMethod("Detonate", NonPublicInstance).Invoke(bolt, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void EngagedSentinelIsFiredAtAndDamaged_ThenDisengagingFallsBackToMaxUnchanged()
        {
            var maxGo = new GameObject("MV-622 test Max", typeof(CharacterController));
            var sentinelGo = new GameObject("MV-622 test Sentinel");
            var enemyGo = new GameObject("MV-622 test Bolter", typeof(CharacterController));
            GameObject firstBoltGo = null, secondBoltGo = null;
            try
            {
                maxGo.AddComponent<PlayerController>();
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();
                // A clearly different bearing from the Sentinel below (+x) than from the enemy's
                // origin, so "the bolt is aimed at the Sentinel, not Max" is actually a meaningful
                // direction check rather than two collinear points that would pass either way.
                maxGo.transform.position = new Vector3(0f, 0f, -20f);

                var sentinel = sentinelGo.AddComponent<Sentinel>();
                sentinel.Init(new Vector3(1f, 0f, 0f), maxHp: 80f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

                var enemy = enemyGo.AddComponent<RobotEnemy>();
                // AddComponent doesn't reliably run Awake outside Play mode (established idiom, see
                // GunnerSentinelBeamTests) — target is seeded before Apply() so its own ResetState/
                // AcquireTarget call picks it up as _playerTarget, exactly as a real spawn would.
                SetPrivateField(enemy, "target", maxGo.transform);
                enemy.Apply(EnemyArchetype.Bolter);

                // --- AC1 part 1: engage the Sentinel via the REAL MV-362 rule (1 m away vs. Max's
                // 20 m), then confirm a Bolter's bolt is aimed at the Sentinel it's engaged on.
                InvokePrivate(enemy, "RetargetIfNeeded");
                Assert.AreSame(sentinel, GetPrivateField(enemy, "_engagedSentinel"),
                    "test setup: 1 m from the Sentinel vs. 20 m from Max should have engaged it");

                InvokePrivate(enemy, "TickBolt", 0.1f);
                firstBoltGo = GameObject.Find("BolterBolt (stand-in)");
                Assert.IsNotNull(firstBoltGo, "TickBolt didn't fire a bolt while engaged on a Sentinel");

                Vector3 toSentinel = sentinel.transform.position - enemyGo.transform.position;
                toSentinel.y = 0f;
                Assert.Less(Vector3.Angle(firstBoltGo.transform.forward, toSentinel.normalized), 1f,
                    "an engaged Bolter's bolt must be aimed at the Sentinel it's engaged on, not Max");

                // --- AC1 part 2: a bolt reaching the Sentinel deals exactly 7% of ITS OWN max health.
                InvokeDetonate(firstBoltGo.GetComponent<BolterBolt>());
                Assert.AreEqual(74.4f, sentinel.HealthCurrent, 1e-3f,
                    "a bolt reaching an engaged Sentinel must deal exactly 7% of ITS OWN max health (80 * 0.07 = 5.6)");
                Assert.AreEqual(playerHealth.Max, playerHealth.Current, 1e-3f,
                    "a bolt that hit the Sentinel must never also damage Max");

                LogAssert.ignoreFailingMessages = true;
                try { Object.DestroyImmediate(firstBoltGo); }
                finally { LogAssert.ignoreFailingMessages = false; }
                firstBoltGo = null;

                // --- AC1 part 3: disengaging (Sentinel pushed outside the aggro radius) falls back to
                // Max, and the existing player-damage behaviour is unchanged.
                sentinelGo.transform.position = new Vector3(500f, 0f, 0f);
                InvokePrivate(enemy, "RetargetIfNeeded");
                Assert.IsNull(GetPrivateField(enemy, "_engagedSentinel"),
                    "test setup: a Sentinel 500 m away must fall outside the aggro radius");

                SetPrivateField(enemy, "_dealtThisLunge", false); // a fresh Lunge cycle's own gate
                InvokePrivate(enemy, "TickBolt", 0.1f);
                secondBoltGo = GameObject.Find("BolterBolt (stand-in)");
                Assert.IsNotNull(secondBoltGo, "TickBolt didn't fire a second bolt once disengaged");

                Vector3 toMax = maxGo.transform.position - enemyGo.transform.position;
                toMax.y = 0f;
                Assert.Less(Vector3.Angle(secondBoltGo.transform.forward, toMax.normalized), 1f,
                    "a disengaged Bolter's bolt must target Max, exactly as before this ticket");

                InvokeDetonate(secondBoltGo.GetComponent<BolterBolt>());
                Assert.AreEqual(playerHealth.Max - BolterBolt.DamageFor(playerHealth.Max), playerHealth.Current, 1e-3f,
                    "the existing player-damage behaviour (7% of Max's own resolved max health) must be unchanged");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    Object.DestroyImmediate(maxGo);
                    Object.DestroyImmediate(sentinelGo);
                    Object.DestroyImmediate(enemyGo);
                    if (firstBoltGo != null) Object.DestroyImmediate(firstBoltGo);
                    if (secondBoltGo != null) Object.DestroyImmediate(secondBoltGo);
                }
                finally { LogAssert.ignoreFailingMessages = false; }
            }
        }
    }
}
