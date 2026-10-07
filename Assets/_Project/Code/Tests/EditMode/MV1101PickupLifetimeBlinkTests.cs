using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1101, Lee (2026-10-06): "Parts stay available for too long. Should need to take risk to get
    /// them. Energy needs similar." Before this ticket, a robot-dropped Part (<c>PickupKind.PowerCell</c>)
    /// sat on the ground for 30s with no warning (MV-626), and a dropped Energy Cell
    /// (<c>PickupKind.PowerCellSecondary</c>) never expired at all. This fails on base commit d30d293:
    /// the 30s/never-expire pair is still live, so the 10.1s retirement assertions below never happen and
    /// the blink assertions see a pickup whose renderers were never touched (<c>Pickup.IsBlinkHidden</c>
    /// does not exist on that commit either).
    ///
    /// Entry points: <c>PickupDirector.Tick(dt)</c> (made public by this ticket specifically so this test
    /// can drive deterministic ageing without reflecting into a private lifecycle method) and the fully
    /// public <c>PlacePartsCache</c>. <c>OnRobotDied</c>/<c>OnFactoryDestroyed</c> are still reached via
    /// reflection, same idiom as every other PickupDirector test in this suite (MV626/MV672/MV948/MV646) —
    /// <c>PickupDirector.OnEnable</c>, where it subscribes to <c>DropSignals.RobotDied</c>/
    /// <c>HudSignals.FactoryDestroyed</c>, is not reliable off a fresh <c>AddComponent</c> outside Play
    /// Mode (see MV698WeaponCoreFinaleDropTests's own doc comment on <c>Pickup.OnEnable</c> for the same
    /// constraint), so routing through the real signals is not an option here; <c>OnRobotDied</c>/
    /// <c>OnFactoryDestroyed</c> themselves are the real drop-policy logic under test, reflection is only
    /// how any EditMode test in this codebase reaches a MonoBehaviour's own lifecycle/event-handler
    /// methods at all. Likewise <c>_live</c> is read via reflection, the same "not test-isolated otherwise"
    /// reasoning MV646/MV698 give for avoiding the global <c>Pickup.Active</c> registry.
    /// </summary>
    public sealed class MV1101PickupLifetimeBlinkTests
    {
        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _maxGo;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
            RigState.Reset();
            FactoryCensus.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();

            _maxGo = new GameObject("Max");
            _maxGo.tag = "Player";
            _maxGo.transform.position = new Vector3(1000f, 0f, 1000f);   // well outside every radius below
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            PickupWallet.Reset();
            DevTuning.Reset();
            RigState.Reset();
            FactoryCensus.Reset();
        }

        private static void InvokeOnRobotDied(PickupDirector director, Vector3 pos, EnemyKind kind) =>
            typeof(PickupDirector).GetMethod("OnRobotDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { pos, kind });

        private static void InvokeOnFactoryDestroyed(PickupDirector director, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("OnFactoryDestroyed", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { pos });

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private MowerHutch MakeHutch(string id)
        {
            var go = new GameObject($"hutch_{id}");
            _spawned.Add(go);
            var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
            hutch.Build();                               // Build() registers it with FactoryCensus
            hutch.SetId(id);
            return hutch;
        }

        [Test]
        public void RobotDroppedPartsAndEnergyCells_BlinkThenExpire_WhileCacheDeviceAndAPulledPartNeverDo()
        {
            // ---- AC: Parts + Energy Cells, dropped through the real robot-death drop path, blink then expire ----
            DevTuning.CellsPerLargeKill = 1f;     // exactly one Parts drop
            DevTuning.PowerCellDropRatio = 1f;    // exactly one Energy Cell drop from that same kill
            InvokeOnRobotDied(_director, Vector3.zero, EnemyKind.Heavy);   // Heavy: avoids the Supercell/Bruiser branch

            Pickup part = LiveList(_director).Single(p => p.Kind == PickupKind.PowerCell);
            Pickup energyCell = LiveList(_director).Single(p => p.Kind == PickupKind.PowerCellSecondary);

            _director.Tick(6.9f);   // age 6.9s -- inside the 7.0s warning threshold
            Assert.That(LiveList(_director), Has.Member(part).And.Member(energyCell),
                "at 6.9s both the Part and the Energy Cell must still be alive");
            Assert.That(part.IsBlinkHidden, Is.False, "steadily visible before the 3s warning window opens");
            Assert.That(energyCell.IsBlinkHidden, Is.False, "steadily visible before the 3s warning window opens");

            _director.Tick(0.1f);   // age 7.0s -- warning window opens, blink phase 0 (visible)
            Assert.That(part.IsBlinkHidden, Is.False, "blink phase 0 (from 7.0s) is visible");

            _director.Tick(0.1f);   // age 7.1s -- still phase 0
            _director.Tick(0.1f);   // age 7.2s -- crosses into blink phase 1 (hidden)
            Assert.That(part.IsBlinkHidden, Is.True, "the blink must alternate at 6Hz once inside the warning window");
            Assert.That(energyCell.IsBlinkHidden, Is.True, "an Energy Cell blinks on the same 6Hz warning as a Part");

            _director.Tick(0.2f);   // age 7.4s -- phase 2 (visible again)
            Assert.That(part.IsBlinkHidden, Is.False, "the blink keeps alternating, not just toggling once");
            Assert.That(energyCell.IsBlinkHidden, Is.False, "an Energy Cell's blink keeps alternating too, not just toggling once");

            _director.Tick(2.7f);   // age 10.1s -- past the 10s lifetime
            Assert.That(LiveList(_director), Has.No.Member(part), "a Part past its 10s lifetime must be retired");
            Assert.That(LiveList(_director), Has.No.Member(energyCell), "an Energy Cell past its 10s lifetime must be retired");
            Assert.That(part.gameObject.activeInHierarchy, Is.False, "an expired drop must be pooled (SetActive(false)), not destroyed");
            Assert.That(energyCell.gameObject.activeInHierarchy, Is.False, "an expired drop must be pooled (SetActive(false)), not destroyed");

            // ---- AC: never expire -- a shed's cell cache and a Morphing Module (Device) ----
            List<Pickup> cache = _director.PlacePartsCache(new Vector3(50f, 0f, 0f));
            Pickup cacheCell = cache.First(p => p.Kind == PickupKind.PowerCell);   // one of the 6-cell ring

            Assert.That(RigState.LockedCategoryIds().Any(), Is.True,
                "sanity: a fresh run-start baseline leaves a category locked");
            MowerHutch hutch = MakeHutch("shed1");
            // A fresh, map-less hutch's EnemySpawner has no authored area composition to draw from on
            // death -- an expected, harmless warning (MV-651), not a test failure.
            LogAssert.Expect(LogType.Warning, new Regex("empty composition"));
            hutch.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            Assert.IsFalse(hutch.IsAlive, "sanity: the shed must actually be destroyed before its drop is judged");
            int beforeFactoryDrop = LiveList(_director).Count;
            InvokeOnFactoryDestroyed(_director, new Vector3(60f, 0f, 0f));
            Pickup device = LiveList(_director).Skip(beforeFactoryDrop).Single(p => p.Kind == PickupKind.Device);

            _director.Tick(60f);   // 6x this ticket's own lifetime -- would retire anything actually tracked
            Assert.That(LiveList(_director), Has.Member(cacheCell), "a shed's cell cache must never expire");
            Assert.That(LiveList(_director), Has.Member(device), "a Morphing Module (Device) must never expire");
            Assert.That(cacheCell.IsBlinkHidden, Is.False, "a never-expiring drop must never enter the warning blink either");
            Assert.That(device.IsBlinkHidden, Is.False, "a never-expiring drop must never enter the warning blink either");

            // ---- AC: an active Magneto pull pauses the clock ----
            RigState.UnlockCategory("ENERGY");   // e_cel is a root node; AcquireCap needs its category unlocked
            RigState.AcquireCap("e_cel");
            RigState.AcquireCap("e_mag");   // level 1: 3m pull radius
            DevTuning.PowerCellDropRatio = 0f;   // isolate: only the Part below, no Energy Cell riding along
            InvokeOnRobotDied(_director, new Vector3(0f, 0f, 0f), EnemyKind.Heavy);
            Pickup pulledPart = LiveList(_director).Last(p => p.Kind == PickupKind.PowerCell);
            pulledPart.transform.position = new Vector3(2.5f, 0f, 0f);   // inside the 3m pull radius, outside 1.4m CollectRadius
            _maxGo.transform.position = Vector3.zero;

            _director.Tick(10.1f);   // one tick carrying 10.1s of elapsed time while the pull is active throughout

            Assert.That(LiveList(_director), Has.Member(pulledPart),
                "a pickup under an active Magneto pull must not expire mid-flight even once its ground lifetime has nominally elapsed");
        }
    }
}
