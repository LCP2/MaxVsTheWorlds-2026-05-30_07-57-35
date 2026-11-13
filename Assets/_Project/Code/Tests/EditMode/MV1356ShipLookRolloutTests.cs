using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1356 AC3 (the one new test, MV-465 Rule 1): across all 30 Mothership areas' generated floor,
    /// wall and cover dressing, no <see cref="MeshRenderer"/> uses the old light plate-atlas material
    /// (<c>M_ShipFloorAtlas</c>, areas 6-30 before this ticket) or a wood-grain surface. Tier 2: reads
    /// the resolved, instantiated materials the builders actually produced, not an authored constant.
    ///
    /// Fails on the base commit: <see cref="ShipKit.BuildAtlasFloor"/> defaulted to the old light atlas
    /// (<c>darkBase = false</c>), so every area's floor wore <c>M_ShipFloorAtlas</c>. See the fix comment
    /// for the quoted failing output.
    /// </summary>
    public sealed class MV1356ShipLookRolloutTests
    {
        [Test]
        public void AllThirtyAreas_FloorWallAndCover_UseNeitherOldPlateAtlasNorWoodGrain()
        {
            var host = new GameObject("MV1356 Host").transform;
            try
            {
                for (int area = 1; area <= 30; area++)
                {
                    var origin = new Vector3(area * 40f, 0f, 0f);
                    var glow = Color.Lerp(Color.yellow, Color.cyan, area / 30f);

                    ShipKit.BuildAtlasFloor(host, new Rect(origin.x, 0f, 20f, 20f), area, glow);
                    ShipKit.BuildBulkheadPanel(host, new Vector2(origin.x, 0f), new Vector2(origin.x + 12f, 0f),
                        new Vector3(0f, 0f, 1f), 3f, 0.3f, glow, true, area);

                    var size = new Vector3(3f, 1.6f, 3f);
                    var at = origin + new Vector3(5f, 0f, 5f);
                    ShipKit.BuildCrate(host, at, size);
                    ShipKit.BuildDropPod(host, at, size);
                    ShipKit.BuildDropship(host, at, new Vector3(10f, 1.6f, 4f));
                    ShipKit.BuildConsoleBank(host, at, new Vector3(3f, 1f, 1f));
                    ShipKit.BuildBulkheadStub(host, at, new Vector3(1.4f, 1.6f, 0.4f));
                }

                var offenders = new List<string>();
                foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        string tex = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "";
                        bool oldAtlas = m.name == "M_ShipFloorAtlas" || tex == "ShipFloorAtlasAlbedo";
                        bool wood = m.name.Contains("tint:Wood:") || m.name.Contains("tint:Wall:") || tex.ToLowerInvariant().Contains("wood");
                        if (oldAtlas || wood) offenders.Add($"{r.name} -> material '{m.name}' texture '{tex}'");
                    }
                }

                Assert.IsEmpty(offenders, "old plate-atlas / wood-grain renderers survive:\n" + string.Join("\n", offenders));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
