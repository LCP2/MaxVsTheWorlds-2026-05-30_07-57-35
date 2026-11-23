using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1423: MV-1417's red canister and MV-1419's orange barricade passed their isolated tests but not the
    /// live City - their colour rode a MaterialPropertyBlock over a textured Stylized surface material, which
    /// renders dark/brick-textured in the real world, and CharacterSkinDirector then skinned every renderer under them as a robot. One test, Tier 2: build the real City through the game's
    /// own path (WorldLibrary.Load, MapRuntime.Build, world materials, CityDressing, the surface sweep, the
    /// MapStaticBatchRoot Start snapshot), run the area gate twice, and read the resolved renderer state of
    /// every a3 canister and destructible piece. // Guards MV-1423
    /// </summary>
    public sealed class MV1423LiveCityLooksTests
    {
        private GameObject _root;
        private Camera[] _suppressed;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            Canister.ResetRegistry();
            DestructibleCover.ResetRegistry();
            _suppressed = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            foreach (var wm in Object.FindObjectsByType<WorldMaterials>(FindObjectsSortMode.None)) Object.DestroyImmediate(wm.gameObject);
            foreach (var cs in Object.FindObjectsByType<CharacterSkinDirector>(FindObjectsSortMode.None)) Object.DestroyImmediate(cs.gameObject);
            foreach (var rs in Object.FindObjectsByType<RuntimeSurfaceDirector>(FindObjectsSortMode.None)) Object.DestroyImmediate(rs.gameObject);
            CameraTestUtil.RestoreAmbientMainCameras(_suppressed);
            Canister.ResetRegistry();
            DestructibleCover.ResetRegistry();
            DevTuning.Reset();
        }

        private static bool Near(Color c, Color32 want) =>
            Mathf.Abs(c.r - want.r / 255f) <= 0.06f && Mathf.Abs(c.g - want.g / 255f) <= 0.06f && Mathf.Abs(c.b - want.b / 255f) <= 0.06f;

        [Test]
        public void LiveCityA3_CanistersAreRed_AndDestructibleCoverIsOrangeBarricade_AfterTheGateRunsTwice()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "setup failure: city config");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            int cityIndex = -1;
            for (int i = 0; i < 8 && cityIndex < 0; i++)
                if (WorldCatalog.Get(i).Kit == WorldKit.City) cityIndex = i;
            Assert.GreaterOrEqual(cityIndex, 0, "setup failure: City world row");

            _root = new GameObject("MV-1423 Host");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            new GameObject("WorldMaterials").AddComponent<WorldMaterials>().Apply(WorldCatalog.Get(cityIndex).Palette);
            CityDressing.Dress(_root.transform, map, cfg, built.Cover);

            // Awake never runs on AddComponent outside Play mode; the live game builds both here.
            foreach (Canister c in built.Canisters) c.Build();
            foreach (CoverPiece p in built.Cover)
                if (p.Body != null) p.Body.GetComponent<DestructibleCover>()?.Build();

            // The surface sweep the live scene runs once at Start.
            var director = new GameObject("RuntimeSurfaces").AddComponent<RuntimeSurfaceDirector>();
            typeof(RuntimeSurfaceDirector).GetMethod("Sweep", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(director, null);

            // ...and the character-skin sweep (CharacterSkinDirector.Start), which skins every renderer under an IDamageable.
            var skins = new GameObject("CharacterSkins").AddComponent<CharacterSkinDirector>();
            typeof(CharacterSkinDirector).GetMethod("DressCharacters", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(skins, null);

            Transform mapRoot = _root.transform.Find($"Map: {map.name}");
            var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
            Assert.IsNotNull(batchRoot, "setup failure: MapStaticBatchRoot");
            typeof(MapStaticBatchRoot).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(batchRoot, null);

            var failures = new List<string>();
            string zoneId = null;
            int canisters = 0, pieces = 0;

            foreach (Canister c in built.Canisters)
            {
                if (!c.Id.StartsWith("a3_")) continue;
                canisters++;
                Vector3 p = c.transform.position;
                zoneId ??= map.ZoneAt(p.x, p.y, p.z)?.id;
            }
            foreach (CoverPiece cp in built.Cover)
            {
                if (cp.Body == null || !cp.Cover.Name.StartsWith("a3_") || cp.Body.GetComponent<DestructibleCover>() == null) continue;
                pieces++;
                Vector3 p = cp.Body.transform.position;
                zoneId ??= map.ZoneAt(p.x, p.y, p.z)?.id;
            }
            Assert.IsNotNull(zoneId, "setup failure: a3 zone");
            Assert.Greater(canisters, 0, "setup failure: a3 canisters");
            Assert.Greater(pieces, 0, "setup failure: a3 destructible pieces");

            batchRoot.ApplyAreaGate(zoneId);
            batchRoot.ApplyAreaGate(zoneId);

            foreach (Canister c in built.Canisters)
            {
                if (!c.Id.StartsWith("a3_")) continue;
                Renderer drum = c.GetComponent<Renderer>();
                if (drum == null || !drum.enabled) { failures.Add($"{c.Id}: drum renderer not enabled"); continue; }
                Color col = FlatColorMaterial.ResolvedColor(drum);
                if (!Near(col, new Color32(0xB2, 0x1C, 0x18, 255))) failures.Add($"{c.Id}: drum colour {col} not within 0.06 of #B21C18");
                if (drum.HasPropertyBlock()) failures.Add($"{c.Id}: drum colour rides a property block");
            }

            foreach (CoverPiece cp in built.Cover)
            {
                if (cp.Body == null || !cp.Cover.Name.StartsWith("a3_")) continue;
                var dc = cp.Body.GetComponent<DestructibleCover>();
                if (dc == null) continue;

                Renderer body = cp.Body.GetComponent<Renderer>();
                if (body != null && body.enabled) failures.Add($"{cp.Cover.Name}: the old full-size cube body is enabled");

                int tops = 0;
                foreach (Renderer r in cp.Body.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name != "Top") continue;
                    tops++;
                    if (!r.enabled) { failures.Add($"{cp.Cover.Name}: a module top is not enabled"); continue; }
                    Color col = FlatColorMaterial.ResolvedColor(r);
                    if (!Near(col, new Color32(0xF0, 0x6E, 0x14, 255))) failures.Add($"{cp.Cover.Name}: top colour {col} not within 0.06 of #F06E14");
                    if (r.HasPropertyBlock()) failures.Add($"{cp.Cover.Name}: top colour rides a property block");
                }
                if (tops == 0) failures.Add($"{cp.Cover.Name}: no module tops built");
            }

            Assert.IsEmpty(failures, string.Join("; ", failures));
        }
    }
}
