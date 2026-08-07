using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// MV-295: a world-authored parts cache (<see cref="PickupDirector.SeedPartsCache"/>) used to
    /// scatter its parts within <see cref="PickupDirector.CollectRadius"/> of each other, so a single
    /// walk-over through the cache's centre collected every part in the same frame — a playtest saw
    /// what read as "one box" bank several parts at once. Each part in a cache must still cost its own
    /// separate walk-over.
    /// </summary>
    public sealed class PartsCachePlayTests
    {
        private GameObject _director;
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PickupWallet.Reset();
            yield return null;
        }

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
            // A PickupDirector self-installs at PlayMode bootstrap and persists across the run, so it
            // would double every drop against our own director (RobotDropPlayTests' lesson).
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            yield return null;

            _director = new GameObject("PickupDirector");
            _director.AddComponent<PickupDirector>();
            yield return null;
        }

        private static int LivePartCount() => Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length;

        [UnityTest]
        public IEnumerator StandingAtTheCacheCentre_BanksAtMostOnePart_NotTheWholeCache()
        {
            yield return NewDirector();
            var director = _director.GetComponent<PickupDirector>();

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.transform.position = Vector3.zero;

            director.SeedPartsCache(Vector3.zero, count: 3);
            yield return null;   // walk-over check runs in Update

            Assert.That(PickupWallet.PartsBanked, Is.LessThanOrEqualTo(1),
                "one visit to a cache's centre must never bank more than one part at once");
        }

        [UnityTest]
        public IEnumerator WalkingToEveryPartInTheCache_BanksExactlyOnePerVisit()
        {
            yield return NewDirector();
            var director = _director.GetComponent<PickupDirector>();

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.transform.position = new Vector3(50f, 0f, 50f);   // far away while the cache seeds

            director.SeedPartsCache(Vector3.zero, count: 3);
            yield return null;

            Vector3[] positions = new Vector3[LivePartCount()];
            int i = 0;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                positions[i++] = p.transform.position;
            Assert.That(positions.Length, Is.EqualTo(3), "the cache must seed exactly the requested count");

            int banked = 0;
            foreach (Vector3 pos in positions)
            {
                _max.transform.position = pos;
                yield return null;
                Assert.That(PickupWallet.PartsBanked, Is.EqualTo(++banked),
                    "each individual part must bank exactly one — never a batch grant from a single box");
                _max.transform.position = new Vector3(50f, 0f, 50f);   // step back off every pickup
                yield return null;
            }

            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(3), "all three parts must eventually bank, one at a time");
        }
    }
}
