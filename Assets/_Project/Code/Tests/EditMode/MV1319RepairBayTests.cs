using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1319 (ticket AC1): "Max at 40% health steps on a READY bay -> 100% health and the bay is USED;
    /// stepping on it again changes nothing; after save/restore it is still USED." Must fail to COMPILE
    /// on the pre-fix base commit: no <see cref="RepairBay"/> type, no <see cref="WorldArea.repairBays"/>
    /// field and no <see cref="WorldRepairBay"/> type exist there — same "fails first" idiom as
    /// <c>MV1318ChallengePadTests</c>. See the fix comment for the captured failure output.
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> (same construction idiom as
    /// <c>MV1318ChallengePadTests.BuildWorld</c>) carrying one area with a repair bay, loaded through the
    /// real <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> pipeline, driving the real
    /// <see cref="RepairBay.Tick"/>/<see cref="PlayerHealth.TakeDamage"/> path — no direct heal-math call
    /// anywhere in this file. "After save/restore" is driven by the real
    /// <see cref="RepairBay.UsedIds"/>/<see cref="RepairBay.ApplyCheckpointUsedIds"/> pair
    /// <see cref="MaxWorlds.Save.SaveSystem"/> itself calls, against a freshly-rebuilt map — the same
    /// "re-load through the real pipeline, not a snapshot shortcut" idiom <c>MV1318ChallengePadTests</c>
    /// uses nowhere else to test for, since MV-1318 never needed a save/restore leg of its own.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value — a
    /// bay's own <see cref="RepairBay.State"/>, Max's own <see cref="PlayerHealth.Current"/> — never an
    /// authored constant, never a rendered pixel.
    /// // Guards MV-1319
    /// </summary>
    public sealed class MV1319RepairBayTests
    {
        // Same "distinctive far-off origin" idiom MV1318ChallengePadTests uses — EditMode tests share one
        // physics scene for the whole cc-verify run with no per-test reset.
        private const float OriginX = 71933f;
        private const float OriginZ = 18447f;

        private GameObject _root;
        private GameObject _playerGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RepairBay.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_root != null) Object.DestroyImmediate(_root);

            RepairBay.ResetRegistry();
            DevTuning.Reset();
        }

        /// <summary>Stub (entry, empty) -> "a1" (one repair bay) — same two-area skeleton
        /// <c>MV1318ChallengePadTests.BuildWorld</c> uses.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1319 Test World",
            dials = new WorldDials
            {
                areaCount = 1, baseThreat = 0f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f }, powerupCadence = 99,
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = OriginX - 2f, z = OriginZ - 6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = OriginX - 20f, z = OriginZ },
                    size = new WorldAreaSize { w = 40f, d = 20f },
                    repairBays = new[]
                    {
                        new WorldRepairBay { id = "a1_repair1", x = OriginX, z = OriginZ + 10f },
                    },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void SteppingOnAReadyBay_HealsToFullAndMarksUsed_SecondStepNoOp_AndUsedSurvivesSaveRestore()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _root = new GameObject("MV-1319 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            Assert.AreEqual(1, built.RepairBays.Count, "setup failure: fixture must build exactly one repair bay");

            // AddComponent's own Awake never runs outside Play mode (RepairBay.Build's own doc comment,
            // same as ChallengePad.Build) — build the actor explicitly.
            RepairBay bay = built.RepairBays[0];
            bay.Build();
            Assert.AreEqual(RepairBay.RepairState.Ready, bay.State, "setup failure: a freshly built bay must start READY");

            _playerGo = new GameObject("MV-1319 Max", typeof(CharacterController)) { tag = "Player" };
            var health = _playerGo.AddComponent<PlayerHealth>();
            health.Initialize();
            _playerGo.AddComponent<PlayerAbilities>();

            float maxHp = health.Max;
            health.TakeDamage(new DamageInfo(maxHp * 0.6f, _playerGo.transform.position, Vector3.forward, Team.Enemy));
            Assert.AreEqual(maxHp * 0.4f, health.Current, 0.01f,
                "setup failure: Max must be at 40% health before stepping on the bay");

            // === Step onto the READY bay ===
            _playerGo.transform.position = bay.transform.position;
            bay.Tick(1f / 60f);

            Assert.AreEqual(maxHp, health.Current, 0.01f, "MV-1319 AC1: stepping on a READY bay must heal Max to full");
            Assert.AreEqual(RepairBay.RepairState.Used, bay.State, "MV-1319 AC1: stepping on a READY bay must mark it USED");

            // === Stepping on it again changes nothing ===
            health.TakeDamage(new DamageInfo(maxHp * 0.6f, _playerGo.transform.position, Vector3.forward, Team.Enemy));
            float healthAfterSecondHit = health.Current;
            bay.Tick(1f / 60f);
            Assert.AreEqual(healthAfterSecondHit, health.Current, 0.01f, "MV-1319 AC1: a USED bay must not heal Max again");
            Assert.AreEqual(RepairBay.RepairState.Used, bay.State);

            // === After save/restore it is still USED — the real checkpoint capture/restore pair,
            // against a freshly-rebuilt map (MapRuntime.Build resets the registry itself, same as a
            // resume's fresh level load). ===
            string[] usedIds = RepairBay.UsedIds();
            Assert.AreEqual(1, usedIds.Length, "setup failure: exactly one bay must have recorded USED");
            Assert.AreEqual(bay.Id, usedIds[0]);

            MapBuild rebuilt = MapRuntime.Build(map, _root.transform);
            RepairBay freshBay = rebuilt.RepairBays[0];
            freshBay.Build();
            Assert.AreEqual(RepairBay.RepairState.Ready, freshBay.State,
                "setup failure: a freshly rebuilt bay must start READY before the checkpoint restore applies");

            RepairBay.ApplyCheckpointUsedIds(usedIds);
            Assert.AreEqual(RepairBay.RepairState.Used, freshBay.State,
                "MV-1319 AC1: a checkpoint restore must re-apply USED onto the matching bay");
        }
    }
}
