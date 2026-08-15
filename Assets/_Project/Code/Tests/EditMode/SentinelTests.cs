using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-362: the Wall/Gunner sentinels' physical shape (solid, non-trigger colliders — the same
    /// "must actually BE solid at runtime" checklist <see cref="ForceFieldBubbleTests"/>/<see
    /// cref="GateSolidityTests"/> already run for the other two structures a robot has to route
    /// around), the Wall's Cover-layer sight/shot blocking, and <see cref="Sentinel"/>'s shared
    /// friendly-fire and lifecycle rules (Team.Player — a robot can hit it, Max's own primary can't;
    /// no repair/recall, only destruction).
    /// </summary>
    public sealed class SentinelTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        [Test]
        public void WallSentinelBuildsASolidNonTriggerColliderAtTheAuthoredSize()
        {
            var go = new GameObject("Wall Sentinel");
            var wall = go.AddComponent<WallSentinel>();
            try
            {
                wall.Init(new Vector3(3f, 0f, -2f), Quaternion.identity, 200f);

                var col = wall.GetComponentInChildren<Collider>();
                Assert.IsNotNull(col, "the wall built no collider at all");
                Assert.IsFalse(col.isTrigger,
                    "a trigger would let a CharacterController pass straight through it (MV-378's bug)");

                var body = wall.transform.Find("Body");
                Assert.IsNotNull(body);
                Assert.That(body.localScale, Is.EqualTo(new Vector3(WallSentinel.Width, WallSentinel.Height, WallSentinel.Depth)));

                Assert.That(wall.Team, Is.EqualTo(Team.Player));
                Assert.That(wall.IsAlive, Is.True);
                Assert.That(wall.HealthCurrent, Is.EqualTo(200f).Within(1e-3f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void WallSentinelBodySitsOnTheCoverLayer()
        {
            if (!CoverLayer.Exists) { Assert.Ignore("no Cover layer in this project"); return; }

            var go = new GameObject("Wall Sentinel");
            var wall = go.AddComponent<WallSentinel>();
            try
            {
                wall.Init(Vector3.zero, Quaternion.identity, 200f);
                var body = wall.transform.Find("Body");
                Assert.That(body.gameObject.layer, Is.EqualTo(CoverLayer.Index),
                    "the wall must block shots and sight-lines the same way every other cover prop does");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GunnerSentinelBuildsASolidNonTriggerColliderAndHasFixedHp()
        {
            var go = new GameObject("Gunner Sentinel");
            var gunner = go.AddComponent<GunnerSentinel>();
            try
            {
                gunner.Init(new Vector3(1f, 0f, 1f), 60f, 7f, 0.6f);

                var col = gunner.GetComponentInChildren<Collider>();
                Assert.IsNotNull(col);
                Assert.IsFalse(col.isTrigger);
                Assert.That(gunner.HealthCurrent, Is.EqualTo(60f).Within(1e-3f));
                Assert.That(gunner.Team, Is.EqualTo(Team.Player));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ARobotCanDamageASentinelButMaxsOwnPrimaryCannot()
        {
            var go = new GameObject("Wall Sentinel");
            var wall = go.AddComponent<WallSentinel>();
            try
            {
                wall.Init(Vector3.zero, Quaternion.identity, 100f);

                wall.TakeDamage(new DamageInfo(30f, Vector3.zero, Vector3.forward, Team.Player));
                Assert.That(wall.HealthCurrent, Is.EqualTo(100f).Within(1e-3f),
                    "Max's own primary (Team.Player) must not damage his own deployment");

                wall.TakeDamage(new DamageInfo(30f, Vector3.zero, Vector3.forward, Team.Enemy));
                Assert.That(wall.HealthCurrent, Is.EqualTo(70f).Within(1e-3f),
                    "a robot (Team.Enemy) must be able to damage a sentinel");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ASentinelDiesAtZeroHpAndFiresDied()
        {
            var go = new GameObject("Gunner Sentinel");
            var gunner = go.AddComponent<GunnerSentinel>();
            gunner.Init(Vector3.zero, 10f, 7f, 0.6f);

            bool died = false;
            gunner.Died += _ => died = true;

            gunner.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));

            Assert.That(died, Is.True);
            Assert.That(gunner.IsAlive, Is.False);
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
            var go = new GameObject("Wall Sentinel");
            var wall = go.AddComponent<WallSentinel>();
            wall.Init(Vector3.zero, Quaternion.identity, 10f);

            bool alreadyRemovedWhenDiedFired = false;
            wall.Died += _ => alreadyRemovedWhenDiedFired = !Sentinel.Active.Contains(wall);

            wall.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));

            Assert.That(alreadyRemovedWhenDiedFired, Is.True,
                "the deployment slot must free the instant the sentinel dies, not wait for OnDisable/Destroy");
        }

        [Test]
        public void ActiveRegistryTracksDeployedSentinelsAndDestroyAllActiveClearsThem()
        {
            var wallGo = new GameObject("Wall Sentinel");
            var wall = wallGo.AddComponent<WallSentinel>();
            wall.Init(Vector3.zero, Quaternion.identity, 200f);

            var gunnerGo = new GameObject("Gunner Sentinel");
            var gunner = gunnerGo.AddComponent<GunnerSentinel>();
            gunner.Init(Vector3.one, 60f, 7f, 0.6f);

            Assert.That(Sentinel.Active.Count, Is.EqualTo(2));

            Sentinel.DestroyAllActive();

            Assert.That(Sentinel.Active.Count, Is.EqualTo(0));
        }
    }
}
