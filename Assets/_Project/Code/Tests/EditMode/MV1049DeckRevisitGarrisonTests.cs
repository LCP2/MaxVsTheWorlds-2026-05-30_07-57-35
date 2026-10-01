using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1049 — Lee (iOS, World 2): no robots on the upper-walkway decks ("10 Up"/"11 Up"/"12 Up"/"13 Up").
    /// Triaged (see ticket comments): those four names are the walkway stops a14-&gt;a15("13 Up")-&gt;a12
    /// ("12 Up")-&gt;a11("11 Up")-&gt;a10("10 Up")-&gt;a16-&gt;a17. a10/a11/a12 are "in-place-deck" areas
    /// (<see cref="MapData.IsOnDeck"/>'s own doc): ONE zone covers floor and deck alike, so their deck
    /// visit is a SECOND visit to the SAME area index, not a fresh one.
    ///
    /// The ticket's own hypothesis ("defect 2": the whole garrison, floor and deck alike, seeds once on
    /// first/floor entry, and the deck revisit's <c>FillArea</c> is a one-shot no-op) was checked FIRST,
    /// directly, with a continuous unresumed walk — it does NOT reproduce: nothing destroys an
    /// already-placed deck robot on a plain walk-through (<see cref="AreaAccumulationDirector.ParkByReach"/>'s
    /// own park/unpark round-trip is non-destructive), so that hypothesis is unconfirmed and no fix was
    /// written for it (Testing policy Rule 1).
    ///
    /// What DOES reproduce it, measured here: a cold-boot Home-screen RESUME captured anywhere at or past
    /// a14 (<see cref="AreaAccumulationDirector.ClearAreasBeforeCheckpoint"/>, called from
    /// <see cref="MaxWorlds.Arena.WorldRunner.ResumeCheckpoint"/>). It despawns every robot — and wipes
    /// every still-queued composition entry — for any area with a LOWER INDEX than the checkpoint, then
    /// marks that area permanently filled, on the assumption index order tracks play order. That holds
    /// for every ordinary monotonic area, but not for a10/a11/a12: their deck visit sits AFTER a14/a15 in
    /// real play despite a LOWER index, so a RESUME captured on the walkway wipes a deck garrison the
    /// player hasn't reached yet and permanently starves it — "no robots on the decks" end to end.
    ///
    /// Fix: <see cref="AreaAccumulationDirector.ClearAreasBeforeCheckpoint"/> now exempts an in-place-deck
    /// area (<c>IsInPlaceDeckArea</c>) from its despawn/wipe/permanent-fill pass entirely — it is never
    /// actually "behind" Max just because its own index is below the checkpoint's. Every ordinary area is
    /// untouched. a15 ("13 Up") is a TRUE overlay pair (its own area index, visited exactly once via
    /// ordinary <c>FillArea</c>/<c>RestoreArea</c>) and was never affected by either mechanism.
    ///
    /// Must fail on 3c6e892: walking the real route, including the RESUME a real session on iOS can hit,
    /// and asserting every authored deck entry individually (not a count, Testing policy Rule 3) catches
    /// exactly what <c>ClearAreasBeforeCheckpoint</c> actually does to it.
    /// </summary>
    public sealed class MV1049DeckRevisitGarrisonTests
    {
        private GameObject _host;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);

            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_host != null) Object.DestroyImmediate(_host);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        // The real shipped route (same graph Mv920AreaTrackingGantryTests/MV1002DeckDeathRespawnGarrisonTests
        // already walk and verify against MapData.AreLinked below): floor chain 1-14, then the walkway
        // itself (g32-g37) — a14->a15 ("13 Up")->a12 ("12 Up")->a11 ("11 Up")->a10 ("10 Up")->a16->a17.
        private static readonly int[] FloorClimb = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
        private static readonly int[] Walkway = { 15, 12, 11, 10, 16, 17 };

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        /// <summary>Same idiom as Mv920AreaTrackingGantryTests/MV1002's own ProbePositionFor — resolves,
        /// via <see cref="MapData.ZoneAt(float,float,float)"/>, to a TRUE overlay zone's own deck (a15,
        /// a17) or a level-0 room's own floor centre. Never correct for an IN-PLACE-deck area (a10/a11/
        /// a12) — they share one zone for floor and deck alike, so this always gives their FLOOR position;
        /// see <see cref="DeckProbePositionFor"/> for their deck revisit.</summary>
        private static Vector3 ProbePositionFor(MapData map, MapZone zone)
        {
            Assert.IsNotNull(zone, "setup failure: ProbePositionFor given a null zone");
            if (zone.level == 0) return new Vector3(zone.x, 0.5f, zone.z);

            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Deck && e.Kind != EntityKind.Hatch) continue;
                if (!zone.Contains(e.x, e.z)) continue;
                return new Vector3(e.x, map.deckHeight, e.z);
            }

            Assert.Fail($"setup failure: no Deck/Hatch entity found inside level>0 zone '{zone.id}'");
            return default;
        }

        /// <summary>A position standing on an IN-PLACE-deck area's own deck (a10/a11/a12) — the centre of
        /// its first authored deck rect, at that deck's own resolved elevation
        /// (<see cref="Garrison.ResolveLevelHeight"/>, the same lookup a garrisoned robot's own spawn Y
        /// resolves against) — so <see cref="MapData.IsOnDeck"/> reads true there exactly as it would for
        /// Max's real feet.</summary>
        private static Vector3 DeckProbePositionFor(WorldConfig cfg, WorldArea area)
        {
            List<Rect> rects = Garrison.DeckFootprints(area, cfg);
            Assert.That(rects.Count, Is.GreaterThan(0), $"setup failure: area {area.id} has no deck rects");
            Rect rect = rects[0];
            Vector2 center = rect.center;
            float y = Garrison.ResolveLevelHeight(area, cfg, center);
            return new Vector3(center.x, y, center.y);
        }

        /// <summary>The live robot standing at authored XZ (<paramref name="x"/>, <paramref name="z"/>) in
        /// <paramref name="areaIndex"/> — the HIGHEST one, when a floor and a deck entry share the
        /// identical column (a10's own sludger stack, measured on this ticket: floor and deck entries
        /// both authored at (206.5, 117.5)), so a deck assertion never accidentally reads its own floor
        /// duplicate.</summary>
        private static RobotEnemy LiveGarrisonRobotAt(int areaIndex, float x, float z)
        {
            RobotEnemy best = null;
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                if (r == null || !r.IsAlive || r.AreaIndex != areaIndex) continue;
                Vector3 p = r.transform.position;
                if (Mathf.Abs(p.x - x) > 0.05f || Mathf.Abs(p.z - z) > 0.05f) continue;
                if (best == null || p.y > best.transform.position.y) best = r;
            }
            return best;
        }

        /// <summary>Tier 2: asserts every authored level-1 garrison entry in <paramref name="areaIndex"/>
        /// is a live robot standing at deck elevation, inside its own deck footprint — per entry, never a
        /// bare count (Testing policy Rule 3).</summary>
        private static void AssertDeckGarrisonPresent(WorldConfig cfg, int areaIndex)
        {
            WorldArea area = cfg.AreaByIndex(areaIndex);
            Assert.IsNotNull(area, $"setup failure: area{areaIndex} missing from World 2's own config");

            List<Rect> deckRects = Garrison.DeckFootprints(area, cfg);
            int deckEntryCount = 0;

            foreach (WorldGarrisonEntry entry in area.garrison)
            {
                if (entry == null || entry.level <= 0) continue;
                deckEntryCount++;

                RobotEnemy r = LiveGarrisonRobotAt(areaIndex, entry.x, entry.z);
                Assert.IsNotNull(r,
                    $"MV-1049: area{areaIndex}'s authored deck entry ({entry.kind} at {entry.x},{entry.z}) " +
                    "must be a live robot once Max reaches the upper walkway — found none");

                Vector3 p = r.transform.position;
                Assert.That(p.y, Is.GreaterThan(1f),
                    $"MV-1049: area{areaIndex}'s deck entry at ({entry.x},{entry.z}) resolved to a live robot " +
                    $"standing at Y={p.y:F2} — not deck elevation");

                bool insideDeck = deckRects.Exists(rect => rect.Contains(new Vector2(p.x, p.z)));
                Assert.IsTrue(insideDeck,
                    $"MV-1049: area{areaIndex}'s deck entry at ({entry.x},{entry.z}) resolved to a live robot " +
                    $"at ({p.x:F1},{p.z:F1}) — outside every deck rect");
            }

            Assert.That(deckEntryCount, Is.GreaterThan(0),
                $"setup failure: area{areaIndex} authors no level-1 garrison entries — this test proves nothing for it");
        }

        [Test]
        public void MaxReachesTheUpperWalkway_EveryAuthoredDeckGarrisonEntryIsLiveOnItsDeck()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            for (int i = 1; i < Walkway.Length; i++)
                Assert.IsTrue(map.AreLinked($"area{Walkway[i - 1]}", $"area{Walkway[i]}"),
                    $"setup failure: area{Walkway[i - 1]}-area{Walkway[i]} is no longer a real MapLink");
            Assert.IsTrue(map.AreLinked("area14", "area15"), "setup failure: World 2's a14-a15 gate (g32) changed");

            _host = new GameObject("MV1049 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            // Same reasoning as MV1002: starve the ambient queue so only garrison (which bypasses its own
            // cap) is ever active, so an ambient overflow spawn can never coincidentally land on an
            // authored deck column and mask a missing garrison entry.
            DevTuning.MaxActiveRobots = 1f;

            _areaGo = new GameObject("Area Accumulation");
            var areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, built.Cover);

            _playerGo = new GameObject("Player") { tag = "Player" };

            // --- floor climb: area1 (Configure()'s own start) up to area14, each area's floor visit
            // --- seeds its own floor-level garrison ---
            foreach (int toIndex in FloorClimb)
            {
                _playerGo.transform.position = ProbePositionFor(map, map.Zone($"area{toIndex}"));
                InvokePrivate(areaDirector, "Update");
                areaDirector.EnterArea(toIndex);
            }

            // MV-1049: simulate a cold-boot Home-screen RESUME captured while Max was standing on a15
            // (plausible on iOS — backgrounding/relaunching mid-session) — the exact call sequence
            // WorldRunner.ResumeCheckpoint uses (ClearAreasBeforeCheckpoint then RestoreArea, both
            // already public), minus the player-teleport/SetCurrentArea part this test doesn't need.
            areaDirector.ClearAreasBeforeCheckpoint(15);
            areaDirector.RestoreArea(15);

            // --- the walkway itself: a15 ("13 Up") -> a12 ("12 Up") -> a11 ("11 Up") -> a10 ("10 Up") -> a16 -> a17 ---
            foreach (int toIndex in Walkway)
            {
                Vector3 pos = toIndex == 10 || toIndex == 11 || toIndex == 12
                    ? DeckProbePositionFor(cfg, cfg.AreaByIndex(toIndex))
                    : ProbePositionFor(map, map.Zone($"area{toIndex}"));

                _playerGo.transform.position = pos;
                InvokePrivate(areaDirector, "Update");
                areaDirector.EnterArea(toIndex);

                // Asserted immediately on arrival, not after the walk moves on — ParkByReach correctly
                // re-parks an area's resting robots once Max is no longer near it (MV-966), so checking
                // only at the very end would read a legitimately-parked EARLIER stop as the MV-1049 defect.
                if (toIndex == 15 || toIndex == 12 || toIndex == 11 || toIndex == 10)
                    AssertDeckGarrisonPresent(cfg, toIndex);
            }
        }
    }
}
