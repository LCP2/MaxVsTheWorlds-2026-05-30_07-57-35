using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-902: the live map labelled the Trolley Yard floor "15" — Lee's workbook names an overlay deck
    /// by the floor it roofs ("13 Up" for a15, "3 Up" for a17), never by its own internal config index
    /// (15/17), which must never appear anywhere a player can see. Root cause (Candidate A, confirmed by
    /// reading <see cref="MapScreen"/>'s room-building loop): it drew one box+label per zone with no
    /// dedup for a same-footprint overlay pair — a15/a17 are authored AFTER their floor (a13/a3) in
    /// world2_config.json's areas array, so the overlay's own box (and its raw-index label) painted on
    /// top, hiding the floor's. Candidate B (<see cref="MapData.ZoneAt(float, float, float)"/>) is NOT
    /// the cause: at floor height it always returns the level-0 zone regardless of any deck entity, and
    /// at deck height it already correctly resolves to the level&gt;0 zone whenever the XZ point is
    /// actually standing over THAT zone's own authored Deck/Hatch rect (MV-833's own rule) — verified
    /// below at a real deck point read off the loaded World 2 config, not a hand-picked coordinate (a15's
    /// single deck strip sits only along one edge of its footprint, not its centre, so a bare footprint-
    /// centre check would fail for a genuinely unrelated reason).
    ///
    /// Fails on b2a1b81 (base commit for this ticket): the "Area 15" room's index label read "15" (not
    /// "13 Up") and the "Area 17" room's read "17" (not "3 Up").
    /// </summary>
    public sealed class Mv902AreaOverlayLabelTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo MapField = typeof(BackyardPath).GetField("_map", NonPublicInstance);
        private static readonly FieldInfo BuildField = typeof(BackyardPath).GetField("_build", NonPublicInstance);

        private GameObject _mapRoot;
        private GameObject _pathGo;
        private GameObject _screenGo;

        [SetUp]
        public void SetUp()
        {
            // Same BuildBody collider-strip [Error] every map-build EditMode test in this suite carries
            // once MapRuntime.Build actually runs (see MV830ReplicatorMapMarkersTests).
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_screenGo != null) Object.DestroyImmediate(_screenGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_mapRoot != null) Object.DestroyImmediate(_mapRoot);
        }

        /// <summary>A real world-space point actually standing on <paramref name="zone"/>'s own authored
        /// Deck/Hatch surface — never the zone's bare footprint centre, which a partial-coverage deck
        /// (like a15's single edge strip) need not include.</summary>
        private static Vector2 DeckPointOver(MapData map, MapZone zone)
        {
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Deck && e.Kind != EntityKind.Hatch) continue;
                if (zone.Contains(e.x, e.z)) return new Vector2(e.x, e.z);
            }
            Assert.Fail($"setup failure: no Deck/Hatch entity found inside zone '{zone.id}'");
            return default;
        }

        /// <summary>The map screen's own index-label Text for the room named "Area &lt;rawAreaIndex&gt;"
        /// (the raw config index MapScreen names every room GameObject after) — the first Text child
        /// under that room, added before the (optional) name label.</summary>
        private static Text IndexLabelFor(GameObject screenGo, int rawAreaIndex)
        {
            foreach (Transform t in screenGo.GetComponentsInChildren<Transform>(true))
                if (t.name == $"Area {rawAreaIndex}")
                    return t.GetComponentInChildren<Text>(true);
            return null;
        }

        [Test]
        public void OverlayDecksResolveAndLabelByTheFloorTheyRoof_NotTheirOwnRawIndex()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "world2_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone a13 = map.Zone("area13");
            MapZone a15 = map.Zone("area15");
            MapZone a3 = map.Zone("area3");
            MapZone a17 = map.Zone("area17");
            Assert.IsNotNull(a13, "setup failure: World 2 must author area13 (Trolley Yard floor)");
            Assert.IsNotNull(a15, "setup failure: World 2 must author area15 (Trolley Yard deck)");
            Assert.IsNotNull(a3, "setup failure: World 2 must author area3 (Junction Hall floor)");
            Assert.IsNotNull(a17, "setup failure: World 2 must author area17 (Junction Hall deck)");

            // === ZoneAt: floor height always resolves to the level-0 zone ===
            Vector2 c13 = a13.CenterXz;
            Vector2 c3 = a3.CenterXz;
            Assert.AreSame(a13, map.ZoneAt(c13.x, 0f, c13.y),
                "MV-902: the centre of a13's footprint must resolve to a13 at floor height");
            Assert.AreSame(a3, map.ZoneAt(c3.x, 0f, c3.y),
                "MV-902: the centre of a3's footprint must resolve to a3 at floor height");

            // === ZoneAt: deck height resolves to the overlay zone, at a real point standing over its
            // own authored deck surface (MV-833: never just the bare footprint) ===
            Vector2 d15 = DeckPointOver(map, a15);
            Vector2 d17 = DeckPointOver(map, a17);
            Assert.AreSame(a15, map.ZoneAt(d15.x, map.deckHeight, d15.y),
                "MV-902: a15's own deck point must resolve to a15 at deck height");
            Assert.AreSame(a17, map.ZoneAt(d17.x, map.deckHeight, d17.y),
                "MV-902: a17's own deck point must resolve to a17 at deck height");

            // === Map screen label: an overlay deck shows "<floor index> Up", never its own raw index ===
            _mapRoot = new GameObject("MV902 map root");
            MapBuild build = MapRuntime.Build(map, _mapRoot.transform);

            _pathGo = new GameObject("MV902 backyard path");
            var path = _pathGo.AddComponent<BackyardPath>();
            Assert.IsNotNull(MapField, "BackyardPath._map went missing");
            Assert.IsNotNull(BuildField, "BackyardPath._build went missing");
            MapField.SetValue(path, map);
            BuildField.SetValue(path, build);

            _screenGo = new GameObject("MV902 map screen");
            var screen = _screenGo.AddComponent<MapScreen>();
            screen.Open();

            Text label15 = IndexLabelFor(_screenGo, 15);
            Text label17 = IndexLabelFor(_screenGo, 17);
            Text label13 = IndexLabelFor(_screenGo, 13);
            Text label3 = IndexLabelFor(_screenGo, 3);
            Assert.IsNotNull(label15, "setup failure: map screen built no 'Area 15' room");
            Assert.IsNotNull(label17, "setup failure: map screen built no 'Area 17' room");
            Assert.IsNotNull(label13, "setup failure: map screen built no 'Area 13' room");
            Assert.IsNotNull(label3, "setup failure: map screen built no 'Area 3' room");

            Assert.AreEqual("13 Up", label15.text, "MV-902: a15's map label must read '13 Up', never '15'");
            Assert.AreEqual("3 Up", label17.text, "MV-902: a17's map label must read '3 Up', never '17'");
            Assert.AreEqual("13", label13.text, "MV-902: a13's own map label must still read '13'");
            Assert.AreEqual("3", label3.text, "MV-902: a3's own map label must still read '3'");
        }
    }
}
