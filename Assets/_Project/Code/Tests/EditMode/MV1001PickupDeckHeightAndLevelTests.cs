using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1001 — Lee's TestFlight play of World 2, 2026-09-28: fighting on an upper deck, no cells or
    /// parts ever appeared up there. Root cause, read from `main` `8c86feb`: <c>Pickup.Place</c> hard-
    /// coded its base Y to <c>FloatHeight</c> off the ground plane, discarding the drop's own Y, so
    /// every pickup hovered above the FLOOR under any deck; and <c>PickupDirector</c>'s walk-over/Magneto
    /// checks were planar (XZ-only), so a Max on the floor beneath a deck could collect a deck drop and
    /// a Max on the deck could never reach one.
    ///
    /// Fails on 8c86feb — see the fix comment for the quoted failure output.
    ///
    /// EditMode only. Reflection drives <c>PickupDirector.Update()</c>/<c>SpawnDrop</c> directly (same
    /// idiom as <see cref="MV439CellCapacityRefusalTests"/>/<see cref="MV848CellMagnetoTests"/>), and
    /// loads World 2's real config plus seeds <c>EnemyNavigation.Map</c> via <c>BackyardPath</c>'s private
    /// field (same idiom as <see cref="MV944FloorDeckCrossLevelDamageTests"/>) so <c>CombatLevel.SameLevel</c>
    /// has a live map to resolve floor-vs-deck against instead of failing open.
    /// </summary>
    public sealed class MV1001PickupDeckHeightAndLevelTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo PickupFloatHeightField =
            typeof(Pickup).GetField("FloatHeight", BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject _pathGo;
        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _maxGo;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            EnemyNavigation.Reset();
            PickupWallet.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_pathGo != null) { Object.DestroyImmediate(_pathGo); _pathGo = null; }
            if (_directorGo != null) { Object.DestroyImmediate(_directorGo); _directorGo = null; }
            if (_maxGo != null) { Object.DestroyImmediate(_maxGo); _maxGo = null; }
            _director = null;
        }

        private static void InvokeUpdate(PickupDirector director) =>
            typeof(PickupDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static Pickup SpawnCellAt(PickupDirector director, Vector3 pos) =>
            (Pickup)typeof(PickupDirector).GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { PickupKind.PowerCell, pos, default(MaxWorlds.Upgrades.PartKind), default(AbilityKind) });

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        [Test]
        public void DeckDropRestsAtDeckHeight_AndCollectsOnlyFromTheSameLevel()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            Assert.IsNotNull(PickupFloatHeightField, "Pickup.FloatHeight went missing");

            // a12 ("Pipe Gallery") authors a garrisoned deck in-place over its own floor (MV-896) — the
            // exact "ground floor + upper walkway" shape MV-944 already exercises against.
            MapEntity deck = map.Entity("a12_deck1");
            Assert.IsNotNull(deck, "MV-1001: world2_config.json must still author a12's own deck");
            Assert.Greater(deck.height, 0f,
                "setup failure: a12_deck1 must be elevated above the floor for this to be a floor/deck test at all");

            _pathGo = new GameObject("MV1001-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();

            _maxGo = new GameObject("Max");
            _maxGo.tag = "Player";

            // A robot dies dead-centre on the deck, at the deck's own top height — the drop's Y this
            // ticket says must not be discarded.
            var deckDeathPos = new Vector3(deck.x, deck.height, deck.z);
            Pickup drop = SpawnCellAt(_director, deckDeathPos);
            Assert.IsNotNull(drop, "setup failure: the reserve must have room for this drop to spawn at all");

            float floatHeight = (float)PickupFloatHeightField.GetValue(null);
            float expectedY = map.SurfaceHeightAt(deckDeathPos) + floatHeight;
            Assert.That(drop.transform.position.y, Is.EqualTo(expectedY).Within(0.01f),
                "MV-1001: a deck death's drop must rest at the DECK's surface height, not the floor's — " +
                "Pickup.Place must read groundPos.y via the shared surface helper, not hard-code FloatHeight " +
                "off a fixed floor plane");

            // Max on the floor directly beneath the deck, well within the 1.4 m walk-over radius in XZ —
            // must NOT collect a drop sitting on the deck above him.
            _maxGo.transform.position = new Vector3(deck.x, 0f, deck.z);
            InvokeUpdate(_director);
            CollectionAssert.Contains(LiveList(_director), drop,
                "MV-1001: a Max standing on the FLOOR must not collect a pickup sitting on the DECK above him");

            // Max on the deck itself, within the walk-over radius — must collect it.
            _maxGo.transform.position = new Vector3(deck.x, deck.height, deck.z);
            InvokeUpdate(_director);
            CollectionAssert.DoesNotContain(LiveList(_director), drop,
                "MV-1001: a Max standing on the DECK, within CollectRadius, must collect the drop next to him");
        }
    }
}
