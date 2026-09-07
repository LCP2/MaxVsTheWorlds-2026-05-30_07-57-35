using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-705's Sludge Drone: killing it spawns two Rushers at half health either side of the death
    /// point, tags them <c>NoReplicate</c>, and leaves a 2 m/4 s sludge puddle behind. EditMode only,
    /// reflection-driven for the same reason every other kind's test here is (repo convention):
    /// <c>Awake</c>/<c>OnEnable</c> are not reliably invoked outside Play mode, so <c>_cc</c> is stamped
    /// by hand (same idiom as <c>MV691PipeTurretTests.NewTurret</c>) and the assertions read
    /// <c>RobotEnemy</c>/<c>SludgePuddle</c> instances via <c>Object.FindObjectsByType</c> rather than
    /// the OnEnable-populated <c>RobotEnemy.Active</c> registry.
    ///
    /// Fails on the MV-701 merge commit (5314c85), the base commit named in the ticket: at that commit
    /// <c>EnemyKind</c> is only <c>{ Rusher, Bruiser, Heavy, Brute, Gunner, Launcher, Blinker, Bolter }</c>
    /// — no <c>Sludger</c> member and no <c>SludgePuddle</c> class exist yet, so this file fails to
    /// COMPILE at that commit (CS0117/CS0246) before a single assertion runs.
    /// </summary>
    public sealed class MV705SludgeDroneTests
    {
        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            SludgePuddle.ResetRegistry();
            foreach (var stray in Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            SludgePuddle.ResetRegistry();
            DevTuning.Reset();
            foreach (var r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                Object.DestroyImmediate(r.gameObject);
            foreach (var p in Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
        }

        // ------------------------------------------------------------------ helpers

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewSludger(Vector3 position)
        {
            var go = new GameObject("Enemy Sludger");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake/OnEnable (same note as MV691PipeTurretTests.NewTurret), so _cc
            // — normally seeded there — has to be stamped by hand.
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Sludger));
            return e;
        }

        // ------------------------------------------------------------------ AC1

        [Test]
        public void KillingASludger_SpawnsTwoHalfHealthRushersAndASludgePuddle_ThatDespawnsAfterFourSeconds()
        {
            RobotEnemy sludger = NewSludger(new Vector3(5f, 0f, 3f));
            Vector3 deathPos = sludger.transform.position;

            sludger.TakeDamage(new DamageInfo(9999f, deathPos, Vector3.forward, Team.Player));

            var rushers = new List<RobotEnemy>();
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (r.Kind == EnemyKind.Rusher) rushers.Add(r);

            Assert.AreEqual(2, rushers.Count, "a Sludger's death must spawn exactly two Rushers");
            foreach (RobotEnemy r in rushers)
            {
                Assert.LessOrEqual(Vector3.Distance(r.transform.position, deathPos), 1f,
                    "each split Rusher must land within 1 m of the Sludger's death position");
                Assert.AreEqual(r.MaxHealth * 0.5f, r.HealthCurrent, 0.01f,
                    "each split Rusher must spawn at exactly 50% of its own MaxHealth");
                Assert.IsTrue(r.NoReplicate, "a freshly split Rusher must be tagged NoReplicate");
            }

            SludgePuddle[] puddles = Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None);
            Assert.AreEqual(1, puddles.Length, "exactly one sludge puddle must be left at the death position");
            SludgePuddle puddle = puddles[0];
            Assert.AreEqual(2f, puddle.Radius, 0.001f, "the puddle's radius must be the ticket's own 2 m");
            Assert.AreEqual(4f, puddle.RemainingLife, 0.01f,
                "the puddle's remaining life must start at the ticket's own 4 s");

            puddle.Tick(4.1f);

            Assert.AreEqual(0, Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None).Length,
                "advancing 4.1 s must despawn the puddle");
        }
    }
}
