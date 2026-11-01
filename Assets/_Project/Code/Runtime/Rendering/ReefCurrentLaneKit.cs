using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// MV-1335: builds one current lane's own visual — a single static quad, sized and oriented exactly
    /// to the lane's authored rect/direction, carrying a fresh instance of the dedicated
    /// <c>MaxWorlds/CurrentLaneBand</c> shader, which paints its own band/chevron pattern procedurally
    /// off the mesh's own metre-scale UV0 (see that shader's own header for why this is procedural
    /// rather than a sampled runtime texture — an earlier pass of this ticket baked the pattern into a
    /// <c>StylizedTextures</c>-authored <c>Texture2D</c> and every GPU sample of it read back flat black
    /// in the Standalone build, isolated to the texture itself rather than the shader/material plumbing
    /// by swapping in <c>Texture2D.whiteTexture</c>, which sampled correctly through the exact same
    /// code path). Same "one quad + a scrolled material" idiom as
    /// <see cref="StormdrainKit.BuildSludgeFlowSurface"/>'s own sludge flow surface, without that
    /// method's bands/foam (a current lane only needs the band + chevron the ticket asks for) and
    /// without any scroll/reversal logic of its own — this Rendering-assembly class cannot depend on
    /// <c>MaxWorlds.Arena.MapCurrentLaneVisual</c> or <c>MapGeometry</c>, so
    /// <c>MapRuntime.BuildCurrentLane</c> attaches that piece itself, the same split
    /// <c>MapRuntime.BuildSludge</c> already keeps with <c>SludgeFlow</c>.
    /// </summary>
    public static class ReefCurrentLaneKit
    {
        /// <summary>Just proud of the floor (MV-1335) — same "flush decal, proud by a hair" idiom as
        /// <c>MapRuntime.DeckShadowProud</c>, so the band never z-fights the Reef floor slab beneath it.</summary>
        public const float LaneY = 0.02f;

        /// <summary>Real-world metres one chevron tile repeats across (MV-1335) — the ticket names no
        /// exact count, only "chevron streaks"; this is dense enough to read as a flow at every lane
        /// length the Reef authors (MV-1301's shortest lane is several metres).</summary>
        public const float ChevronPeriodMetres = 2.5f;

        /// <summary>Builds the lane's quad + its own fresh material (the caller needs the material back
        /// to hand to <c>MapCurrentLaneVisual</c>, which mutates it every frame). <paramref name="flowDir"/>
        /// need not be normalised or Y-zeroed — this does both, same contract as
        /// <see cref="StormdrainKit.DressSludgeTile"/>'s own <c>flowDirection</c> parameter.</summary>
        public static GameObject BuildLane(Transform parent, string name, Vector3 center, float width, float depth,
            Vector3 flowDir, out Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;

            Vector3 flow = flowDir.sqrMagnitude < 0.001f ? Vector3.forward : flowDir.normalized;
            flow.y = 0f;
            flow.Normalize();
            Vector3 across = new Vector3(flow.z, 0f, -flow.x);
            bool alongZ = Mathf.Abs(Vector3.Dot(flow, Vector3.forward)) > 0.5f;
            float run = alongZ ? depth : width;
            float span = alongZ ? width : depth;

            go.AddComponent<MeshFilter>().sharedMesh = BuildQuad(flow, across, run, span);
            MeshRenderer mr = go.AddComponent<MeshRenderer>();

            material = BuildLaneMaterial(span, name);
            mr.sharedMaterial = material;

            return go;
        }

        /// <summary>A fresh (never cached) <c>MaxWorlds/CurrentLaneBand</c> instance — a caller that
        /// mutates its OWN instance every frame (a current lane's scroll offset, reversal sign, and
        /// pre-flip pulse tint, <c>MaxWorlds.Arena.MapCurrentLaneVisual</c>) cannot share one material
        /// with every other lane on the map, same reasoning <c>StormdrainKit.Unlit</c>'s per-colour cache
        /// explicitly does NOT apply to its own transparent/mutable callers. Band/chevron colour comes
        /// from the shader's own Properties defaults (already tuned to the ticket's hue/alpha targets)
        /// — only the per-lane span and the per-frame scroll/pulse floats need to vary.</summary>
        private static Material BuildLaneMaterial(float span, string name)
        {
            Shader shader = Shader.Find("MaxWorlds/CurrentLaneBand");
            var m = new Material(shader) { name = $"CurrentLane_{name}", hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_Span", span);
            m.SetFloat("_ChevronPeriod", ChevronPeriodMetres);
            m.SetFloat("_ScrollOffset", 0f);
            m.SetFloat("_PulseBoost", 0f);
            return m;
        }

        /// <summary>UV0 carries the tile-local coordinate in METRES, centred on the quad (u along the
        /// flow axis, v across it) — same convention <c>SludgeChannelFlow.shader</c>'s own mesh UV
        /// already established, so <c>CurrentLaneBand.shader</c>'s chevron math reads directly in the
        /// same units <see cref="ChevronPeriodMetres"/> is authored in, no tiling-scale property
        /// needed.</summary>
        private static Mesh BuildQuad(Vector3 flow, Vector3 across, float run, float span)
        {
            float halfRun = run * 0.5f;
            float halfSpan = span * 0.5f;
            Vector3 up = Vector3.up * LaneY;

            var verts = new[]
            {
                -flow * halfRun - across * halfSpan + up,
                 flow * halfRun - across * halfSpan + up,
                 flow * halfRun + across * halfSpan + up,
                -flow * halfRun + across * halfSpan + up,
            };
            var uvs = new[]
            {
                new Vector2(-halfRun, -halfSpan),
                new Vector2(halfRun, -halfSpan),
                new Vector2(halfRun, halfSpan),
                new Vector2(-halfRun, halfSpan),
            };
            var normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };

            var mesh = new Mesh { name = "CurrentLaneQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
