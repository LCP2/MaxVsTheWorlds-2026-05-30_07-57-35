using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Robot drops wired up for real (YT-131): the tough tier drops parts + power cells, the light
    /// tier drops nothing, and Max collects by walking over them — all with no scene wiring.
    /// </summary>
    public sealed class RobotDropPlayTests
    {
        private GameObject _director;
        private GameObject _max;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PickupWallet.Reset();
            if (_max != null) Object.Destroy(_max);
            if (_director != null) Object.Destroy(_director);
            yield return null;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.Destroy(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            yield return null;
        }

        private IEnumerator NewDirector()
        {
            PickupWallet.Reset();
            // A PickupDirector self-installs at PlayMode bootstrap and persists across the run, so it
            // would receive the same DropSignals as our test's director and double every drop. Clear
            // any existing one first so this test owns exactly one director.
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            yield return null;

            _director = new GameObject("PickupDirector");
            _director.AddComponent<PickupDirector>();
            yield return null;   // OnEnable subscribes to DropSignals
        }

        private static int LivePickups(PickupKind kind)
        {
            int n = 0;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                if (p.gameObject.activeInHierarchy && p.Kind == kind) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator ABruiserDeathDropsOnePartAndSomeCells()
        {
            yield return NewDirector();

            DropSignals.EmitRobotDied(new Vector3(5f, 0f, 5f), EnemyKind.Bruiser);
            yield return null;

            Assert.That(LivePickups(PickupKind.Part), Is.EqualTo(1), "a bruiser must drop exactly one part");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(PickupDirector.CellsPerDrop),
                $"a bruiser must drop {PickupDirector.CellsPerDrop} power cells");
        }

        [UnityTest]
        public IEnumerator ARusherDeathDropsNothing()
        {
            yield return NewDirector();

            DropSignals.EmitRobotDied(new Vector3(5f, 0f, 5f), EnemyKind.Rusher);
            yield return null;

            Assert.That(LivePickups(PickupKind.Part) + LivePickups(PickupKind.PowerCell), Is.EqualTo(0),
                "the light rusher swarm must not carpet the lawn in loot — only the tough tier drops");
        }

        [UnityTest]
        public IEnumerator WalkingOverACellBanksItAndRemovesIt()
        {
            yield return NewDirector();

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.transform.position = new Vector3(20f, 0f, 20f); // far from the drop for now

            DropSignals.EmitRobotDied(new Vector3(0f, 0f, 0f), EnemyKind.Bruiser);
            yield return null;
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "nothing banked while Max is across the yard");

            _max.transform.position = Vector3.zero;   // walk onto the pile
            yield return null;   // director's Update does the walk-over check

            Assert.That(PickupWallet.PowerCells, Is.GreaterThan(0),
                "walking onto the drop must bank power cells — walk-over collection, no button");
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(1), "and pick up the part");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(0), "collected cells must leave the ground");
        }

        [UnityTest]
        public IEnumerator CollectedPickupsAreReturnedToThePool_NotLeaked()
        {
            yield return NewDirector();

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.transform.position = Vector3.zero;

            // Two bruiser deaths at Max's feet: everything is collected, and the second wave should
            // reuse the first wave's pooled objects rather than spawning a second full set.
            DropSignals.EmitRobotDied(Vector3.zero, EnemyKind.Bruiser);
            yield return null;
            yield return null;
            int afterFirst = Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            DropSignals.EmitRobotDied(Vector3.zero, EnemyKind.Bruiser);
            yield return null;
            yield return null;
            int afterSecond = Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Assert.That(afterSecond, Is.EqualTo(afterFirst),
                "the second drop must reuse pooled pickups, not leak a fresh set each time");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(PickupDirector.CellsPerDrop * 2),
                "both waves of cells banked");
        }
    }
}
