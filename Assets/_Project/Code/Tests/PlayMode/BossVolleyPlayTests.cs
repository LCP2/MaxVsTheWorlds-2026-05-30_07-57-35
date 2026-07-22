using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Big Bermuda's SECOND attack, driven for real (YT-157): the brood volley flings robots out of the
    /// side hatches, they arc onto the lawn, and from the moment they land they are ordinary robots that
    /// come for Max. PlayMode, because every claim here is about time and Unity lifecycle — the fling, the
    /// arc, the OnEnable that turns a thrown body into a chasing robot — none of which a struct can show.
    ///
    /// A green EditMode test proved the CADENCE (BroodVolleyTests); this proves the cadence is actually
    /// wired to a spawn, that the adds become real, and that the arena stays capped and kiteable — the
    /// things a passing sequencer test cannot see.
    /// </summary>
    public sealed class BossVolleyPlayTests
    {
        private GameObject _boss;
        private GameObject _player;
        private GameObject _hutch;

        private BigBermudaBoss Boss => _boss.GetComponent<BigBermudaBoss>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevTuning.Reset();
            FactoryCensus.Reset();

            // The boss exactly as the scaffold bakes it: a cube on a CharacterController, scaled big.
            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.name = "Big Bermuda";
            _boss.transform.position = new Vector3(0f, 2f, 33f);
            _boss.transform.localScale = new Vector3(3.5f, 3f, 3.5f);
            _boss.AddComponent<BigBermudaBoss>();

            // The fight never leaves Reposition without something to chase — and the adds need a Player
            // tag to acquire when they land.
            _player = new GameObject("Max") { tag = "Player" };
            _player.AddComponent<CharacterController>();
            _player.transform.position = new Vector3(0f, 0f, 20f);

            // Fast, tight volleys so the test is quick and deterministic.
            DevTuning.BossVolleyInterval = 2f;
            DevTuning.BossVolleyWindup = 0.6f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject go in new[] { _boss, _player, _hutch })
                if (go != null) Object.Destroy(go);
            // Any add root is a child of the boss's own cleanup (OnDestroy); belt-and-braces here.
            var stray = GameObject.Find("Brood Adds");
            if (stray != null) Object.Destroy(stray);
            DevTuning.Reset();
            FactoryCensus.Reset();
            yield return null;
        }

        /// <summary>Kill the yard's one factory — the last one wakes the boss (YT-92) — and run through the
        /// intro into the fight proper.</summary>
        private IEnumerator Wake()
        {
            _hutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutch.name = "Mower Hutch";
            _hutch.transform.position = new Vector3(0f, 1f, 0f);
            var hutch = _hutch.AddComponent<MowerHutch>();
            yield return null;

            hutch.TakeDamage(new DamageInfo(100000f, _hutch.transform.position, Vector3.forward, Team.Player));

            float t = 0f;
            while (t < 2.4f) { t += Time.deltaTime; yield return null; }   // through intro + wake flicker
        }

        private static List<RobotEnemy> Adds() =>
            Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                  .Where(r => r.name.StartsWith("Brood Add"))
                  .ToList();

        [UnityTest]
        public IEnumerator TheBossFlingsAdds_ThatBecomeRealChasingRobots()
        {
            yield return Wake();
            Assert.IsTrue(Boss.Engaged, "precondition: the boss must be awake and fighting");

            // Watch the telegraph precede the fling, then wait for at least one add to LAND (its own
            // component re-enabled) and be a live enemy.
            float sawWindup = 0f;
            RobotEnemy landed = null;
            float t = 0f;
            while (t < 10f && landed == null)
            {
                sawWindup = Mathf.Max(sawWindup, Boss.SpawnWindup01);
                landed = Adds().FirstOrDefault(r => r.enabled && r.IsAlive);
                t += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(sawWindup, 0f,
                "the hatches never telegraphed — the volley must open the hatches before it flings (YT-157).");
            Assert.IsNotNull(landed,
                "no add ever landed as a live robot — the volley cadence is not wired to a real spawn.");
            Assert.AreEqual(Team.Enemy, landed.Team, "a flung add must fight for the enemy, like any robot");
        }

        [UnityTest]
        public IEnumerator TheAddsStayCapped_SoTheArenaRemainsKiteable()
        {
            // A deliberately tight cap and a fat wave: the volley must refuse to breach the ceiling, or the
            // boss fight — the one time no factory bounds the robot count — becomes a wall of bodies (YT-74).
            DevTuning.BossMaxAdds = 3f;
            DevTuning.BossAddsPerVolley = 6f;
            DevTuning.BossVolleyInterval = 1.2f;

            yield return Wake();

            int peak = 0;
            float t = 0f;
            while (t < 12f)
            {
                peak = Mathf.Max(peak, Adds().Count(r => r.gameObject.activeInHierarchy));
                Assert.LessOrEqual(Adds().Count(r => r.gameObject.activeInHierarchy), 3,
                    "the arena is over the add cap — a volley flung past the ceiling");
                t += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(peak, 0, "no adds were ever flung, so the cap was never actually tested");
        }

        [UnityTest]
        public IEnumerator RammingStillHappens_TheVolleyIsAdditive()
        {
            // The signature attack is a SECOND attack, not a replacement: the charge cycle must still run.
            yield return Wake();

            var seen = new HashSet<BossAction>();
            float t = 0f;
            while (t < 12f && !(seen.Contains(BossAction.Charge) && seen.Contains(BossAction.Reposition)))
            {
                seen.Add(Boss.Action);
                t += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(seen.Contains(BossAction.Charge),
                "the boss never charged — the volley must be additive, not a replacement for ramming.");
        }
    }
}
