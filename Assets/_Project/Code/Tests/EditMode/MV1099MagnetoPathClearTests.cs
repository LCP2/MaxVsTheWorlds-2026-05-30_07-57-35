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
    /// MV-1099 — Lee, device, World 2 decks: "Magneto for parts and cells on other sections of the
    /// area. The magneto effect seems to pull the item in to the wall then when I go around to that
    /// area I can see it faintly in the wall but can't pick it up." Root cause (PickupDirector.cs
    /// ~565-610, base commit d30d293): the Magneto pull moved a caught pickup straight toward Max every
    /// frame with no check that the straight path was over walkable ground — only the coarse
    /// floor-vs-deck <see cref="CombatLevel.SameLevel"/> gate, which reads every deck as the same
    /// abstract "deck" regardless of which physical deck strip it actually is, and every floor room as
    /// the same abstract "floor" regardless of which room. A pickup dragged across a deck-strip gap or
    /// into a parapet stopped the instant it left "same level", stuck in geometry or over a void for good.
    ///
    /// Fix: <c>PickupDirector.MagnetoPathClear</c> gates every pull step on every 0.5 m sample along the
    /// straight path still reading as Max's own walkable surface (<see cref="MapData.IsWalkable"/>) —
    /// data only, no <c>Physics</c> call. An earlier version of this gate also raycast the Cover layer
    /// (<c>LineOfSight.Clear</c>), but a deck's parapet is deliberately kept OFF that layer, so the
    /// raycast half could never catch the exact parapet/gap case this ticket is about, and it coupled
    /// the gate to a scene this project deliberately never auto-syncs (<c>Physics.autoSyncTransforms</c>
    /// is off project-wide, <c>DynamicsManager.asset</c>) — a global, order-dependent channel that broke
    /// an unrelated full-suite test (<c>MV1122ClearTheAreaTests</c>, see the Jira triage comment on this
    /// ticket). <see cref="MapData.IsWalkable"/>'s own zone/deck-rect containment already IS the
    /// wall/parapet boundary in this rect-based map model: a point outside Max's own room rect, or
    /// outside his own deck's rect, is on the far side of a wall or across a deck-strip gap, with no
    /// separate geometry needed. Checked BEFORE every step, so a blocked pull simply never advances past
    /// wherever it last validly sat — there is no separate "snap back" state to prove, which is why
    /// AC(a)/(b)/(d) below all reduce to "the part's position did not change this tick".
    ///
    /// Real World 2 map (<c>WorldLibrary.Load</c> + <c>WorldMapLoader.TryLoad</c>, same raw <c>MapData</c>
    /// <see cref="MV1026PowerCellRingFollowsSurfaceTests"/> loads — no <c>MapRuntime.Build</c>/scene
    /// colliders needed, since the fix never touches <c>Physics</c>). AC(a) uses area a13 "Trolley Yard
    /// (floor)" / a15 "Trolley Yard (deck)" — an overlay pair authoring identical deck rects (MV-907
    /// dedupes the base's own copy, so the built ids are a15_deck1/a15_deck3, not a13's): two tiny
    /// switch-style decks, genuinely disjoint platforms. AC(b)/(c)/(d) use a1 "Outfall Steps" and a2
    /// "Grate Hall" — two plain floor rooms sharing a wall at x=30 (a1: x8-30; a2: x30-54, z100-120 both)
    /// — close enough either side of that wall to stay well inside e_mag's own level-5 radius (11 m).
    ///
    /// <see cref="PickupDirector.Tick"/> (now public — MV-1099 split it out of <c>Update()</c>, the same
    /// <c>Update() =&gt; Tick(dt)</c> idiom every other per-frame director in this codebase already uses)
    /// and <see cref="PickupDirector.PlacePickupAt"/> (a thin public wrapper around the private
    /// <c>SpawnDrop</c>, same shape as <c>SpawnWeaponCore</c>/<c>PlacePartsCache</c>) are the two real
    /// entry points this test drives — no reflection into <see cref="PickupDirector"/> itself.
    /// <c>BackyardPath._map</c> is still seeded by reflection purely to connect
    /// <see cref="MaxWorlds.Enemies.EnemyNavigation.Map"/> to the already-loaded, already-validated
    /// <see cref="MapData"/> — the same scaffolding idiom <c>MV1026PowerCellRingFollowsSurfaceTests</c>
    /// and <c>MV1001PickupDeckHeightAndLevelTests</c> already use, since <c>BackyardPath.Map</c> has no
    /// public setter. This is orthogonal map-loading scaffolding, not the Magneto mechanism itself.
    ///
    /// ONE new EditMode test (MV-465 Rule 1), all four ACs as sub-checks of one scenario.
    /// </summary>
    // Guards MV-1099
    public sealed class MV1099MagnetoPathClearTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _pathGo;
        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _maxGo;
        private MapData _map;

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            PickupWallet.Reset();   // also resets RigState
            RigBoard.UseWorld(1);   // World 2 -- e_mag's board

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out _map, out string reason), reason);
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing -- EnemyNavigation.Map can't be seeded");

            _pathGo = new GameObject("MV1099-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, _map);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();

            _maxGo = new GameObject("Max") { tag = "Player" };

            RigState.UnlockCategory("ENERGY");
            Assert.IsTrue(RigState.AcquireCap("e_cel"), "sanity: e_cel must be acquirable once ENERGY is unlocked");
            Assert.IsTrue(RigState.AcquireCap("e_mag"), "sanity: e_mag must be acquirable once e_cel is owned");
            for (int i = 0; i < 4; i++)
                Assert.IsTrue(RigState.RaiseLevel("e_mag"), "sanity: e_mag must raise all the way to level 5");
            Assert.That(RigState.Level("e_mag"), Is.EqualTo(5), "sanity: e_mag must actually be at level 5 (11 m pull radius)");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            EnemyNavigation.Reset();
            PickupWallet.Reset();
            RigBoard.ResetForTests();
        }

        private void TickSeconds(float totalSeconds, float dt = 0.1f)
        {
            int steps = Mathf.CeilToInt(totalSeconds / dt);
            for (int i = 0; i < steps; i++) _director.Tick(dt);
        }

        [Test]
        public void MagnetoNeverDragsAPickupAcrossADeckGapOrARoomWall_AndStopsCleanlyIfMaxCrossesOneMidPull() // Guards MV-1099
        {
            // --- (a) two disjoint deck strips -- a part on one must never move toward Max on the
            // --- other, even though both independently read as "on deck" (CombatLevel.SameLevel's own
            // --- coarse floor-vs-deck test, the gate the old code relied on alone).
            // a13 ("Trolley Yard (floor)") and a15 ("Trolley Yard (deck)") author identical deck rects
            // over the same footprint (MV-907: an overlay's own copy is the one that builds) --
            // a15_deck1/a15_deck3 are the IDs that actually end up in the built MapData, not a13's.
            MapEntity deck1 = _map.Entity("a15_deck1");
            MapEntity deck3 = _map.Entity("a15_deck3");
            Assert.IsNotNull(deck1, "setup failure: world2_config.json must still author a15_deck1");
            Assert.IsNotNull(deck3, "setup failure: world2_config.json must still author a15_deck3");

            Vector3 deck1Pos = new Vector3(deck1.x, _map.deckHeight, deck1.z);
            Vector3 deck3Pos = new Vector3(deck3.x, _map.deckHeight, deck3.z);
            Assert.That(Vector3.Distance(deck1Pos, deck3Pos), Is.LessThan(11f),
                "setup failure: this test needs the two deck strips within e_mag's own level-5 radius");

            Pickup partA = _director.PlacePickupAt(PickupKind.PowerCell, deck1Pos);
            Vector3 partAStart = partA.transform.position;
            _maxGo.transform.position = deck3Pos;

            TickSeconds(3f);
            Assert.That(partA.transform.position, Is.EqualTo(partAStart).Within(0.001f),
                "MV-1099 AC(a): a part on one deck strip must never move toward Max standing on a " +
                "DIFFERENT, disjoint deck strip even within Magneto's own radius");

            // --- (b) a wall on the SAME level -- a1 ("Outfall Steps", x8-30) and a2 ("Grate Hall",
            // --- x30-54) are two plain floor rooms sharing a wall at x=30; z100 stays inside both.
            Vector3 westOfWall = new Vector3(29.5f, 0f, 110f);
            Vector3 eastOfWall = new Vector3(30.5f, 0f, 110f);
            MapZone zoneWest = _map.ZoneAt(westOfWall.x, westOfWall.y, westOfWall.z);
            MapZone zoneEast = _map.ZoneAt(eastOfWall.x, eastOfWall.y, eastOfWall.z);
            Assert.IsNotNull(zoneWest, "setup failure: world2_config.json must still author a1 at x8-30/z100-120");
            Assert.IsNotNull(zoneEast, "setup failure: world2_config.json must still author a2 at x30-54/z100-124");
            Assert.AreNotEqual(zoneWest.id, zoneEast.id,
                "setup failure: these two points either side of x=30 must sit in different rooms for this to test a wall at all");

            Pickup partB = _director.PlacePickupAt(PickupKind.PowerCell, eastOfWall);
            Vector3 partBStart = partB.transform.position;
            _maxGo.transform.position = westOfWall;

            TickSeconds(3f);
            Assert.That(partB.transform.position, Is.EqualTo(partBStart).Within(0.001f),
                "MV-1099 AC(b): a part on the far side of a wall from Max, same level, must never move");

            // --- (c) 4 m away, same room, clear path -- must be pulled all the way in and collected.
            Vector3 clearFar = new Vector3(10f, 0f, 115f);
            Vector3 clearNear = new Vector3(10f, 0f, 119f);
            MapZone zoneClearFar = _map.ZoneAt(clearFar.x, clearFar.y, clearFar.z);
            MapZone zoneClearNear = _map.ZoneAt(clearNear.x, clearNear.y, clearNear.z);
            Assert.AreEqual(zoneClearFar?.id, zoneClearNear?.id,
                "setup failure: this 4 m strip must sit inside a single room for a clear-path case");

            Pickup partC = _director.PlacePickupAt(PickupKind.PowerCell, clearFar);
            _maxGo.transform.position = clearNear;

            TickSeconds(3f);
            Assert.IsFalse(partC.gameObject.activeSelf,
                "MV-1099 AC(c): a part 4 m away with a clear walkable path must be collected within 3 s");

            // --- (d) a pull IN PROGRESS when Max steps into a different room (across the SAME a1/a2
            // --- wall AC(b) already proved blocks) -- the part must stop exactly where it is (never
            // --- snap back, never keep advancing), end up over walkable surface, still resolving to a
            // --- real room, and still be collectable by walking to it.
            Vector3 partDStart = new Vector3(19f, 0f, 110f);   // a1, well clear of the wall
            Vector3 maxDStart = new Vector3(28f, 0f, 110f);    // a1, 9 m east -- eligible, same room, clear
            MapZone zoneD = _map.ZoneAt(partDStart.x, partDStart.y, partDStart.z);
            MapZone zoneDMax = _map.ZoneAt(maxDStart.x, maxDStart.y, maxDStart.z);
            Assert.AreEqual(zoneD?.id, zoneDMax?.id, "setup failure: the pull must start inside a single, clear room");

            Pickup partD = _director.PlacePickupAt(PickupKind.PowerCell, partDStart);
            _maxGo.transform.position = maxDStart;

            _director.Tick(0.1f);
            _director.Tick(0.1f);
            Vector3 midPull = partD.transform.position;
            Assert.That(Vector3.Distance(midPull, partDStart), Is.GreaterThan(0.5f),
                "setup failure: the pull must already be under way before Max steps into the other room");

            // Max steps across x=30 into a2 -- close enough that the pull would still be eligible by
            // range alone (<11 m, e_mag's own level-5 radius), so what stops it is specifically the
            // room/wall boundary, not falling out of range.
            Vector3 acrossWall = new Vector3(31f, 0f, 110f);
            MapZone zoneAcross = _map.ZoneAt(acrossWall.x, acrossWall.y, acrossWall.z);
            Assert.AreNotEqual(zoneD?.id, zoneAcross?.id, "setup failure: stepping to this point must cross into a different room");
            Assert.That(Vector3.Distance(midPull, acrossWall), Is.LessThan(11f),
                "setup failure: stepping across the wall must stay within e_mag's own level-5 radius");

            _maxGo.transform.position = acrossWall;   // Max steps into the other room, mid-pull
            TickSeconds(1f);

            Assert.That(partD.transform.position, Is.EqualTo(midPull).Within(0.001f),
                "MV-1099 AC(d): a pull interrupted by Max crossing into a different room must stop exactly " +
                "where it was, never keep advancing");

            Vector3 restPos = partD.transform.position;
            MapZone restZone = _map.ZoneAt(restPos.x, restPos.y, restPos.z);
            Assert.IsNotNull(restZone,
                "MV-1099 AC(d): the stopped part must still resolve to a real walkable room, not off the map");
            Assert.IsTrue(_map.IsWalkable(restPos, restPos),
                "MV-1099 AC(d): the stopped part must still sit over its own walkable surface");

            _maxGo.transform.position = restPos;   // walk directly to where it stopped
            _director.Tick(0.016f);
            Assert.IsFalse(partD.gameObject.activeSelf,
                "MV-1099 AC(d): the stopped part must still be collectable by walking to it");
        }
    }
}
