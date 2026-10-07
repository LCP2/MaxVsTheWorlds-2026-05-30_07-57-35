using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1092 — Lee's device playthrough, 2026-10-06: "Enemy robots need to attack my robots - if my
    /// robots are closer than I am then enemies should attack them instead of me." Before this ticket,
    /// <c>RobotEnemy.RetargetIfNeeded</c> (MV-362) only ever weighed a Sentinel against Max — a captured
    /// (<see cref="Team.Player"/>) robot standing right next to an enemy never drew its aggro at all, and
    /// <c>BolterBolt.Detonate</c>'s own type-gate (<c>is PlayerHealth</c> / <c>is Sentinel</c>) would have
    /// silently swallowed a bolt that DID reach one even if targeting were fixed alone.
    ///
    /// One consolidated test (MV-465 Rule 1) carries every acceptance criterion as a sub-check, through
    /// real entry points throughout: the real Chase -&gt; Telegraph -&gt; Lunge state machine
    /// (<see cref="RobotEnemy.Tick"/>) drives a melee Rusher (Scrap Eel's real stand-in) into its real
    /// contact lunge and a ranged Bolter (Reef Bolter's real stand-in) into its real bolt fire; a captured
    /// robot is produced through <see cref="RobotEnemy.TryConvert(bool)"/>, the same production entry
    /// point RobotTrap's own conversion completion calls (MV-1035); <see cref="RobotEnemy.CurrentTarget"/>
    /// (public, MV-1015) is read for the engage decision itself rather than any private field; health is
    /// read through the real <see cref="IDamageable.TakeDamage"/> path for both Max and the captured
    /// robot. The Bolter's own bolt-flight is resolved the same way <c>MV622BolterEngagesSentinelTests</c>
    /// already does — firing a real bolt and reflection-invoking its private, already separately-tested
    /// <c>Detonate</c> instead of being at the mercy of <c>Time.deltaTime</c> in edit mode — never a
    /// shortcut around the retargeting decision itself, which only ever runs through the real
    /// <see cref="RobotEnemy.Tick"/>.
    ///
    /// Must fail on base commit d30d293: <c>RobotEnemy.RetargetIfNeeded</c> there never looks at
    /// <see cref="RobotEnemy.Converted"/> at all, so a melee enemy standing right next to a captured robot
    /// walks straight past it at Max instead, and a Bolter's bolt would (even if retargeted by hand) deal
    /// zero damage to one — see the PR's own quoted failure.
    /// </summary>
    public sealed class MV1092EnemyEngagesCapturedRobotTests
    {
        private const float Dt = 1f / 60f;
        private const int FiveSecondFrameCap = 300; // 300 * 1/60s = 5s, the ticket's own window

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RobotAwakeMethod = typeof(RobotEnemy).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo RobotCcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo BoltDetonateMethod = typeof(BolterBolt).GetMethod("Detonate", NonPublicInstance);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            RobotEnemy.ResetRegistry();
            RobotEnemy.ConversionCap = 1;
            LungeTokenPool.Reset();
            DevTuning.Reset();
            EnemyNavigation.Reset();
        }

        private static GameObject NewMax(out PlayerHealth health)
        {
            var go = new GameObject("MV1092 Max", typeof(CharacterController)) { tag = "Player" };
            health = go.AddComponent<PlayerHealth>();
            health.Initialize();
            return go;
        }

        /// <summary>A real robot body — AddComponent doesn't reliably run Awake/OnEnable outside Play
        /// mode (same idiom <c>MV1089CaptureConversionTests.NewRigRobot</c> uses), minus the RobotRig
        /// this ticket's assertions never need.</summary>
        private static RobotEnemy NewRobot(string name, Vector3 position, in EnemyArchetype archetype)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            RobotCcField.SetValue(e, cc); // Awake below also sets this; stamped first so nothing reads null in between
            RobotAwakeMethod.Invoke(e, null);
            e.Apply(archetype);
            RobotOnEnableMethod.Invoke(e, null);
            go.transform.position = position;
            // MV1089's own NewRigRobot precedent: a CharacterController's physics-side position caches
            // separately from transform.position until synced, or SafeMove's collision math resolves
            // against the stale (pre-move) spot instead of where this robot was actually placed.
            Physics.SyncTransforms();
            return e;
        }

        /// <summary>Damage resolution only — bypasses bolt flight, the same shortcut and the same
        /// reasoning <c>MV622BolterEngagesSentinelTests.InvokeDetonate</c> already uses (already proven
        /// by <c>BolterBoltTests.WithinHitRadius...</c>; <c>Destroy(gameObject)</c> inside Detonate is
        /// edit-mode-illegal, hence the log suppression).</summary>
        private static void InvokeDetonate(BolterBolt bolt)
        {
            LogAssert.ignoreFailingMessages = true;
            try { BoltDetonateMethod.Invoke(bolt, null); }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void DestroyAll(List<GameObject> objects)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                foreach (GameObject go in objects)
                    if (go != null) Object.DestroyImmediate(go);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            objects.Clear();
        }

        [Test]
        public void EnemyEngagesCapturedRobotsOverMax_MV1092()
        {
            RobotEnemy.ConversionCap = 3; // three independent captures across the three parts below
            var live = new List<GameObject>();
            try
            {
                // === Part 1 (melee, AC1 first half): a Scrap Eel (Rusher) closer to a captured robot
                // (1.5m) than to Max (3m) must engage and damage the captured robot, never Max.
                GameObject maxAGo = NewMax(out PlayerHealth maxAHealth);
                live.Add(maxAGo);
                RobotEnemy capturedA = NewRobot("MV1092 pre-capture A", new Vector3(3f, 0f, 1.5f), EnemyArchetype.Rusher);
                live.Add(capturedA.gameObject);
                Assert.IsTrue(capturedA.TryConvert(requirePortExposed: false),
                    "fixture: TryConvert(false) is TRAP's own real capture-completion entry point (MV-1035) and must succeed with a fresh ConversionCap");

                RobotEnemy scrapEel = NewRobot("MV1092 Scrap Eel", new Vector3(3f, 0f, 0f), EnemyArchetype.Rusher);
                live.Add(scrapEel.gameObject);

                scrapEel.Tick(Dt);
                Assert.AreSame(capturedA.transform, scrapEel.CurrentTarget,
                    "AC1: a captured robot 1.5m from an enemy 3m from Max must outrank Max on retarget");

                bool capturedATookDamage = false;
                for (int i = 1; i < FiveSecondFrameCap && !capturedATookDamage; i++)
                {
                    scrapEel.Tick(Dt);
                    Assert.AreEqual(maxAHealth.Max, maxAHealth.Current, 0f,
                        $"AC1 tick {i}: Max must take 0 damage while a closer captured robot is being engaged");
                    if (capturedA.HealthCurrent < capturedA.MaxHealth) capturedATookDamage = true;
                }
                Assert.IsTrue(capturedATookDamage,
                    "AC1: the captured robot must have lost health within 5s of a closer enemy engaging it");
                Assert.AreEqual(maxAHealth.Max, maxAHealth.Current, 0f,
                    "AC1: Max must still be at full health once the captured robot has taken damage");

                // === AC1 second half: killing the captured robot must return the Scrap Eel's target to
                // Max, who then takes damage within the following 5s.
                capturedA.TakeDamage(new DamageInfo(capturedA.MaxHealth + 100f, capturedA.transform.position,
                    Vector3.up, Team.Enemy));
                Assert.IsFalse(capturedA.IsAlive, "fixture: the captured robot must be dead before the fallback is checked");

                // The Scrap Eel may still be mid-Lunge/Recover on the kill frame (it never switches
                // target mid-attack, per the ticket's own rule 3) — it falls back to Max on its next
                // real Chase tick, not necessarily this literal frame, so this polls rather than
                // asserting the very next Tick().
                bool targetFellBackToMax = false;
                bool maxATookDamage = false;
                for (int i = 0; i < FiveSecondFrameCap && !maxATookDamage; i++)
                {
                    scrapEel.Tick(Dt);
                    if (!targetFellBackToMax && scrapEel.CurrentTarget == maxAGo.transform) targetFellBackToMax = true;
                    if (maxAHealth.Current < maxAHealth.Max) maxATookDamage = true;
                }
                Assert.IsTrue(targetFellBackToMax,
                    "AC1: the Scrap Eel's target must fall back to Max within 5s once the captured robot it was engaging has died");
                Assert.IsTrue(maxATookDamage,
                    "AC1: once its captured target is dead, the Scrap Eel must resume damaging Max within 5s");

                DestroyAll(live);

                // === Part 2 (ranged, AC1 "repeat with a ranged kind"): a Reef Bolter (Bolter) closer to
                // a captured robot (4.5m, inside its own standoff band) than to Max (6m) must engage it,
                // and its bolt must damage the captured robot, never Max.
                GameObject maxBGo = NewMax(out PlayerHealth maxBHealth);
                live.Add(maxBGo);
                RobotEnemy capturedB = NewRobot("MV1092 pre-capture B", new Vector3(6f, 0f, 4.5f), EnemyArchetype.Rusher);
                live.Add(capturedB.gameObject);
                Assert.IsTrue(capturedB.TryConvert(requirePortExposed: false),
                    "fixture: TryConvert(false) must succeed with a fresh ConversionCap");

                RobotEnemy reefBolter = NewRobot("MV1092 Reef Bolter", new Vector3(6f, 0f, 0f), EnemyArchetype.Bolter);
                live.Add(reefBolter.gameObject);

                GameObject boltGo = null;
                for (int i = 0; i < FiveSecondFrameCap && boltGo == null; i++)
                {
                    reefBolter.Tick(Dt);
                    Assert.AreEqual(maxBHealth.Max, maxBHealth.Current, 0f,
                        $"AC1 (ranged) tick {i}: Max must take 0 damage while a closer captured robot is being engaged");
                    boltGo = GameObject.Find("BolterBolt (stand-in)");
                }
                Assert.IsNotNull(boltGo, "AC1 (ranged): the Reef Bolter must fire within 5s at a captured robot closer than Max");
                live.Add(boltGo);

                Assert.AreSame(capturedB.transform, reefBolter.CurrentTarget,
                    "AC1 (ranged): the Reef Bolter that fired this bolt must have been engaged on the captured robot, not Max");

                float capturedBHealthBeforeHit = capturedB.HealthCurrent;
                InvokeDetonate(boltGo.GetComponent<BolterBolt>());

                Assert.Less(capturedB.HealthCurrent, capturedBHealthBeforeHit,
                    "AC1 (ranged): a bolt fired at an engaged captured robot must damage it");
                Assert.AreEqual(maxBHealth.Max, maxBHealth.Current, 0f,
                    "AC1 (ranged): a bolt aimed at the captured robot must never also damage Max");

                DestroyAll(live);

                // === AC2: a captured robot outside the 10m aggro radius (12m from the enemy) must be
                // ignored in favour of Max (3m from the enemy).
                GameObject maxCGo = NewMax(out PlayerHealth maxCHealth);
                live.Add(maxCGo);
                RobotEnemy capturedC = NewRobot("MV1092 pre-capture C", new Vector3(15f, 0f, 0f), EnemyArchetype.Rusher);
                live.Add(capturedC.gameObject);
                Assert.IsTrue(capturedC.TryConvert(requirePortExposed: false),
                    "fixture: TryConvert(false) must succeed with a fresh ConversionCap");

                RobotEnemy enemyC = NewRobot("MV1092 far-ignore enemy", new Vector3(3f, 0f, 0f), EnemyArchetype.Rusher);
                live.Add(enemyC.gameObject);

                enemyC.Tick(Dt);
                Assert.AreSame(maxCGo.transform, enemyC.CurrentTarget,
                    "AC2: a captured robot 12m away (beyond the 10m aggro radius) must never outrank Max 3m away");

                bool maxCTookDamage = false;
                for (int i = 1; i < FiveSecondFrameCap && !maxCTookDamage; i++)
                {
                    enemyC.Tick(Dt);
                    Assert.AreEqual(capturedC.MaxHealth, capturedC.HealthCurrent, 0f,
                        $"AC2 tick {i}: the far captured robot must never take damage while ignored");
                    if (maxCHealth.Current < maxCHealth.Max) maxCTookDamage = true;
                }
                Assert.IsTrue(maxCTookDamage, "AC2: Max must take damage within 5s since the far captured robot is ignored");
            }
            finally
            {
                DestroyAll(live);
            }
        }
    }
}
