using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1416: a destroyed canister / destructible cover piece hid its renderers with
    /// <c>enabled = false</c> but never called <c>MapStaticBatchRoot.MarkPermanentlyHidden</c>, so the
    /// next <c>ApplyAreaGate</c> (every 0.25 s) turned them back on while the collider stayed off.
    /// One test, Tier 2 (resolved <c>Renderer.enabled</c> / <c>Collider.enabled</c> after the real gate
    /// has run). Fails on bab2b0a: the canister drum and the cover body read enabled after the gate.
    /// // Guards MV-1416
    /// </summary>
    public sealed class MV1416DestroyedStaysHiddenTests
    {
        private const float OriginX = 97531f;
        private const float OriginZ = 113246f;

        private GameObject _root;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            Canister.ResetRegistry();
            DestructibleCover.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            Canister.ResetRegistry();
            DestructibleCover.ResetRegistry();
            DevTuning.Reset();
        }

        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1416 Test World",
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
                    origin = new WorldAreaOrigin { x = OriginX - 10f, z = OriginZ },
                    size = new WorldAreaSize { w = 20f, d = 20f },
                    canisters = new[]
                    {
                        new WorldCanister { id = "a1_canister1", x = OriginX - 6f, z = OriginZ + 10f },
                    },
                    cover = new[]
                    {
                        new WorldCover
                        {
                            id = "a1_cover1", x = OriginX + 4f, z = OriginZ + 10f,
                            width = 2f, height = 1.5f, depth = 1f, shape = "box", hp = 100f,
                            dressing = "shed",
                        },
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
        public void DestroyedCanisterAndCover_StayHidden_AfterTheAreaGateRunsWithTheirZoneActive()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(BuildWorld(), out MapData map, out string reason), reason);

            _root = new GameObject("MV-1416 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            Assert.AreEqual(1, built.Canisters.Count, "setup failure: one canister");
            Assert.AreEqual(1, built.Cover.Count, "setup failure: one cover piece");

            Transform mapRoot = _root.transform.Find($"Map: {map.name}");
            var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
            Assert.IsNotNull(batchRoot, "setup failure: MapRuntime attaches a MapStaticBatchRoot");
            // Start() (initial gate + Active registration) never fires on its own outside Play mode.
            typeof(MapStaticBatchRoot).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(batchRoot, null);

            Canister canister = built.Canisters[0];
            canister.Build();
            GameObject coverBody = built.Cover[0].Body;
            DestructibleCover cover = coverBody.GetComponent<DestructibleCover>();
            cover.Build();

            // A dressing part under the body, as ReefDressing/BackyardDressing.DressCover builds them.
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = "Dressing Part";
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(coverBody.transform, false);

            Vector3 canisterPos = canister.transform.position;
            canister.TakeDamage(new DamageInfo(40f, canisterPos, Vector3.down, Team.Player));
            for (float t = 0f; t < 1.5f; t += 0.1f) canister.Tick(0.1f);
            cover.TakeDamage(new DamageInfo(100f, coverBody.transform.position, Vector3.down, Team.Player));
            Assert.IsFalse(canister.IsAlive, "setup failure: canister destroyed");
            Assert.IsFalse(cover.IsAlive, "setup failure: cover destroyed");

            batchRoot.ApplyAreaGate("area1", new Vector3(OriginX, 0f, OriginZ + 10f));

            var failures = new List<string>();
            foreach (Renderer r in canister.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) failures.Add($"canister renderer '{r.name}' enabled after the gate");
            foreach (Renderer r in coverBody.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) failures.Add($"cover renderer '{r.name}' enabled after the gate");
            foreach (Collider c in canister.GetComponentsInChildren<Collider>(true))
                if (c.enabled) failures.Add("canister collider enabled");
            foreach (Collider c in coverBody.GetComponentsInChildren<Collider>(true))
                if (c.enabled) failures.Add("cover collider enabled");

            Assert.IsEmpty(failures, string.Join("; ", failures));
        }
    }
}
