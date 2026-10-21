using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Rendering
{
    /// <summary>One of the 8 atlas-baked deck plate looks <see cref="ShipKit.BuildAtlasFloor"/> tiles a
    /// deck floor from (MV-1259). Int value doubles as the atlas cell index: cell = row*4 + col, where
    /// row/col come from <c>(int)variant % 4</c>/<c>(int)variant / 4</c> — see
    /// <see cref="ShipKit.AtlasCellUv"/>.</summary>
    public enum FloorPlateVariant { PlainLight = 0, PlainMid = 1, PlainDark = 2, Vent = 3, Hatch = 4, Tread = 5, RivetRing = 6, FloorLight = 7 }

    /// <summary>MV-1230: spins a conveyor roller slowly around its own long axis (ticket, change item
    /// 2: "rollers animated slowly"). <see cref="ShipKit.BuildConveyor"/> bakes the roller's rotation
    /// so its local X axis already lies along the belt's own run — <see cref="Space.Self"/> spins
    /// around that axis regardless of which way the belt itself is authored to face.</summary>
    [PerfSection("rendering")]
    public sealed class ShipConveyorRoller : MonoBehaviour
    {
        private const float DegreesPerSecond = 40f;
        private void Update() => transform.Rotate(Vector3.right, DegreesPerSecond * Time.deltaTime, Space.Self);
    }

    /// <summary>
    /// World 5's Mothership ship kit (MV-1230) — the robot design formula at ship scale (ticket's own
    /// "Context"): a panel split by ONE hard straight line into a rust-orange half and a teal half,
    /// near-black machinery, small gold lens lamps. Follows <see cref="CityKit"/>'s own contract:
    /// every builder takes a parent <see cref="Transform"/> plus world-space positions/sizes, never a
    /// map or config, so the Hangar Bay layout ticket (MV-1232) decides where a piece lands, not this
    /// one. Nothing here carries a collider — <see cref="StormdrainKit.Strip"/> strips every piece —
    /// colliders come from the map's own cover entries, routed through <see cref="MaxWorlds.Arena.ShipDressing"/>,
    /// same "collider stays, art swaps" contract every kit in this namespace keeps.
    ///
    /// RESERVED COLOUR GUARD. The ticket's own "Reserved colours" list (cradle red, vent orange, Light
    /// Missile violet, Orbitals gold) — see <see cref="IsReservedColour"/>.
    /// </summary>
    public static class ShipKit
    {
        // ---------------------------------------------------------------- palette

        /// <summary>#B5572E family — the ticket's own rust-orange half of every split panel.</summary>
        public static readonly Color RustOrange = Rgb(181, 87, 46);

        /// <summary>#2F8F95 family — the ticket's own teal half of every split panel.</summary>
        public static readonly Color Teal = Rgb(47, 143, 149);

        /// <summary>Near-black machinery — the seam rib between a split panel's two halves and every
        /// bare-metal fitting (conveyor belt body, glass case plinth).</summary>
        public static readonly Color NearBlack = Rgb(18, 18, 22);

        /// <summary>The Mothership's own gold lens tone — reused, not re-authored, from
        /// <c>WorldCatalog</c>'s own Sentinel bolt/eye colour for this world (MV-1225, Lee 17 Oct 2026,
        /// hex #F3D27A), so every gold lamp this kit builds reads as the same metal the world's own
        /// robots already wear. The ticket's own explicit exemption ("except the small gold lamps")
        /// — see <see cref="IsReservedColour"/>'s own doc for why this tone never trips that guard.</summary>
        public static readonly Color GoldLamp = Rgb(243, 210, 122);

        private static readonly Color DeckPlate = Rgb(58, 64, 76);
        private static readonly Color HazardYellow = Rgb(224, 182, 48);
        private static readonly Color GlassPane = Rgb(150, 205, 210);

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // ---------------------------------------------------------------- reserved colour guard

        /// <summary>True for a saturated, bright colour in the cradle-red, vent-orange or Light Missile
        /// violet bands (ticket's own "Reserved colours": "no kit piece may use the cradle red, vent
        /// orange, Light Missile violet or Orbitals gold except the small gold lamps"). Same shape as
        /// <see cref="CityKit.IsReservedRelayColour"/>'s own guard — only ever meaningful against an
        /// unlit/emissive material; a lit one is unrestricted.
        ///
        /// Bands, each centred on an existing named colour already shipped elsewhere in this project:
        /// cradle red ~4&#176; (<c>MaxWorlds.VFX.FactoryBodies.CradleRed</c>, #E2483D, MV-1227's own
        /// "red is reserved for cradles in this world"); vent orange ~27&#176; — now MV-1240's own
        /// confirmed value, <c>MaxWorlds.VFX.PlasmaVentVisual.GlowOrange</c>, #FF8C2E family, the actual
        /// plasma vent this band was reserved for; Light Missile violet ~275&#176; (<c>LightMissileBall.HaloColor</c>,
        /// #B040FF). Orbitals (MV-1236) has shipped no colour yet, so it is not guarded here.
        ///
        /// Every band leaves a clear margin either side of this kit's own <see cref="RustOrange"/>
        /// (~18&#176;) and <see cref="GoldLamp"/> (~44&#176;, and separately exempt on saturation alone
        /// — 0.50 already sits under this guard's own 0.6 floor) so the kit can never trip its own
        /// guard.</summary>
        public static bool IsReservedColour(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.6f || v < 0.6f) return false;

            float deg = h * 360f;
            bool red = deg >= 350f || deg <= 12f;
            bool vent = deg >= 22f && deg <= 34f;
            bool violet = deg >= 260f && deg <= 290f;
            return red || vent || violet;
        }

        // ---------------------------------------------------------------- internals: shared helpers

        private static Transform NewRoot(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<KeepsOwnMaterial>();
            return go.transform;
        }

        private static Quaternion FacingInward(Vector3 n) => Quaternion.LookRotation(-n.normalized, Vector3.up);

        private static void AddGoldLamp(Transform root, string name, Vector3 pos, Vector3 facing)
        {
            StormdrainKit.Glow(root, name, pos, new Vector3(0.22f, 0.14f, 1f), FacingInward(facing), GoldLamp);
        }

        /// <summary>A solid prop's own compliance body: sized to 96%/94% of <paramref name="colliderSize"/>
        /// on X/Z and Y respectively (CityKit's own <c>SolidBody</c> fraction), so the union of a
        /// prop's renderers always lands inside the AC1 fit window (at least 90%, at most size + 0.15 m,
        /// at least 90% tall) regardless of whatever cosmetic detail the specific piece adds on top.</summary>
        private static GameObject SolidBody(Transform root, string name, Vector3 centre, Vector3 colliderSize,
                                            Color tone, SurfaceKind kind = SurfaceKind.Metal)
        {
            Vector3 size = new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.94f, colliderSize.z * 0.96f);
            return StormdrainKit.Box(root, name, centre + Vector3.up * (size.y * 0.5f), size, tone, kind);
        }

        /// <summary>The robot-design formula at ship scale (ticket's own "Context"): a solid body split
        /// by ONE hard straight horizontal line into a rust-orange lower half and a teal upper half,
        /// with a near-black rib marking the seam. Every solid ShipKit piece below is built from this.</summary>
        private static void BuildSplitBody(Transform root, string name, Vector3 centre, Vector3 colliderSize,
                                           SurfaceKind kind = SurfaceKind.Metal)
        {
            Vector3 size = new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.94f, colliderSize.z * 0.96f);
            SolidBody(root, name, centre, colliderSize, RustOrange, kind);

            float halfY = size.y * 0.5f;
            StormdrainKit.Box(root, $"{name} Upper", centre + Vector3.up * (halfY + halfY * 0.5f),
                new Vector3(size.x + 0.01f, halfY, size.z + 0.01f), Teal, kind);
            StormdrainKit.Box(root, $"{name} Seam", centre + Vector3.up * halfY,
                new Vector3(size.x + 0.015f, size.y * 0.05f, size.z + 0.015f), NearBlack, kind);
        }

        // ---------------------------------------------------------------- wall / floor dressing

        // ---------------------------------------------------------------- bulkhead wall dressing (MV-1260)

        private const float BulkheadPanelPitch = 3f;          // ticket: "Panels every ~3 m"
        private const float BulkheadBackingDepth = 0.04f;
        private const float BulkheadPanelProud = 0.08f;       // how far the diagonal face stands off the backing
        private const float BulkheadKickHeight = 0.16f;
        private const float BulkheadRibRadius = 0.05f;
        private const float BulkheadPipeRadius = 0.045f;
        private const float BulkheadTopTrimHeight = 0.09f;
        private const float BulkheadLampSize = 0.2f;
        private const float BulkheadLightPoolRadius = 1.0f;
        private const float BulkheadWindowHeightFraction = 0.42f;
        private const float BulkheadWindowSillFraction = 0.32f;
        private const float BulkheadWindowSpillRadius = 1.3f;
        private const float RobotRackPitch = 6.5f;            // ticket: "every ~6.5 m on long walls"
        private const float RobotRackMinWallLength = 5f;      // "long walls" only — skip a short stub
        private const float RobotRackWidth = 1.9f;
        private const float RobotRackDepth = 0.55f;
        private const float ClutterPitch = 4.5f;
        private const float HullMassDepth = 8f;               // ticket: "solid dark hull masses" outside the hull

        // MV-1269: at the 72-degree play camera a 1.5-3 m wall's FACE is barely visible edge-on (or not
        // visible at all, facing away), but its TOP and the floor at its base are squarely in view — the
        // constants below move the same rust/teal/lamp/kick detail already on the face onto those two
        // surfaces instead.
        private const float BulkheadCapThickness = 0.07f;     // the cap plate's own vertical thickness
        private const float BulkheadCapRibWidth = 0.08f;      // near-black rib at every ~3 m cap section
        private const float FloorKickStripWidth = 0.3f;       // ticket: "0.3 m wide"
        private const float FloorKickStripHeight = 0.03f;
        private const float FloorKickGlowWidth = 0.03f;
        private const float RobotRackStandoff = 0.6f;         // ticket: "stand at least 0.6 m into the room"

        // MV-1274 addendum ("the wall blocks are 'super basic boxes'"): the Hangar's own wall-top cap —
        // a framed, bevelled housing with a recessed inset panel, not a flat slab. Hangar areas (1-5)
        // only; every other deck keeps BuildWallTopCap's original flat MV-1269 cap unchanged.
        private const float HangarCapFrameBevel = 0.03f;       // pronounced vs DefaultBevel's ~0.0084 clamp-to-0.015 for this thin a box — "bevelled... not a sharp extruded box"
        private const float HangarCapInsetFraction = 0.55f;    // per-axis scale -> ~0.3 (30%) of the top's own area, under the ticket's 40% cap
        private const float HangarCapBumperFraction = 0.08f;
        private const float HangarCapStudPitch = 0.5f;
        // MV-1274 recapture fix-pass: the Neutral tonemapper compresses a 2.0-2.6x HDR boost to well
        // under AC3's >230/255 bloom-pixel threshold (measured 0 bloom pixels across the whole frame
        // at the original boosts) -- raised well past the curve's compression so the source itself
        // clips bright, not just the bloom halo around it.
        private const float HangarCapNeonBoost = 6.0f;         // HDR multiplier, see Hdr() below
        private static readonly Color GunmetalTone = Rgb(42, 46, 54);

        private static readonly Color SteelPipe = Rgb(150, 155, 162);
        private static readonly Color CopperPipe = Rgb(176, 108, 68);

        /// <summary>Pushes <paramref name="c"/> past 1.0 so it clips <see cref="BackyardLook.BloomThreshold"/>
        /// and reads as true HDR neon rather than a flat emissive tint — MV-1274's own "HDR emissive
        /// above the bloom threshold" ask, applied only where a caller explicitly opts in (every existing
        /// emissive tone in this file stays &lt;= 1.0 unless routed through this).</summary>
        private static Color Hdr(Color c, float boost)
        {
            Color r = c * boost;
            r.a = 1f;
            return r;
        }

        /// <summary>The ticket's own explicit "red standby eye" ask — the one sanctioned exception to
        /// <see cref="IsReservedColour"/>'s cradle-red band (same exemption shape <see cref="GoldLamp"/>'s
        /// own doc already carries): the rack tells the player these are dormant units from the same
        /// cradle line, so its eye deliberately echoes cradle red rather than avoiding it. Never used by
        /// anything <see cref="MV1230ShipKitTests"/>'s reserved-colour sweep scans (that sweep only ever
        /// walks the "Ship Kit" cover-dressing host, not this wall-dressing host).</summary>
        private static readonly Color StandbyEyeRed = Rgb(214, 62, 52);

        private static readonly Color HullMassTone = Rgb(10, 11, 14);
        private static readonly Color WindowGlassTone = Rgb(60, 150, 160);
        private static readonly Color SotamiTone = Rgb(70, 190, 185);

        /// <summary>A collider-free box in a flat tinted, lit material — the generated-mesh substitute
        /// for <see cref="StormdrainKit.Box"/> that never calls <see cref="GameObject.CreatePrimitive"/>
        /// even transiently (Lee, 19 Aug 2026, quoted on <see cref="ReefKit"/>'s own structure-kit
        /// section: primitives read "glued together from cardboard boxes and tubes"). Routed through
        /// <see cref="StormdrainKit.AddMeshPart"/>'s existing cached-material helper so every tone here
        /// still batches the same way a <see cref="StormdrainKit.Box"/> call would have.</summary>
        private static GameObject MeshBox(Transform parent, string name, Vector3 pos, Vector3 size, Color tone)
            => StormdrainKit.AddMeshPart(parent, name, CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size)), pos, SurfaceKind.Metal, tone);

        /// <summary>A collider-free part built directly from a <see cref="CharacterMeshes"/> mesh — the
        /// turned-form counterpart to <see cref="MeshBox"/>, for a rib/pipe/rack piece that is prism- or
        /// lathe-shaped rather than a chamfered box.</summary>
        private static GameObject MeshPart(Transform parent, string name, Mesh mesh, Vector3 pos, Color tone, Quaternion? rot = null)
        {
            GameObject go = StormdrainKit.AddMeshPart(parent, name, mesh, pos, SurfaceKind.Metal, tone);
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            return go;
        }

        /// <summary>An unlit, flat-tinted box — the lamp lens/light-pool/glow-line counterpart to
        /// <see cref="MeshBox"/>, same "generated mesh, no primitive" contract.</summary>
        private static GameObject EmissiveBox(Transform parent, string name, Vector3 pos, Vector3 size, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Bevelled(size, 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = StormdrainKit.Unlit(tone, name);
            return go;
        }

        /// <summary>A single flat triangle standing <paramref name="proud"/> metres off the wall along
        /// <paramref name="depthAxis"/> — the generated-mesh substitute for carving a diagonal seam into
        /// a panel's own face (ticket: "split by ONE hard diagonal line"). <paramref name="p0"/>/
        /// <paramref name="p1"/>/<paramref name="p2"/> are local (along, height) coordinates relative to
        /// the panel's own origin corner. Winding self-corrects (the dot-product check below) so the one
        /// face a player standing in the room ever sees — the one facing <paramref name="depthAxis"/> —
        /// is always front-facing, regardless of which world axis <paramref name="alongAxis"/> happens to
        /// be for this particular wall.</summary>
        private static Mesh FlatTriMesh(Vector3 alongAxis, Vector3 depthAxis, Vector2 p0, Vector2 p1, Vector2 p2, float proud)
        {
            Vector3 V(Vector2 p) => alongAxis * p.x + Vector3.up * p.y + depthAxis * proud;
            Vector3 vA = V(p0), vB = V(p1), vC = V(p2);

            bool flip = Vector3.Dot(Vector3.Cross(vB - vA, vC - vA), depthAxis) < 0f;
            var verts = new[] { vA, vB, vC };
            int[] tris = flip ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };

            var mesh = new Mesh { name = "BulkheadPanelTri" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Bulkhead wall dressing (MV-1260, wiring up MV-1230's own never-called builder): split
        /// rust/teal panels every ~3 m with a diagonal seam that alternates direction panel to panel, a
        /// near-black rib between panels, a dark kick plate with a thin emissive line in
        /// <paramref name="deckGlow"/>, two top pipes (steel + copper) and a top trim the wall's full
        /// length, a lamp plus a floor light-pool every other panel, a framed window onto Sotami plus a
        /// floor spill on every alternate panel of an OUTER wall only, a robot rack every ~6.5 m on a
        /// long wall, seeded side clutter, and — outer walls only — a solid hull mass standing just
        /// beyond it. MV-1269 adds a wall-top cap (<see cref="BuildWallTopCap"/>) and a floor-edge kick
        /// strip (<see cref="BuildFloorKickStrip"/>) — the two surfaces the 72-degree play camera actually
        /// reads, since a 1.5-3 m wall's FACE is nearly edge-on at that angle. <paramref name="a"/>/
        /// <paramref name="b"/> are the wall run's own resolved endpoints in world XZ; <paramref name="inward"/>
        /// points INTO the room this call is dressing — an interior wall gets one call per side, same
        /// "no single front face" reasoning <see cref="ReefKit.BuildWallFaceDetail"/> already documents
        /// for the Reef's own wall ribs. Called by <see cref="MaxWorlds.Arena.ShipAreaKit"/>, which
        /// resolves the real <see cref="StructuralWall"/>/<see cref="MaxWorlds.Arena.WorldArea"/> data
        /// this Rendering-assembly method never sees — same WHERE/WHAT split every other kit in this
        /// namespace keeps.</summary>
        public static GameObject BuildBulkheadPanel(Transform parent, Vector2 a, Vector2 b, Vector3 inward,
            float wallHeight, float wallThickness, Color deckGlow, bool isOuterEdge, int wallSeed, bool capVisible = true,
            bool hangarStyle = false)
        {
            Transform root = NewRoot(parent, "Bulkhead Wall");
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) { StormdrainKit.Strip(root.gameObject); return root.gameObject; }

            Vector2 dir = d / length;
            Vector3 n = new Vector3(inward.x, 0f, inward.z);
            n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
            float h = Mathf.Max(0.1f, wallHeight);
            float wt = Mathf.Max(0.05f, wallThickness);

            int panels = Mathf.Max(1, Mathf.CeilToInt(length / BulkheadPanelPitch));
            for (int i = 0; i < panels; i++)
            {
                Vector2 pa = Vector2.Lerp(a, b, (float)i / panels);
                Vector2 pb = Vector2.Lerp(a, b, (float)(i + 1) / panels);
                BuildPanelCell(root, pa, pb, dir, n, h, deckGlow, flip: (i % 2) == 1);

                if (i % 2 == 0)
                    BuildWallLamp(root, Vector2.Lerp(pa, pb, 0.5f), n, h, deckGlow, hangarStyle);
                else if (isOuterEdge)
                    BuildWindowPanel(root, pa, pb, dir, n, h);
            }

            for (int i = 0; i <= panels; i++)
                BuildRib(root, Vector2.Lerp(a, b, (float)i / panels), n, h);

            BuildTopPipes(root, a, b, dir, n, h);
            BuildTopTrim(root, a, b, dir, n, h);
            // MV-1272: a wall shared by two neighbouring areas (A's east IS B's west) gets this call
            // once per area per side — see ShipAreaKit.BuildWallDressing. a/b/wallThickness/panels are
            // all resolved off the matched StructuralWall's own geometry, so both calls land the
            // identical cap at the identical Y with their own independent rust/teal alternation,
            // depth-fighting on a real device. The second visit still builds the same "Wall Top Cap"/
            // "Cap Lamp Strip"/"Cap Rib" children (so a per-side structural check, e.g.
            // MV1269ShipWallTopCapTests, still finds them under EVERY area's own wall root) but with
            // their renderers disabled — same "hide the box, keep the structure" contract this file
            // already uses on the wall's own backing renderer two lines below — so only one copy is
            // ever actually drawn.
            BuildWallTopCap(root, a, b, dir, n, h, wt, deckGlow, panels, capVisible, hangarStyle);
            BuildFloorKickStrip(root, a, b, dir, n, deckGlow);

            if (length >= RobotRackMinWallLength)
            {
                int racks = Mathf.Max(1, Mathf.FloorToInt(length / RobotRackPitch));
                for (int i = 0; i < racks; i++)
                {
                    float t = (i + 0.5f) / racks;
                    BuildRobotRack(root, Vector2.Lerp(a, b, t), dir, n, h, wallSeed * 101 + i);
                }
            }

            BuildSideClutter(root, a, b, dir, n, wallSeed);

            if (isOuterEdge)
                BuildHullMass(root, a, b, dir, n, h);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>One ~3 m panel cell: a near-black backing plate, the diagonal rust/teal split
        /// standing proud of it (direction alternates with <paramref name="flip"/> — "alternating the
        /// diagonal"), and a dark kick plate with a thin emissive line in <paramref name="deckGlow"/>.
        /// Wrapped in its own "Bulkhead Panel" root so a test (or a future pass) can count panel cells
        /// directly rather than re-deriving the count from wall length.</summary>
        private static void BuildPanelCell(Transform root, Vector2 pa, Vector2 pb, Vector2 dir, Vector3 n,
            float wallHeight, Color deckGlow, bool flip)
        {
            float w = (pb - pa).magnitude;
            if (w < 0.01f) return;

            Transform cell = NewRoot(root, "Bulkhead Panel");
            Vector2 mid = (pa + pb) * 0.5f;
            Vector3 dir3 = new Vector3(dir.x, 0f, dir.y);
            Vector3 origin = new Vector3(pa.x, 0f, pa.y);

            MeshBox(cell, "Panel Backing", new Vector3(mid.x, wallHeight * 0.5f, mid.y) + n * (BulkheadBackingDepth * 0.5f),
                new Vector3(w * 0.98f, wallHeight * 0.92f, BulkheadBackingDepth).Oriented(dir), NearBlack);

            // "Alternating the diagonal" (ticket): the split direction itself flips panel to panel —
            // (w,0)-(0,h) one panel, (0,0)-(w,h) the next — rather than just swapping which colour sits
            // on a fixed diagonal, so the wall reads as a genuine zigzag rather than a flat repeat.
            float proud = BulkheadBackingDepth + BulkheadPanelProud;
            var bl = new Vector2(0f, 0f);
            var br = new Vector2(w, 0f);
            var tr = new Vector2(w, wallHeight);
            var tl = new Vector2(0f, wallHeight);

            Mesh rustTri, tealTri;
            if (!flip)
            {
                rustTri = FlatTriMesh(dir3, n, bl, br, tl, proud);   // lower-left triangle
                tealTri = FlatTriMesh(dir3, n, br, tr, tl, proud);   // upper-right triangle
            }
            else
            {
                rustTri = FlatTriMesh(dir3, n, bl, br, tr, proud);   // lower-right triangle
                tealTri = FlatTriMesh(dir3, n, bl, tr, tl, proud);   // upper-left triangle
            }
            MeshPart(cell, "Panel Diagonal Rust", rustTri, origin, RustOrange);
            MeshPart(cell, "Panel Diagonal Teal", tealTri, origin, Teal);

            MeshBox(cell, "Kick Plate", new Vector3(mid.x, BulkheadKickHeight * 0.5f, mid.y) + n * (proud + 0.01f),
                new Vector3(w * 0.96f, BulkheadKickHeight, 0.03f).Oriented(dir), NearBlack);
            EmissiveBox(cell, "Kick Glow", new Vector3(mid.x, 0.02f, mid.y) + n * (proud + 0.02f),
                new Vector3(w * 0.9f, 0.02f, 0.01f).Oriented(dir), deckGlow);
        }

        /// <summary>A near-black rib between panels (chamfered 4-sided <see cref="CharacterMeshes.Prism"/>,
        /// standing vertical the full wall height) — ticket: "a near-black rib between panels".</summary>
        private static void BuildRib(Transform root, Vector2 at, Vector3 n, float wallHeight)
        {
            Vector3 pos = new Vector3(at.x, wallHeight * 0.5f, at.y) + n * (BulkheadBackingDepth + BulkheadPanelProud * 0.5f);
            Mesh rib = CharacterMeshes.Prism(4, BulkheadRibRadius, BulkheadRibRadius, wallHeight * 0.96f, 0.12f);
            MeshPart(root, "Bulkhead Rib", rib, pos, NearBlack);
        }

        /// <summary>Two pipes along the top of the wall's full run (ticket: "two pipes along the top
        /// (steel and copper)") — one <see cref="CharacterMeshes.Beam"/> mesh shared by both instances.</summary>
        private static void BuildTopPipes(Transform root, Vector2 a, Vector2 b, Vector2 dir, Vector3 n, float wallHeight)
        {
            float length = (b - a).magnitude;
            Vector2 mid = (a + b) * 0.5f;
            bool alongX = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);
            Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);

            Mesh pipe = CharacterMeshes.Beam(length * 0.96f, BulkheadPipeRadius, BulkheadPipeRadius, 8);
            Vector3 steelPos = new Vector3(mid.x, wallHeight * 0.9f, mid.y) + n * (BulkheadBackingDepth + BulkheadPipeRadius + 0.02f);
            Vector3 copperPos = steelPos + Vector3.up * (BulkheadPipeRadius * 2.2f);
            MeshPart(root, "Top Pipe Steel", pipe, steelPos, SteelPipe, lie);
            MeshPart(root, "Top Pipe Copper", pipe, copperPos, CopperPipe, lie);
        }

        /// <summary>A top trim the wall's full length (ticket: "a top trim").</summary>
        private static void BuildTopTrim(Transform root, Vector2 a, Vector2 b, Vector2 dir, Vector3 n, float wallHeight)
        {
            float length = (b - a).magnitude;
            Vector2 mid = (a + b) * 0.5f;
            Vector3 pos = new Vector3(mid.x, wallHeight - BulkheadTopTrimHeight * 0.5f, mid.y) + n * (BulkheadBackingDepth * 0.5f);
            MeshBox(root, "Top Trim", pos, new Vector3(length, BulkheadTopTrimHeight, BulkheadBackingDepth + 0.02f).Oriented(dir), NearBlack);
        }

        /// <summary>MV-1269: a wall-top cap plate spanning the wall's own full thickness, split along its
        /// length into rust/teal sections (same ~3 m pitch and panel-to-panel alternation
        /// <see cref="BuildPanelCell"/> uses below it), a near-black rib at every section boundary, and a
        /// lamp strip in <paramref name="deckGlow"/> on every other section — the ticket's own "move that
        /// detail to where the play camera can actually see it". Built from the same <paramref name="panels"/>
        /// count the face loop already computed, so the cap's own coverage tracks the wall's real length
        /// exactly (each section just under its own share of the run, for a hairline seam between
        /// sections) rather than being derived separately.
        ///
        /// MV-1272: <paramref name="visible"/> is false for the second of a shared wall's two dressing
        /// calls (<see cref="MaxWorlds.Arena.ShipAreaKit.BuildWallDressing"/>) — every piece below still
        /// gets built, so a per-side structural check still finds a "Wall Top Cap"/"Cap Lamp Strip"/
        /// "Cap Rib" under every area's own wall root, but its renderer is disabled so the duplicate
        /// never actually draws (and so never depth-fights the first copy).
        ///
        /// MV-1272 also found a SECOND, distinct defect the same way: at an L-corner two perpendicular
        /// walls meet at, each wall's cap spans its own full run end to end, and each run's full span
        /// already reaches that shared corner point — so the two caps' thickness-wide footprints overlap
        /// in a wallThickness x wallThickness square right over the corner, each with whatever colour its
        /// own alternation landed there. <paramref name="a"/>/<paramref name="b"/> are inset by half the
        /// wall's own thickness at each end before tiling, below, so the cap's footprint stays within its
        /// own wall's run and never reaches into the corner the perpendicular wall's own cap already
        /// owns — not a "lift one 0.01 m" patch, since the actual duplicate geometry here is removed
        /// rather than hidden behind an offset.</summary>
        private static void BuildWallTopCap(Transform root, Vector2 a, Vector2 b, Vector2 dir, Vector3 n,
            float wallHeight, float wallThickness, Color deckGlow, int panels, bool visible, bool hangarStyle = false)
        {
            float capY = wallHeight + BulkheadCapThickness * 0.5f;

            float fullLength = (b - a).magnitude;
            float inset = Mathf.Min(wallThickness + BulkheadCapRibWidth, fullLength * 0.4f);
            a += dir * inset;
            b -= dir * inset;

            for (int i = 0; i < panels; i++)
            {
                Vector2 pa = Vector2.Lerp(a, b, (float)i / panels);
                Vector2 pb = Vector2.Lerp(a, b, (float)(i + 1) / panels);
                float w = (pb - pa).magnitude;
                if (w < 0.01f) continue;

                Vector2 mid = (pa + pb) * 0.5f;
                bool flip = (i % 2) == 1;
                Vector3 pos = new Vector3(mid.x, capY, mid.y);

                if (hangarStyle)
                {
                    BuildHangarCapSection(root, pos, w, dir, n, wallThickness, flip, deckGlow, visible);
                    continue;
                }

                GameObject cap = MeshBox(root, "Wall Top Cap", pos,
                    new Vector3(w * 0.99f, BulkheadCapThickness, wallThickness).Oriented(dir), flip ? Teal : RustOrange);
                if (!visible) HideRenderer(cap);
                TagDressing(cap);

                if (i % 2 == 0)
                {
                    GameObject lamp = EmissiveBox(root, "Cap Lamp Strip", pos + Vector3.up * (BulkheadCapThickness * 0.5f + 0.008f),
                        new Vector3(w * 0.5f, 0.012f, wallThickness * 0.3f).Oriented(dir), deckGlow);
                    if (!visible) HideRenderer(lamp);
                    TagDressing(lamp);
                }
            }

            for (int i = 0; i <= panels; i++)
            {
                Vector2 at = Vector2.Lerp(a, b, (float)i / panels);
                GameObject rib = MeshBox(root, "Cap Rib", new Vector3(at.x, capY, at.y),
                    new Vector3(BulkheadCapRibWidth, BulkheadCapThickness * 1.4f, wallThickness * 1.05f).Oriented(dir), NearBlack);
                if (!visible) HideRenderer(rib);
                TagDressing(rib);
            }
        }

        /// <summary>MV-1274 addendum: one Hangar wall-top cap section — a gunmetal frame (pronounced
        /// bevel, "not a sharp extruded box"), a recessed rust/teal inset panel covering
        /// <see cref="HangarCapInsetFraction"/>&#178; (~30%, under the ticket's 40% cap) of the frame's
        /// own footprint, two end bumpers, a row of rivet/conduit studs along the frame's outer edge, and
        /// an HDR neon edge line along the room-facing edge. <paramref name="pos"/>/<paramref name="w"/>
        /// are this section's own centre/along-wall length (world space, already resolved by the caller's
        /// loop) — never read back off an <see cref="StormdrainKit.Oriented"/>'d size, which only ever
        /// reports WORLD axis extents, not "along-wall" ones, once the wall runs along Z instead of X.</summary>
        private static void BuildHangarCapSection(Transform root, Vector3 pos, float w, Vector2 dir, Vector3 n,
            float wallThickness, bool flip, Color deckGlow, bool visible)
        {
            Vector3 dir3 = new Vector3(dir.x, 0f, dir.y);
            Vector3 frameSize = new Vector3(w * 0.99f, BulkheadCapThickness, wallThickness).Oriented(dir);

            GameObject frame = StormdrainKit.AddMeshPart(root, "Wall Top Cap", CharacterMeshes.Bevelled(frameSize, HangarCapFrameBevel), pos, SurfaceKind.Metal, GunmetalTone);
            if (!visible) HideRenderer(frame);
            TagDressing(frame);

            Vector3 insetSize = new Vector3(w * HangarCapInsetFraction, BulkheadCapThickness * 0.55f, wallThickness * HangarCapInsetFraction).Oriented(dir);
            Vector3 insetPos = pos + Vector3.down * (BulkheadCapThickness * 0.5f - insetSize.y * 0.5f - 0.004f);
            GameObject inset = MeshBox(root, "Wall Top Cap Inset", insetPos, insetSize, flip ? Teal : RustOrange);
            if (!visible) HideRenderer(inset);
            TagDressing(inset);

            float bumperLen = Mathf.Min(w * HangarCapBumperFraction, 0.16f);
            Vector3 bumperSize = new Vector3(bumperLen, BulkheadCapThickness * 1.5f, wallThickness * 0.92f).Oriented(dir);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 bumperPos = pos + dir3 * (side * (w * 0.5f - bumperLen * 0.5f)) + Vector3.up * (BulkheadCapThickness * 0.2f);
                GameObject bumper = MeshBox(root, "Wall Top Cap Bumper", bumperPos, bumperSize, GunmetalTone);
                if (!visible) HideRenderer(bumper);
                TagDressing(bumper);
            }

            int studs = Mathf.Clamp(Mathf.FloorToInt(w / HangarCapStudPitch), 2, 8);
            Mesh studMesh = CharacterMeshes.Prism(6, 0.016f, 0.016f, BulkheadCapThickness * 0.9f, 0.12f);
            for (int s = 0; s < studs; s++)
            {
                float t = (s + 0.5f) / studs - 0.5f;
                Vector3 studPos = pos + dir3 * (t * w * 0.9f) - n * (wallThickness * 0.32f) + Vector3.up * (BulkheadCapThickness * 0.5f + 0.001f);
                GameObject rivet = MeshPart(root, "Wall Top Cap Rivet", studMesh, studPos, GunmetalTone);
                if (!visible) HideRenderer(rivet);
                TagDressing(rivet);
            }

            Vector3 neonPos = pos + n * (wallThickness * 0.5f - 0.01f) + Vector3.up * (BulkheadCapThickness * 0.5f + 0.003f);
            Vector3 neonSize = new Vector3(w * 0.96f, 0.012f, 0.02f).Oriented(dir);
            GameObject neon = EmissiveBox(root, "Wall Top Cap Neon Edge", neonPos, neonSize, Hdr(deckGlow, HangarCapNeonBoost));
            if (!visible) HideRenderer(neon);
            TagDressing(neon);
        }

        /// <summary>MV-1272: disables a built piece's own renderer without removing it — same "hide the
        /// box, keep the structure" contract this file already uses on a dressed wall's own backing
        /// renderer, applied to a wall-top-cap piece that structurally exists (for a per-side test, e.g.
        /// MV1269ShipWallTopCapTests) but must not actually draw a second time.</summary>
        private static void HideRenderer(GameObject go)
        {
            var renderer = go != null ? go.GetComponent<Renderer>() : null;
            if (renderer != null) renderer.enabled = false;
        }

        /// <summary>MV-1277: the Unity layer <see cref="CaptureDirector"/>'s differential capture
        /// clears from <c>cam.cullingMask</c> to exclude a collider-free dressing/VFX piece from a
        /// frame -- the renderer-side exclusion idiom named by this file's own class doc ("nothing here
        /// carries a collider ... colliders come from the map's own cover entries"), reused rather than
        /// invented a third mechanism (ticket's own "Method"). Never applied to <see cref="BuildAtlasFloor"/>'s
        /// own floor tiles or <see cref="BuildGraduatedPool"/>'s light-pool rings -- those are the
        /// measurement SUBJECT, not a candidate occluder of it.</summary>
        public const string DressingLayerName = "Dressing";

        /// <summary>Tags <paramref name="go"/>'s own layer as <see cref="DressingLayerName"/> so a
        /// differential capture can exclude it via <c>cam.cullingMask</c>. A no-op if that layer hasn't
        /// been added to <c>ProjectSettings/TagManager.asset</c> (defensive only -- MV-1277 adds it).</summary>
        private static void TagDressing(GameObject go)
        {
            if (go == null) return;
            int layer = LayerMask.NameToLayer(DressingLayerName);
            if (layer >= 0) go.layer = layer;
        }

        /// <summary>MV-1269: a floor-edge kick strip along the base of this wall, extending
        /// <see cref="FloorKickStripWidth"/> into the room with a thin deck-colour emissive line near its
        /// inner edge (ticket, item 2: "so every room's edge is lit"). Distinct from
        /// <see cref="BuildPanelCell"/>'s own per-panel "Kick Plate"/"Kick Glow", which stand proud of the
        /// WALL FACE itself (a few cm off the wall) rather than lying flat on the floor across the wall's
        /// full run.</summary>
        private static void BuildFloorKickStrip(Transform root, Vector2 a, Vector2 b, Vector2 dir, Vector3 n, Color deckGlow)
        {
            float length = (b - a).magnitude;
            Vector2 mid = (a + b) * 0.5f;

            Vector3 stripCentre = new Vector3(mid.x, FloorKickStripHeight * 0.5f, mid.y) + n * (FloorKickStripWidth * 0.5f);
            MeshBox(root, "Floor Kick Strip", stripCentre,
                new Vector3(length * 0.99f, FloorKickStripHeight, FloorKickStripWidth).Oriented(dir), NearBlack);

            Vector3 glowCentre = new Vector3(mid.x, FloorKickStripHeight + 0.006f, mid.y) + n * (FloorKickStripWidth * 0.85f);
            EmissiveBox(root, "Floor Kick Glow", glowCentre,
                new Vector3(length * 0.97f, 0.01f, FloorKickGlowWidth).Oriented(dir), deckGlow);
        }

        /// <summary>A lamp near the top in <paramref name="deckGlow"/>, with an additive floor light-pool
        /// just inside the wall (ticket, change item 2) — the pool is <see cref="CityKit.BuildPool"/>'s
        /// own generated disc, not a primitive quad.
        ///
        /// MV-1274: <paramref name="hangarStyle"/> swaps <see cref="CityKit.BuildPool"/>'s own flat disc
        /// (a single hard-edged radius — the ticket's own "not flat discs") for <see cref="BuildGraduatedPool"/>'s
        /// stepped radial falloff, at <see cref="HangarLightPoolRadius"/> (1.6 m) instead of the shared
        /// <see cref="BulkheadLightPoolRadius"/> — every other deck's own lamp pool is untouched (AC8).</summary>
        private static void BuildWallLamp(Transform root, Vector2 at, Vector3 n, float wallHeight, Color deckGlow, bool hangarStyle = false)
        {
            Vector3 lampPos = new Vector3(at.x, wallHeight * 0.72f, at.y) + n * (BulkheadBackingDepth + BulkheadLampSize * 0.5f + 0.02f);
            TagDressing(EmissiveBox(root, "Wall Lamp", lampPos, new Vector3(BulkheadLampSize, BulkheadLampSize * 0.7f, 0.05f), deckGlow));

            if (hangarStyle)
            {
                Vector3 hangarPoolPos = new Vector3(at.x, 0f, at.y) + n * (HangarLightPoolRadius * 0.9f);
                BuildGraduatedPool(root, hangarPoolPos, HangarLightPoolRadius, deckGlow);
                return;
            }

            // MV-1264: was 0.6 — well outside CityKit's own 0.12-0.26 pool-strength range, which is why
            // these painted as large flat solid discs on a real device (no bloom falloff left to read as
            // a light pool rather than a dyed patch of deck). 0.18 sits inside that range.
            Vector3 poolPos = new Vector3(at.x, 0f, at.y) + n * (BulkheadLightPoolRadius * 0.9f);
            CityKit.BuildPool(root, poolPos, BulkheadLightPoolRadius, BulkheadLightPoolRadius, deckGlow, 0.18f);
        }

        private const float HangarLightPoolRadius = 1.6f;     // ticket: "graduated radial falloff over at least 1.5 m"

        /// <summary>MV-1274 AC4's own "graduated radial falloff ... not flat discs": five concentric
        /// additive rings (<see cref="CharacterMeshes.Ring"/>, a real lathed annulus — plus one innermost
        /// lathed disc), each a step dimmer outward, in place of <see cref="CityKit.BuildPool"/>'s single
        /// flat-strength disc. A real continuous gradient would need a baked radial-alpha texture (the
        /// floor atlas's own "no imported image files, baked in code" contract would still allow a
        /// generated one, but every other additive glow in this kit is a flat-tinted material, never a
        /// textured one) — this reuses that same flat-material idiom, so the "graduated" look is carried
        /// by genuinely stepped geometry instead, which is also the literal shape AC4's own acceptance
        /// test measures ("at least 4 distinct steps, each step at least 5 apart").</summary>
        private static void BuildGraduatedPool(Transform parent, Vector3 groundCentre, float radius, Color tone)
        {
            // MV-1274 triage (21 Oct, Jira comment 15685): the original strengths put the pool centre at
            // ~224-232 luma, clipped flat against the Neutral tonemapper's shoulder -- AC4's own falloff
            // probe starts inside that plateau, so no monotonic gradient can exist there at any sampling
            // distance. Cut roughly in half so the brightest step lands in the tonemapper's still-responsive
            // range instead of its ceiling.
            var steps = new (float outerFraction, float strength)[]
            {
                (0.22f, 0.42f),
                (0.42f, 0.24f),
                (0.62f, 0.14f),
                (0.82f, 0.075f),
                (1.00f, 0.035f),
            };

            float innerR = 0f;
            for (int i = 0; i < steps.Length; i++)
            {
                float outerR = radius * steps[i].outerFraction;
                Mesh ring = i == 0
                    ? CharacterMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f) }, 24)
                    : CharacterMeshes.Ring(innerR / outerR, 1f, 0.01f, 24);

                var go = new GameObject($"Pool Step {i}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = groundCentre + Vector3.up * (0.02f + i * 0.001f);
                go.transform.localScale = new Vector3(outerR, 1f, outerR);
                go.AddComponent<MeshFilter>().sharedMesh = ring;
                go.AddComponent<MeshRenderer>().sharedMaterial = StormdrainLightKit.AdditiveUnlit(tone * steps[i].strength, $"HangarPool{i}");
                go.AddComponent<KeepsOwnMaterial>();
                StormdrainKit.Strip(go);

                innerR = outerR;
            }
        }

        /// <summary>A framed window onto stars and the limb of the planet Sotami, plus a faint additive
        /// floor spill in front (ticket, change item 3) — outer walls only, called by the caller's own
        /// <paramref name="isOuterEdge"/> gate in <see cref="BuildBulkheadPanel"/>.</summary>
        private static void BuildWindowPanel(Transform root, Vector2 pa, Vector2 pb, Vector2 dir, Vector3 n, float wallHeight)
        {
            float w = (pb - pa).magnitude;
            Vector2 mid = (pa + pb) * 0.5f;
            Vector3 dir3 = new Vector3(dir.x, 0f, dir.y);
            float sill = wallHeight * BulkheadWindowSillFraction;
            float winH = wallHeight * BulkheadWindowHeightFraction;

            Vector3 paneCentre = new Vector3(mid.x, sill + winH * 0.5f, mid.y) + n * (BulkheadBackingDepth + 0.015f);
            MeshBox(root, "Window Frame", paneCentre, new Vector3(w * 0.8f, winH, 0.05f).Oriented(dir), NearBlack);
            MeshBox(root, "Window Pane", paneCentre + n * 0.03f, new Vector3(w * 0.68f, winH * 0.82f, 0.02f).Oriented(dir), WindowGlassTone);

            Vector3 limbCentre = paneCentre + n * 0.035f + dir3 * (w * 0.12f) + Vector3.up * (winH * 0.12f);
            EmissiveBox(root, "Sotami Limb", limbCentre, new Vector3(w * 0.3f, winH * 0.3f, 0.01f).Oriented(dir), SotamiTone);

            Vector3 spillPos = new Vector3(mid.x, 0f, mid.y) + n * (BulkheadWindowSpillRadius * 0.9f);
            CityKit.BuildPool(root, spillPos, BulkheadWindowSpillRadius, BulkheadWindowSpillRadius, SotamiTone, 0.35f);
        }

        /// <summary>A robot rack every ~6.5 m on a long wall (ticket, change item 4): an alcove frame
        /// holding three dormant robot shells, standing at least <see cref="RobotRackStandoff"/> into the
        /// room from the wall face (MV-1269, ticket change item 3 — the rack used to sit almost flush on
        /// the wall, where it read as part of the wall rather than a free-standing structure at the
        /// 72-degree play camera's angle).</summary>
        private static void BuildRobotRack(Transform root, Vector2 at, Vector2 dir, Vector3 n, float wallHeight, int seed)
        {
            Vector3 dir3 = new Vector3(dir.x, 0f, dir.y);
            Vector3 basePos = new Vector3(at.x, 0f, at.y) + n * RobotRackStandoff;
            float frameH = wallHeight * 0.55f;

            Vector3 frameCentre = basePos + Vector3.up * (frameH * 0.5f + 0.1f) + n * (RobotRackDepth * 0.5f);
            MeshBox(root, "Robot Rack Frame", frameCentre, new Vector3(RobotRackWidth, frameH, RobotRackDepth).Oriented(dir), NearBlack);

            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.5f) / 3f - 0.5f;
                Vector3 shellPos = basePos + dir3 * (t * RobotRackWidth * 0.78f) + Vector3.up * 0.1f + n * (RobotRackDepth * 0.35f);
                BuildDormantRobotShell(root, shellPos, n, frameH * 0.82f, seed + i * 7);
            }
        }

        /// <summary>One dormant robot shell (dark, kind-neutral) with a small red standby eye — a
        /// tapered <see cref="CharacterMeshes.Prism"/> torso and head, never a primitive.</summary>
        private static void BuildDormantRobotShell(Transform root, Vector3 basePos, Vector3 n, float height, int seed)
        {
            float torsoH = height * 0.62f, headH = height * 0.22f;
            float torsoR = height * 0.16f;

            Mesh torso = CharacterMeshes.Prism(6, torsoR, torsoR * 0.78f, torsoH, 0.14f);
            Vector3 torsoPos = basePos + Vector3.up * (torsoH * 0.5f);
            MeshPart(root, "Robot Shell Torso", torso, torsoPos, DeckPlate);

            Mesh head = CharacterMeshes.Prism(6, torsoR * 0.6f, torsoR * 0.5f, headH, 0.2f);
            Vector3 headPos = torsoPos + Vector3.up * (torsoH * 0.5f + headH * 0.5f);
            MeshPart(root, "Robot Shell Head", head, headPos, NearBlack);

            // MV-1269: the eye moved onto the head's TOP surface (was the side, facing the room at
            // head height) — at the 72-degree play camera these racks are seen from almost directly
            // above, so a side-mounted eye was edge-on and never read (ticket, change item 3: "so they
            // read from above").
            Vector3 eyePos = headPos + Vector3.up * (headH * 0.5f + 0.012f);
            EmissiveBox(root, "Standby Eye", eyePos, new Vector3(0.05f, 0.02f, 0.05f), StandbyEyeRed);
        }

        /// <summary>Small crates, canisters, consoles and cable loops seeded by <paramref name="wallSeed"/>
        /// (ticket, change item 5) — placed in the wall's own 1 m band, never a play lane (the band this
        /// runs in is purely decorative floor space the map's own cover/lane solver never claims).</summary>
        private static void BuildSideClutter(Transform root, Vector2 a, Vector2 b, Vector2 dir, Vector3 n, int wallSeed)
        {
            float length = (b - a).magnitude;
            int slots = Mathf.Max(0, Mathf.FloorToInt(length / ClutterPitch) - 1);
            Vector3 dir3 = new Vector3(dir.x, 0f, dir.y);

            for (int i = 0; i < slots; i++)
            {
                float t = (i + 1f) / (slots + 1f);
                Vector2 at = Vector2.Lerp(a, b, t);
                float roll = Hash01($"shipclutter_{wallSeed}_{i}");
                Vector3 basePos = new Vector3(at.x, 0f, at.y) + n * 0.4f;

                if (roll < 0.34f) BuildClutterCrate(root, basePos);
                else if (roll < 0.6f) BuildClutterCanister(root, basePos);
                else if (roll < 0.82f) BuildClutterConsole(root, basePos, n);
                else BuildClutterCableLoop(root, basePos);
            }
        }

        private static void BuildClutterCrate(Transform root, Vector3 at)
        {
            Vector3 size = new Vector3(0.5f, 0.5f, 0.45f);
            MeshBox(root, "Clutter Crate", at + Vector3.up * (size.y * 0.5f), size, NearBlack);
            MeshBox(root, "Clutter Crate Band", at + Vector3.up * (size.y * 0.82f), new Vector3(size.x * 1.01f, 0.06f, size.z * 1.01f), HazardYellow);
        }

        private static void BuildClutterCanister(Transform root, Vector3 at)
        {
            Mesh canister = CharacterMeshes.Prism(8, 0.18f, 0.16f, 0.6f, 0.2f);
            MeshPart(root, "Clutter Canister", canister, at + Vector3.up * 0.3f, Teal);
        }

        private static void BuildClutterConsole(Transform root, Vector3 at, Vector3 n)
        {
            Vector3 size = new Vector3(0.6f, 0.55f, 0.4f);
            MeshBox(root, "Clutter Console", at + Vector3.up * (size.y * 0.5f), size, NearBlack);
            EmissiveBox(root, "Clutter Console Light", at + Vector3.up * (size.y * 0.82f) + n * (size.z * 0.3f),
                new Vector3(0.08f, 0.08f, 0.02f), GoldLamp);
        }

        private static void BuildClutterCableLoop(Transform root, Vector3 at)
        {
            Mesh loop = CharacterMeshes.Ring(0.16f, 0.22f, 0.05f, 10);
            MeshPart(root, "Clutter Cable Loop", loop, at + Vector3.up * 0.04f, NearBlack);
        }

        /// <summary>Solid dark hull mass standing just beyond an outer wall (ticket, change item 6):
        /// "outside the outer walls: solid dark hull masses, never open space".</summary>
        private static void BuildHullMass(Transform root, Vector2 a, Vector2 b, Vector2 dir, Vector3 n, float wallHeight)
        {
            float length = (b - a).magnitude;
            Vector2 mid = (a + b) * 0.5f;
            Vector3 outward = -n;
            Vector3 pos = new Vector3(mid.x, wallHeight * 0.5f, mid.y) + outward * (HullMassDepth * 0.5f + 0.1f);
            MeshBox(root, "Hull Mass", pos, new Vector3(length * 1.05f, wallHeight * 3f, HullMassDepth).Oriented(dir), HullMassTone);
        }

        /// <summary>Launch-pad hazard marking (MV-1268): yellow hazard stripes along the area's north/south
        /// edges only, with no underlying slab or seams — the atlas floor (<see cref="BuildAtlasFloor"/>)
        /// shows through everywhere else in the pad's footprint. Was <c>BuildDeckFloor</c>, a full dark
        /// blue-grey slab + seams + stripes; the slab sat flush over the atlas floor and hid it under the
        /// player's own start point (ticket's own "Also in area 1"). Flat and walkable — same reusable,
        /// layout-agnostic contract as <see cref="BuildBulkheadPanel"/>.</summary>
        public static GameObject BuildLaunchPadHazardStripes(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Launch Pad Hazard Stripes");

            // MV-1274 triage: neonWidth widened 0.05 -> 0.12 so the HDR source itself has more physical
            // extent for the bloom kernel to spread from, same AC3 fix as the 9.0x boost above.
            // MV-1274 round 3: 0.12/9.0x left the near-stripe delta at 19.2 against the >=25 gate.
            // 0.22/15.0x moved it to 23.6 (close) but read as a solid garish bar rather than a neon
            // line at this width -- pulled back partway, trading some of AC3's margin for a look that
            // still reads as a thin accent.
            const float stripe = 0.3f, neonWidth = 0.16f;
            StormdrainKit.Box(root, "Hazard N", new Vector3(area.center.x, 0.016f, area.yMax - stripe * 0.5f),
                new Vector3(area.width, 0.006f, stripe), HazardYellow, SurfaceKind.Metal);
            StormdrainKit.Box(root, "Hazard S", new Vector3(area.center.x, 0.016f, area.yMin + stripe * 0.5f),
                new Vector3(area.width, 0.006f, stripe), HazardYellow, SurfaceKind.Metal);

            // MV-1274 ("Neon... on: the hazard stripe"): a thin HDR centreline on top of the walkable lit
            // stripe above — the stripe itself stays readable/walkable-lit, the centreline is what blooms.
            // MV-1274 triage: AC3's near-hazard-stripe delta (0.15 m past the stripe's own outer edge,
            // so ~0.3 m past this centreline) is in Neutral's near-linear mid-range, which triage read as
            // reachable with more spill, not a brighter clamped source -- widened and boosted here rather
            // than touching BloomIntensity/PostExposure again (both already tuned against AC1/AC2's bands).
            EmissiveBox(root, "Hazard N Neon", new Vector3(area.center.x, 0.019f, area.yMax - stripe * 0.5f),
                new Vector3(area.width * 0.98f, 0.004f, neonWidth), Hdr(HazardYellow, 12.0f));
            EmissiveBox(root, "Hazard S Neon", new Vector3(area.center.x, 0.019f, area.yMin + stripe * 0.5f),
                new Vector3(area.width * 0.98f, 0.004f, neonWidth), Hdr(HazardYellow, 12.0f));

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- atlas floor (MV-1259)

        private const float PlateSize = 2f;
        private const int AtlasCols = 4, AtlasRows = 2, CellPx = 128;

        // Y-stack, all below ShipDressing.BuildLaunchPad's own hazard slab (0 to 0.012): the generated
        // floor must read as the base layer everywhere EXCEPT the Hangar Bay's launch-pad patch, where
        // the hazard slab already built on top of it hides it without either one needing to know about
        // the other.
        //
        // MV-1264: raised from 0.001/0.0016/0.0022/0.0028 (same relative spacing, +0.009 across the
        // board) — at 0.001 the atlas floor sat essentially flush with the single map-spanning "Map
        // Floor" slab (MapGeometry.Floor's top is exactly y = 0), so the two coplanar-enough surfaces
        // z-fought and the map floor's own flat GroundBase tone won on a real device, hiding every
        // plate/seam/decal this kit draws. ShipDressing.BuildFloors now also disables the Map Floor's
        // renderer under a built atlas floor, but the margin here is kept regardless — belt and braces
        // for any area a future pass adds without that disable (e.g. a non-"area&lt;N&gt;" zone).
        private const float FloorY = 0.01f, DecalY = 0.0106f, SeamY = 0.0112f, GlowY = 0.0118f;

        // MV-1259 fix-pass 1 (verifier subagent, capture read): the first-pass tones read as a near-
        // uniform dark grey under the Mothership's own already-shipped, deliberately-dim per-deck
        // lighting (MV-1249, QA Passed — not this ticket's to retune) — a real EditMode pass off the
        // resolved mesh still proves 8 distinct variants exist, but a human eye couldn't tell plain
        // from vent from tread apart in a fresh MV-1249 capture. Widened the light/dark spread here
        // (never touching MV-1249's own light levels) so the SAME lighting reads the variety instead
        // of flattening it.
        private static readonly Color PlainLightTone = Rgb(175, 180, 190);
        private static readonly Color PlainMidTone = Rgb(120, 126, 138);
        private static readonly Color PlainDarkTone = Rgb(55, 58, 68);
        private static readonly Color VentSlatTone = Rgb(32, 34, 40);
        private static readonly Color TreadBumpTone = Rgb(44, 47, 54);

        private static Texture2D s_floorAlbedo, s_floorEmission;
        private static Material s_floorMaterial, s_decalMaterial;

        // MV-1274: a second, darker-navy atlas for the Hangar (areas 1-5) only — cached independently
        // of s_floorAlbedo/s_floorEmission/s_floorMaterial above so every other deck's own atlas (and
        // every byte of its baked texture) stays bit-identical (AC8's "areas 6-30 unchanged"). Ticket's
        // own target floor-plate albedo: RGB 26/34/50.
        private static readonly Color HangarPlainLightTone = Rgb(34, 44, 62);
        private static readonly Color HangarPlainMidTone = Rgb(26, 34, 50);     // the ticket's own exact target
        private static readonly Color HangarPlainDarkTone = Rgb(17, 23, 36);
        private static readonly Color HangarVentSlatTone = Rgb(10, 13, 21);
        private static readonly Color HangarTreadBumpTone = Rgb(14, 18, 29);

        private static Texture2D s_floorAlbedoDark, s_floorEmissionDark;
        private static Material s_floorMaterialDark;

        /// <summary>The ticket's own per-area deck floor (MV-1259): one combined mesh of 2 m atlas-plate
        /// quads (<see cref="BuildPlateMesh"/>), dark seams with a thin deck-coloured emissive line every
        /// 8 m (<see cref="BuildSeams"/>), and one combined, sparse wear-decal mesh
        /// (<see cref="BuildWearDecalMesh"/>). <paramref name="areaIndex"/> seeds every random pick —
        /// stable between runs, never <see cref="UnityEngine.Random"/> — and <paramref name="seamGlow"/>
        /// is the caller's already-resolved deck accent colour (<c>MothershipDecks.LookFor(areaIndex).RimColor</c>,
        /// an Arena/Gameplay-assembly lookup this Rendering-assembly kit cannot make itself — see this
        /// file's own header on why every builder here takes plain data, never a map or config). No
        /// collider anywhere: purely a visual overlay on top of the zone's own real, already-walkable
        /// floor.
        ///
        /// MV-1274: <paramref name="darkBase"/> — Hangar areas only (<see cref="MaxWorlds.Arena.MothershipDecks"/>'s
        /// own area-to-deck gate, resolved by the Arena-assembly caller, same "plain data down" contract
        /// this file's own header documents) — swaps in the darker navy atlas (target plate albedo RGB
        /// 26/34/50, MV-1274 AC2) and HDR-boosts the seam glow lines. Every other deck still builds the
        /// original MV-1259 atlas unchanged (AC8).</summary>
        public static GameObject BuildAtlasFloor(Transform parent, Rect area, int areaIndex, Color seamGlow, bool darkBase = false)
        {
            Transform root = NewRoot(parent, $"Atlas Floor A{areaIndex}");

            var plates = new GameObject("Plates") { isStatic = true };
            plates.transform.SetParent(root, false);
            plates.AddComponent<MeshFilter>().sharedMesh = BuildPlateMesh(area, areaIndex);
            plates.AddComponent<MeshRenderer>().sharedMaterial = FloorMaterial(darkBase);

            BuildSeams(root, area, seamGlow, hdrGlow: darkBase);

            Mesh wear = BuildWearDecalMesh(area, areaIndex);
            if (wear != null)
            {
                var decals = new GameObject("Wear Decals") { isStatic = true };
                decals.transform.SetParent(root, false);
                decals.AddComponent<MeshFilter>().sharedMesh = wear;
                decals.AddComponent<MeshRenderer>().sharedMaterial = DecalMaterial();
            }

            root.gameObject.isStatic = true;
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>One mesh of <see cref="PlateSize"/> quads tiling <paramref name="area"/> exactly (the
        /// last row/column is clipped to the remainder rather than overhanging, so the built mesh's own
        /// bounds always equal the area's footprint to within float error — AC1's "at least 98%"
        /// coverage is 100% by construction). Each quad's UVs pick one of 8 atlas cells from a seeded
        /// weighted hash (ticket: "70% plain, 10% vent, 5% hatch, 6% tread, 3% floor light, 6% rivet"),
        /// never <see cref="UnityEngine.Random"/> — same stable-hash idiom <c>ReefFloorDetail.StableHash</c>
        /// already established for this project, duplicated here rather than shared across the Rendering/
        /// Arena assembly boundary (see <see cref="StableHash"/>'s own doc).</summary>
        private static Mesh BuildPlateMesh(Rect area, int areaIndex)
        {
            int cols = Mathf.Max(1, Mathf.CeilToInt(area.width / PlateSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt(area.height / PlateSize));

            var verts = new List<Vector3>(cols * rows * 4);
            var uvs = new List<Vector2>(cols * rows * 4);
            var normals = new List<Vector3>(cols * rows * 4);
            var tris = new List<int>(cols * rows * 6);

            for (int gz = 0; gz < rows; gz++)
            {
                float z0 = area.yMin + gz * PlateSize;
                float z1 = Mathf.Min(area.yMax, z0 + PlateSize);
                for (int gx = 0; gx < cols; gx++)
                {
                    float x0 = area.xMin + gx * PlateSize;
                    float x1 = Mathf.Min(area.xMax, x0 + PlateSize);

                    Rect uv = AtlasCellUv(PickVariant(areaIndex, gx, gz));

                    int b = verts.Count;
                    verts.Add(new Vector3(x0, FloorY, z0));
                    verts.Add(new Vector3(x1, FloorY, z0));
                    verts.Add(new Vector3(x1, FloorY, z1));
                    verts.Add(new Vector3(x0, FloorY, z1));

                    uvs.Add(new Vector2(uv.xMin, uv.yMin));
                    uvs.Add(new Vector2(uv.xMax, uv.yMin));
                    uvs.Add(new Vector2(uv.xMax, uv.yMax));
                    uvs.Add(new Vector2(uv.xMin, uv.yMax));

                    normals.Add(Vector3.up); normals.Add(Vector3.up); normals.Add(Vector3.up); normals.Add(Vector3.up);

                    // MV-1268: (x0,z0)->(x1,z0)->(x1,z1) winds counter-clockwise seen from above (x
                    // right, z up), which Unity's clockwise-front convention culls as back-facing under
                    // URP/Lit's cull-back default. b,b+2,b+1 / b,b+3,b+2 is the same two triangles with
                    // the opposite winding, so the quad faces up instead of down.
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            var mesh = new Mesh { name = "ShipFloorPlates" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The weighted pick itself (ticket's own percentages). The 70% "plain" band resolves to
        /// one of the 3 plain shades via a second, independent hash, so a cell's shade never correlates
        /// with its neighbour's band roll.</summary>
        private static FloorPlateVariant PickVariant(int areaIndex, int gx, int gz)
        {
            string key = $"deckplate_{areaIndex}_{gx}_{gz}";
            float h = Hash01(key);

            if (h < 0.70f)
            {
                int shade = Mathf.Clamp((int)(Hash01(key + "_shade") * 3f), 0, 2);
                return (FloorPlateVariant)shade;
            }
            if (h < 0.80f) return FloorPlateVariant.Vent;
            if (h < 0.85f) return FloorPlateVariant.Hatch;
            if (h < 0.91f) return FloorPlateVariant.Tread;
            if (h < 0.94f) return FloorPlateVariant.FloorLight;
            return FloorPlateVariant.RivetRing;
        }

        /// <summary>The atlas-space UV rect for one plate cell, inset 3% of a cell's own size on every
        /// edge so bilinear filtering never bleeds a neighbouring cell's colour across the seam — the
        /// atlas itself bakes no mips (<see cref="NewAtlasTexture"/>) for the same reason.</summary>
        private static Rect AtlasCellUv(FloorPlateVariant variant)
        {
            int cell = (int)variant;
            int cx = cell % AtlasCols, cy = cell / AtlasCols;
            float cellW = 1f / AtlasCols, cellH = 1f / AtlasRows;
            float insetU = cellW * 0.03f, insetV = cellH * 0.03f;
            return Rect.MinMaxRect(cx * cellW + insetU, cy * cellH + insetV,
                                    (cx + 1) * cellW - insetU, (cy + 1) * cellH - insetV);
        }

        /// <summary>Dark seams every 8 m on both axes, each paired with a thin emissive line in the
        /// deck's own accent colour (<paramref name="seamGlow"/>) dimmed to "low intensity" (ticket) by a
        /// flat 0.6x multiplier — the only place a per-deck colour reaches this kit, since every other
        /// ShipKit tone is fixed (see this file's own header). MV-1259 fix-pass 1: raised from an initial
        /// 0.35x, which read as barely-there against a fresh MV-1249 capture — this is still well under
        /// <see cref="GoldLamp"/>'s own full-intensity use, so "low" still holds.
        ///
        /// MV-1274: the glow line itself used to be a bare <see cref="StormdrainKit.Glow"/> — a Unity
        /// primitive Quad left entirely unreplaced (every OTHER glow/lamp piece in this file already
        /// swaps its primitive's mesh for a generated one; this was the one spot that didn't) — which is
        /// what MV1274HangarNoPrimitivesTests catches under every Hangar area's own floor dressing. Now a
        /// flat <see cref="EmissiveBox"/> instead, same visible footprint, generated mesh.
        /// <paramref name="hdrGlow"/> (Hangar only — see <see cref="BuildAtlasFloor"/>'s own
        /// <c>darkBase</c> doc) pushes the line past 1.0 so it clips <see cref="BackyardLook.BloomThreshold"/>
        /// and reads as neon (ticket change item 2: "the faint floor grid lines") instead of the flat
        /// 0.6x wash every other deck still gets unchanged.</summary>
        private static void BuildSeams(Transform root, Rect area, Color seamGlow, bool hdrGlow = false)
        {
            const float spacing = 8f, seamWidth = 0.05f, glowWidth = 0.025f, glowThin = 0.004f;
            Color dim = seamGlow * (hdrGlow ? 5.0f : 0.6f);
            dim.a = 1f;

            for (float x = area.xMin + spacing; x < area.xMax - 0.01f; x += spacing)
            {
                StormdrainKit.Box(root, $"Seam X{x:F0}", new Vector3(x, SeamY, area.center.y),
                    new Vector3(seamWidth, 0.01f, area.height), NearBlack, SurfaceKind.Metal);
                EmissiveBox(root, $"Seam Glow X{x:F0}", new Vector3(x, GlowY, area.center.y),
                    new Vector3(glowWidth, glowThin, area.height), dim);
            }

            for (float z = area.yMin + spacing; z < area.yMax - 0.01f; z += spacing)
            {
                StormdrainKit.Box(root, $"Seam Z{z:F0}", new Vector3(area.center.x, SeamY, z),
                    new Vector3(area.width, 0.01f, seamWidth), NearBlack, SurfaceKind.Metal);
                EmissiveBox(root, $"Seam Glow Z{z:F0}", new Vector3(area.center.x, GlowY, z),
                    new Vector3(area.width, glowThin, glowWidth), dim);
            }
        }

        /// <summary>One combined, sparse wear mesh per area (ticket: "oil stains, scuffs, tread tracks
        /// ... one batched transparent mesh") — a 4 m scan grid, ~35% of cells seeded a decal, jittered
        /// off its own cell centre so the grid itself never reads as a grid. Returns null for an area with
        /// no decals at all (never built smaller than 4 m across) rather than an empty renderer.</summary>
        private static Mesh BuildWearDecalMesh(Rect area, int areaIndex)
        {
            const float scanStep = 4f, keepChance = 0.35f, minSize = 0.4f, maxSize = 1.1f;

            int cols = Mathf.Max(1, Mathf.FloorToInt(area.width / scanStep));
            int rows = Mathf.Max(1, Mathf.FloorToInt(area.height / scanStep));
            float cellW = area.width / cols, cellH = area.height / rows;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int gz = 0; gz < rows; gz++)
            {
                for (int gx = 0; gx < cols; gx++)
                {
                    string key = $"deckdecal_{areaIndex}_{gx}_{gz}";
                    if (Hash01(key) > keepChance) continue;

                    float cx = area.xMin + (gx + 0.5f) * cellW + (Hash01(key + "_jx") - 0.5f) * cellW * 0.6f;
                    float cz = area.yMin + (gz + 0.5f) * cellH + (Hash01(key + "_jz") - 0.5f) * cellH * 0.6f;
                    float yaw = Hash01(key + "_yaw") * 360f;
                    float size = Mathf.Lerp(minSize, maxSize, Hash01(key + "_size"));
                    int kind = Mathf.Clamp((int)(Hash01(key + "_kind") * 3f), 0, 2);

                    AddDecalQuads(verts, uvs, tris, new Vector2(cx, cz), yaw, size, kind);
                }
            }

            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = "ShipFloorWear" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>One of the ticket's own three wear kinds: 0 = oil stain (a square-ish blob), 1 = scuff
        /// (an elongated streak), 2 = tread track (two short parallel bars straddling the centre).</summary>
        private static void AddDecalQuads(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                           Vector2 center, float yawDeg, float size, int kind)
        {
            switch (kind)
            {
                case 0:
                    AddFlatQuad(verts, uvs, tris, center, yawDeg, size, size);
                    break;
                case 1:
                    AddFlatQuad(verts, uvs, tris, center, yawDeg, size * 2.4f, size * 0.35f);
                    break;
                default:
                    float rad = (yawDeg + 90f) * Mathf.Deg2Rad;
                    var side = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (size * 0.3f);
                    AddFlatQuad(verts, uvs, tris, center + side, yawDeg, size * 1.6f, size * 0.18f);
                    AddFlatQuad(verts, uvs, tris, center - side, yawDeg, size * 1.6f, size * 0.18f);
                    break;
            }
        }

        private static void AddFlatQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                         Vector2 center, float yawDeg, float lengthX, float lengthZ)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            Vector2 fwd = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (lengthX * 0.5f);
            Vector2 side = new Vector2(-fwd.y, fwd.x).normalized * (lengthZ * 0.5f);

            int b = verts.Count;
            verts.Add(new Vector3(center.x - fwd.x - side.x, DecalY, center.y - fwd.y - side.y));
            verts.Add(new Vector3(center.x + fwd.x - side.x, DecalY, center.y + fwd.y - side.y));
            verts.Add(new Vector3(center.x + fwd.x + side.x, DecalY, center.y + fwd.y + side.y));
            verts.Add(new Vector3(center.x - fwd.x + side.x, DecalY, center.y - fwd.y + side.y));

            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.one); uvs.Add(Vector2.up);

            // MV-1268: same up-facing winding fix as BuildPlateMesh above — this quad was wound
            // identically and was being culled by the same URP/Lit cull-back default.
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
        }

        /// <summary>The shared atlas material (<see cref="MaterialLibrary.SurfaceShader"/>, lit, so the
        /// floor reads the same per-deck ambient light as everything else) — built and cached once, the
        /// same lazy-static idiom <c>StylizedTextures</c> uses for its own baked textures.</summary>
        private static Material FloorMaterial(bool darkBase = false)
        {
            if (darkBase)
            {
                if (s_floorMaterialDark != null && s_floorAlbedoDark != null && s_floorEmissionDark != null) return s_floorMaterialDark;
                BuildFloorAtlasTextures(darkBase: true, out s_floorAlbedoDark, out s_floorEmissionDark);
                Material dark = NewFloorMaterial("M_ShipFloorAtlasHangar", s_floorAlbedoDark, s_floorEmissionDark);
                s_floorMaterialDark = dark;
                return dark;
            }

            if (s_floorMaterial != null && s_floorAlbedo != null && s_floorEmission != null) return s_floorMaterial;
            BuildFloorAtlasTextures(darkBase: false, out s_floorAlbedo, out s_floorEmission);
            s_floorMaterial = NewFloorMaterial("M_ShipFloorAtlas", s_floorAlbedo, s_floorEmission);
            return s_floorMaterial;
        }

        private static Material NewFloorMaterial(string name, Texture2D albedo, Texture2D emission)
        {
            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emission);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white);
            m.EnableKeyword("_EMISSION");
            return m;
        }

        /// <summary>An alpha-blended (never additive — a dark stain must darken, not add light) unlit
        /// material, cached once: the ticket's "one batched transparent mesh" needs exactly one material.
        /// Same URP Unlit property-guarded setup as <c>StormdrainKit.AdditiveUnlit</c>, with standard
        /// alpha <c>SrcAlpha</c>/<c>OneMinusSrcAlpha</c> blend constants in place of its additive ones.</summary>
        private static Material DecalMaterial()
        {
            if (s_decalMaterial != null) return s_decalMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported) shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var tone = new Color(0.04f, 0.04f, 0.045f, 0.3f);
            var m = new Material(shader) { name = "M_ShipFloorWear", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tone);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tone);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            s_decalMaterial = m;
            return m;
        }

        /// <summary>Bakes the shared 8-cell atlas (ticket: "ONE shared floor atlas texture... no imported
        /// image files"), 128 px per cell — one 512x256 albedo plus a matching emission mask (black except
        /// the <see cref="FloorPlateVariant.FloorLight"/> cell's own dot). No mips: an atlas's own packed
        /// cells would bleed into each other under minification the moment Unity generates them.</summary>
        private static void BuildFloorAtlasTextures(bool darkBase, out Texture2D albedo, out Texture2D emission)
        {
            int w = AtlasCols * CellPx, h = AtlasRows * CellPx;
            var albedoPx = new Color32[w * h];
            var emissionPx = new Color32[w * h];

            for (int cell = 0; cell < AtlasCols * AtlasRows; cell++)
            {
                int ox = (cell % AtlasCols) * CellPx;
                int oy = (cell / AtlasCols) * CellPx;
                DrawPlateCell((FloorPlateVariant)cell, albedoPx, emissionPx, w, ox, oy, darkBase);
            }

            albedo = NewAtlasTexture(darkBase ? "ShipFloorAtlasAlbedoHangar" : "ShipFloorAtlasAlbedo", w, h, albedoPx);
            emission = NewAtlasTexture(darkBase ? "ShipFloorAtlasEmissionHangar" : "ShipFloorAtlasEmission", w, h, emissionPx);
        }

        private static Texture2D NewAtlasTexture(string name, int w, int h, Color32[] px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            t.SetPixels32(px);
            t.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return t;
        }

        /// <summary>Every variant shares the ticket's own common framing — a bevel highlight on the top
        /// edge, a shadow on the bottom edge, four corner rivets — then layers its own pattern on top.</summary>
        private static void DrawPlateCell(FloorPlateVariant variant, Color32[] albedo, Color32[] emission, int atlasW, int ox, int oy, bool darkBase = false)
        {
            Color plainLight = darkBase ? HangarPlainLightTone : PlainLightTone;
            Color plainMid = darkBase ? HangarPlainMidTone : PlainMidTone;
            Color plainDark = darkBase ? HangarPlainDarkTone : PlainDarkTone;
            Color ventSlat = darkBase ? HangarVentSlatTone : VentSlatTone;
            Color treadBump = darkBase ? HangarTreadBumpTone : TreadBumpTone;

            Color baseTone = variant switch
            {
                FloorPlateVariant.PlainLight => plainLight,
                FloorPlateVariant.PlainMid => plainMid,
                FloorPlateVariant.PlainDark => plainDark,
                FloorPlateVariant.Vent => plainDark,
                FloorPlateVariant.Tread => plainMid,
                FloorPlateVariant.RivetRing => plainMid,
                FloorPlateVariant.FloorLight => plainDark,
                _ => plainMid, // Hatch
            };

            for (int ly = 0; ly < CellPx; ly++)
            {
                for (int lx = 0; lx < CellPx; lx++)
                {
                    Color c = baseTone;
                    switch (variant)
                    {
                        case FloorPlateVariant.Vent when (ly / 8) % 2 == 0:
                            c = ventSlat;
                            break;
                        case FloorPlateVariant.Hatch when IsCornerMark(lx, ly):
                            c = HazardYellow;
                            break;
                        case FloorPlateVariant.Tread when IsTreadBump(lx, ly):
                            c = treadBump;
                            break;
                        case FloorPlateVariant.RivetRing when IsRingRivet(lx, ly):
                            c = NearBlack;
                            break;
                    }

                    if (ly >= CellPx - 6) c = Color.Lerp(c, Color.white, 0.22f);       // bevel highlight
                    else if (ly < 6) c = Color.Lerp(c, Color.black, 0.28f);            // bottom shadow
                    if (IsCornerRivet(lx, ly)) c = NearBlack;                          // 4 corner rivets

                    int gi = (oy + ly) * atlasW + (ox + lx);
                    albedo[gi] = c;
                    emission[gi] = new Color(0f, 0f, 0f, 1f);
                }
            }

            if (variant == FloorPlateVariant.FloorLight)
            {
                int ccx = ox + CellPx / 2, ccy = oy + CellPx / 2;
                const int r = 10;
                for (int ly = -r; ly <= r; ly++)
                    for (int lx = -r; lx <= r; lx++)
                        if (lx * lx + ly * ly <= r * r)
                        {
                            int gi = (ccy + ly) * atlasW + (ccx + lx);
                            albedo[gi] = GoldLamp;
                            emission[gi] = GoldLamp;
                        }
            }
        }

        private static bool IsCornerRivet(int lx, int ly) => NearCorner(lx, ly, inset: 14, r: 5);

        private static bool NearCorner(int lx, int ly, int inset, int r)
        {
            int[] c = { inset, CellPx - inset };
            foreach (int cx in c)
                foreach (int cy in c)
                {
                    int dx = lx - cx, dy = ly - cy;
                    if (dx * dx + dy * dy <= r * r) return true;
                }
            return false;
        }

        private static bool IsCornerMark(int lx, int ly)
        {
            const int inset = 8, size = 12;
            int[] starts = { inset, CellPx - inset - size };
            foreach (int cx in starts)
                foreach (int cy in starts)
                    if (lx >= cx && lx < cx + size && ly >= cy && ly < cy + size) return true;
            return false;
        }

        private static bool IsTreadBump(int lx, int ly)
        {
            const int cell = 16;
            int mx = lx % cell - cell / 2, my = ly % cell - cell / 2;
            return Mathf.Abs(mx) + Mathf.Abs(my) < cell / 4;
        }

        private static bool IsRingRivet(int lx, int ly)
        {
            const float ringR = 48f, dotR = 4f;
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float dx = lx - (CellPx / 2f + Mathf.Cos(a) * ringR);
                float dy = ly - (CellPx / 2f + Mathf.Sin(a) * ringR);
                if (dx * dx + dy * dy <= dotR * dotR) return true;
            }
            return false;
        }

        /// <summary>Stable, cross-platform string hash (FNV-1a) — same algorithm as
        /// <c>ReefFloorDetail.StableHash</c>/<c>ReefKit.StableHash</c>, its own small copy rather than a
        /// cross-assembly dependency for one pure function (<c>MaxWorlds.Rendering.asmdef</c> references
        /// only Core and the render pipeline packages, never the Arena/Gameplay assembly those live in).</summary>
        private static uint StableHash(string s)
        {
            uint hash = 2166136261u;
            if (s != null)
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            return hash;
        }

        private static float Hash01(string key) => (StableHash(key) % 100000u) / 100000f;

        // ---------------------------------------------------------------- cover pieces

        /// <summary><c>dropship</c> cover (ticket: hull 10 x 4 m at 1.6 m, wings 4 x 1.4 m at 0.8 m).
        /// The wings deliberately stand outside the hull's own Z footprint — the same "a deliberate new
        /// form, not the AC1 fit regression" exemption <see cref="CityKit.BuildTruck"/>'s Tail Lift
        /// already uses, excluded from a fit check by name, not by being inside the collider.</summary>
        public static GameObject BuildDropship(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Dropship");
            BuildSplitBody(root, "Hull", center, colliderSize, SurfaceKind.Metal);

            var wingSize = new Vector3(4f, 0.8f, 1.4f);
            float wingReach = colliderSize.z * 0.5f + wingSize.z * 0.5f - 0.05f;
            foreach (float sz in new[] { -1f, 1f })
                StormdrainKit.Box(root, sz < 0 ? "Wing A" : "Wing B",
                    center + new Vector3(0f, wingSize.y * 0.5f, sz * wingReach), wingSize, Teal, SurfaceKind.Metal);

            AddGoldLamp(root, "Nav Lamp", center + new Vector3(colliderSize.x * 0.46f, colliderSize.y * 0.6f, 0f), Vector3.right);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>drop_pod</c> cover (ticket: 1.4 m square, 1.2 m tall).</summary>
        public static GameObject BuildDropPod(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Drop Pod");
            BuildSplitBody(root, "Body", center, colliderSize, SurfaceKind.Metal);
            AddGoldLamp(root, "Hatch Lamp", center + new Vector3(0f, colliderSize.y * 0.72f, colliderSize.z * 0.46f), Vector3.forward);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>crate</c>/<c>crate_low</c> cover (ticket: full 3 x 3 m at 1.6 m, low at 1.0 m) —
        /// one builder for both: the only difference is the authored <paramref name="colliderSize"/>'s
        /// own height.</summary>
        public static GameObject BuildCrate(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Crate");
            BuildSplitBody(root, "Body", center, colliderSize, SurfaceKind.Metal);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>glass_case</c> cover (ticket: 2.2 m square, 1.6 m, glass — blocks walking, shots
        /// pass, as World 2's pipe barrier — with a plinth inside). <see cref="CoverKind"/> (see-through)
        /// is authored on the map entity, not decided here — this only builds the look.</summary>
        public static GameObject BuildGlassCase(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Glass Case");
            SolidBody(root, "Pane", center, colliderSize, GlassPane, SurfaceKind.Metal);

            var plinthSize = new Vector3(colliderSize.x * 0.5f, colliderSize.y * 0.3f, colliderSize.z * 0.5f);
            StormdrainKit.Box(root, "Plinth", center + Vector3.up * (plinthSize.y * 0.5f), plinthSize, NearBlack, SurfaceKind.Metal);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>conveyor</c> cover (ticket: 0.6 m tall, 3 m deep, blocks walking, shots pass
        /// over, rollers animated slowly) — a near-black belt body carrying a row of rust-orange rollers,
        /// each spun by <see cref="ShipConveyorRoller"/>.</summary>
        public static GameObject BuildConveyor(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Conveyor");
            SolidBody(root, "Belt", center, colliderSize, NearBlack, SurfaceKind.Metal);

            float beltTop = colliderSize.y * 0.94f;
            float rollerR = beltTop * 0.3f;
            int count = Mathf.Max(2, Mathf.FloorToInt(colliderSize.z / 0.5f));
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count - 0.5f;
                Vector3 pos = center + new Vector3(0f, beltTop * 0.5f, t * colliderSize.z * 0.9f);
                GameObject roller = StormdrainKit.Tube(root, $"Roller{i}", pos, rollerR, colliderSize.x * 0.92f,
                    Quaternion.Euler(0f, 0f, 90f), RustOrange);
                roller.AddComponent<ShipConveyorRoller>();
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>console_bank</c> cover (ticket: 1.0 m tall) — a split-panel desk with a row of
        /// gold indicator lamps along its front face.</summary>
        public static GameObject BuildConsoleBank(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Console Bank");
            BuildSplitBody(root, "Body", center, colliderSize, SurfaceKind.Metal);

            int lights = Mathf.Max(1, Mathf.FloorToInt(colliderSize.x / 0.4f));
            for (int i = 0; i < lights; i++)
            {
                float t = (i + 0.5f) / lights - 0.5f;
                Vector3 pos = center + new Vector3(t * colliderSize.x * 0.8f, colliderSize.y * 0.8f, colliderSize.z * 0.46f);
                AddGoldLamp(root, $"Indicator{i}", pos, Vector3.forward);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>bulkhead_stub</c> cover (ticket: 1.4 m wide, 1.6 m tall) — a standalone upright
        /// split panel, the free-standing-cover counterpart of <see cref="BuildBulkheadPanel"/>'s own
        /// wall-run dressing.</summary>
        public static GameObject BuildBulkheadStub(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Bulkhead Stub");
            BuildSplitBody(root, "Panel", center, colliderSize, SurfaceKind.Metal);
            AddGoldLamp(root, "Lamp", center + new Vector3(0f, colliderSize.y * 0.82f, colliderSize.z * 0.46f), Vector3.forward);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- hero set pieces & template dressing (MV-1261)

        // Lee, 19 Oct 2026 ("I want interest ... make this incredible"): one signature set piece for
        // every hero area (Hangar Bay a1, Assembly Line a8, Specimen Vault a15, Reactor Ring a22, The
        // Bridge a30) plus a themed dressing pass for every other ("template") area, so no two adjacent
        // areas read alike. A "stencilled" label (the ticket's own "01"/"LINE"/"THE COLLECTION") is
        // built as an abstract painted roundel/plaque, never a literal glyph: this kit has no text-
        // rasterising primitive, every other builder in this file already paints an abstract motif the
        // same way (e.g. BuildPlateMesh's own "corner mark"/"ring rivet" atlas cells), and the ticket's
        // own BUILD MODE: INDICATIVE makes its reference image, not its prose, authoritative on exact
        // appearance. WHERE each piece lands is decided by MaxWorlds.Arena.ShipSetPieces (the WorldArea/
        // CoverPiece/StructuralWall data this Rendering-assembly kit cannot see); every method below only
        // ever takes plain positions/sizes, same contract every other builder in this file keeps.

        private static readonly Color HangarFieldTone = Rgb(120, 200, 235);
        private static readonly Color StarTone = Rgb(235, 240, 250);
        private static readonly Color AmberWork = Rgb(230, 160, 40);
        private static readonly Color WeldSpark = Rgb(255, 235, 180);
        private static readonly Color TrophyBronze = Rgb(196, 150, 80);
        private static readonly Color VentChannelOrange = Rgb(255, 120, 40);
        private static readonly Color CarpetRed = Rgb(90, 20, 24);
        private static readonly Color HoloTeal = Rgb(90, 220, 215);

        /// <summary>Hangar Bay (a1, ticket Change item 1): the north wall's own hangar mouth — a
        /// shimmering force-field pane framed in near-black, a scatter of stars and a glimpse of the
        /// planet Sotami beyond. <paramref name="a"/>/<paramref name="b"/> are the opening's own
        /// endpoints in world XZ, <paramref name="inward"/> points into the room (same convention as
        /// <see cref="BuildBulkheadPanel"/>).</summary>
        public static GameObject BuildHangarMouth(Transform parent, Vector2 a, Vector2 b, Vector3 inward, float wallHeight)
        {
            Transform root = NewRoot(parent, "Hangar Mouth");
            Vector2 mid = (a + b) * 0.5f;
            float w = Mathf.Max(0.5f, (b - a).magnitude);
            Vector2 dir = (b - a).normalized;
            Vector3 n = new Vector3(inward.x, 0f, inward.z);
            n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
            Vector3 dir3 = new Vector3(dir.x, 0f, dir.y);

            MeshBox(root, "Mouth Frame", new Vector3(mid.x, wallHeight * 0.5f, mid.y) - n * 0.02f,
                new Vector3(w, wallHeight, 0.1f).Oriented(dir), NearBlack);
            EmissiveBox(root, "Force Field", new Vector3(mid.x, wallHeight * 0.52f, mid.y) + n * 0.04f,
                new Vector3(w * 0.94f, wallHeight * 0.9f, 0.02f).Oriented(dir), HangarFieldTone);

            for (int i = 0; i < 5; i++)
            {
                float along = Hash01($"hangarstar_{i}") - 0.5f;
                float up = Hash01($"hangarstar_h_{i}");
                Vector3 starPos = new Vector3(mid.x, wallHeight * (0.55f + up * 0.5f), mid.y)
                    + n * (0.3f + up * 0.2f) + dir3 * (along * w * 0.85f);
                EmissiveBox(root, $"Star {i}", starPos, new Vector3(0.06f, 0.06f, 0.015f), StarTone);
            }

            EmissiveBox(root, "Sotami Glimpse", new Vector3(mid.x, wallHeight * 0.8f, mid.y) + n * 0.5f,
                new Vector3(w * 0.2f, w * 0.2f, 0.02f).Oriented(dir), SotamiTone);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Hangar Bay (a1, ticket Change item 1): the painted launch ring on the deck — a gold
        /// outer ring, a cyan inner ring, and a bright roundel at the centre standing in for the
        /// ticket's own stencilled "01" (see this section's own header on why).
        ///
        /// MV-1274 ("Neon... on: the launch-ring circles"): the rings themselves move from a LIT
        /// <see cref="MeshPart"/> (reads the deck's own dim ambient like any other metal surface) to an
        /// HDR-boosted unlit ring, via <see cref="MeshPartGlow"/> — so they clip
        /// <see cref="BackyardLook.BloomThreshold"/> and read as neon rather than painted metal.</summary>
        public static GameObject BuildLaunchRingMarking(Transform parent, Vector3 groundCenter, float radius)
        {
            Transform root = NewRoot(parent, "Launch Ring");
            Mesh outer = CharacterMeshes.Ring(radius * 0.92f, radius, 0.03f, 32);
            Mesh inner = CharacterMeshes.Ring(radius * 0.74f, radius * 0.82f, 0.03f, 32);

            TagDressing(MeshPartGlow(root, "Ring Outer Gold", outer, groundCenter + Vector3.up * 0.02f, Hdr(GoldLamp, 5.0f)));
            TagDressing(MeshPartGlow(root, "Ring Inner Cyan", inner, groundCenter + Vector3.up * 0.021f, Hdr(HangarFieldTone, 5.0f)));
            TagDressing(EmissiveBox(root, "Mark Roundel", groundCenter + Vector3.up * 0.022f,
                new Vector3(radius * 0.5f, 0.01f, radius * 0.5f), Hdr(GoldLamp, 6.0f)));

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A collider-free unlit part in an HDR tone — the <see cref="MeshPart"/> counterpart
        /// for a lathed/ring mesh that needs to glow rather than read the deck's own lit ambient.</summary>
        private static GameObject MeshPartGlow(Transform parent, string name, Mesh mesh, Vector3 pos, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = StormdrainKit.Unlit(tone, name);
            return go;
        }

        /// <summary>Assembly Line (a8, ticket Change item 2): amber edge-light strips flanking the
        /// conveyor's own run, 4 slow-riding part silhouettes along its top, overhead-rail shadow stripes
        /// across the floor, and yellow lane lines either side of the belt (the ticket's own "LINE"
        /// stencil — see this section's header — is these lane lines, not a separate glyph).
        /// <paramref name="conveyorGroundCenter"/>/<paramref name="conveyorSize"/> are the authored
        /// <c>ship_conveyor</c> cover's own ground-level centre/size.</summary>
        public static GameObject BuildAssemblyLineHero(Transform parent, Vector3 conveyorGroundCenter, Vector3 conveyorSize, Rect areaFootprint)
        {
            Transform root = NewRoot(parent, "Assembly Line Hero");
            float beltTop = conveyorSize.y * 0.94f;
            bool alongX = conveyorSize.x >= conveyorSize.z;

            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 edgePos = conveyorGroundCenter + new Vector3(0f, beltTop, 0f) +
                    (alongX ? new Vector3(0f, 0f, side * conveyorSize.z * 0.52f) : new Vector3(side * conveyorSize.x * 0.52f, 0f, 0f));
                Vector3 edgeSize = alongX
                    ? new Vector3(conveyorSize.x * 0.96f, 0.03f, 0.04f)
                    : new Vector3(0.04f, 0.03f, conveyorSize.z * 0.96f);
                EmissiveBox(root, $"Edge Light {(side < 0 ? "A" : "B")}", edgePos, edgeSize, AmberWork);
            }

            const int parts = 4;
            for (int i = 0; i < parts; i++)
            {
                float t = (i + 0.5f) / parts - 0.5f;
                Vector3 along = alongX ? new Vector3(t * conveyorSize.x * 0.8f, 0f, 0f) : new Vector3(0f, 0f, t * conveyorSize.z * 0.8f);
                Vector3 partPos = conveyorGroundCenter + Vector3.up * (beltTop + 0.1f) + along;
                Mesh part = CharacterMeshes.Prism(5, 0.16f, 0.12f, 0.22f, 0.05f);
                MeshPart(root, $"Riding Part {i}", part, partPos, i % 2 == 0 ? RustOrange : Teal);
            }

            for (int i = 0; i < 3; i++)
            {
                float t = (i + 1f) / 4f;
                // MV-1272: 0.01 put this at the same height as the Mothership's own floor-atlas seam
                // grid (ShipKit.BuildSeams' SeamY/GlowY, 0.0112-0.0162) — a different, unrelated kit
                // pass that also runs across every area including this one's, so a shadow stripe and a
                // seam line could land coplanar and overlapping right across an area's own middle.
                // +0.01 clears that band with margin (MV-1272's own EditMode test, which scans every
                // pair of ship-dressing renderers for exactly this).
                Vector3 stripePos = alongX
                    ? new Vector3(Mathf.Lerp(areaFootprint.xMin, areaFootprint.xMax, t), 0.02f, areaFootprint.center.y)
                    : new Vector3(areaFootprint.center.x, 0.02f, Mathf.Lerp(areaFootprint.yMin, areaFootprint.yMax, t));
                Vector3 stripeSize = alongX
                    ? new Vector3(0.6f, 0.004f, areaFootprint.height * 0.9f)
                    : new Vector3(areaFootprint.width * 0.9f, 0.004f, 0.6f);
                MeshBox(root, $"Overhead Shadow {i}", stripePos, stripeSize, NearBlack);
            }

            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 lanePos = conveyorGroundCenter + Vector3.up * 0.015f +
                    (alongX ? new Vector3(0f, 0f, side * conveyorSize.z * 0.7f) : new Vector3(side * conveyorSize.x * 0.7f, 0f, 0f));
                Vector3 laneSize = alongX
                    ? new Vector3(conveyorSize.x * 1.1f, 0.006f, 0.12f)
                    : new Vector3(0.12f, 0.006f, conveyorSize.z * 1.1f);
                MeshBox(root, $"Lane Line {(side < 0 ? "A" : "B")}", lanePos, laneSize, HazardYellow);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Assembly Line (a8, ticket Change item 2): welding sparks and light at one assembly
        /// cradle — a bright spark glint plus a dim additive floor pool.</summary>
        public static GameObject BuildCradleWeldSpark(Transform parent, Vector3 cradleGroundCenter)
        {
            Transform root = NewRoot(parent, "Cradle Weld Spark");
            EmissiveBox(root, "Spark", cradleGroundCenter + Vector3.up * 1.1f, new Vector3(0.1f, 0.14f, 0.02f), WeldSpark);
            CityKit.BuildPool(root, cradleGroundCenter, 0.9f, 0.9f, AmberWork, 0.5f);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Specimen Vault (a15, ticket Change item 3): the two inlaid floor rings around the
        /// room's own centre, plus the empty centre case's own slowly-spinning floating gold ring.</summary>
        public static GameObject BuildVaultFloorRings(Transform parent, Vector3 groundCenter, float radius)
        {
            Transform root = NewRoot(parent, "Vault Floor Rings");

            Mesh outer = CharacterMeshes.Ring(radius * 0.9f, radius, 0.03f, 36);
            Mesh inner = CharacterMeshes.Ring(radius * 0.55f, radius * 0.62f, 0.03f, 36);
            MeshPart(root, "Inlaid Ring Outer", outer, groundCenter + Vector3.up * 0.02f, GoldLamp);
            MeshPart(root, "Inlaid Ring Inner", inner, groundCenter + Vector3.up * 0.021f, GoldLamp);

            // "Floating Gold Ring" is left for the caller (MaxWorlds.Arena.ShipSetPieces) to find by
            // name and spin — the Rendering assembly has no reference to MaxWorlds.VFX's motion
            // components, same "Rendering builds it, Arena finds it by name and animates it" split
            // ReefKit.BuildReactorCore's own doc comment documents for its own spinning rings.
            Mesh floatingRing = CharacterMeshes.Ring(0.32f, 0.4f, 0.05f, 24);
            MeshPart(root, "Floating Gold Ring", floatingRing, groundCenter + Vector3.up * 1.4f, GoldLamp);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Specimen Vault (a15, ticket Change item 3): one glass case's own warm spotlight pool
        /// and a small trophy standing on its plinth — <paramref name="trophyIndex"/> just varies the
        /// silhouette (the ticket's own 8-trophy list) so the 4 authored cases don't read identically.
        /// <paramref name="caseCenter"/> is the case cover's own mid-height centre (its authored
        /// convention, same as every other ShipKit caller that reads a cover piece's <c>Cover.Center</c>
        /// directly).</summary>
        public static GameObject BuildVaultTrophyCase(Transform parent, Vector3 caseCenter, Vector3 caseSize, int trophyIndex)
        {
            Transform root = NewRoot(parent, "Vault Trophy");
            CityKit.BuildPool(root, new Vector3(caseCenter.x, 0f, caseCenter.z), caseSize.x * 0.9f, caseSize.z * 0.9f, GoldLamp, 0.5f);

            Vector3 plinthTop = caseCenter + Vector3.up * (caseSize.y * 0.3f + 0.02f);
            bool tall = trophyIndex % 2 == 0;
            Mesh trophy = tall
                ? CharacterMeshes.Prism(6, 0.14f, 0.05f, 0.4f, 0.05f)
                : CharacterMeshes.Sphere(12);
            GameObject go = MeshPart(root, "Trophy", trophy, plinthTop + Vector3.up * (tall ? 0.2f : 0.14f), TrophyBronze);
            if (!tall) go.transform.localScale = Vector3.one * 0.28f;

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Reactor Ring (a22, ticket Change item 4): the reactor core itself — a dark housing, a
        /// cyan-glowing core, 8 radial light slits on its lid, a cyan outer ring line on the floor, and 3
        /// heavy cables snaking away from its base.</summary>
        public static GameObject BuildShipReactorCore(Transform parent, Vector3 groundCenter, float radius, float height)
        {
            Transform root = NewRoot(parent, "Reactor Core");
            Mesh housing = CharacterMeshes.Prism(10, radius, radius * 0.88f, height, 0.15f);
            MeshPart(root, "Housing", housing, groundCenter + Vector3.up * (height * 0.5f), NearBlack);

            Mesh core = CharacterMeshes.Beam(height * 0.8f, radius * 0.45f, radius * 0.45f, 16);
            MeshPart(root, "Core Glow", core, groundCenter + Vector3.up * (height * 0.5f), HangarFieldTone);

            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Mesh slit = CharacterMeshes.Beam(radius * 0.7f, 0.025f, 0.025f, 6);
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
                Vector3 pos = groundCenter + Vector3.up * (height * 1.01f) + dir * (radius * 0.35f);
                MeshPart(root, $"Lid Slit {i}", slit, pos, HangarFieldTone, rot);
            }

            Mesh ring = CharacterMeshes.Ring(radius * 1.3f, radius * 1.38f, 0.04f, 32);
            MeshPart(root, "Outer Ring Line", ring, groundCenter + Vector3.up * 0.015f, HangarFieldTone);

            for (int i = 0; i < 3; i++)
            {
                float a = (i + 0.5f) / 3f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Mesh cable = CharacterMeshes.Beam(radius * 1.1f, 0.06f, 0.06f, 8);
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
                Vector3 pos = groundCenter + dir * (radius * 0.9f) + Vector3.up * 0.04f;
                MeshPart(root, $"Cable {i}", cable, pos, SteelPipe, rot);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Reactor Ring (a22, ticket Change item 4): one glowing orange channel running from the
        /// core to an authored plasma vent — a flat emissive beam, the cheapest "the reactor feeds this
        /// vent" readable line. Null when <paramref name="ventGroundCenter"/> sits on top of
        /// <paramref name="coreGroundCenter"/> (nothing to draw).</summary>
        public static GameObject BuildReactorVentChannel(Transform parent, Vector3 coreGroundCenter, Vector3 ventGroundCenter)
        {
            Vector3 delta = ventGroundCenter - coreGroundCenter;
            float length = delta.magnitude;
            if (length < 0.05f) return null;

            Transform root = NewRoot(parent, "Reactor Vent Channel");
            Vector3 mid = (coreGroundCenter + ventGroundCenter) * 0.5f;
            Vector2 dir2 = new Vector2(delta.x, delta.z).normalized;
            MeshBox(root, "Channel", mid + Vector3.up * 0.012f, new Vector3(length, 0.01f, 0.18f).Oriented(dir2), VentChannelOrange);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>The Bridge (a30, ticket Change item 5): the full-width viewport onto Sotami behind
        /// the throne, a glowing teal holo star-map table with a small spinning planet hologram, and a
        /// dark red carpet stripe up the middle flanked by gold lane lines. <paramref name="viewportA"/>/
        /// <paramref name="viewportB"/> are the throne wall's own endpoints in world XZ, same convention
        /// as <see cref="BuildHangarMouth"/>.</summary>
        public static GameObject BuildBridgeHero(Transform parent, Vector2 viewportA, Vector2 viewportB,
            Vector3 inward, float wallHeight, Vector3 tableGroundCenter, Rect areaFootprint)
        {
            Transform root = NewRoot(parent, "Bridge Hero");
            Vector2 mid = (viewportA + viewportB) * 0.5f;
            float w = Mathf.Max(0.5f, (viewportB - viewportA).magnitude);
            Vector2 dir = (viewportB - viewportA).normalized;
            Vector3 n = new Vector3(inward.x, 0f, inward.z);
            n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;

            MeshBox(root, "Viewport Frame", new Vector3(mid.x, wallHeight * 0.55f, mid.y) - n * 0.02f,
                new Vector3(w, wallHeight * 0.8f, 0.08f).Oriented(dir), NearBlack);
            EmissiveBox(root, "Viewport Pane", new Vector3(mid.x, wallHeight * 0.56f, mid.y) + n * 0.04f,
                new Vector3(w * 0.95f, wallHeight * 0.72f, 0.02f).Oriented(dir), SotamiTone);

            Mesh tableBase = CharacterMeshes.Ring(0f, 1.1f, 0.06f, 24);
            MeshPart(root, "Holo Table Base", tableBase, tableGroundCenter + Vector3.up * 0.9f, NearBlack);
            EmissiveBox(root, "Holo Glow", tableGroundCenter + Vector3.up * 0.95f, new Vector3(1.8f, 0.02f, 1.8f), HoloTeal);
            // "Planet Hologram" is left for the caller (MaxWorlds.Arena.ShipSetPieces) to find by name
            // and spin — same Rendering/VFX split as "Floating Gold Ring" above.
            GameObject planet = MeshPart(root, "Planet Hologram", CharacterMeshes.Sphere(16), tableGroundCenter + Vector3.up * 1.35f, HoloTeal);
            planet.transform.localScale = Vector3.one * 0.5f;

            float carpetLength = areaFootprint.height * 0.9f;
            // MV-1272: +0.01 (was 0.01) clears the floor-atlas seam grid's own Y band — see the matching
            // comment on BuildAssemblyLineHero's "Overhead Shadow" above.
            MeshBox(root, "Carpet Stripe", new Vector3(areaFootprint.center.x, 0.02f, areaFootprint.center.y),
                new Vector3(2.2f, 0.006f, carpetLength), CarpetRed);
            foreach (float side in new[] { -1f, 1f })
                MeshBox(root, $"Carpet Lane {(side < 0 ? "A" : "B")}",
                    new Vector3(areaFootprint.center.x + side * 1.3f, 0.012f, areaFootprint.center.y),
                    new Vector3(0.1f, 0.006f, carpetLength), GoldLamp);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>The Bridge (a30, ticket Change item 5): one console's own small lit-screen accent on
        /// top of an already-built <c>ship_console_bank</c> cover piece.</summary>
        public static GameObject BuildConsoleScreen(Transform parent, Vector3 consoleGroundCenter, Vector3 consoleSize)
        {
            Transform root = NewRoot(parent, "Console Screen");
            EmissiveBox(root, "Screen", consoleGroundCenter + Vector3.up * (consoleSize.y * 0.86f),
                new Vector3(consoleSize.x * 0.5f, consoleSize.y * 0.22f, 0.02f), HoloTeal);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- template area theming (MV-1261)

        /// <summary>One of the ticket's own 6 template-area themes (Change item 6). A cheap, visually
        /// distinct 2-3 element accent dropped at a non-hero area's own centre; the caller
        /// (<see cref="MaxWorlds.Arena.ShipSetPieces"/>) cycles through all 6 by area index, so "no two
        /// consecutive areas share a theme colour" (ticket) holds by construction — two distinct enum
        /// values 1 apart in a 6-long cycle are never equal.</summary>
        public enum TemplateTheme { Spine, CargoBay, Junction, CrewQuarters, PodRacks, VentHall }

        public static GameObject BuildTemplateTheme(Transform parent, TemplateTheme theme, Vector3 groundCenter, Rect areaFootprint)
        {
            Transform root = NewRoot(parent, $"Template Theme {theme}");
            switch (theme)
            {
                case TemplateTheme.Spine: BuildSpineTheme(root, groundCenter, areaFootprint); break;
                case TemplateTheme.CargoBay: BuildCargoBayTheme(root, groundCenter); break;
                case TemplateTheme.Junction: BuildJunctionTheme(root, groundCenter); break;
                case TemplateTheme.CrewQuarters: BuildCrewQuartersTheme(root, groundCenter); break;
                case TemplateTheme.PodRacks: BuildPodRacksTheme(root, groundCenter); break;
                default: BuildVentHallTheme(root, groundCenter); break;
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Spine: hazard-striped bulkhead stubs and lane lines (ticket, Change item 6).</summary>
        private static void BuildSpineTheme(Transform root, Vector3 groundCenter, Rect areaFootprint)
        {
            bool alongX = areaFootprint.width >= areaFootprint.height;
            Vector3 laneSize = alongX
                ? new Vector3(areaFootprint.width * 0.7f, 0.006f, 0.1f)
                : new Vector3(0.1f, 0.006f, areaFootprint.height * 0.7f);
            // MV-1272: +0.01 clears the floor-atlas seam grid's own Y band — see BuildAssemblyLineHero's
            // "Overhead Shadow" comment.
            MeshBox(root, "Lane Line", groundCenter + Vector3.up * 0.02f, laneSize, HazardYellow);
            MeshBox(root, "Bulkhead Stub Accent", groundCenter + Vector3.up * 0.4f, new Vector3(0.7f, 0.8f, 0.2f), NearBlack);
        }

        /// <summary>Cargo Bay: crate stencils and floor numbers (ticket, Change item 6).</summary>
        private static void BuildCargoBayTheme(Transform root, Vector3 groundCenter)
        {
            MeshBox(root, "Crate Stencil", groundCenter + Vector3.up * 0.25f, new Vector3(0.5f, 0.5f, 0.45f), RustOrange);
            // MV-1272: +0.01 clears the floor-atlas seam grid's own Y band — see BuildAssemblyLineHero's
            // "Overhead Shadow" comment.
            EmissiveBox(root, "Floor Number", groundCenter + Vector3.up * 0.021f, new Vector3(0.4f, 0.01f, 0.4f), Teal);
        }

        /// <summary>Junction: a centre ring and directional arrows (ticket, Change item 6) — the 4
        /// arrows are axis-aligned bars (N/E/S/W) rather than an arbitrary rotation, so no custom
        /// transform maths is needed to keep them flat on the floor.</summary>
        private static void BuildJunctionTheme(Transform root, Vector3 groundCenter)
        {
            Mesh ring = CharacterMeshes.Ring(0.8f, 0.95f, 0.04f, 24);
            MeshPart(root, "Centre Ring", ring, groundCenter + Vector3.up * 0.015f, GoldLamp);

            MeshBox(root, "Arrow N", groundCenter + new Vector3(0f, 0.012f, 1.3f), new Vector3(0.18f, 0.03f, 0.6f), GoldLamp);
            MeshBox(root, "Arrow S", groundCenter + new Vector3(0f, 0.012f, -1.3f), new Vector3(0.18f, 0.03f, 0.6f), GoldLamp);
            MeshBox(root, "Arrow E", groundCenter + new Vector3(1.3f, 0.012f, 0f), new Vector3(0.6f, 0.03f, 0.18f), GoldLamp);
            MeshBox(root, "Arrow W", groundCenter + new Vector3(-1.3f, 0.012f, 0f), new Vector3(0.6f, 0.03f, 0.18f), GoldLamp);
        }

        /// <summary>Crew Quarters: bunk lights and lockers along walls (ticket, Change item 6).</summary>
        private static void BuildCrewQuartersTheme(Transform root, Vector3 groundCenter)
        {
            MeshBox(root, "Locker", groundCenter + Vector3.up * 0.7f, new Vector3(0.6f, 1.4f, 0.4f), NearBlack);
            foreach (float side in new[] { -1f, 1f })
                EmissiveBox(root, $"Bunk Light {(side < 0 ? "A" : "B")}", groundCenter + new Vector3(side * 0.9f, 1.3f, 0f),
                    new Vector3(0.18f, 0.04f, 0.04f), Teal);
        }

        /// <summary>Pod Racks: pod status lights (ticket, Change item 6).</summary>
        private static void BuildPodRacksTheme(Transform root, Vector3 groundCenter)
        {
            MeshBox(root, "Pod Accent", groundCenter + Vector3.up * 0.6f, new Vector3(1.2f, 1.2f, 1.2f), RustOrange);
            EmissiveBox(root, "Status Light", groundCenter + Vector3.up * 1.25f, new Vector3(0.12f, 0.12f, 0.02f), GoldLamp);
        }

        /// <summary>Vent Hall: grate glow (ticket, Change item 6).</summary>
        private static void BuildVentHallTheme(Transform root, Vector3 groundCenter)
        {
            Color dim = HangarFieldTone * 0.4f;
            dim.a = 1f;
            // MV-1272: +0.01 clears the floor-atlas seam grid's own Y band — see BuildAssemblyLineHero's
            // "Overhead Shadow" comment.
            EmissiveBox(root, "Grate Glow", groundCenter + Vector3.up * 0.021f, new Vector3(0.9f, 0.01f, 0.9f), HangarFieldTone);
            EmissiveBox(root, "Grate Glow Dim", groundCenter + Vector3.up * 0.022f, new Vector3(1.3f, 0.005f, 1.3f), dim);
        }
    }
}
