using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1110 — Lee, device, World 1 area 30: "Big Bermuda two has already made its way down before
    /// I've even seen it. Should be found near top right so Max can see the exit corridor." Area 30 is
    /// 44x56 m and Max enters through its south gate; the old wake rule (<c>MapRuntime.BuildBoss</c>'s
    /// own comment: "wakes the instant Max's planar position enters this boss's own authored area")
    /// meant the boss was already walking toward him from clear across the arena before he had ever
    /// seen it. This ticket moves the post to (329, 110) — inside sight of the exit door — and replaces
    /// the area-entry wake trigger with proximity (<see cref="BossTuning.WakeRadius"/>, 16 m) plus a
    /// clear line of sight, for every boss in every world.
    ///
    /// Built against the real World 1/2/3 maps (<see cref="MapRuntime.Build"/>), never a synthetic
    /// fixture — AC1 names "the real World 1 map" explicitly, and the World 2/3 sub-checks need the
    /// real authored "sludgequeen"/"anchorhead" boss entities and gate positions, not a stand-in.
    /// World 1/3 build as a <see cref="BigBermudaBoss"/>; World 2's "sludgequeen" builds as a
    /// <see cref="SludgequeenBoss"/> (MV-1127) — both share the exact same private
    /// <c>TickDormant</c>/public <c>Engaged</c> wake-rule shape, so this test drives either through
    /// reflection rather than a concrete type.
    ///
    /// Fails on base commit d30d293 (the tip before this fix): area 30's "south gate, stand just inside"
    /// position wakes the boss immediately (it only had to enter the area), and the boss sits at its
    /// pre-fix post (316, 127) rather than (329, 110).
    /// </summary>
    public sealed class MV1110BossWakeAtPostTests
    {
        private const float Fov = 40f;             // FixedAngleCameraRig's shipped Cinemachine lens (CameraFramingTests' own convention)
        private const float Aspect = 16f / 9f;

        private GameObject _playerGo;
        private readonly List<GameObject> _roots = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("MV1110 Max") { tag = "Player" };
            BossCensus.Reset();
            FixedAngleCameraRig.SimulatePhoneClass = false; // deterministic desktop default for the camera sub-check
        }

        [TearDown]
        public void TearDown()
        {
            FixedAngleCameraRig.SimulatePhoneClass = null;
            BossCensus.Reset();
            foreach (GameObject root in _roots)
                if (root != null) Object.DestroyImmediate(root);
            _roots.Clear();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static void InvokeAwake(Component c) =>
            c.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(c, null);

        private static void InvokeTickDormant(Component b) =>
            b.GetType().GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(b, null);

        private static bool IsEngaged(Component b) =>
            (bool)b.GetType().GetProperty("Engaged").GetValue(b);

        private static MapData LoadMap(string worldKey)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey} failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            return map;
        }

        /// <summary>The resolved world-space centre of area gate <paramref name="gateId"/> — the exact
        /// doorway point <see cref="WorldMapLoader"/> itself computed from the authored wall/fraction
        /// (MV-267's own "resolving a wall at a fraction into an absolute point"), not a hand-derived
        /// approximation.</summary>
        private static Vector3 GatePosition(MapData map, string gateId)
        {
            MapEntity gate = map.entities.FirstOrDefault(e => e.id == gateId);
            Assert.IsNotNull(gate, $"map must author gate '{gateId}' for this test to mean anything");
            return new Vector3(gate.x, 0f, gate.z);
        }

        private Component BuildBoss(MapData map, string bossEntityId)
        {
            var root = new GameObject($"MV1110 {bossEntityId} root");
            _roots.Add(root);
            MapBuild built = MapRuntime.Build(map, root.transform);
            Assert.IsTrue(built.Actors.TryGetValue(bossEntityId, out GameObject bossGo) && bossGo != null,
                $"map must build boss entity '{bossEntityId}'");
            Component boss = (Component)bossGo.GetComponent<BigBermudaBoss>() ?? bossGo.GetComponent<SludgequeenBoss>();
            Assert.IsNotNull(boss, $"'{bossEntityId}' must build as a boss component");
            InvokeAwake(boss);
            return boss;
        }

        [Test]
        public void BossStaysAtItsPost_WakesOnlyOnProximityAndSight_InEveryWorld()
        {
            // ================================================================= World 1, area 30
            MapData world1 = LoadMap(WorldLibrary.World1);
            Component boss1 = BuildBoss(world1, "a30_boss1");

            var post = new Vector3(329f, boss1.transform.position.y, 110f);
            Assert.That(Vector2.Distance(new Vector2(boss1.transform.position.x, boss1.transform.position.z),
                new Vector2(post.x, post.z)), Is.LessThan(0.5f),
                "AC1: a30_boss1 must be authored at (329, 110)");

            // --- Max enters area 30 through its south gate and stands just inside, 20 simulated seconds. ---
            Vector3 southGate = GatePosition(world1, "g29");
            _playerGo.transform.position = new Vector3(southGate.x, 0f, southGate.z + 1f); // just inside

            for (int i = 0; i < 1200; i++) // 20 s at 60 fps -- TickDormant is a plain per-frame check, no internal timer
                InvokeTickDormant(boss1);

            Assert.IsFalse(IsEngaged(boss1), "AC1: must still be Dormant after 20 s at the south gate, well beyond WakeRadius");
            Assert.That(Vector2.Distance(new Vector2(boss1.transform.position.x, boss1.transform.position.z),
                new Vector2(post.x, post.z)), Is.LessThan(0.5f),
                "AC1: a Dormant boss must not have moved from its post");

            // --- Sub-check: with the boss at its post, the exit door mouth and the boss both sit inside
            // the default play camera's ground rectangle when Max stands 8 m west of the boss. ---
            var camTargetGo = new GameObject("MV1110 Camera Rig");
            var rig = camTargetGo.AddComponent<FixedAngleCameraRig>();
            rig.ApplyDeviceDefault();
            Vector3 maxEightMWest = boss1.transform.position + new Vector3(-8f, 0f, 0f);
            Rect groundRect = TeleportZoomFraming.GroundRect(maxEightMWest, rig.Distance, rig.Pitch, Fov, Aspect);
            Assert.IsTrue(groundRect.Contains(new Vector2(post.x, post.z)),
                $"sub-check: the boss at its post must sit inside the default camera's ground rect {groundRect}");
            Assert.IsTrue(groundRect.Contains(new Vector2(338f, 106f)),
                $"sub-check: the exit door mouth (338, 106) must sit inside the default camera's ground rect {groundRect}");
            Object.DestroyImmediate(camTargetGo);

            // --- Max moves to 15 m from the boss with clear sight: awake within 1 s (TickDormant runs
            // every frame off this boss's own Update -- a single tick already proves "within 1 s"). ---
            _playerGo.transform.position = boss1.transform.position + new Vector3(-15f, 0f, 0f);
            InvokeTickDormant(boss1);
            Assert.IsTrue(IsEngaged(boss1), "AC1: must wake once Max is within WakeRadius (16 m) with clear sight");

            // ================================================================= World 2, Sludgequeen's area
            MapData world2 = LoadMap(WorldLibrary.World2);
            Component boss2 = BuildBoss(world2, "sludgequeen");
            Vector3 w2Entrance = GatePosition(world2, "g24");
            Assert.That(Vector3.Distance(w2Entrance, boss2.transform.position), Is.GreaterThan(BossTuning.WakeRadius),
                "fixture: World 2's own area entrance must sit beyond WakeRadius from the boss for this sub-check to mean anything");
            _playerGo.transform.position = w2Entrance;

            for (int i = 0; i < 1200; i++)
                InvokeTickDormant(boss2);
            Assert.IsFalse(IsEngaged(boss2), "sub-check: World 2's Sludgequeen must stay Dormant after 20 s at the area entrance, beyond 16 m");

            // ================================================================= World 3, Anchorhead's area
            MapData world3 = LoadMap(WorldLibrary.World3);
            Component boss3 = BuildBoss(world3, "anchorhead");
            Vector3 w3Entrance = GatePosition(world3, "g29");
            Assert.That(Vector3.Distance(w3Entrance, boss3.transform.position), Is.GreaterThan(BossTuning.WakeRadius),
                "fixture: World 3's own area entrance must sit beyond WakeRadius from the boss for this sub-check to mean anything");
            _playerGo.transform.position = w3Entrance;

            for (int i = 0; i < 1200; i++)
                InvokeTickDormant(boss3);
            Assert.IsFalse(IsEngaged(boss3), "sub-check: World 3's Anchorhead must stay Dormant after 20 s at the area entrance, beyond 16 m");
        }
    }
}
