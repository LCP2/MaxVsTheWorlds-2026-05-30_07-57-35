using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1419: City destructible cover was dressed as one of 18 street props (a "car", a "bin") tinted grey,
    /// so nothing said "destructible". One test, Tier 2: a built 3x1 m piece dressed <c>car</c> must be three
    /// barricade modules with the resolved orange top, show cracks/chips at 30% HP, and be fully hidden with a
    /// rubble decal after the killing hit.
    /// </summary>
    public sealed class MV1419CityDestructibleLookTests
    {
        private const float OriginX = 97531f;
        private const float OriginZ = 113246f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private GameObject _root;
        private Camera[] _suppressed;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            DestructibleCover.ResetRegistry();
            _suppressed = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            CameraTestUtil.RestoreAmbientMainCameras(_suppressed);
            DestructibleCover.ResetRegistry();
            DevTuning.Reset();
        }

        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1419 Test World",
            dials = new WorldDials { areaCount = 1, baseThreat = 0f, threatGrowth = 0f, pacingRhythm = new[] { 1f }, powerupCadence = 99 },
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
                    cover = new[]
                    {
                        new WorldCover
                        {
                            id = "a1_cover1", x = OriginX + 4f, z = OriginZ + 10f,
                            width = 3f, height = 1.5f, depth = 1f, shape = "box", hp = 150f, dressing = "car",
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
        public void CityDestructiblePiece_IsBarricadeModules_ShowsDamage_AndBreaksCleanly()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _root = new GameObject("MV-1419 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            CityDressing.Dress(_root.transform, map, cfg, built.Cover);

            GameObject body = built.Cover[0].Body;
            DestructibleCover cover = body.GetComponent<DestructibleCover>();
            cover.Build();

            var failures = new List<string>();

            Transform[] modules = body.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Module").ToArray();
            if (modules.Length != 3) failures.Add($"expected exactly 3 module children, found {modules.Length}");
            if (body.GetComponentsInChildren<Transform>(true).Any(t => t.name.IndexOf("car", System.StringComparison.OrdinalIgnoreCase) >= 0 && t != body.transform))
                failures.Add("a child still carries the car dressing");
            if (_root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Car"))
                failures.Add("the car dressing mesh was still built");

            if (modules.Length > 0)
            {
                Renderer top = modules[0].Find("Top")?.GetComponent<Renderer>();
                if (top == null) failures.Add("module has no Top renderer");
                else
                {
                    var mpb = new MaterialPropertyBlock();
                    top.GetPropertyBlock(mpb);
                    Color c = mpb.GetColor(BaseColorId);
                    Color want = new Color32(0xF0, 0x6E, 0x14, 255);
                    if (Mathf.Abs(c.r - want.r) > 0.05f || Mathf.Abs(c.g - want.g) > 0.05f || Mathf.Abs(c.b - want.b) > 0.05f)
                        failures.Add($"module top resolved colour {c} is not within 0.05 of #F06E14");
                }
            }

            cover.TakeDamage(new DamageInfo(105f, body.transform.position, Vector3.down, Team.Player)); // 150 -> 45 = 30%
            foreach (Transform m in modules)
            {
                bool shows = m.GetComponentsInChildren<Transform>(false)
                    .Any(t => (t.name == "Crack" || t.name == "Chip") && t.gameObject.activeInHierarchy);
                if (!shows) failures.Add("a module shows no crack or chip at 30% HP");
            }

            cover.TakeDamage(new DamageInfo(60f, body.transform.position, Vector3.down, Team.Player));
            Assert.IsFalse(cover.IsAlive, "setup failure: piece destroyed");

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) failures.Add($"renderer '{r.name}' still enabled after the break");
            if (_root.GetComponentsInChildren<Transform>(true).All(t => t.name != "Rubble"))
                failures.Add("no rubble decal after the break");

            Assert.IsEmpty(failures, string.Join("; ", failures));
        }
    }
}
