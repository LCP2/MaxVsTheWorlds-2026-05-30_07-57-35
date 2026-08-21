using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Robot drops wired up for real (YT-131, recut WV-226, MV-401): the large tier drops a guaranteed
    /// number of power cells every kill; the small tier drops nothing at all; and Max collects by
    /// walking over them — all with no scene wiring. Parts drop specifically from a Bruiser kill
    /// (MV-401) — in these tests there is no live <c>AreaAccumulationDirector</c>, so
    /// <c>PickupDirector.IsLastBruiserInArea</c> falls back to "every Bruiser kill drops a part" (the
    /// same flat-fallback idiom cells use outside a live area context) rather than waiting for a
    /// specific last-Bruiser-in-the-arena signal these tests have no area composition to provide.
    /// </summary>
    public sealed class RobotDropPlayTests
    {
        private GameObject _director;
        private GameObject _max;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
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
        public IEnumerator ALargeKillDropsOnePartAndSomeCells()
        {
            yield return NewDirector();

            DropSignals.EmitRobotDied(new Vector3(5f, 0f, 5f), EnemyKind.Bruiser);
            yield return null;

            int expectedCells = Mathf.RoundToInt(CellEconomyTuning.DefaultCellsPerLargeKill);
            Assert.That(LivePickups(PickupKind.Supercell), Is.EqualTo(1), "a large kill must drop exactly one part");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(expectedCells),
                $"a large kill must drop {expectedCells} power cell(s)");
        }

        [UnityTest]
        public IEnumerator HeavyAndBruteKillsDropCellsButNeverAPart()
        {
            // v0.5 recut spec §5, MV-224: heavy/brute count as "large" for cell purposes. MV-401
            // narrows the part trigger specifically to a Bruiser kill, though — heavy and brute pace
            // cells the same as a bruiser kill, but must never drop a part themselves.
            yield return NewDirector();

            DropSignals.EmitRobotDied(new Vector3(5f, 0f, 5f), EnemyKind.Heavy);
            DropSignals.EmitRobotDied(new Vector3(-5f, 0f, 5f), EnemyKind.Brute);
            yield return null;

            int expectedCells = Mathf.RoundToInt(CellEconomyTuning.DefaultCellsPerLargeKill) * 2;
            Assert.That(LivePickups(PickupKind.Supercell), Is.EqualTo(0),
                "only a Bruiser kill triggers a part (MV-401) — heavy and brute kills must never drop one");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(expectedCells),
                "heavy and brute kills must drop power cells the same as a bruiser kill");
        }

        [UnityTest]
        public IEnumerator ASmallKillDropsNothingAtAll()
        {
            // WV-226: the small tier drops nothing at all — no roll, no chance, no cell trickle.
            yield return NewDirector();

            DropSignals.EmitRobotDied(new Vector3(5f, 0f, 5f), EnemyKind.Rusher);
            yield return null;

            Assert.That(LivePickups(PickupKind.Supercell) + LivePickups(PickupKind.PowerCell), Is.EqualTo(0),
                "the small robot tier must never drop a part or a power cell");
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

            Assert.That(PickupWallet.PowerCells, Is.GreaterThanOrEqualTo(PickupWallet.SupercellCellValue),
                "walking onto the drop must bank power cells and the Supercell's instant +10 (MV-519) — walk-over collection, no button");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(0), "collected cells must leave the ground");
        }

        [UnityTest]
        public IEnumerator CollectedPickupsAreReturnedToThePool_NotLeaked()
        {
            yield return NewDirector();

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.transform.position = Vector3.zero;

            // Two large-kills at Max's feet: everything is collected, and the second wave should
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
            int expectedCells = Mathf.RoundToInt(CellEconomyTuning.DefaultCellsPerLargeKill);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(expectedCells * 2), "both waves of cells banked");
        }

        [UnityTest]
        public IEnumerator SuccessiveBruiserKillsEachDropAPart_NotGatedToEveryNth()
        {
            // MV-401 replaces the old every-Nth-large-kill pacing: three Bruiser kills in a row must
            // drop three parts, not just the last (or every 4th, per the old default interval).
            yield return NewDirector();

            for (int i = 0; i < 3; i++)
                DropSignals.EmitRobotDied(new Vector3(i * 3f, 0f, 0f), EnemyKind.Bruiser);
            yield return null;

            Assert.That(LivePickups(PickupKind.Supercell), Is.EqualTo(3),
                "every Bruiser kill drops its own part — no pacing interval gates it anymore");
        }

        [UnityTest]
        public IEnumerator PartsKeepDroppingPastTheOldSevenPartCap()
        {
            // WV-228: parts are universal upgrade tokens now, not a five/seven-and-done unique table
            // (YT-133) — a long run must be able to earn far more than the old catalog's size. Every
            // kill here is a Bruiser, so MV-401's fallback ("no live area context → every Bruiser kill
            // drops a part") fires on all of them.
            yield return NewDirector();

            int total = UpgradeCatalog.AllKinds.Length;
            for (int i = 0; i < total + 5; i++)
                DropSignals.EmitRobotDied(new Vector3(i * 3f, 0f, 0f), EnemyKind.Bruiser);
            yield return null;

            Assert.That(LivePickups(PickupKind.Supercell), Is.EqualTo(total + 5),
                "a part must drop on every paced kill, with no cap once the old catalog's kinds are exhausted");
        }
    }
}
