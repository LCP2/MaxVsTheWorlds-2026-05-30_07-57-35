using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1100 — Lee, device, World 2 deck: "Max weapon direction indicator draws on the lower level
    /// when it should be drawn on the level he's on." Root cause, read in code:
    /// <see cref="AimReticle.Tick"/> flattened the wedge to a fixed world height
    /// (<see cref="AimReticle.GroundLift"/> above y=0), discarding whatever surface Max was actually
    /// standing on. The audit this ticket asks for found the same fixed-height assumption in two more
    /// of Max's own ground marks — <see cref="TeleportJoystickControl"/>'s and
    /// <see cref="WaterBalloonJoystickControl"/>'s landing circles, both hard-coded to y=0.01 — while
    /// the ring under his own feet (<c>GroundAnchorVfx</c>, MV-898) and the Force Field bubble
    /// (<c>ForceFieldBubble</c>, parented straight to Max's own transform and never flattened at all)
    /// were already clear.
    ///
    /// Fails on base commit d30d293 (unchanged on these three files through the current HEAD — see the
    /// fix comment for the quoted failure output): on a10's deck the reticle and both landing circles
    /// resolve to the FLOOR's height, ~2.5 m below where Max is actually standing.
    ///
    /// Tier 2 (resolved values), real entry points only: <see cref="AimReticle.Tick"/> is the same
    /// method <c>LateUpdate</c> calls every frame, exposed so this test can drive it without reflecting
    /// into a private method; <see cref="TeleportJoystickControl.OnPointerDown"/> and
    /// <see cref="WaterBalloonJoystickControl.OnPointerDown"/> are the real
    /// <see cref="IPointerDownHandler"/> entry point a touch actually fires, the same idiom
    /// <c>WaterBalloonJoystickControlTests</c> already uses. No authored constant and no rendered pixel
    /// is asserted anywhere below.
    /// </summary>
    public sealed class MV1100MaxGroundMarksFollowSurfaceHeightTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _pathGo;
        private GameObject _reticleOwner;
        private GameObject _teleportOrigin, _teleportPad;
        private GameObject _balloonOrigin, _balloonPad;
        private GameObject _ffOwner;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            EnemyNavigation.Reset();
            WeaponSystemState.Reset();
            RigState.Reset();

            if (_pathGo != null) { Object.DestroyImmediate(_pathGo); _pathGo = null; }
            if (_reticleOwner != null) { Object.DestroyImmediate(_reticleOwner); _reticleOwner = null; }
            if (_teleportPad != null) { Object.DestroyImmediate(_teleportPad); _teleportPad = null; }
            if (_teleportOrigin != null) { Object.DestroyImmediate(_teleportOrigin); _teleportOrigin = null; }
            if (_balloonPad != null) { Object.DestroyImmediate(_balloonPad); _balloonPad = null; }
            if (_balloonOrigin != null) { Object.DestroyImmediate(_balloonOrigin); _balloonOrigin = null; }
            if (_ffOwner != null) { Object.DestroyImmediate(_ffOwner); _ffOwner = null; }
        }

        private void SeedMap(MapData map)
        {
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            // EnemyNavigation.Map caches the BackyardPath it found on first lookup (_looked) — a swap
            // from World 2 to World 1 mid-test must invalidate that cache or every later lookup keeps
            // resolving the destroyed World 2 path instead of the new one.
            EnemyNavigation.Reset();
            _pathGo = new GameObject("MV1100-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);
        }

        private static PointerEventData At(Vector2 pos) => new PointerEventData(EventSystem.current) { position = pos };

        [Test]
        public void MaxOwnedGroundMarksDrawOnTheSurfaceHeStandsOn_NotAFixedFloorPlane()
        {
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");

            WorldConfig world2Cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(world2Cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(world2Cfg, out MapData world2, out string reason2), reason2);

            MapZone zone = world2.Zone("area10");
            Assert.IsNotNull(zone, "MV-1100: world2_config.json must still author area 'a10' (zone id 'area10')");
            MapEntity deck = world2.Entity("a10_deck1");
            Assert.IsNotNull(deck, "MV-1100: world2_config.json must still author 'a10_deck1'");
            Assert.AreEqual(2.5f, deck.height, 0.01f, "setup: a10_deck1 must still be the 2.5m deck the ticket names");

            // A floor probe point: inside a10's own zone, but clear of the deck's rect.
            float farZ = Mathf.Abs(zone.ZMin - deck.z) > Mathf.Abs(zone.ZMax - deck.z) ? zone.ZMin + 1f : zone.ZMax - 1f;
            var floorPos = new Vector3(zone.x, 0f, farZ);
            Assert.Greater(Mathf.Abs(floorPos.z - deck.z), deck.depth * 0.5f + 0.5f,
                "test setup: the floor probe point must sit outside the deck's own rect");

            var deckPos = new Vector3(deck.x, deck.height, deck.z);

            SeedMap(world2);

            // --- 1. AimReticle: Max's own weapon-reach wedge (the ticket's named defect) -----------
            _reticleOwner = new GameObject("MV1100 ReticleOwner");
            _reticleOwner.transform.forward = Vector3.forward;
            var reticle = _reticleOwner.AddComponent<AimReticle>();
            reticle.Init(_reticleOwner.transform, 6f, 35f);

            _reticleOwner.transform.position = deckPos;
            reticle.Tick(0f);
            Assert.AreEqual(deck.height + AimReticle.GroundLift, reticle.ResolvedPosition.y, 0.02f,
                "MV-1100: the aim reticle must draw on the DECK Max is standing on, not the floor below it");

            _reticleOwner.transform.position = floorPos;
            reticle.Tick(0f);
            Assert.AreEqual(AimReticle.GroundLift, reticle.ResolvedPosition.y, 0.02f,
                "MV-1100: the aim reticle on the floor must resolve to the floor's own height");

            // --- 2. Teleport's landing circle (audit found the same fixed-height assumption) -------
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            WeaponSystemState.Acquire(AbilityKind.Teleport);

            _teleportOrigin = new GameObject("MV1100 TeleportOrigin");
            _teleportOrigin.transform.forward = Vector3.forward;
            _teleportPad = new GameObject("MV1100 Teleport Pad", typeof(RectTransform), typeof(Image));
            var teleportControl = _teleportPad.AddComponent<TeleportJoystickControl>();
            var teleportKnob = new GameObject("MV1100 Teleport Knob", typeof(RectTransform)).GetComponent<RectTransform>();
            teleportControl.Init(teleportKnob, _teleportOrigin.transform, null);

            _teleportOrigin.transform.position = deckPos;
            teleportControl.OnPointerDown(At(Vector2.zero));
            Assert.AreEqual(deck.height + 0.01f, teleportControl.LandingCirclePosition.y, 0.02f,
                "MV-1100: the teleport landing circle must draw on the deck Max is aiming from, not the floor below");

            _teleportOrigin.transform.position = floorPos;
            teleportControl.OnPointerDown(At(Vector2.zero));
            Assert.AreEqual(0.01f, teleportControl.LandingCirclePosition.y, 0.02f,
                "MV-1100: the teleport landing circle on the floor must resolve to the floor's own height");

            // --- 3. Water Balloon's landing circle (same audit finding) -----------------------------
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);

            _balloonOrigin = new GameObject("MV1100 BalloonOrigin");
            _balloonOrigin.transform.forward = Vector3.forward;
            _balloonPad = new GameObject("MV1100 Balloon Pad", typeof(RectTransform), typeof(Image));
            var balloonControl = _balloonPad.AddComponent<WaterBalloonJoystickControl>();
            var balloonKnob = new GameObject("MV1100 Balloon Knob", typeof(RectTransform)).GetComponent<RectTransform>();
            balloonControl.Init(balloonKnob, _balloonOrigin.transform, null);

            _balloonOrigin.transform.position = deckPos;
            balloonControl.OnPointerDown(At(Vector2.zero));
            Assert.AreEqual(deck.height + 0.01f, balloonControl.LandingCirclePosition.y, 0.02f,
                "MV-1100: the water balloon landing circle must draw on the deck Max is aiming from, not the floor below");

            _balloonOrigin.transform.position = floorPos;
            balloonControl.OnPointerDown(At(Vector2.zero));
            Assert.AreEqual(0.01f, balloonControl.LandingCirclePosition.y, 0.02f,
                "MV-1100: the water balloon landing circle on the floor must resolve to the floor's own height");

            // --- 4. Force Field bubble: audited, already clear — it is parented straight to Max's
            // own transform and never flattens to ANY fixed height, deck or otherwise, so it cannot
            // carry this bug class. Proven here, not assumed. ---------------------------------------
            _ffOwner = new GameObject("MV1100 ForceFieldOwner");
            _ffOwner.transform.position = deckPos;
            var bubbleGo = new GameObject("MV1100 ForceFieldBubble");
            var bubble = bubbleGo.AddComponent<ForceFieldBubble>();
            bubble.Init(_ffOwner.transform, null, 2f);

            Assert.AreEqual(deckPos.y, bubble.transform.position.y, 1e-4f,
                "MV-1100 audit: the force field bubble must track Max's own actual Y exactly — any " +
                "flattening here would be the same bug class this ticket fixes");

            _ffOwner.transform.position = floorPos;
            Assert.AreEqual(floorPos.y, bubble.transform.position.y, 1e-4f,
                "MV-1100 audit: the force field bubble must keep tracking Max's own actual Y when he " +
                "steps off the deck, not snap to a fixed height of its own");

            // --- 5. World 1's lawn: every mark above is unchanged from today ------------------------
            WorldConfig world1Cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(world1Cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(world1Cfg, out MapData world1, out string reason1), reason1);
            SeedMap(world1);

            MapZone lawnZone = world1.Zone("area1");
            Assert.IsNotNull(lawnZone, "MV-1100: world1_config.json must still author area 'a1' (zone id 'area1')");
            var lawnPos = new Vector3(lawnZone.x, 0f, lawnZone.z);

            _reticleOwner.transform.position = lawnPos;
            reticle.Tick(0f);
            Assert.AreEqual(AimReticle.GroundLift, reticle.ResolvedPosition.y, 0.02f,
                "MV-1100: World 1's lawn must be unaffected by this fix — the reticle still resolves to " +
                "the undecorated GroundLift it always has");

            _teleportOrigin.transform.position = lawnPos;
            teleportControl.OnPointerDown(At(Vector2.zero));
            Assert.AreEqual(0.01f, teleportControl.LandingCirclePosition.y, 0.02f,
                "MV-1100: World 1's lawn must be unaffected by this fix — the teleport circle still " +
                "resolves to the undecorated 0.01 lift it always has");

            _balloonOrigin.transform.position = lawnPos;
            balloonControl.OnPointerDown(At(Vector2.zero));
            Assert.AreEqual(0.01f, balloonControl.LandingCirclePosition.y, 0.02f,
                "MV-1100: World 1's lawn must be unaffected by this fix — the water balloon circle " +
                "still resolves to the undecorated 0.01 lift it always has");
        }
    }
}
