using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-819. MV-802's overhead pipe structure builds, but the mains sit entirely tucked under the
    /// wall soffit (MV-765) and each cross-main lands on top of its own far wall's hugging main — the
    /// drain reads as having no pipes at all from the actual gameplay camera, even though the geometry
    /// exists in the scene. One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED
    /// state (Tier 2): builds World 2 area a3's real dressing and casts sample rays from the gameplay
    /// camera's own resting pose (<see cref="FixedAngleCameraRig.RestingPose"/>, the same pose the live
    /// rig settles on) against every OTHER enabled renderer the dressing built — never the authored
    /// placement numbers.
    /// </summary>
    public sealed class MV819PipeVisibilityTests
    {
        // World 2's authored area id "a3" (index 3) is renamed to the old engine's "area<N>"
        // convention by WorldMapLoader.TryLoad — a resolved MapZone never carries "a3" as its own id.
        private const string AreaId = "area3";
        private const int SamplesPerMain = 10;
        private const float MinUnoccludedFraction = 0.6f;

        /// <summary>Slack on the occlusion distance check so a sample sitting exactly on another
        /// renderer's own bounds (e.g. a collar wrapping the same pipe right at that point) is not
        /// flagged as "blocked" by floating-point noise in the ray/AABB entry distance.</summary>
        private const float OcclusionEpsilon = 0.03f;

        [Test]
        public void OverheadMainsAreVisibleFromTheGameplayCamera_CrossMainsClearWallRuns_AndA3GetsAJunctionBox()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            MapZone zone = map.zones.FirstOrDefault(z => z != null && z.id == AreaId);
            Assert.IsNotNull(zone, $"World 2 must author area '{AreaId}' for this test to mean anything");
            Rect zoneRect = zone.Footprint;

            var host = new GameObject("MV819 host").transform;
            var rigGo = new GameObject("MV819 cam rig");
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");
                Transform overheadHost = dressingHost.Find("Overhead");
                Assert.IsNotNull(overheadHost, "the overhead structure host was never built");

                // ---- the real gameplay camera pose (60 deg / 26.02 m, FixedAngleCameraRig's own
                // shipped defaults), resting on area a3's own centre — never a hand-picked eye point. ----
                var rig = rigGo.AddComponent<FixedAngleCameraRig>();
                var target = new Vector3(zoneRect.center.x, 0f, zoneRect.center.y);
                rig.RestingPose(target, out Vector3 cameraPos, out _);

                // ---- every enabled renderer the dressing pass built, so occlusion is checked against
                // the whole built scene (soffit included) — a disabled renderer (an original wall/cover
                // face the dressing swapped out) is never actually drawn, so it cannot occlude anything. ----
                Renderer[] allRenderers = dressingHost.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.enabled)
                    .ToArray();

                // ---- a3's own overhead mains and cross-mains: matched by exact name, so a collar,
                // bracket or junction sub-part (none of which this AC gates on) is never swept in. ----
                Renderer[] areaMains = overheadHost.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.enabled && (r.gameObject.name == "Overhead Main" || r.gameObject.name == "Overhead Cross Main"))
                    .Where(r => zoneRect.Contains(new Vector2(r.bounds.center.x, r.bounds.center.z)))
                    .ToArray();
                Assert.IsNotEmpty(areaMains, $"area '{AreaId}' must build at least one overhead main for this test to mean anything");

                Renderer[] wallMains = areaMains.Where(r => r.gameObject.name == "Overhead Main").ToArray();
                Renderer[] crossMains = areaMains.Where(r => r.gameObject.name == "Overhead Cross Main").ToArray();
                Assert.IsNotEmpty(crossMains, $"area '{AreaId}' must build a cross-main for this test to mean anything");

                foreach (Renderer main in areaMains)
                {
                    int visible = 0;
                    foreach (Vector3 sample in SamplePoints(main.bounds, SamplesPerMain))
                    {
                        if (!IsOccluded(cameraPos, sample, allRenderers, main))
                            visible++;
                    }

                    float fraction = visible / (float)SamplesPerMain;
                    Assert.GreaterOrEqual(fraction, MinUnoccludedFraction,
                        $"{main.name} at {main.bounds.center} is only {fraction:P0} visible from the gameplay " +
                        "camera (needs >= 60%)");
                }

                // ---- change 2: a cross-main must never share its footprint with the wall-hugging main
                // it used to land directly on top of. ----
                foreach (Renderer cross in crossMains)
                    foreach (Renderer wallMain in wallMains)
                        Assert.IsFalse(cross.bounds.Intersects(wallMain.bounds),
                            $"{cross.name} at {cross.bounds.center} intersects {wallMain.name} at {wallMain.bounds.center}");

                // ---- change 3: junction boxes must actually build at a3's own corners now that the
                // matching tolerance accounts for how far MapGeometry.Cap extends a wall run's own ends. ----
                int junctionCount = overheadHost.GetComponentsInChildren<Transform>(true)
                    .Count(t => t.name == "Overhead Junction"
                                && zoneRect.Contains(new Vector2(t.position.x, t.position.z)));
                Assert.Greater(junctionCount, 0, $"area '{AreaId}' must get at least one overhead junction box");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
                Object.DestroyImmediate(rigGo);
            }
        }

        /// <summary>Evenly spaced points along a main's own dominant horizontal axis, at its bounds'
        /// own centre on the other two axes — the AABB IS the resolved shape here (a pipe never carries
        /// a collider of its own), so sampling its bounds is sampling the main itself.</summary>
        private static IEnumerable<Vector3> SamplePoints(Bounds b, int count)
        {
            bool alongX = b.size.x >= b.size.z;
            Vector3 minPt = alongX
                ? new Vector3(b.min.x, b.center.y, b.center.z)
                : new Vector3(b.center.x, b.center.y, b.min.z);
            Vector3 maxPt = alongX
                ? new Vector3(b.max.x, b.center.y, b.center.z)
                : new Vector3(b.center.x, b.center.y, b.max.z);

            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                yield return Vector3.Lerp(minPt, maxPt, t);
            }
        }

        /// <summary>True if some other renderer's bounds sits on the segment between
        /// <paramref name="cameraPos"/> and <paramref name="sample"/>, strictly nearer the camera than
        /// the sample itself — a ray/AABB slab test (<see cref="Bounds.IntersectRay"/>), never
        /// <see cref="Physics"/>: nothing this kit builds carries a collider.</summary>
        private static bool IsOccluded(Vector3 cameraPos, Vector3 sample, Renderer[] renderers, Renderer self)
        {
            Vector3 delta = sample - cameraPos;
            float maxDist = delta.magnitude;
            if (maxDist < 0.001f) return false;
            var ray = new Ray(cameraPos, delta / maxDist);

            foreach (Renderer r in renderers)
            {
                if (r == self) continue;
                if (r.bounds.IntersectRay(ray, out float dist) && dist < maxDist - OcclusionEpsilon)
                    return true;
            }
            return false;
        }
    }
}
