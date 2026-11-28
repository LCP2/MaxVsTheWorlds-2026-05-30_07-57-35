using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1487 (the one new test, Rule 1; Tier 2 resolved values): a SEALED Parts Cache's label is about one lock clamp tall and
    /// never wider than 2.2x the dome's diameter, and the shield dome's base circle is exactly the dashed floor ring (min Y on
    /// the floor, same XZ centre and radius). Fails on the base commit: the shield was a sphere centred at 0.55 x height and the
    /// label was ~4-5x the cache's width.
    /// </summary>
    public sealed class MV1487CacheLabelAndDomeTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            PartsCache.ResetRegistry();
        }

        [Test]
        public void SealedCache_LabelIsClampSized_AndDomeRimIsTheDashedRing()
        {
            PartsCache.ResetRegistry();
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go.transform.position = new Vector3(10f, WorldMapLoader.PartsCacheHeight * 0.5f, 10f);
            _go.transform.localScale = new Vector3(WorldMapLoader.PartsCacheFootprint, WorldMapLoader.PartsCacheHeight, WorldMapLoader.PartsCacheFootprint);
            var cache = _go.AddComponent<PartsCache>();
            cache.Configure("mv1487", WorldCatalog.IndexOf(WorldIds.City), 1);
            cache.Build();

            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(PartsCache).GetField("_populated", f).SetValue(cache, true);
            typeof(PartsCache).GetProperty("RobotsLeft").GetSetMethod(true).Invoke(cache, new object[] { 12 });
            typeof(PartsCache).GetMethod("UpdateLabel", f).Invoke(cache, null);

            Renderer shield = cache.ShieldRenderer;
            Bounds sb = shield.bounds;
            float floorY = _go.transform.position.y - WorldMapLoader.PartsCacheHeight * 0.5f;

            // Dashed ring: centre and radius from its resolved segment positions.
            Transform ring = cache.DashedRing;
            Vector3 centre = Vector3.zero;
            foreach (Transform s in ring) centre += s.position;
            centre /= ring.childCount;
            float ringR = 0f;
            foreach (Transform s in ring)
                ringR = Mathf.Max(ringR, new Vector2(s.position.x - centre.x, s.position.z - centre.z).magnitude);

            Assert.AreEqual(floorY, sb.min.y, 0.05f, "dome base must be at floor level");
            Assert.AreEqual(centre.x, sb.center.x, 0.05f, "dome centre X = ring centre X");
            Assert.AreEqual(centre.z, sb.center.z, 0.05f, "dome centre Z = ring centre Z");
            Assert.AreEqual(ringR, sb.extents.x, 0.05f, "dome radius = dashed ring radius");

            Vector2 label = cache.LabelWorldSize;
            Transform lockT = _go.transform.Find("Body/Lid/Lock0");
            Assert.IsNotNull(lockT);
            float clamp = lockT.lossyScale.z;   // the clamp's longest face dimension, resolved
            Assert.LessOrEqual(label.y, clamp * 1.2f, "label glyph height vs lock clamp: " + label.y);
            Assert.LessOrEqual(label.x, 2.2f * (2f * ringR), "label width vs 2.2x dome diameter: " + label.x);
        }
    }
}
