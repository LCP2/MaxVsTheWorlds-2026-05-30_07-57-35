using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 3's Market Street dressing pass (MV-1177) — the City counterpart of
    /// <see cref="StormdrainDressing"/>/<see cref="ReefDressing"/>, called from
    /// <see cref="BackyardPath.ApplyWorldMaterials"/> for <see cref="WorldKit.City"/>.
    ///
    /// Four things, matching the ticket's own change list: (a) paints every authored
    /// <see cref="WorldSurface"/> onto the road with <see cref="CityKit"/>'s floor-paint builders; (b)
    /// replaces every <see cref="StructuralWall"/>'s renderer with the matching facade/building-end/
    /// cutaway art, keeping its collider untouched, and frames both door gaps (g0 and the world exit)
    /// with posts, a neon bar and a floor pool; (c) swaps every authored cover piece's box renderer for
    /// its own City prop, split between <see cref="CityKit"/>'s reusable pieces (vehicles, barriers,
    /// fence, bollards, the lamp, bench/bin/post box/tub/planter) and this class's own Market-Street-only
    /// props (the sign, the stalls, the clock, the cart, the cafe tables, the lockers, the bike rack);
    /// (d) marks its own root <see cref="KeepsOwnMaterial"/> so neither the per-world material sweep nor
    /// the runtime re-dress pass ever repaints it.
    ///
    /// Idempotent per load, same contract as every dressing pass in this namespace: it builds under one
    /// named host, and a second call replaces that host rather than doubling every prop.
    /// </summary>
    public static class CityDressing
    {
        private const string HostName = "City Dressing";

        /// <summary>A pastel tuft tone the flower stall rows use alongside <see cref="CityKit.Cream"/>/
        /// <see cref="CityKit.Plum"/>/<see cref="CityKit.Mustard"/> — a LIT material, so the relay
        /// colour guard (unlit/emissive/additive only, see <see cref="CityKit"/>'s own header) never
        /// applies to it regardless of hue.</summary>
        private static readonly Color TuftPink = new Color(224f / 255f, 150f / 255f, 190f / 255f);

        public static void Dress(Transform host, MapData map, WorldConfig cfg, IReadOnlyList<CoverPiece> cover)
        {
            if (host == null || map == null || cfg == null) return;

            Transform existing = host.Find(HostName);
            if (existing != null)
            {
                if (Application.isPlaying) Object.Destroy(existing.gameObject);
                else Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject(HostName).transform;
            root.SetParent(host, false);
            root.gameObject.AddComponent<KeepsOwnMaterial>();

            DressGround(host);

            WorldArea a1 = cfg.Area("a1");
            if (a1 != null) DressSurfaces(root, a1);

            DressWalls(root, host, map, cfg);
            DressDoorFrames(root, host);
            DressCover(root, cover);
        }

        // ---------------------------------------------------------------- (0) ground

        /// <summary>MV-1205: re-skins every City floor renderer from the generic per-world sweep's
        /// grass-shaded Ground material (<see cref="WorldMaterials.Apply"/>, already run by the time
        /// <see cref="Dress"/> is called — see <see cref="BackyardPath.ApplyWorldMaterials"/>) onto
        /// the City's own baked asphalt texture. Every City area's floor gets this, not just a1's —
        /// unlike <see cref="DressSurfaces"/>'s authored pavement/kerb paint, which only a1 carries
        /// surfaces data for. Same "runs after the sweep, overrides by KindOf, marks dressed" shape
        /// as <see cref="ReefKit.DressHull"/>'s own floor override.</summary>
        private static void DressGround(Transform host)
        {
            Material asphalt = MaterialLibrary.Tinted(SurfaceKind.Asphalt, CityKit.Asphalt);
            if (asphalt == null) return;

            foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!WorldMaterials.IsWorldSurface(r)) continue;
                if (WorldMaterials.KindOf(r) != SurfaceKind.Ground) continue;

                r.sharedMaterial = asphalt;
                if (r.GetComponent<KeepsOwnMaterial>() == null) r.gameObject.AddComponent<KeepsOwnMaterial>();
            }
        }

        // ---------------------------------------------------------------- (a) surfaces

        private static void DressSurfaces(Transform root, WorldArea area)
        {
            if (area.surfaces == null || area.surfaces.Length == 0) return;

            var surfaceHost = new GameObject("Surfaces").transform;
            surfaceHost.SetParent(root, false);

            foreach (WorldSurface s in area.surfaces)
            {
                if (s == null) continue;

                var rect = new Rect(s.x - s.w * 0.5f, s.z - s.d * 0.5f, s.w, s.d);
                var centre = new Vector3(s.x, 0f, s.z);

                switch (s.kind)
                {
                    case "pavement": CityKit.BuildPavement(surfaceHost, rect); break;
                    case "kerb": BuildLine(surfaceHost, s, CityKit.BuildKerbLine); break;
                    case "edgeline": BuildLine(surfaceHost, s, CityKit.BuildEdgeLine); break;
                    case "centreline": BuildLine(surfaceHost, s, (p, a, b) => CityKit.BuildDashedLine(p, a, b)); break;
                    case "zebra": CityKit.BuildZebra(surfaceHost, rect); break;
                    case "hatch": CityKit.BuildHatchBox(surfaceHost, rect); break;
                    case "bay": CityKit.BuildBayOutline(surfaceHost, rect); break;
                    case "pad": CityKit.BuildPad(surfaceHost, rect); break;
                    case "manhole": CityKit.BuildManhole(surfaceHost, centre); break;
                    case "drain": CityKit.BuildDrainGrate(surfaceHost, rect); break;
                    case "puddle": CityKit.BuildPuddle(surfaceHost, centre, new Vector2(s.w, s.d)); break;
                    case "arrowE": CityKit.BuildLaneArrow(surfaceHost, centre, Vector2.right); break;
                    case "arrowW": CityKit.BuildLaneArrow(surfaceHost, centre, Vector2.left); break;
                    case "deck": CityKit.BuildDeck(surfaceHost, rect); break;
                    default:
                        Debug.LogWarning($"[CityDressing] area '{area.id}': unknown surface kind '{s.kind}' (id '{s.id}') skipped.");
                        break;
                }
            }
        }

        private static void BuildLine(Transform parent, WorldSurface s, System.Func<Transform, Vector2, Vector2, GameObject> build)
        {
            bool alongX = s.w >= s.d;
            Vector2 a = alongX ? new Vector2(s.x - s.w * 0.5f, s.z) : new Vector2(s.x, s.z - s.d * 0.5f);
            Vector2 b = alongX ? new Vector2(s.x + s.w * 0.5f, s.z) : new Vector2(s.x, s.z + s.d * 0.5f);
            build(parent, a, b);
        }

        // ---------------------------------------------------------------- (b) walls + door frames

        /// <summary>True if <paramref name="a"/> (a built wall segment's own, possibly-offset centre
        /// coordinate) names the same zone edge as <paramref name="b"/> (that edge's authored
        /// coordinate). A party wall (rooms on both sides) sits exactly on the line (offset 0), but a
        /// one-sided (exterior) wall is pushed half a wall thickness OUTSIDE the room it belongs to
        /// (<see cref="MapGeometry.Solids"/>'s own "a room keeps every metre it was authored" offset) —
        /// so the tolerance has to clear that half-thickness, not just float noise.</summary>
        private static bool Near(float a, float b, float wallThickness) => Mathf.Abs(a - b) <= wallThickness * 0.5f + 0.05f;

        private static void DressWalls(Transform root, Transform host, MapData map, WorldConfig cfg)
        {
            WorldArea a1 = cfg.Area("a1");
            if (a1 == null) return;
            WorldArea stub = cfg.Area("stub");
            float wt = map.wallThickness;

            List<WallSegment> segments = MapGeometry.Walls(map);
            if (segments.Count == 0) return;

            var byName = new Dictionary<string, StructuralWall>();
            foreach (StructuralWall wall in host.GetComponentsInChildren<StructuralWall>(true))
                byName[wall.gameObject.name] = wall;

            var wallHost = new GameObject("Walls").transform;
            wallHost.SetParent(root, false);

            foreach (WallSegment seg in segments)
            {
                if (!byName.TryGetValue(seg.Name, out StructuralWall wall) || wall == null) continue;

                Vector2 a, b;
                if (seg.AlongX)
                {
                    a = new Vector2(seg.Center.x - seg.Size.x * 0.5f, seg.Center.z);
                    b = new Vector2(seg.Center.x + seg.Size.x * 0.5f, seg.Center.z);
                }
                else
                {
                    a = new Vector2(seg.Center.x, seg.Center.z - seg.Size.z * 0.5f);
                    b = new Vector2(seg.Center.x, seg.Center.z + seg.Size.z * 0.5f);
                }

                GameObject built;
                if (seg.AlongX && Near(seg.Center.z, a1.ZMax, wt))
                    built = CityKit.BuildShopfrontRun(wallHost, a, b, new Vector3(0f, 0f, -1f), map.wallHeight, 0);
                else if (seg.AlongX && Near(seg.Center.z, a1.ZMin, wt))
                    built = CityKit.BuildCutaway(wallHost, a, b, new Vector3(0f, 0f, -1f));
                else if (!seg.AlongX && Near(seg.Center.x, a1.XMax, wt))
                    built = CityKit.BuildBuildingEnd(wallHost, a, b, new Vector3(-1f, 0f, 0f), map.wallHeight, CityKit.Bricks[2], 2);
                else if (!seg.AlongX && Near(seg.Center.x, a1.XMin, wt))
                    built = CityKit.BuildBuildingEnd(wallHost, a, b, new Vector3(1f, 0f, 0f), map.wallHeight, CityKit.Bricks[0], 2);
                else if (stub != null && seg.AlongX && Near(seg.Center.z, stub.ZMax, wt))
                    built = CityKit.BuildBuildingEnd(wallHost, a, b, new Vector3(0f, 0f, -1f), map.wallHeight, CityKit.Bricks[1], 1);
                else if (stub != null && seg.AlongX && Near(seg.Center.z, stub.ZMin, wt))
                    built = CityKit.BuildBuildingEnd(wallHost, a, b, new Vector3(0f, 0f, 1f), map.wallHeight, CityKit.Bricks[1], 1);
                else if (stub != null && !seg.AlongX && Near(seg.Center.x, stub.XMin, wt))
                    built = CityKit.BuildBuildingEnd(wallHost, a, b, new Vector3(1f, 0f, 0f), map.wallHeight, CityKit.Bricks[1], 1);
                else
                    built = null;

                if (built == null) continue;

                var rend = wall.GetComponent<Renderer>();
                if (rend != null) rend.enabled = false;
            }
        }

        /// <summary>Frames every built <see cref="AreaGate"/> (g0 and the world exit door) with two
        /// <see cref="CityKit.Pale"/> posts, a neon bar over the opening — pink at g0, white at the
        /// exit, per the ticket — and a floor pool. Reads the gate's own built transform (position,
        /// width off its local scale, orientation off <see cref="AreaGate.AwayFromPlayerDirection"/>/
        /// <c>transform.right</c>) rather than re-deriving it from <paramref name="host"/>'s world
        /// config, same reasoning <see cref="StormdrainDressing"/>'s hazard-bulkhead pass gives for
        /// doing the same off a built gate.</summary>
        private static void DressDoorFrames(Transform root, Transform host)
        {
            AreaGate[] gates = host.GetComponentsInChildren<AreaGate>(true);
            if (gates.Length == 0) return;

            var frameHost = new GameObject("Door Frames").transform;
            frameHost.SetParent(root, false);

            foreach (AreaGate gate in gates)
            {
                Transform t = gate.transform;
                bool isG0 = gate.gameObject.name == "g0";
                Color neon = isG0 ? CityKit.NeonPink : CityKit.NeonWhite;

                Vector3 along = t.right;
                bool alongWorldX = Mathf.Abs(along.x) >= Mathf.Abs(along.z);
                float halfWidth = Mathf.Max(0.5f, Mathf.Abs(t.localScale.x) * 0.5f);
                float wallHeight = Mathf.Max(0.5f, t.localScale.y);

                Vector3 centre = t.position;
                centre.y = 0f;

                Vector3 postSize = alongWorldX
                    ? new Vector3(0.5f, wallHeight, 0.4f)
                    : new Vector3(0.4f, wallHeight, 0.5f);

                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 postPos = centre + along.normalized * (side * (halfWidth - postSize.x * 0.3f))
                                      + Vector3.up * (wallHeight * 0.5f);
                    StormdrainKit.Box(frameHost, $"{gate.gameObject.name} Post{(side < 0 ? "A" : "B")}",
                        postPos, postSize, CityKit.Pale, SurfaceKind.Metal);
                }

                Vector3 forward = gate.AwayFromPlayerDirection.sqrMagnitude > 0.001f
                    ? gate.AwayFromPlayerDirection.normalized
                    : t.forward;
                Quaternion barRot = Quaternion.LookRotation(forward, Vector3.up);
                Vector3 barPos = centre + Vector3.up * (wallHeight * 0.86f);
                StormdrainKit.Glow(frameHost, $"{gate.gameObject.name} Neon Bar", barPos,
                    new Vector3(halfWidth * 1.8f, wallHeight * 0.1f, 1f), barRot, neon);

                CityKit.BuildPool(frameHost, centre, halfWidth * 1.6f, halfWidth * 1.6f, neon, 0.16f);
            }
        }

        // ---------------------------------------------------------------- (c) cover

        /// <summary>Dressings <see cref="CityKit"/> already builds a reusable piece for (the ticket's
        /// own list) — the rest dispatch to this class's own per-piece builders below.</summary>
        private static void DressCover(Transform root, IReadOnlyList<CoverPiece> cover)
        {
            if (cover == null || cover.Count == 0) return;

            var coverHost = new GameObject("Cover").transform;
            coverHost.SetParent(root, false);

            foreach (CoverPiece piece in cover)
            {
                if (piece.Body == null) continue;

                var pieceRoot = new GameObject(piece.Cover.Name).transform;
                pieceRoot.SetParent(coverHost, false);
                pieceRoot.gameObject.AddComponent<KeepsOwnMaterial>();

                GameObject art = BuildCoverArt(pieceRoot, piece.Cover);
                if (art == null) continue; // unhandled dressing: the box stays visible, per the ticket

                var rend = piece.Body.GetComponent<Renderer>();
                if (rend != null) rend.enabled = false;
            }
        }

        private static GameObject BuildCoverArt(Transform parent, ArenaCover cover)
        {
            Vector3 at = new Vector3(cover.CenterXz.x, 0f, cover.CenterXz.y);
            Vector3 size = cover.Size;

            switch (cover.Dressing)
            {
                // ---- CityKit's own reusable pieces ----
                case CoverDressing.Truck: return CityKit.BuildTruck(parent, at, size);
                case CoverDressing.Bus: return CityKit.BuildBus(parent, at, size);
                case CoverDressing.Car: return CityKit.BuildCar(parent, at, size);
                case CoverDressing.Barrier: return CityKit.BuildConcreteBarrier(parent, at, size);
                case CoverDressing.Fence: return CityKit.BuildWireFence(parent, at, size);
                case CoverDressing.Bollard: return CityKit.BuildBollard(parent, at, size);
                case CoverDressing.Bench: return CityKit.BuildBench(parent, at, size);
                case CoverDressing.Bin: return CityKit.BuildBin(parent, at, size);
                case CoverDressing.ABoard: return CityKit.BuildABoard(parent, at, size);
                case CoverDressing.PostBox: return CityKit.BuildPostBox(parent, at, size);
                case CoverDressing.Tub: return CityKit.BuildTub(parent, at, size);
                case CoverDressing.PlanterBox: return CityKit.BuildPlanterBox(parent, at, size);
                case CoverDressing.LampPost:
                {
                    Vector3 armDir = new Vector3(0f, 0f, cover.CenterXz.y > 12f ? -1f : 1f);
                    return CityKit.BuildStreetLamp(parent, at, armDir);
                }

                // ---- Market Street's own pieces ----
                case CoverDressing.RoadSign: return BuildRoadSign(parent, at, size);
                case CoverDressing.Pallet: return BuildPallet(parent, at, size);
                case CoverDressing.FlowerStall: return BuildFlowerStall(parent, at, size);
                case CoverDressing.FruitStall: return BuildFruitStall(parent, at, size);
                case CoverDressing.ClockPost: return BuildClockPost(parent, at, size);
                case CoverDressing.BookCart: return BuildBookCart(parent, at, size);
                case CoverDressing.CafeTable: return BuildCafeTable(parent, at, size);
                case CoverDressing.Lockers: return BuildLockers(parent, at, size);
                case CoverDressing.BikeRack: return BuildBikeRack(parent, at, size);

                default: return null;
            }
        }

        private static Transform NewRoot(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<KeepsOwnMaterial>();
            return go.transform;
        }

        /// <summary>White face on two legs, a pink unlit arrow of three bars, a pink floor pool. The
        /// two legs alone (0.46 x size.x each, offset to the collider's own edges) already satisfy the
        /// ticket's AC1 fit window; everything else layers inside that footprint.</summary>
        private static GameObject BuildRoadSign(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Road Sign");

            Vector3 legSize = new Vector3(size.x * 0.46f, size.y * 0.94f, size.z * 0.9f);
            foreach (float sx in new[] { -1f, 1f })
                StormdrainKit.Box(root, $"Leg{(sx < 0 ? "L" : "R")}",
                    centre + new Vector3(sx * size.x * 0.25f, legSize.y * 0.5f, 0f), legSize, CityKit.Pale, SurfaceKind.Metal);

            float faceH = size.y * 0.5f;
            Vector3 facePos = centre + Vector3.up * (size.y * 0.94f - faceH * 0.5f);
            StormdrainKit.Box(root, "Face", facePos, new Vector3(size.x * 0.9f, faceH, size.z * 0.8f), CityKit.Pale);

            for (int i = 0; i < 3; i++)
            {
                float t = (i - 1f) * 0.22f;
                StormdrainKit.Glow(root, $"Arrow{i}", facePos + new Vector3(t * size.x, 0f, size.z * 0.41f),
                    new Vector3(size.x * 0.16f, faceH * 0.3f, 1f), Quaternion.identity, CityKit.NeonPink);
            }

            CityKit.BuildPool(root, centre, Mathf.Max(1.2f, size.x), Mathf.Max(1.2f, size.x), CityKit.NeonPink, 0.14f);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A wooden base with four mustard/olive crates, each at one corner of the base.</summary>
        private static GameObject BuildPallet(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Pallet");

            Vector3 baseSize = new Vector3(size.x * 0.96f, size.y * 0.12f, size.z * 0.96f);
            StormdrainKit.Box(root, "Base", centre + Vector3.up * (baseSize.y * 0.5f), baseSize, CityKit.Wood, SurfaceKind.Wood);

            Color[] crateColors = { CityKit.Mustard, CityKit.Olive, CityKit.Olive, CityKit.Mustard };
            float crateH = size.y * 0.94f - baseSize.y;
            Vector3 crateSize = new Vector3(size.x * 0.46f, crateH, size.z * 0.46f);

            int idx = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 cPos = centre + new Vector3(sx * size.x * 0.24f, baseSize.y + crateH * 0.5f, sz * size.z * 0.24f);
                    StormdrainKit.Box(root, $"Crate{idx}", cPos, crateSize, crateColors[idx % crateColors.Length], SurfaceKind.Wood);
                    idx++;
                }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Three slate steps carrying rows of cream/plum/mustard/pink tufts, a pink neon bar
        /// on the back board.</summary>
        private static GameObject BuildFlowerStall(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Flower Stall");
            Color[] tuftColors = { CityKit.Cream, CityKit.Plum, CityKit.Mustard, TuftPink };

            const int steps = 3;
            for (int s = 0; s < steps; s++)
            {
                float t = steps <= 1 ? 0f : s / (float)(steps - 1); // 0 = front/low, 1 = back/high
                float stepY = Mathf.Lerp(size.y * 0.05f, size.y * 0.65f, t); // front step's own foot touches the floor
                float stepZ = Mathf.Lerp(size.z * 0.35f, -size.z * 0.35f, t);
                var stepSize = new Vector3(size.x * 0.94f, size.y * 0.1f, size.z * 0.26f);
                StormdrainKit.Box(root, $"Step{s}", centre + new Vector3(0f, stepY, stepZ), stepSize, CityKit.Slate, SurfaceKind.Stone);

                const int tufts = 6;
                for (int i = 0; i < tufts; i++)
                {
                    float tx = i / (float)(tufts - 1) - 0.5f;
                    var tuftSize = new Vector3(size.x * 0.12f, size.y * 0.16f, size.z * 0.14f);
                    Vector3 tuftPos = centre + new Vector3(tx * size.x * 0.88f, stepY + stepSize.y * 0.5f + tuftSize.y * 0.5f, stepZ);
                    StormdrainKit.Box(root, $"Tuft{s}_{i}", tuftPos, tuftSize, tuftColors[(s * tufts + i) % tuftColors.Length], SurfaceKind.Foliage);
                }
            }

            Vector3 boardPos = centre + new Vector3(0f, size.y * 0.78f, -size.z * 0.46f);
            StormdrainKit.Box(root, "Back Board", boardPos, new Vector3(size.x * 0.9f, size.y * 0.46f, size.z * 0.06f), CityKit.Slate);
            StormdrainKit.Glow(root, "Neon Bar", boardPos + Vector3.up * (size.y * 0.1f),
                new Vector3(size.x * 0.76f, size.y * 0.1f, 1f), Quaternion.identity, CityKit.NeonPink);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A trestle table carrying tilted crates of mustard/olive/plum fruit, a white lit
        /// price board.</summary>
        private static GameObject BuildFruitStall(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Fruit Stall");

            Vector3 legSize = new Vector3(size.x * 0.05f, size.y * 0.5f, size.z * 0.9f);
            foreach (float sx in new[] { -1f, 1f })
                StormdrainKit.Box(root, $"Leg{(sx < 0 ? "L" : "R")}",
                    centre + new Vector3(sx * size.x * 0.44f, legSize.y * 0.5f, 0f), legSize, CityKit.WoodDark, SurfaceKind.Wood);

            Vector3 topPos = centre + Vector3.up * (size.y * 0.5f);
            StormdrainKit.Box(root, "Tabletop", topPos, new Vector3(size.x * 0.96f, size.y * 0.1f, size.z * 0.94f), CityKit.Wood, SurfaceKind.Wood);

            Color[] fruitColors = { CityKit.Mustard, CityKit.Olive, CityKit.Plum };
            const int crates = 3;
            for (int i = 0; i < crates; i++)
            {
                float t = i / (float)(crates - 1) - 0.5f;
                Vector3 crateCentre = topPos + new Vector3(t * size.x * 0.64f, size.y * 0.16f, 0f);
                StormdrainKit.Box(root, $"Crate{i}", crateCentre, new Vector3(size.x * 0.26f, size.y * 0.24f, size.z * 0.7f), CityKit.WoodDark, SurfaceKind.Wood);

                for (int k = 0; k < 3; k++)
                {
                    float tz = k / 2f - 0.5f;
                    Vector3 fruitPos = crateCentre + new Vector3(0f, size.y * 0.16f, tz * size.z * 0.5f);
                    StormdrainKit.Box(root, $"Fruit{i}_{k}", fruitPos, new Vector3(size.x * 0.14f, size.y * 0.14f, size.z * 0.16f),
                        fruitColors[(i + k) % fruitColors.Length], SurfaceKind.Prop);
                }
            }

            Vector3 boardPos = topPos + new Vector3(size.x * 0.38f, size.y * 0.38f, 0f);
            StormdrainKit.Glow(root, "Price Board", boardPos, new Vector3(size.z * 0.6f, size.y * 0.42f, 1f),
                Quaternion.LookRotation(Vector3.right, Vector3.up), CityKit.NeonWhite);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A 4 m dark post (the kit's same lamp-post exception to the 1.6 m cover-fit rule — a
        /// post's own collider is a thin cylinder no art could ever span 90% of without reading as a
        /// solid drum) topped with a round, camera-tilted white lit clock face and two dark hands, plus
        /// a warm floor pool.</summary>
        private static GameObject BuildClockPost(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Clock Post");
            const float height = 4f;

            StormdrainKit.AddMeshPart(root, "Post", CharacterMeshes.Beam(height, 0.08f, 0.06f),
                centre + Vector3.up * (height * 0.5f), SurfaceKind.Metal, CityKit.Pole);

            Vector3 facePos = centre + Vector3.up * (height * 0.88f);
            Quaternion faceRot = Quaternion.Euler(20f, 0f, 0f);
            var face = new GameObject("Clock Face");
            face.transform.SetParent(root, false);
            face.transform.localPosition = facePos;
            face.transform.localRotation = faceRot;
            face.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Lathe(
                new[] { new Vector2(0f, 0f), new Vector2(0.55f, 0f) }, 24);
            var faceRend = face.AddComponent<MeshRenderer>();
            faceRend.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Prop, CityKit.Pale);

            foreach (float len in new[] { 0.3f, 0.42f })
            {
                var hand = new GameObject(len < 0.4f ? "HourHand" : "MinuteHand");
                hand.transform.SetParent(root, false);
                hand.transform.localPosition = facePos + faceRot * (Vector3.up * 0.01f);
                hand.transform.localRotation = faceRot * Quaternion.Euler(0f, 0f, len < 0.4f ? -40f : 70f);
                hand.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Beam(len, 0.015f, 0.01f);
                hand.transform.localRotation *= Quaternion.Euler(90f, 0f, 0f);
                var handRend = hand.AddComponent<MeshRenderer>();
                handRend.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, CityKit.Pole);
            }

            CityKit.BuildPool(root, centre, 1.8f, 1.8f, CityKit.PoolWarm, 0.18f);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A plum cart carrying coloured book blocks, with one warm lamp on a stalk and its
        /// own floor pool.</summary>
        private static GameObject BuildBookCart(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Book Cart");

            Vector3 bodySize = new Vector3(size.x * 0.96f, size.y * 0.6f, size.z * 0.94f);
            StormdrainKit.Box(root, "Cart", centre + Vector3.up * (bodySize.y * 0.5f), bodySize, CityKit.Plum, SurfaceKind.Wood);

            Color[] bookColors = { CityKit.Mustard, CityKit.Navy, CityKit.Cream, CityKit.Olive, CityKit.Plum };
            const int books = 5;
            for (int i = 0; i < books; i++)
            {
                float t = i / (float)(books - 1) - 0.5f;
                Vector3 bookSize = new Vector3(size.x * 0.14f, size.y * 0.3f, size.z * 0.7f);
                Vector3 bookPos = centre + new Vector3(t * size.x * 0.8f, bodySize.y + bookSize.y * 0.5f, 0f);
                StormdrainKit.Box(root, $"Book{i}", bookPos, bookSize, bookColors[i % bookColors.Length], SurfaceKind.Prop);
            }

            Vector3 lampFoot = centre + new Vector3(size.x * 0.46f, 0f, -size.z * 0.4f);
            StormdrainKit.AddMeshPart(root, "Lamp Stalk", CharacterMeshes.Beam(size.y * 1.2f, 0.035f, 0.02f),
                lampFoot + Vector3.up * (size.y * 0.6f), SurfaceKind.Metal, CityKit.Pole);
            Vector3 headPos = lampFoot + Vector3.up * (size.y * 1.2f);
            StormdrainKit.Box(root, "Lamp Head", headPos, new Vector3(0.22f, 0.12f, 0.22f), CityKit.LampHead);

            CityKit.BuildPool(root, lampFoot, 2.2f, 2.2f, CityKit.PoolWarm, 0.16f);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A cream tabletop on a dark stem with two stools on the X axis, a warm table lamp
        /// and its pool.</summary>
        private static GameObject BuildCafeTable(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Cafe Table");

            Vector3 topSize = new Vector3(size.x * 0.96f, size.y * 0.1f, size.z * 0.9f);
            Vector3 topPos = centre + Vector3.up * (size.y * 0.78f);
            StormdrainKit.Box(root, "Top", topPos, topSize, CityKit.Cream, SurfaceKind.Wood);
            StormdrainKit.Box(root, "Stem", centre + Vector3.up * (size.y * 0.39f), new Vector3(size.x * 0.1f, size.y * 0.72f, size.z * 0.1f),
                CityKit.Pole, SurfaceKind.Metal);

            foreach (float sx in new[] { -1f, 1f })
                StormdrainKit.Box(root, $"Stool{(sx < 0 ? "L" : "R")}",
                    centre + new Vector3(sx * size.x * 0.4f, size.y * 0.32f, 0f),
                    new Vector3(size.x * 0.14f, size.y * 0.6f, size.z * 0.6f), CityKit.Slate, SurfaceKind.Wood);

            Vector3 lampFoot = topPos + Vector3.up * (topSize.y * 0.5f);
            StormdrainKit.AddMeshPart(root, "Lamp", CharacterMeshes.Beam(size.y * 0.3f, 0.03f, 0.02f),
                lampFoot + Vector3.up * (size.y * 0.15f), SurfaceKind.Metal, CityKit.Pole);
            StormdrainKit.Box(root, "Lamp Head", lampFoot + Vector3.up * (size.y * 0.3f), new Vector3(0.15f, 0.1f, 0.15f), CityKit.LampHead);

            CityKit.BuildPool(root, centre, 1.6f, 1.6f, CityKit.PoolWarm, 0.14f);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A slate bank with a door grid, a white lit screen, a pink neon bar, pink and cool
        /// pools.</summary>
        private static GameObject BuildLockers(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Lockers");

            Vector3 bodySize = new Vector3(size.x * 0.96f, size.y * 0.94f, size.z * 0.9f);
            StormdrainKit.Box(root, "Bank", centre + Vector3.up * (bodySize.y * 0.5f), bodySize, CityKit.Slate, SurfaceKind.Metal);

            const int cols = 6;
            for (int i = 0; i < cols; i++)
            {
                float t = i / (float)(cols - 1) - 0.5f;
                StormdrainKit.Box(root, $"Door{i}", centre + new Vector3(t * size.x * 0.9f, bodySize.y * 0.5f, size.z * 0.44f),
                    new Vector3(size.x * 0.12f, bodySize.y * 0.86f, 0.015f), CityKit.SlateLight, SurfaceKind.Metal);
            }

            StormdrainKit.Glow(root, "Screen", centre + new Vector3(0f, bodySize.y * 0.82f, size.z * 0.46f),
                new Vector3(size.x * 0.5f, size.y * 0.24f, 1f), Quaternion.identity, CityKit.NeonWhite);
            StormdrainKit.Glow(root, "Neon Bar", centre + new Vector3(0f, bodySize.y * 0.14f, size.z * 0.46f),
                new Vector3(size.x * 0.9f, size.y * 0.08f, 1f), Quaternion.identity, CityKit.NeonPink);

            CityKit.BuildPool(root, centre + new Vector3(-size.x * 0.3f, 0f, size.z * 0.6f), 1.4f, 1.4f, CityKit.NeonPink, 0.12f);
            CityKit.BuildPool(root, centre + new Vector3(size.x * 0.3f, 0f, size.z * 0.6f), 1.4f, 1.4f, CityKit.PoolCool, 0.12f);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Five steel hoops on a rail, each a two-legged loop spanning the collider's own
        /// depth, holding four simple bikes (two thin rings and a bar each). Reads see-through — no
        /// panel ever fills the gaps between hoops.</summary>
        private static GameObject BuildBikeRack(Transform parent, Vector3 centre, Vector3 size)
        {
            Transform root = NewRoot(parent, "Bike Rack");

            const int hoops = 5;
            for (int i = 0; i < hoops; i++)
            {
                float t = i / (float)(hoops - 1) - 0.5f;
                float hx = t * size.x * 0.96f;

                foreach (float sz in new[] { -1f, 1f })
                    StormdrainKit.AddMeshPart(root, $"Hoop{i}_{(sz < 0 ? "A" : "B")}", CharacterMeshes.Beam(size.y * 0.94f, 0.02f, 0.02f),
                        centre + new Vector3(hx, size.y * 0.47f, sz * size.z * 0.44f), SurfaceKind.Metal, CityKit.Pole);

                StormdrainKit.Box(root, $"HoopTop{i}", centre + new Vector3(hx, size.y * 0.94f, 0f),
                    new Vector3(0.03f, 0.03f, size.z * 0.86f), CityKit.Pole, SurfaceKind.Metal);
            }

            const int bikes = 4;
            for (int i = 0; i < bikes; i++)
            {
                float t = (i + 0.5f) / bikes - 0.5f;
                Vector3 wheelCentre = centre + new Vector3(t * size.x * 0.8f, size.y * 0.3f, 0f);
                float wheelR = size.y * 0.26f;

                foreach (float sx in new[] { -1f, 1f })
                {
                    var wheel = new GameObject($"Wheel{i}_{(sx < 0 ? "L" : "R")}");
                    wheel.transform.SetParent(root, false);
                    wheel.transform.localPosition = wheelCentre + new Vector3(sx * size.x * 0.1f, 0f, 0f);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    wheel.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Beam(0.015f, wheelR, wheelR, 12);
                    var rend = wheel.AddComponent<MeshRenderer>();
                    rend.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, CityKit.Pole);
                }

                StormdrainKit.Box(root, $"Frame{i}", wheelCentre, new Vector3(size.x * 0.18f, 0.03f, 0.03f), CityKit.Navy, SurfaceKind.Metal);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }
    }
}
