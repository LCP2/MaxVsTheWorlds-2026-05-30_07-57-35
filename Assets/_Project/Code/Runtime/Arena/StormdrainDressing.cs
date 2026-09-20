using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 2's dressing pass — the Stormdrain counterpart of <see cref="BackyardDressing"/> and
    /// <see cref="ReefDressing"/>, and the thing World 2 has never had.
    ///
    /// Called from <see cref="BackyardPath"/>'s existing per-world sweep hook, exactly where
    /// <c>ApplyReefKit</c> is called for World 3, so it runs after <see cref="WorldMaterials.Apply"/>
    /// has already painted the biome and overrides rather than races it.
    ///
    /// Two passes, and the split matters. The WALL pass walks
    /// <see cref="MapGeometry.Faces"/> — the seam that struct was built for ("hand the art layer these
    /// and it can dress ANY map") — and hangs a kerb, a pipe bank and lamps on every face that looks
    /// into a room. The COVER pass swaps each grey block's renderer for a piece of drain machinery,
    /// keeping the block's own collider, the same contract every <c>DressCover</c> case keeps.
    ///
    /// Nothing here is authored per area. A drain that needed twenty-three hand-placed prop lists
    /// would never get built, and the moment the level moved it would be wrong.
    /// </summary>
    public static class StormdrainDressing
    {
        /// <summary>Below this a room is a connector stub, and dressing every face of it just fills
        /// the doorway with pipework the player has to walk through.</summary>
        private const float MinFaceLength = 1.2f;

        /// <summary>What one pass placed, so an EditMode test can assert on density and variety rather
        /// than on presence (<c>feedback_count_based_acs_pass_a_stub</c>: an AC that counts lets a
        /// skeleton pass, so the test asserts pieces-per-metre and how many DISTINCT kinds appeared).</summary>
        public readonly struct DressReport
        {
            public readonly int Kerbs, Pipes, Lamps, Soffits, CoverProps, SludgeTiles;
            public readonly int DistinctCoverKinds;

            public DressReport(int kerbs, int pipes, int lamps, int soffits,
                               int coverProps, int sludgeTiles, int distinctCoverKinds)
            {
                Kerbs = kerbs; Pipes = pipes; Lamps = lamps; Soffits = soffits;
                CoverProps = coverProps; SludgeTiles = sludgeTiles;
                DistinctCoverKinds = distinctCoverKinds;
            }

            public int Total => Kerbs + Pipes + Lamps + Soffits + CoverProps + SludgeTiles;
        }

        // ---------------------------------------------------------------- lit ground (MV-799)

        /// <summary>One light-fitting source for <see cref="LightingMultiplier"/> (MV-799, change 1-2):
        /// its own world XZ position and the radius its own kind reaches to. Collected as the wall pass
        /// (<see cref="StormdrainKit.DressWallFace"/>, which also builds the kerb strip) and
        /// <see cref="DressHazardBulkheads"/> actually build fittings — not re-derived, so a fitting the
        /// post-pass sees is always one that actually exists in the built scene.</summary>
        private readonly struct Fitting
        {
            public readonly Vector2 Pos;
            public readonly float Radius;
            public Fitting(Vector2 pos, float radius) { Pos = pos; Radius = radius; }
        }

        private const float BulkheadLampFittingRadius = 4.6f;
        private const float KerbStripFittingRadius = 3.0f;

        private const float LitGroundBaseMultiplier = 0.72f;
        private const float LitGroundMaxMultiplier = 1.30f;
        private const float LitGroundFalloffExponent = 1.35f;
        private const float LitGroundFittingContribution = 0.62f;

        /// <summary>MV-799: how much a fitting brightens one point on the floor — the approved design's
        /// own formula, summed over every fitting that reaches <paramref name="p"/> and capped. Consumed
        /// for a bay's own resolved tone and, so a stain or crack never floats brighter than the bay
        /// under it, for that bay's silt stains, water stains and cracks too.</summary>
        private static float LightingMultiplier(Vector2 p, List<Fitting> fittings)
        {
            float sum = 0f;
            foreach (Fitting f in fittings)
            {
                float d = Vector2.Distance(p, f.Pos);
                if (d >= f.Radius) continue;
                sum += Mathf.Pow(1f - d / f.Radius, LitGroundFalloffExponent) * LitGroundFittingContribution;
            }
            return Mathf.Min(LitGroundMaxMultiplier, LitGroundBaseMultiplier + sum);
        }

        /// <summary>How many pump housings the last <see cref="Dress"/> call actually built (MV-794) —
        /// what <see cref="StormdrainFloodRunner"/> reads instead of a hard-coded 0, so
        /// <see cref="StormdrainFlood"/>'s pump counterweight runs off the real world instead of a
        /// literal. Pump housings are cosmetic dressing with no destruction lifecycle yet (a follow-up,
        /// the same status quo Replicators were in before <see cref="MaxWorlds.Factories.FactoryCensus"/>
        /// gave them one) — so "alive" here means "built for this level," which today is every housing
        /// that exists. Reset by <see cref="Reset"/> (called from <c>MapRuntime.Build</c>, same point
        /// that resets <c>FactoryCensus</c>) so a world with no drain dressing at all — World 1, World 3
        /// — reads 0, not the last World 2 level's count.</summary>
        public static int PumpHousingsAlive { get; private set; }

        /// <summary>Back to no pump housings. Called when a level starts building, same reasoning as
        /// <c>FactoryCensus.Reset</c> — a fresh run must not inherit the last one's count. A World 2
        /// level then overwrites this for real the moment its own <see cref="Dress"/> call runs.</summary>
        public static void Reset() => PumpHousingsAlive = 0;

        /// <summary>Dresses the whole drain. Idempotent per load — it builds under one named host, and
        /// a second call replaces that host rather than doubling every pipe in the world (and, as of
        /// MV-794, replaces <see cref="PumpHousingsAlive"/> rather than adding to it, for the same
        /// reason).</summary>
        public static DressReport Dress(Transform host, MapData map, IReadOnlyList<CoverPiece> cover)
        {
            if (host == null || map == null) return default;

            Transform existing = host.Find("Stormdrain Dressing");
            if (existing != null)
            {
                if (Application.isPlaying) Object.Destroy(existing.gameObject);
                else Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject("Stormdrain Dressing").transform;
            root.SetParent(host, false);
            root.gameObject.AddComponent<KeepsOwnMaterial>();

            int kerbs = 0, pipes = 0, lamps = 0, soffits = 0;
            var fittings = new List<Fitting>();

            var walls = new GameObject("Walls").transform;
            walls.SetParent(root, false);

            // MV-802: overhead structure gets its own host, not "Walls" — it sits inset 0.95 m off the
            // wall face by design (the ticket's own "the single constraint that makes the feature
            // work"), which would fail MV765's "wall-hung pieces stay within half a metre of their
            // face" guard if it were parented there instead.
            var overhead = new GameObject("Overhead").transform;
            overhead.SetParent(root, false);
            var overheadFaces = new List<WallFace>();

            // MV-819: resolved BEFORE the face loop below (not after, as MV-802 had it) so each face can
            // be checked against its own zone's cross-main placement before building a competing main on
            // the same wall — see FaceIsCrossMainWall's own doc for why the two can't coexist.
            List<CrossMainTarget> crossMainTargets = ResolveCrossMainTargets(map);

            int seed = 0;
            foreach (WallFace face in MapGeometry.Faces(map))
            {
                seed++;
                if (!face.FacesRoom) continue;
                if (face.Length < MinFaceLength) continue;

                int before = walls.childCount;
                StormdrainKit.DressWallFace(walls, face.A, face.B, face.Out, map.wallHeight, seed);
                StormdrainKit.BuildSoffit(walls, face.A, face.B, face.Out, map.wallHeight);

                // Counted off what was actually created, not off what the call was asked to create —
                // DressWallFace declines short faces and skips lamps that would land in a doorway.
                for (int i = before; i < walls.childCount; i++)
                {
                    Transform child = walls.GetChild(i);
                    string n = child.name;
                    if (n.StartsWith("Kerb")) kerbs++;
                    else if (n.StartsWith("Pipe") || n.StartsWith("Collar")) pipes++;
                    else if (n.StartsWith("Bulkhead Lamp"))
                    {
                        lamps++;
                        fittings.Add(new Fitting(new Vector2(child.position.x, child.position.z),
                            BulkheadLampFittingRadius));
                    }
                    else if (n.StartsWith("Soffit")) soffits++;
                    else if (n == "Guide Rail")
                    {
                        // MV-799: each kerb-strip segment is its own fitting (change 1) — the guide
                        // rail's host object itself carries no position worth collecting.
                        foreach (Transform seg in child)
                            fittings.Add(new Fitting(new Vector2(seg.position.x, seg.position.z),
                                KerbStripFittingRadius));
                    }
                }

                // MV-819: a face that IS the wall a cross-main insets from never gets its own separate
                // hugging main — see FaceIsCrossMainWall's own doc. Still added to overheadFaces below
                // regardless, so the corner it sits on keeps its junction box.
                if (!FaceIsCrossMainWall(face, crossMainTargets, map.wallThickness))
                    StormdrainKit.DressOverheadRun(overhead, face.A, face.B, face.Out, map.wallHeight, seed);
                if (map.wallHeight >= 2.2f) overheadFaces.Add(face);
            }

            DressOverheadJunctions(overhead, overheadFaces, map.wallThickness, map);
            DressOverheadCrossMains(overhead, crossMainTargets);

            DressWallPanels(root, host, map);

            int coverProps = 0;
            int pumpHousings = 0;
            var kinds = new HashSet<CoverDressing>();
            var props = new GameObject("Cover").transform;
            props.SetParent(root, false);

            if (cover != null)
            {
                foreach (CoverPiece piece in cover)
                {
                    if (piece.Body == null) continue;
                    if (!BuildFor(props, piece)) continue;

                    // The block's own box stays the collider — only its art is replaced. Same
                    // contract ReefDressing keeps, and the reason a re-dressed room still plays
                    // identically to the greybox it was tuned on.
                    var rend = piece.Body.GetComponent<Renderer>();
                    if (rend != null) rend.enabled = false;

                    kinds.Add(piece.Cover.Dressing);
                    coverProps++;

                    // MV-794: Shed/Machinery is BuildFor's own "pump housing" case (see its switch) —
                    // counted here rather than inferred later, so PumpHousingsAlive is never a second
                    // formula that could drift from the one that actually built the housing.
                    if (piece.Cover.Dressing == CoverDressing.Shed || piece.Cover.Dressing == CoverDressing.Machinery)
                        pumpHousings++;
                }
            }

            PumpHousingsAlive = pumpHousings;

            var channelRects = new List<Rect>();
            int tiles = DressSludge(root, host, map, channelRects);

            // MV-799: hazard bulkheads must build (and add to fittings) BEFORE the floor composition
            // post-pass runs, or a gate/outfall's own light would never lighten the bays around it.
            DressHazardBulkheads(root, host, map, fittings);
            DressFloorComposition(root, map, fittings, channelRects);
            LowerMapFloor(host);

            return new DressReport(kerbs, pipes, lamps, soffits, coverProps, tiles, kinds.Count);
        }

        // ---------------------------------------------------------------- overhead structure (MV-802)

        /// <summary>MV-819: two wall faces that meet at what reads as one room corner are NOT built from
        /// the same point — <see cref="MapGeometry.Cap"/> extends each wall run's own ends outward by a
        /// full <c>wallThickness</c>, independently per line, so an alongX face's endpoint sits pushed
        /// out along X while the perpendicular alongZ face's endpoint sits pushed out along Z. The two
        /// points end up up to <c>wallThickness * sqrt(2)</c> apart, not touching — which is why no
        /// junction box ever built before this ticket. <see cref="DressOverheadJunctions"/> is called
        /// with <c>2 * wallThickness</c>, comfortably above that gap on this game's own wall scale,
        /// without being loose enough to merge two actually-different corners.</summary>
        private static float JunctionEpsilon(float wallThickness) => 2f * wallThickness;

        /// <summary>One junction box per corner where two of this ticket's own overhead runs meet
        /// (change 4) — found by pairing up the wall faces that actually got a run (already filtered to
        /// <c>wallHeight &gt;= 2.2 m</c> by the caller) and looking for a shared endpoint, deduplicated so
        /// three-plus faces meeting at one point (an L or a T) still get exactly one box.
        ///
        /// MV-852: at a T-junction (one area's wall ending exactly where a second, unrelated area's wall
        /// begins — World 2's a10/a11-vs-a18 step, where a18 sits flush with a10's own north wall but a11
        /// doesn't reach that far west) <see cref="JunctionEpsilon"/>'s tolerance can bridge a face to the
        /// WRONG neighbour: the nearby wall's far-side surface (a DIFFERENT room's own wall, not the true
        /// corner partner), landing the box's own LED-panel light pool nowhere near either room's walls —
        /// exactly the "crossing the playable middle" MV-802 guards against, just from a junction rather
        /// than a hugging main. <see cref="FacesShareZone"/> rejects a pair whose two faces don't actually
        /// bound the SAME room WITHOUT claiming <paramref name="faces"/>' corner key, so the genuine
        /// partner — tried later in the same double loop, since every (i, j) pair is visited exactly
        /// once — still gets to build the box.</summary>
        private static void DressOverheadJunctions(Transform overhead, List<WallFace> faces, float wallThickness, MapData map)
        {
            float epsilon = JunctionEpsilon(wallThickness);
            var seen = new HashSet<Vector2Int>();

            for (int i = 0; i < faces.Count; i++)
            {
                for (int j = i + 1; j < faces.Count; j++)
                {
                    if (!SharedEndpoint(faces[i], faces[j], epsilon, out Vector2 corner, out Vector2 outSum)) continue;
                    if (outSum.sqrMagnitude < 0.0001f) continue;
                    // MV-852: a real corner turns — its two faces' Out vectors are roughly perpendicular.
                    // Two collinear segments of the SAME wall line, independently capped where a third
                    // area's boundary splits them (World 2's a10/a11 wall, fragmented exactly where a18
                    // begins), share an Out direction and can still land within epsilon of each other;
                    // without this they read as a false "corner" and build a junction mid-wall.
                    if (Mathf.Abs(Vector2.Dot(faces[i].Out, faces[j].Out)) > 0.1f) continue;
                    if (!FacesShareZone(faces[i], faces[j], corner, map)) continue;

                    var key = new Vector2Int(Mathf.RoundToInt(corner.x * 2f), Mathf.RoundToInt(corner.y * 2f));
                    if (!seen.Add(key)) continue;

                    Vector2 n = outSum.normalized;
                    Vector3 at = new Vector3(corner.x, 0f, corner.y)
                                 + new Vector3(n.x, 0f, n.y) * StormdrainKit.OverheadMainInset
                                 + Vector3.up * StormdrainKit.OverheadMainY;
                    StormdrainKit.BuildOverheadJunctionBox(overhead, at);
                }
            }
        }

        /// <summary>True if <paramref name="f1"/> and <paramref name="f2"/> both actually bound the SAME
        /// room — the real distinguishing fact a shared endpoint alone can't tell apart at a T-junction
        /// (see <see cref="DressOverheadJunctions"/>'s own MV-852 note): two DIFFERENT rooms' walls can
        /// end up with endpoints within <see cref="JunctionEpsilon"/> of each other purely because a
        /// third room's corner happens to sit nearby, with no shared corner between the two at all.
        /// Resolved by sampling a point just inside each face's OWN room (a short step in from
        /// <paramref name="corner"/> along the face's own run, then <see cref="WallFace.Out"/>'s own
        /// direction) and checking they land in the same <see cref="MapZone"/> — ambiguous right at
        /// <paramref name="corner"/> itself, unambiguous a metre in from it.</summary>
        private static bool FacesShareZone(WallFace f1, WallFace f2, Vector2 corner, MapData map)
        {
            MapZone z1 = ZoneContainingPoint(map, SampleInsideOwnRoom(f1, corner));
            MapZone z2 = ZoneContainingPoint(map, SampleInsideOwnRoom(f2, corner));
            return z1 != null && ReferenceEquals(z1, z2);
        }

        private static Vector2 SampleInsideOwnRoom(WallFace f, Vector2 corner)
        {
            Vector2 near = Vector2.Distance(f.A, corner) <= Vector2.Distance(f.B, corner) ? f.A : f.B;
            Vector2 far = near == f.A ? f.B : f.A;
            float inset = Mathf.Min(1f, f.Length * 0.4f);
            Vector2 alongFace = (far - near).normalized;
            return near + alongFace * inset + f.Out * 0.5f;
        }

        /// <summary>The first floor-level (<c>level == 0</c>) zone whose footprint contains
        /// <paramref name="p"/> — restricted to level 0 so a deck overlay sharing a floor zone's own
        /// footprint (MV-697, e.g. a3/a19) never reads as a "different room" from that floor.</summary>
        private static MapZone ZoneContainingPoint(MapData map, Vector2 p)
        {
            if (map?.zones == null) return null;
            foreach (MapZone zone in map.zones)
                if (zone != null && zone.level == 0 && zone.Contains(p.x, p.y)) return zone;
            return null;
        }

        private static bool SharedEndpoint(WallFace f1, WallFace f2, float epsilon, out Vector2 corner, out Vector2 outSum)
        {
            if (Vector2.Distance(f1.A, f2.A) < epsilon) { corner = f1.A; outSum = f1.Out + f2.Out; return true; }
            if (Vector2.Distance(f1.A, f2.B) < epsilon) { corner = f1.A; outSum = f1.Out + f2.Out; return true; }
            if (Vector2.Distance(f1.B, f2.A) < epsilon) { corner = f1.B; outSum = f1.Out + f2.Out; return true; }
            if (Vector2.Distance(f1.B, f2.B) < epsilon) { corner = f1.B; outSum = f1.Out + f2.Out; return true; }
            corner = default; outSum = default; return false;
        }

        /// <summary>One zone's own resolved cross-main placement (MV-819, split out of
        /// <see cref="DressOverheadCrossMains"/> so <see cref="FaceIsCrossMainWall"/> can test a wall
        /// face against it BEFORE any main is built, not just build off it after the fact).
        /// <see cref="FarLine"/> is the WALL's own coordinate (the zone edge the cross-main insets from),
        /// not the cross-main's own line — that is what a wall face's own constant coordinate is
        /// compared against.</summary>
        private readonly struct CrossMainTarget
        {
            public readonly bool AlongX;
            public readonly float FarLine;
            public readonly float Sign;
            public readonly float CrossSpan;
            public readonly float CrossMid;
            public readonly Vector3 AcrossDir;

            public CrossMainTarget(bool alongX, float farLine, float sign, float crossSpan, float crossMid, Vector3 acrossDir)
            {
                AlongX = alongX; FarLine = farLine; Sign = sign;
                CrossSpan = crossSpan; CrossMid = crossMid; AcrossDir = acrossDir;
            }
        }

        /// <summary>Every zone's own cross-main placement (change 1) — at the end furthest from that
        /// zone's own entry, never over the middle. <see cref="MapRuntime.EntryDirection"/> already
        /// resolves "which way did the player walk in from" per zone; the far end is simply further
        /// along that same direction, inset from the wall there exactly like every overhead run already
        /// insets from the wall it hugs. A zone with no resolvable entry (area 1 — entered from outside
        /// the map, not through any authored gate) has no "far end" to speak of and gets no cross-main;
        /// nor does one too narrow, across, to carry a main inset on both sides.</summary>
        private static List<CrossMainTarget> ResolveCrossMainTargets(MapData map)
        {
            var result = new List<CrossMainTarget>();
            if (map.zones == null || map.wallHeight < 2.2f) return result;

            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level > 0) continue;

                Vector3 entryDir = MapRuntime.EntryDirection(map, zone.id);
                if (entryDir.sqrMagnitude < 0.0001f) continue;

                Rect r = zone.Footprint;
                bool alongX = Mathf.Abs(entryDir.x) >= Mathf.Abs(entryDir.z);

                float farLine = alongX
                    ? (entryDir.x > 0f ? r.xMax : r.xMin)
                    : (entryDir.z > 0f ? r.yMax : r.yMin);
                float sign = alongX
                    ? (entryDir.x > 0f ? -1f : 1f)
                    : (entryDir.z > 0f ? -1f : 1f);

                // MV-819: retreats each end by inset+clearance, not inset alone — the perpendicular
                // (end) wall's OWN hugging main sits exactly OverheadMainInset off that same wall, so a
                // plain -inset retreat put the cross-main's own tip at THE SAME coordinate as that main's
                // line, guaranteeing an intersection wherever the cross-main's run crosses it (confirmed
                // empirically: AC1's change-2 check failed with the cross-main's bounds intersecting a
                // perpendicular wall's main at exactly that shared coordinate). The extra
                // OverheadCrossClearance matches the gap already proven to clear two parallel mains'
                // AABBs (see that constant's own doc).
                float endInset = StormdrainKit.OverheadMainInset + StormdrainKit.OverheadCrossClearance;
                float crossSpan = (alongX ? r.height : r.width) - endInset * 2f;
                if (crossSpan < 1.2f) continue;

                float crossMid = alongX ? r.center.y : r.center.x;
                Vector3 acrossDir = alongX ? Vector3.forward : Vector3.right;

                result.Add(new CrossMainTarget(alongX, farLine, sign, crossSpan, crossMid, acrossDir));
            }
            return result;
        }

        private static void DressOverheadCrossMains(Transform overhead, List<CrossMainTarget> targets)
        {
            foreach (CrossMainTarget t in targets)
            {
                float lineCoord = t.FarLine + t.Sign * (StormdrainKit.OverheadMainInset + StormdrainKit.OverheadCrossClearance);
                Vector3 center = t.AlongX
                    ? new Vector3(lineCoord, StormdrainKit.OverheadCrossY, t.CrossMid)
                    : new Vector3(t.CrossMid, StormdrainKit.OverheadCrossY, lineCoord);

                StormdrainKit.BuildOverheadCrossMain(overhead, center, t.CrossSpan, t.AcrossDir);
            }
        }

        /// <summary>MV-819: true if <paramref name="face"/> IS (or closely parallels) the wall a
        /// cross-main insets from. Found by comparing axes: a cross-main built off an alongX target runs
        /// along Z, parallel to any X-normal wall face at the same X coordinate as that target's own
        /// <see cref="CrossMainTarget.FarLine"/> — which is exactly the far wall itself, since that is
        /// where <see cref="CrossMainTarget.FarLine"/> comes from. Building this face's OWN hugging main
        /// too put a second, separately-inset pipe running the full length of the same wall only
        /// <see cref="StormdrainKit.OverheadCrossClearance"/> away from the cross-main's own line —
        /// visually indistinguishable from the gameplay camera for nearly the run's whole length
        /// (confirmed empirically: a real far-wall main scored 0% visible under the AC1 sweep, occluded
        /// almost entirely by its own zone's cross-main). The cross-main alone already reads as "pipe
        /// along this wall"; skipping the wall's own separate main here is what removes the duplicate,
        /// not a threshold tweak on either one — the two mains simply cannot both exist this close
        /// together and still both read as visible pipes.</summary>
        private static bool FaceIsCrossMainWall(WallFace face, List<CrossMainTarget> targets, float wallThickness)
        {
            float epsilon = JunctionEpsilon(wallThickness);
            bool faceIsXNormal = Mathf.Abs(face.Out.x) > Mathf.Abs(face.Out.y);
            float faceCoord = faceIsXNormal ? face.A.x : face.A.y;

            foreach (CrossMainTarget t in targets)
            {
                if (t.AlongX != faceIsXNormal) continue;
                if (Mathf.Abs(faceCoord - t.FarLine) < epsilon) return true;
            }
            return false;
        }

        /// <summary>MV-791: <c>MapGeometry.Floor</c>'s single "Map Floor" slab and this kit's cast bays
        /// are both authored with their top face at y = 0 — exact coincidence, not a tight tolerance, so
        /// which one wins is decided by floating-point noise and flickers as the camera moves. Bays
        /// don't reach every zone (a connector stub narrower than one bay pitch gets none, per
        /// <see cref="BayRects"/>), so Map Floor must stay VISIBLE rather than being switched off —
        /// sinking it below the bays removes the coincidence everywhere at once without leaving a hole
        /// where nothing else is drawing floor. World 1 and World 3 never call this and keep their own
        /// ground plane exactly where it is.</summary>
        private const float MapFloorLowerY = 0.25f;

        private static void LowerMapFloor(Transform host)
        {
            foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Map Floor") continue;
                Vector3 pos = t.localPosition;
                pos.y -= MapFloorLowerY;
                t.localPosition = pos;
                return;
            }
        }

        // ---------------------------------------------------------------- hazard bulkheads (MV-787, change 2)

        /// <summary>Hazard bulkheads — the red, pulsing variant of <see cref="StormdrainKit.DressWallFace"/>'s
        /// own amber fitting — one at every gate and one at every outfall (the ticket's own table). One
        /// per gate rather than a literal pair either side of it: the lens is a sphere, so a single
        /// fixture already reads from both approaches to the doorway, and World 2 authors enough gated
        /// rooms that a pair at every one of them would have pushed red past the ticket's own 20% cap
        /// (change 3) on this map — this ticket's own numbers win, not a placement reading of a table
        /// cell. Gates are read back off what <see cref="MapRuntime.Build"/> already built under
        /// <paramref name="host"/> (the same lookup <see cref="BackyardPath"/> already uses to reskin
        /// them), not re-derived from <paramref name="map"/>, so a gate this pass sees is always one
        /// that actually exists in the built scene.</summary>
        private static void DressHazardBulkheads(Transform root, Transform host, MapData map, List<Fitting> fittings)
        {
            var fittingHost = new GameObject("Light Fittings").transform;
            fittingHost.SetParent(root, false);

            float mountHeight = Mathf.Min(StormdrainLightKit.BulkheadHeight, map.wallHeight * 0.9f);

            foreach (AreaGate gate in host.GetComponentsInChildren<AreaGate>(true))
            {
                Transform t = gate.transform;
                Vector3 inward = gate.AwayFromPlayerDirection.sqrMagnitude > 0.001f
                    ? gate.AwayFromPlayerDirection.normalized
                    : t.forward;
                Vector3 along = t.right;

                Vector3 at = t.position;
                at.y = 0f;
                StormdrainLightKit.BuildBulkheadLamp(fittingHost, "Hazard Bulkhead", at, inward, along,
                    StormdrainLightKit.Red, pulsing: true, mountHeight);
                fittings.Add(new Fitting(new Vector2(at.x, at.z), BulkheadLampFittingRadius));
            }

            MapEntity outfall = map.Entity(OutfallGateId);
            if (outfall != null)
            {
                Vector3 at = new Vector3(outfall.x, 0f, outfall.z);
                StormdrainLightKit.BuildBulkheadLamp(fittingHost, "Hazard Bulkhead", at, Vector3.forward, Vector3.right,
                    StormdrainLightKit.Red, pulsing: true, mountHeight);
                fittings.Add(new Fitting(new Vector2(at.x, at.z), BulkheadLampFittingRadius));
            }
        }

        // ---------------------------------------------------------------- floor composition (MV-781)

        /// <summary>Panel joints every this many metres, phased off world position (not each area's own
        /// origin) so the grid is continuous across an area boundary.</summary>
        private const float JointSpacing = 3.2f;

        /// <summary>The entity kinds whose rects a joint or patch must never cross (the ticket's own
        /// list) — all already resolved to world-space centre/size by <see cref="WorldMapLoader"/>.</summary>
        private static bool IsFloorObstacle(EntityKind kind) =>
            kind == EntityKind.Grate || kind == EntityKind.Deck || kind == EntityKind.Ramp ||
            kind == EntityKind.Hatch || kind == EntityKind.Sludge;

        private static List<Rect> FloorObstacles(MapData map)
        {
            var rects = new List<Rect>();
            if (map.entities == null) return rects;
            foreach (MapEntity e in map.entities)
            {
                if (e == null || !IsFloorObstacle(e.Kind)) continue;

                // A Grate's x/z is its authored MIN CORNER (WorldGrate's own doc comment; WorldMapLoader
                // carries it onto the entity unchanged) — every other obstacle kind here (Deck/Ramp/
                // Hatch/Sludge) is centre-authored by WorldMapLoader, so only Grate needs the different
                // corner-to-rect conversion.
                Rect r = e.Kind == EntityKind.Grate
                    ? new Rect(e.x, e.z, e.width, e.depth)
                    : new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                rects.Add(r);
            }
            return rects;
        }

        /// <summary>Builds every floor-level zone's cast bays and cracks (MV-784, changes 1 and 3),
        /// panel joints (change 2), and silt/standing-water stains (change 4) — skipped for a
        /// <see cref="MapZone.level"/> &gt; 0 zone (a deck overlay shares its target's floor, MV-697, so
        /// it never gets a second pass of it).</summary>
        private static void DressFloorComposition(Transform root, MapData map, List<Fitting> fittings,
                                                   IReadOnlyList<Rect> channelRects)
        {
            if (map.zones == null) return;

            var floorHost = new GameObject("Floor Composition").transform;
            floorHost.SetParent(root, false);

            List<Rect> obstacles = FloorObstacles(map);

            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level > 0) continue;
                Rect zoneRect = zone.Footprint;

                foreach (Bay bay in BayRects(zoneRect, zone.id, channelRects))
                {
                    // MV-799: the floor itself carries the lighting — a bay's authored tone is only the
                    // starting point, scaled by how close it sits to a fitting.
                    float m = LightingMultiplier(bay.Rect.center, fittings);
                    StormdrainKit.BuildBay(floorHost, bay.Rect, bay.Tone * m);
                    if (bay.HasCrack)
                        StormdrainKit.BuildCrack(floorHost,
                            new Vector3(bay.Rect.center.x, 0f, bay.Rect.center.y), bay.CrackHash, toneScale: m);
                }

                foreach (Rect seg in JointRects(zoneRect, obstacles))
                    StormdrainKit.BuildPanelJoint(floorHost, seg);

                foreach (Stain silt in SiltRects(zoneRect, zone.id, obstacles))
                {
                    float m = LightingMultiplier(silt.Center, fittings);
                    StormdrainKit.BuildSiltStain(floorHost,
                        new Vector3(silt.Center.x, 0f, silt.Center.y), silt.CoreRadius, silt.Seed, toneScale: m);
                }

                foreach (Stain water in WaterRects(zoneRect, zone.id, obstacles))
                {
                    float m = LightingMultiplier(water.Center, fittings);
                    StormdrainKit.BuildWaterStain(floorHost,
                        new Vector3(water.Center.x, 0f, water.Center.y), water.CoreRadius, water.Seed, toneScale: m);
                }
            }
        }

        // ---------------------------------------------------------------- cast bays and cracks (MV-784)

        private const float BayPitch = JointSpacing; // same 3.2 m grid the joints already phase off
        private const float BayInset = StormdrainKit.BayInset;
        private const float BayDarkThreshold = 0.33f;
        private const float BayLightThreshold = 0.78f;
        private const float BayCrackThreshold = 0.62f;

        /// <summary>One cast bay (MV-784, change 1): its own inset footprint, the tone its grid-coordinate
        /// hash resolved to, and whether a second, different-salted hash gave it a crack (change 3) —
        /// carrying that hash forward so the crack's own rotation is "the same hash", per the ticket.
        /// </summary>
        public readonly struct Bay
        {
            public readonly Rect Rect;
            public readonly Color Tone;
            public readonly bool HasCrack;
            public readonly float CrackHash;

            public Bay(Rect rect, Color tone, bool hasCrack, float crackHash)
            {
                Rect = rect; Tone = tone; HasCrack = hasCrack; CrackHash = crackHash;
            }
        }

        /// <summary>Every cast bay in one floor zone (MV-784, change 1) — a <see cref="BayPitch"/> grid
        /// local to the zone's own origin, so a zone's own bay count always matches
        /// floor(width/pitch) * floor(height/pitch) exactly (the ticket's own acceptance count), rather
        /// than a world-anchored grid that could clip a partial row/column at the zone edge. Each bay's
        /// tone and crack are both a hash of the bay's own grid coordinates AND the zone's id, so two
        /// zones never tile identically — never <see cref="UnityEngine.Random"/>, so the same map always
        /// casts the same bays.
        ///
        /// <paramref name="channelObstacles"/> (MV-801) is the rect list of channel-eligible sludge
        /// footprints only — NOT <see cref="FloorObstacles"/>'s full Grate/Deck/Ramp/Hatch/Sludge set,
        /// which also holds every flat (non-channel) sludge rect. A flat sludge tile keeps its floor
        /// exactly as it was before this ticket (its ooze sits ON the bay grid, unchanged); only a
        /// channel gets its floor genuinely cut, so only channel rects may drop a bay here.</summary>
        public static List<Bay> BayRects(Rect zone, string zoneId, IReadOnlyList<Rect> channelObstacles = null)
        {
            var result = new List<Bay>();
            int cols = Mathf.FloorToInt(zone.width / BayPitch);
            int rows = Mathf.FloorToInt(zone.height / BayPitch);
            if (cols <= 0 || rows <= 0) return result;

            int zoneSeed = DeterministicSeed(zoneId, zone);
            float size = BayPitch - BayInset;

            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    var rect = new Rect(zone.xMin + col * BayPitch + BayInset * 0.5f,
                                         zone.yMin + row * BayPitch + BayInset * 0.5f, size, size);

                    if (channelObstacles != null && Overlaps(rect, channelObstacles)) continue;

                    float toneHash = BayHash(zoneSeed, col, row, 0);
                    Color tone = toneHash < BayDarkThreshold ? StormdrainKit.GroundDry
                               : toneHash > BayLightThreshold ? StormdrainKit.GroundAccent
                               : StormdrainKit.GroundBase;

                    float crackHash = BayHash(zoneSeed, col, row, 1);
                    result.Add(new Bay(rect, tone, crackHash > BayCrackThreshold, crackHash));
                }
            }
            return result;
        }

        /// <summary>Deterministic 0..1 from a zone seed, a bay's own grid coordinates, and a salt (never
        /// <see cref="UnityEngine.Random"/>) — same integer-mix idiom
        /// <c>MaxWorlds.Enemies.SludgePuddle.Hash01</c> already uses, extended to four inputs so the
        /// tone draw (salt 0) and the crack draw (salt 1) off the same bay never move in lockstep.
        /// </summary>
        private static float BayHash(int zoneSeed, int col, int row, int salt)
        {
            unchecked
            {
                int h = zoneSeed;
                h = h * 374761393 + col * 668265263;
                h = h * 1274126177 + row * 374761393;
                h = h * 668265263 + salt * 1013904223;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // ---------------------------------------------------------------- silt/water stains (MV-784)

        private const int SiltGridSize = 3;  // 3x3 -> nine per area
        private const int WaterGridSize = 2; // 2x2 -> four per area
        private const int SiltSalt = 101;
        private const int WaterSalt = 202;

        /// <summary>One silt drift or standing-water pool's placement (MV-784, change 4): its centre,
        /// its own core radius, and the seed its builder derives every per-segment jitter from.</summary>
        public readonly struct Stain
        {
            public readonly Vector2 Center;
            public readonly float CoreRadius;
            public readonly float Seed;

            public Stain(Vector2 center, float coreRadius, float seed)
            {
                Center = center; CoreRadius = coreRadius; Seed = seed;
            }
        }

        /// <summary>Nine silt drifts per area (MV-784, change 4), one per cell of a 3x3 jittered grid
        /// across the zone so they never clump.</summary>
        public static List<Stain> SiltRects(Rect zone, string zoneId, IReadOnlyList<Rect> obstacles) =>
            StainGrid(zone, zoneId, obstacles, SiltGridSize, SiltSalt);

        /// <summary>Four standing-water pools per area (MV-784, change 4), one per cell of a 2x2
        /// jittered grid across the zone.</summary>
        public static List<Stain> WaterRects(Rect zone, string zoneId, IReadOnlyList<Rect> obstacles) =>
            StainGrid(zone, zoneId, obstacles, WaterGridSize, WaterSalt);

        private static List<Stain> StainGrid(Rect zone, string zoneId, IReadOnlyList<Rect> obstacles,
                                              int gridSize, int salt)
        {
            var result = new List<Stain>();
            int zoneSeed = DeterministicSeed(zoneId, zone) + salt;
            float cellW = zone.width / gridSize;
            float cellH = zone.height / gridSize;

            for (int cx = 0; cx < gridSize; cx++)
            {
                for (int cz = 0; cz < gridSize; cz++)
                {
                    int cellSeed = zoneSeed * 31 + cx * 7 + cz;
                    float radius = StormdrainKit.StainCoreRadiusMin + Frac(cellSeed, 3) *
                        (StormdrainKit.StainCoreRadiusMax - StormdrainKit.StainCoreRadiusMin);

                    var cell = new Rect(zone.xMin + cx * cellW, zone.yMin + cz * cellH, cellW, cellH);
                    if (TryPlaceStain(cell, radius, obstacles, cellSeed, out Vector2 center))
                        result.Add(new Stain(center, radius, cellSeed));
                }
            }
            return result;
        }

        private static bool TryPlaceStain(Rect cell, float radius, IReadOnlyList<Rect> obstacles, int seed,
                                          out Vector2 center)
        {
            float margin = Mathf.Min(Mathf.Max(radius, Mathf.Min(cell.width, cell.height) * 0.15f),
                                     Mathf.Min(cell.width, cell.height) * 0.49f);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                float tx = Frac(seed, attempt * 2 + 1);
                float tz = Frac(seed, attempt * 2 + 2);
                float cx = Mathf.Lerp(cell.xMin + margin, cell.xMax - margin, tx);
                float cz = Mathf.Lerp(cell.yMin + margin, cell.yMax - margin, tz);

                var footprint = new Rect(cx - radius, cz - radius, radius * 2f, radius * 2f);
                if (Overlaps(footprint, obstacles)) continue;

                center = new Vector2(cx, cz);
                return true;
            }

            center = default;
            return false;
        }

        /// <summary>Panel-joint segments for one floor zone's rect (MV-781, change 2): a recessed line
        /// every <see cref="JointSpacing"/> metres on both axes, split into one segment per bay so a
        /// segment that would cross an obstacle can be dropped without breaking the rest of the line.
        /// Pure function of its inputs — calling it twice for the same zone/obstacles is how
        /// <c>MV781FloorCompositionTests</c> proves the layout is deterministic, not re-rolled.</summary>
        public static List<Rect> JointRects(Rect zone, IReadOnlyList<Rect> obstacles)
        {
            var result = new List<Rect>();
            AddJointAxis(result, zone, obstacles, alongX: true);
            AddJointAxis(result, zone, obstacles, alongX: false);
            return result;
        }

        private static void AddJointAxis(List<Rect> result, Rect zone, IReadOnlyList<Rect> obstacles, bool alongX)
        {
            // alongX: the joint LINE runs parallel to Z at a fixed X (a "vertical" line on the floor
            // plan); otherwise the line runs parallel to X at a fixed Z.
            float lineMin = alongX ? zone.xMin : zone.yMin;
            float lineMax = alongX ? zone.xMax : zone.yMax;
            float spanMin = alongX ? zone.yMin : zone.xMin;
            float spanMax = alongX ? zone.yMax : zone.xMax;

            float firstLine = Mathf.Ceil((lineMin + 0.01f) / JointSpacing) * JointSpacing;
            for (float line = firstLine; line < lineMax - 0.01f; line += JointSpacing)
            {
                float firstBay = Mathf.Floor(spanMin / JointSpacing) * JointSpacing;
                for (float bayStart = firstBay; bayStart < spanMax - 0.01f; bayStart += JointSpacing)
                {
                    float segMin = Mathf.Max(bayStart, spanMin);
                    float segMax = Mathf.Min(bayStart + JointSpacing, spanMax);
                    if (segMax - segMin < 0.05f) continue;

                    Rect seg = alongX
                        ? new Rect(line - StormdrainKit.PanelJointWidth * 0.5f, segMin, StormdrainKit.PanelJointWidth, segMax - segMin)
                        : new Rect(segMin, line - StormdrainKit.PanelJointWidth * 0.5f, segMax - segMin, StormdrainKit.PanelJointWidth);

                    if (Overlaps(seg, obstacles)) continue;
                    result.Add(seg);
                }
            }
        }

        private static bool Overlaps(Rect r, IReadOnlyList<Rect> obstacles)
        {
            foreach (Rect o in obstacles)
                if (r.Overlaps(o)) return true;
            return false;
        }

        /// <summary>Deterministic, non-negative hash of a zone's id and resolved world rect — the
        /// "area index and origin" input the ticket's determinism rule asks for, expressed off what a
        /// zone actually carries rather than reaching back into the raw <c>WorldArea</c> it came from.</summary>
        private static int DeterministicSeed(string zoneId, Rect zone)
        {
            unchecked
            {
                int hash = 17;
                if (!string.IsNullOrEmpty(zoneId))
                    foreach (char c in zoneId) hash = hash * 31 + c;
                hash = hash * 31 + Mathf.RoundToInt(zone.x * 100f);
                hash = hash * 31 + Mathf.RoundToInt(zone.y * 100f);
                return hash & 0x7fffffff;
            }
        }

        /// <summary>Deterministic 0..1 from an integer seed and a salt — the same golden-ratio-hash
        /// idiom <c>StormdrainKit.Frac</c>/<c>StormdrainDressing.DeterministicYaw</c> already use for
        /// "same input, same output, never <see cref="UnityEngine.Random"/>", salted so two different
        /// draws off the same seed don't move in lockstep.</summary>
        private static float Frac(int seed, int salt) =>
            Mathf.Abs((seed * 0.6180339887f + salt * 0.3247179572f) % 1f);

        /// <summary>Maps a cover piece's authored dressing class onto its drain equivalent (MV-786:
        /// five turned forms — Standpipe cluster, Collapsed grating, Silt hopper, Pump set, Burst
        /// main; MV-802 adds a sixth, Pipe main). Every class has one — including
        /// <see cref="CoverDressing.None"/>, which here falls back to Burst main, same as anything that
        /// fails to parse. That is the difference from <see cref="ReefDressing"/>, which deliberately
        /// dresses only one class: World 3's ticket said place nothing where there is no equivalent, and
        /// the result is a world of grey boxes. World 2 is not repeating that.</summary>
        private static bool BuildFor(Transform parent, CoverPiece piece)
        {
            ArenaCover c = piece.Cover;
            Vector3 at = new Vector3(c.CenterXz.x, 0f, c.CenterXz.y);
            Vector3 size = c.Size;

            // MV-818: a cover collider's own w/d ratio decides HOW it gets dressed — a near-square
            // piece stays one prop (change 2 explicitly allows that below aspect 2); a long one is a
            // repeated run of modules instead of one prop stretched into a sliver. Hedge is the one
            // dressing that never repeats (its single panel already spans its own long axis once the
            // spanDir/crossDir fix above is in) but still gets the same no-yaw and fit treatment. Pipe
            // is the other exception (MV-863): a run of modules is what made a long pipe read as a row
            // of stubs, the ticket's own reported fault — BuildPipeMain already lathes ONE main along
            // its full length (with its own support posts, see that method's doc comment), so a pipe
            // must always take the single-prop path below, at any aspect.
            float aspect = Aspect(size);

            bool neverModular = c.Dressing == CoverDressing.Hedge || c.Dressing == CoverDressing.Pipe;
            if (!neverModular && aspect >= StormdrainKit.ModularRunAspectThreshold)
            {
                BuildModularRun(parent, at, size, c.Dressing);
                return true;
            }

            GameObject built = BuildSingleDressing(parent, at, size, c.Dressing);

            // MV-818, change 3: a wide yaw on a long piece swings its own ends outside its collider —
            // never rotate a piece at or above the no-yaw aspect. Shed/Machinery/Pipe never yawed at
            // all, aspect or not — pre-dates this ticket (they are "structure, not loose debris", the
            // same reasoning BuildPumpHousing's and BuildPipeMain's own doc comments already give). The
            // yaw is set BEFORE the fit below measures anything — see that method's own comment for why
            // the order matters.
            bool structureType = c.Dressing == CoverDressing.Shed || c.Dressing == CoverDressing.Machinery
                                  || c.Dressing == CoverDressing.Pipe;
            if (!structureType && aspect < StormdrainKit.NoYawAspectThreshold)
                built.transform.rotation = Quaternion.Euler(0f, DeterministicYaw(at), 0f);

            FitFootprintAndHeight(built, size, parent, at);
            return true;
        }

        /// <summary>The one-prop-per-piece dispatch <see cref="BuildFor"/> used before MV-818, unchanged
        /// in what it builds — only the caller now also fits the result to its collider and gates the
        /// yaw, instead of doing both inline per case.</summary>
        private static GameObject BuildSingleDressing(Transform parent, Vector3 at, Vector3 size, CoverDressing dressing)
        {
            switch (dressing)
            {
                case CoverDressing.Tree:
                    return StormdrainKit.BuildStandpipe(parent, at, size);
                case CoverDressing.Hedge:
                    return StormdrainKit.BuildCollapsedGrating(parent, at, size);
                case CoverDressing.Planter:
                    return StormdrainKit.BuildSiltHopper(parent, at, size);
                case CoverDressing.Shed:
                case CoverDressing.Machinery:
                    // Heavy fixed machinery, not loose debris — stays square.
                    return StormdrainKit.BuildPumpHousing(parent, at, size);
                case CoverDressing.Pipe:
                    // MV-802, change 3: structure, not loose debris — stays square, same reasoning as
                    // Shed/Machinery above, so it keeps lying exactly along its own footprint's axis.
                    return StormdrainKit.BuildPipeMain(parent, at, size);
                default:
                    return StormdrainKit.BuildBurstMain(parent, at, size);
            }
        }

        /// <summary>MV-818, change 2: a long piece (aspect &gt;= <see cref="StormdrainKit.ModularRunAspectThreshold"/>)
        /// built as a repeated run of the SAME single-prop form along its own long axis — a trough of
        /// hoppers, a row of standpipes, a line of pump housings joined by a pipe — rather than one prop
        /// stretched into a sliver. Each module gets its own square-ish <paramref name="size"/> slice
        /// (its short side) fitted to that slice exactly the way a non-modular piece fits its own full
        /// collider, plus a small (&lt;= <see cref="StormdrainKit.ModuleJitterMaxDeg"/>) deterministic
        /// yaw jitter — the run's own axis never rotates as a whole (change 3).</summary>
        private static void BuildModularRun(Transform parent, Vector3 at, Vector3 size, CoverDressing dressing)
        {
            bool alongX = size.x >= size.z;
            float longDim = alongX ? size.x : size.z;
            float shortDim = alongX ? size.z : size.x;
            Vector3 along = alongX ? Vector3.right : Vector3.forward;

            // The two extreme module CENTRES sit this far apart; each module then fits itself to a
            // shortDim*coverage slice, so the outermost extent (centreSpan + one module width) lands
            // exactly on the ticket's own coverage target of the full collider length — not that plus
            // a whole module's overrun.
            float moduleWidth = shortDim * StormdrainKit.CoverFootprintCoverage;
            float centreSpan = Mathf.Max(0f, StormdrainKit.CoverFootprintCoverage * longDim - moduleWidth);

            int count = Mathf.Max(2, Mathf.RoundToInt(centreSpan / Mathf.Max(0.01f, shortDim * 1.8f)) + 1);

            var run = new GameObject($"{dressing} Run").transform;
            run.SetParent(parent, false);
            run.position = at;

            Vector3 moduleSize = new Vector3(shortDim, size.y, shortDim);

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (i / (float)(count - 1)) - 0.5f : 0f;
                Vector3 moduleAt = at + along * (t * centreSpan);

                GameObject module = BuildSingleDressing(run, moduleAt, moduleSize, dressing);

                float jitterHash = Frac(HashSeed(moduleAt), i);
                float jitterDeg = (jitterHash - 0.5f) * 2f * StormdrainKit.ModuleJitterMaxDeg;
                module.transform.rotation = Quaternion.Euler(0f, jitterDeg, 0f);

                FitFootprintAndHeight(module, moduleSize, run, moduleAt);
            }

            // A connecting member along the run so the gaps between modules never read as open floor —
            // "a line of pump housings joined by a pipe", the ticket's own example.
            float connectorY = dressing == CoverDressing.Planter ? size.y * 0.22f
                              : dressing == CoverDressing.Shed || dressing == CoverDressing.Machinery ? size.y * 0.55f
                              : size.y * 0.30f;
            float connectorRadius = Mathf.Min(shortDim * 0.12f, 0.18f);
            Quaternion lie = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            // Tube() takes a position LOCAL to its parent (unlike the BuildXxx forms above, which take
            // a world "at" and set .position directly) — run is already sitting at the piece's own
            // world "at", so this only needs the small vertical offset, not "at" added a second time.
            StormdrainKit.Tube(run, "Run Connector", Vector3.up * connectorY, connectorRadius,
                centreSpan, lie, StormdrainKit.Rust);
        }

        /// <summary>MV-818, change 1: stretches a just-built dressing's own combined renderer bounds
        /// (never its collider) so its XZ footprint fills <see cref="StormdrainKit.CoverFootprintCoverage"/>
        /// of <paramref name="colliderSize"/>'s own w/d, and its height clears
        /// <see cref="StormdrainKit.CoverMinVisibleHeight"/>. Must run AFTER any yaw is set on
        /// <paramref name="built"/> — a <c>Transform</c> always composes as rotate(scale(vertex)), so a
        /// non-uniform scale set on the SAME transform that also carries a rotation would apply in the
        /// object's own pre-rotation axes and then get carried along by that rotation, which is not the
        /// axis-aligned fit this needs. Measuring bounds after rotation and pushing the scale onto a
        /// fresh, never-rotated WRAPPER instead scales the already-rotated world-space shape directly,
        /// so the result hits the target exactly regardless of what <paramref name="built"/>'s own
        /// rotation is.</summary>
        private static void FitFootprintAndHeight(GameObject built, Vector3 colliderSize, Transform parent, Vector3 at)
        {
            Renderer[] renderers = built.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (b.size.x < 0.001f || b.size.y < 0.001f || b.size.z < 0.001f) return;

            float scaleX = colliderSize.x * StormdrainKit.CoverFootprintCoverage / b.size.x;
            float scaleZ = colliderSize.z * StormdrainKit.CoverFootprintCoverage / b.size.z;
            float scaleY = Mathf.Max(1f, StormdrainKit.CoverMinVisibleHeight / b.size.y);

            var wrapper = new GameObject(built.name + " Fit").transform;
            wrapper.SetParent(parent, false);
            wrapper.position = at;
            built.transform.SetParent(wrapper, true);
            wrapper.localScale = new Vector3(scaleX, scaleY, scaleZ);
        }

        /// <summary>Long/short aspect ratio of a cover collider's own XZ footprint (MV-818) — &gt;= 1 by
        /// construction, so both the modular-run and no-yaw thresholds read as plain lower bounds.</summary>
        private static float Aspect(Vector3 size) =>
            Mathf.Max(size.x, size.z) / Mathf.Max(0.01f, Mathf.Min(size.x, size.z));

        /// <summary>A deterministic non-negative integer hash of a world XZ position (MV-818) — the
        /// same "position, not <see cref="UnityEngine.Random"/>" contract <see cref="DeterministicYaw"/>
        /// already keeps, exposed separately so a module's per-index jitter (<see cref="Frac(int,int)"/>)
        /// can salt off it without colliding with that formula's own output range.</summary>
        private static int HashSeed(Vector3 at)
        {
            unchecked
            {
                int h = Mathf.RoundToInt(at.x * 100f) * 374761393 + Mathf.RoundToInt(at.z * 100f) * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7fffffff;
            }
        }

        /// <summary>
        /// MV-778, change 3 ("break the axis"): nothing in the drain sat off 90 degrees, which reads
        /// as machine-generated. This is the ticket's own formula, hashed from world position — not
        /// <see cref="Random"/> — so the level lays out identically every run: the same map always
        /// jitters the same piece the same way.
        /// </summary>
        private static float DeterministicYaw(Vector3 at)
            => ((Mathf.Abs(at.x * 73.1f + at.z * 149.7f) % 1f) - 0.5f) * 24f;

        /// <summary>MV-786, change 2: restructures every already-built <see cref="StructuralWall"/>
        /// from one long slab into panels, ribs, pilasters, a coping and a kerb — same "keep the
        /// collider, replace the art" contract the cover pass keeps. <see cref="MapGeometry.Walls"/> is
        /// a pure function of <paramref name="map"/>, so calling it again here reproduces the exact
        /// same segments <c>MapRuntime.Build</c> already built under <paramref name="host"/>, and their
        /// shared <see cref="WallSegment.Name"/> is what lines the two up.</summary>
        private static void DressWallPanels(Transform root, Transform host, MapData map)
        {
            List<WallSegment> segments = MapGeometry.Walls(map);
            if (segments.Count == 0) return;

            var byName = new Dictionary<string, StructuralWall>();
            foreach (StructuralWall wall in host.GetComponentsInChildren<StructuralWall>(true))
                byName[wall.gameObject.name] = wall;

            var panelHost = new GameObject("Wall Panels").transform;
            panelHost.SetParent(root, false);

            foreach (WallSegment seg in segments)
            {
                if (!byName.TryGetValue(seg.Name, out StructuralWall wall) || wall == null) continue;

                var rend = wall.GetComponent<Renderer>();
                Material wallMat = rend != null ? rend.sharedMaterial : null;
                if (rend != null) rend.enabled = false;

                StormdrainKit.BuildWallPanels(panelHost, seg.Center, seg.Size, seg.AlongX, wallMat);
            }
        }

        /// <summary>The id <c>MapRuntime</c> already uses for the gate the sludge grades toward. Reused
        /// here as the downstream anchor, so the chevrons point the same way the tone gradient does
        /// rather than disagreeing with it.</summary>
        private const string OutfallGateId = "outfall";

        private static int DressSludge(Transform root, Transform mapHost, MapData map, List<Rect> channelRects)
        {
            if (map.entities == null) return 0;

            var sludgeHost = new GameObject("Sludge").transform;
            sludgeHost.SetParent(root, false);

            int tiles = 0, seed = 0;

            foreach (MapEntity e in map.entities)
            {
                seed++;
                if (e == null || e.Kind != EntityKind.Sludge) continue;

                Vector3 center = new Vector3(e.x, 0f, e.z);
                Vector3 flow = SludgeFlowDirection(map, e);

                var sludgeRect = new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                MapZone zone = map.ZoneAt(e.x, e.z);
                bool isChannel = zone != null && IsChannelEligible(sludgeRect, zone.Footprint);
                if (isChannel)
                {
                    channelRects.Add(sludgeRect);

                    // MV-822: MapRuntime.BuildSludge already built this rect's own opaque slab at
                    // y 0-0.05 (SludgeThickness), covering the trough BuildChannelTrough is about to cut
                    // below y 0 — that slab, not this dressing pass, is what the player actually saw as
                    // "no diagonal striping" at every channel. Found by the SludgeFlow component MapRuntime
                    // uniquely tags its own slab with (StormdrainKit's own SludgeFlowRig is a different
                    // type), matched by id since a level can build more than one sludge rect.
                    HideMapRuntimeSlabRenderer(mapHost, e.id);
                }

                StormdrainKit.DressSludgeTile(sludgeHost, center, e.width, e.depth, flow, seed, isChannel);
                tiles++;
            }

            return tiles;
        }

        /// <summary>Switches off the renderer <see cref="MaxWorlds.Arena.MapRuntime.BuildSludge"/> built
        /// for a channel-eligible rect (see <see cref="DressSludge"/>'s own call site) — its slow-zone
        /// behaviour comes from <see cref="MaxWorlds.Arena.MapSlowZones"/> sampling the map data directly,
        /// never from this renderer or this GameObject's active state, so disabling only the renderer
        /// (not the whole object, not the <see cref="SludgeFlow"/> component driving it) changes nothing
        /// gameplay depends on.</summary>
        private static void HideMapRuntimeSlabRenderer(Transform mapHost, string sludgeId)
        {
            foreach (SludgeFlow flow in mapHost.GetComponentsInChildren<SludgeFlow>(true))
            {
                if (flow.gameObject.name != sludgeId) continue;
                var rend = flow.GetComponent<Renderer>();
                if (rend != null) rend.enabled = false;
                return;
            }
        }

        /// <summary>MV-801, "the second thing the level forces" — computed from the rect itself rather
        /// than a hand-authored id list, so a level change re-derives the answer instead of drifting
        /// from a table someone forgot to update. Against <c>world2_config.json</c> this gives a
        /// channel for 17 of the 20 authored sludge rects and leaves <c>a8</c> (10 x 10, a sump basin —
        /// its narrower dimension fails the 8 m test) and <c>a14</c>/<c>a18</c> (whole-area floods) flat.
        /// A blocking trough is not on the table for ANY of these: <c>a14</c> is 44 x 12 with a
        /// 44 x 12 sludge rect, <c>a18</c> is 26 x 26 with a 26 x 26 rect, and <c>a23</c>'s four rects
        /// ring the arena — a collider here would either be impossible to route around or moat the
        /// interior shut, which is exactly why this ticket is visual-only.</summary>
        private const float ChannelMaxNarrowDimension = 8f;

        private static bool IsChannelEligible(Rect sludgeRect, Rect zoneRect)
        {
            float narrower = Mathf.Min(sludgeRect.width, sludgeRect.height);
            bool coversWholeArea = sludgeRect.width >= zoneRect.width - 0.01f
                                 && sludgeRect.height >= zoneRect.height - 0.01f;
            return narrower <= ChannelMaxNarrowDimension && !coversWholeArea;
        }

        /// <summary>MV-792: a rect's own shape, not a lookup that can fail, gives the flow AXIS — the
        /// wider dimension is always the channel's run, for every one of World 2's 20 authored sludge
        /// rects. The lookup only ever decides the SENSE along that axis: toward the map's <c>outfall</c>
        /// entity if one is authored (none is, today), otherwise toward this rect's own area's exit gate
        /// — the <see cref="MapLink"/> whose <see cref="MapLink.from"/> is the zone this rect sits in.
        /// If neither resolves (an interior rect with no outgoing link, or an exit gate that lands
        /// exactly on the rect's own axis coordinate), the rect still gets a valid, resolvable direction
        /// — +X for a width-run rect, +Z for a depth-run rect — logged once by name rather than silently
        /// defaulting, so a future author can see which rects still need a real link.</summary>
        public static Vector3 SludgeFlowDirection(MapData map, MapEntity sludge)
        {
            bool axisX = sludge.width >= sludge.depth;
            Vector3 axis = axisX ? Vector3.right : Vector3.forward;

            MapEntity target = map.Entity(OutfallGateId);
            if (target == null)
            {
                MapZone zone = map.ZoneAt(sludge.x, sludge.z);
                if (zone != null && map.links != null)
                {
                    foreach (MapLink link in map.links)
                    {
                        if (link == null || link.from != zone.id) continue;
                        target = map.Entity(link.gate);
                        break;
                    }
                }
            }

            if (target != null)
            {
                float delta = axisX ? target.x - sludge.x : target.z - sludge.z;
                if (Mathf.Abs(delta) > 0.0001f) return delta > 0f ? axis : -axis;
            }

            Debug.Log($"StormdrainDressing: sludge rect '{sludge.id}' has no resolvable downstream gate — " +
                      $"defaulting flow to +{(axisX ? "X" : "Z")}.");
            return axis;
        }
    }
}
