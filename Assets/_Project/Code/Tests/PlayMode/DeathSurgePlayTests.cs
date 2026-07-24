using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The death-throes surge (YT-182): destroying a factory shed should erupt, not go quiet — a
    /// burst of robots (and, on a roll, one "elite" Bruiser) crawls out of the wreck. Proves the
    /// burst actually fires at the instant of death (not from the normal spawn timer), that it never
    /// breaks the live-enemy cap, and that an elite can be forced in via DevTuning.
    /// </summary>
    public sealed class DeathSurgePlayTests
    {
        private GameObject _hutch;
        private GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "Ground";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(400f, 1f, 400f);

            _hutch = NewHutch(new Vector3(0f, 1f, 15f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { _hutch, _ground })
                if (go != null) Object.Destroy(go);
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        private static GameObject NewHutch(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Mower Hutch";
            go.transform.position = at;
            go.transform.localScale = new Vector3(3f, 2f, 3f);
            go.AddComponent<MowerHutch>();
            return go;
        }

        private static void Destroy(GameObject hutch) =>
            hutch.GetComponent<MowerHutch>().TakeDamage(
                new DamageInfo(100000f, hutch.transform.position, Vector3.forward, Team.Player));

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        [UnityTest]
        public IEnumerator DestroyingTheFactory_SpawnsABurstImmediately()
        {
            // Fresh factory, zero elapsed time: the normal spawn timer hasn't come round once yet
            // (spawnIntervalStart is 1.8s), so any robot present the very next frame after death can
            // only be the surge, not a coincidental first tick of the timer.
            var spawner = _hutch.GetComponent<EnemySpawner>();

            Destroy(_hutch);
            yield return null;

            Assert.Greater(spawner.Emitted, 0,
                "destroying the shed produced no robots at all — the wreck should erupt, not go quiet");
        }

        [UnityTest]
        public IEnumerator TheSurgeNeverBreaksTheLiveEnemyCap()
        {
            var spawner = _hutch.GetComponent<EnemySpawner>();
            Set(spawner, "maxLiveEnemies", 3);
            DevTuning.DeathSurgeBurstSize = 10f; // deliberately ask for more than the cap allows

            Destroy(_hutch);
            yield return null;

            Assert.LessOrEqual(spawner.LiveCount, 3,
                $"the surge put {spawner.LiveCount} robots on the field against a cap of 3");
        }

        [UnityTest]
        public IEnumerator ADeadFactoryWithNoRoomLeft_SurgesWithNothing()
        {
            var spawner = _hutch.GetComponent<EnemySpawner>();
            Set(spawner, "maxLiveEnemies", 0);
            DevTuning.DeathSurgeBurstSize = 5f;

            Destroy(_hutch);
            yield return null;

            Assert.AreEqual(0, spawner.LiveCount,
                "a factory with zero room on the field still surged robots onto it");
        }

        [UnityTest]
        public IEnumerator ADevTuningOverride_CanForceAGuaranteedElite()
        {
            var spawner = _hutch.GetComponent<EnemySpawner>();
            DevTuning.DeathSurgeBurstSize = 3f;
            DevTuning.DeathSurgeEliteChance = 1f; // guaranteed

            Destroy(_hutch);
            yield return null;

            Assert.Greater(spawner.LiveCountOf(EnemyKind.Bruiser), 0,
                "a guaranteed elite chance produced no Bruiser in the surge");
        }

        [UnityTest]
        public IEnumerator AZeroEliteChance_NeverForcesOneIn()
        {
            var spawner = _hutch.GetComponent<EnemySpawner>();
            // bruiserEvery/firstBruiserAt left at their defaults but with _emitted starting at 0, the
            // ordinary mix (YT-66) doesn't produce a bruiser until robot #3 — well past this burst.
            DevTuning.DeathSurgeBurstSize = 2f;
            DevTuning.DeathSurgeEliteChance = 0f;

            Destroy(_hutch);
            yield return null;

            Assert.AreEqual(0, spawner.LiveCountOf(EnemyKind.Bruiser),
                "a zero elite chance still produced a Bruiser in the surge");
        }

        [UnityTest]
        public IEnumerator TheBurstSizeGrows_WithTheInvasionLevel()
        {
            // Two identical spawners, one left at Invasion Level 0 and one fully escalated — same
            // shape as DifficultyDirectorPlayTests' toughness/cadence proofs.
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationRate = 0f;
            DevTuning.EscalationMax = 1f;
            DevTuning.EscalationPerShedBump = 1f;

            var atStart = _hutch.GetComponent<EnemySpawner>();
            Set(atStart, "maxLiveEnemies", 100);
            Destroy(_hutch);
            yield return null;
            int burstAtStart = atStart.LiveCount;

            DifficultyDirector.ReportShedDestroyed(); // fully escalated: level hits the ceiling

            var hutch2 = NewHutch(new Vector3(30f, 1f, 15f));
            var atMax = hutch2.GetComponent<EnemySpawner>();
            Set(atMax, "maxLiveEnemies", 100);
            Destroy(hutch2);
            yield return null;
            int burstAtMax = atMax.LiveCount;
            Object.Destroy(hutch2);

            Assert.Greater(burstAtMax, burstAtStart,
                $"a fully escalated Invasion Level did not grow the death-throes burst " +
                $"(start={burstAtStart}, max={burstAtMax})");
        }
    }
}
