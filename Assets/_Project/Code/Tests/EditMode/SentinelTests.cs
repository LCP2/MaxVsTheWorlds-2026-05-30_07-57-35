using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-362, restructured MV-422 (Wall deleted entirely — one sentinel only): the sentinel's
    /// physical shape (a solid, non-trigger collider — the same "must actually BE solid at runtime"
    /// checklist <see cref="ForceFieldBubbleTests"/>/<see cref="GateSolidityTests"/> already run for
    /// the other structures a robot has to route around) and <see cref="Sentinel"/>'s friendly-fire
    /// and lifecycle rules (Team.Player — a robot can hit it, Max's own primary can't; no repair/
    /// recall, only destruction).
    /// </summary>
    public sealed class SentinelTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static Sentinel NewSentinel(Vector3 position, float maxHp, GameObject go)
        {
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
            return sentinel;
        }

        [Test]
        public void BuildsASolidNonTriggerColliderAndReadsBackTheGivenHp()
        {
            var go = new GameObject("Sentinel");
            try
            {
                var sentinel = NewSentinel(new Vector3(1f, 0f, 1f), 60f, go);

                var col = sentinel.GetComponentInChildren<Collider>();
                Assert.IsNotNull(col, "the sentinel built no collider at all");
                Assert.IsFalse(col.isTrigger,
                    "a trigger would let a CharacterController pass straight through it (MV-378's bug)");

                Assert.That(sentinel.Team, Is.EqualTo(Team.Player));
                Assert.That(sentinel.IsAlive, Is.True);
                Assert.That(sentinel.HealthCurrent, Is.EqualTo(60f).Within(1e-3f));
                Assert.That(sentinel.ReadoutName, Is.EqualTo("SENTINEL"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ARobotCanDamageASentinelButMaxsOwnPrimaryCannot()
        {
            var go = new GameObject("Sentinel");
            try
            {
                var sentinel = NewSentinel(Vector3.zero, 100f, go);

                sentinel.TakeDamage(new DamageInfo(30f, Vector3.zero, Vector3.forward, Team.Player));
                Assert.That(sentinel.HealthCurrent, Is.EqualTo(100f).Within(1e-3f),
                    "Max's own primary (Team.Player) must not damage his own deployment");

                sentinel.TakeDamage(new DamageInfo(30f, Vector3.zero, Vector3.forward, Team.Enemy));
                Assert.That(sentinel.HealthCurrent, Is.EqualTo(70f).Within(1e-3f),
                    "a robot (Team.Enemy) must be able to damage a sentinel");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ASentinelDiesAtZeroHpAndFiresDied()
        {
            var go = new GameObject("Sentinel");
            var sentinel = NewSentinel(Vector3.zero, 10f, go);

            bool died = false;
            sentinel.Died += _ => died = true;

            sentinel.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));

            Assert.That(died, Is.True);
            Assert.That(sentinel.IsAlive, Is.False);
            // Die() calls DestroyImmediate outside play mode — no manual cleanup needed/possible here.
        }

        [Test]
        public void DyingSentinelIsRemovedFromActiveBeforeTheDiedEventFires()
        {
            // MV-397: a destroyed sentinel's slot must free immediately, not only once OnDisable
            // eventually runs (which, unlike this test's edit-mode DestroyImmediate, is deferred to
            // end-of-frame for the real game's Destroy() call). Asserting the removal has already
            // happened by the time Died fires — rather than merely after TakeDamage returns — is what
            // actually distinguishes "removed synchronously in Die()" from "removed via OnDisable".
            var go = new GameObject("Sentinel");
            var sentinel = NewSentinel(Vector3.zero, 10f, go);

            bool alreadyRemovedWhenDiedFired = false;
            sentinel.Died += _ => alreadyRemovedWhenDiedFired = !Sentinel.Active.Contains(sentinel);

            sentinel.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));

            Assert.That(alreadyRemovedWhenDiedFired, Is.True,
                "the deployment slot must free the instant the sentinel dies, not wait for OnDisable/Destroy");
        }

        [Test]
        public void ActiveRegistryTracksDeployedSentinelsAndDestroyAllActiveClearsThem()
        {
            var go1 = new GameObject("Sentinel A");
            NewSentinel(Vector3.zero, 60f, go1);

            var go2 = new GameObject("Sentinel B");
            NewSentinel(Vector3.one, 60f, go2);

            Assert.That(Sentinel.Active.Count, Is.EqualTo(2));

            Sentinel.DestroyAllActive();

            Assert.That(Sentinel.Active.Count, Is.EqualTo(0));
        }
    }
}
