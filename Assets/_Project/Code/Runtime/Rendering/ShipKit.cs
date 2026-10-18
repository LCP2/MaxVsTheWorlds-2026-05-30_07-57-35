using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
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
        private static readonly Color DeckSeam = Rgb(40, 45, 54);
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
        /// "red is reserved for cradles in this world"); vent orange ~27&#176; (the best in-repo match,
        /// <c>MaxWorlds.VFX.FactoryLife</c>'s own vent tint #FF8C2E — this one is NOT a named "reserved"
        /// constant anywhere, so treat the band as a best-effort placeholder until the design workbook's
        /// own value is confirmed); Light Missile violet ~275&#176; (<c>LightMissileBall.HaloColor</c>,
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

        /// <summary>Bulkhead wall dressing (ticket, change item 2): plating with a hard rust/teal split
        /// line, near-black ribs every ~2 m, a small gold lamp every 6 m. A reusable per-segment
        /// builder, same "axis-aligned run in XZ" assumption <see cref="CityKit.BuildShopfrontRun"/>
        /// already makes — ready for the Hangar Bay layout ticket (MV-1232) to call once real wall
        /// segments exist; this ticket's own one-area placeholder has none worth dressing yet.</summary>
        public static GameObject BuildBulkheadPanel(Transform parent, Vector2 a, Vector2 b, Vector3 inward, float wallHeight)
        {
            Transform root = NewRoot(parent, "Bulkhead Panel");
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) { StormdrainKit.Strip(root.gameObject); return root.gameObject; }

            Vector2 dir = d / length;
            Vector3 n = new Vector3(inward.x, 0f, inward.z);
            n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
            float h = Mathf.Max(0.1f, wallHeight);
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);

            float lowerH = h * 0.5f;
            StormdrainKit.Box(root, "Lower Plate", mid + n * 0.06f + Vector3.up * (lowerH * 0.5f),
                new Vector3(length, lowerH, 0.12f).Oriented(dir), RustOrange, SurfaceKind.Metal);
            StormdrainKit.Box(root, "Upper Plate", mid + n * 0.06f + Vector3.up * (lowerH + (h - lowerH) * 0.5f),
                new Vector3(length, h - lowerH, 0.12f).Oriented(dir), Teal, SurfaceKind.Metal);
            StormdrainKit.Box(root, "Seam Rib", mid + n * 0.07f + Vector3.up * lowerH,
                new Vector3(length, h * 0.04f, 0.14f).Oriented(dir), NearBlack, SurfaceKind.Metal);

            int ribCount = Mathf.Max(1, Mathf.FloorToInt(length / 2f));
            for (int i = 0; i <= ribCount; i++)
            {
                float t = (float)i / ribCount;
                Vector2 at = Vector2.Lerp(a, b, t);
                StormdrainKit.Box(root, $"Rib{i}", new Vector3(at.x, 0f, at.y) + n * 0.065f + Vector3.up * (h * 0.5f),
                    new Vector3(0.08f, h * 0.98f, 0.1f).Oriented(dir), NearBlack, SurfaceKind.Metal);
            }

            int lampCount = Mathf.Max(1, Mathf.FloorToInt(length / 6f));
            for (int i = 0; i < lampCount; i++)
            {
                float t = (i + 0.5f) / lampCount;
                Vector2 at = Vector2.Lerp(a, b, t);
                Vector3 lampPos = new Vector3(at.x, h * 0.78f, at.y) + n * 0.09f;
                AddGoldLamp(root, $"Lamp{i}", lampPos, n);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Deck floor (ticket, change item 2): dark blue-grey plates in 4 m panels with faint
        /// seams, yellow hazard stripes along the north/south edges for launch markings. Flat and
        /// walkable — same reusable, layout-agnostic contract as <see cref="BuildBulkheadPanel"/>.</summary>
        public static GameObject BuildDeckFloor(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Deck Floor");
            StormdrainKit.Box(root, "Slab", new Vector3(area.center.x, 0.006f, area.center.y),
                new Vector3(area.width, 0.012f, area.height), DeckPlate, SurfaceKind.Metal);

            bool alongX = area.width >= area.height;
            float longExtent = alongX ? area.width : area.height;
            int seams = Mathf.Max(0, Mathf.FloorToInt(longExtent / 4f));
            for (int i = 1; i <= seams; i++)
            {
                float t = i * 4f;
                if (t >= longExtent) break;
                Vector3 pos = alongX
                    ? new Vector3(area.xMin + t, 0.014f, area.center.y)
                    : new Vector3(area.center.x, 0.014f, area.yMin + t);
                Vector3 size = alongX
                    ? new Vector3(0.03f, 0.004f, area.height)
                    : new Vector3(area.width, 0.004f, 0.03f);
                StormdrainKit.Box(root, $"Seam{i}", pos, size, DeckSeam, SurfaceKind.Metal);
            }

            const float stripe = 0.3f;
            StormdrainKit.Box(root, "Hazard N", new Vector3(area.center.x, 0.016f, area.yMax - stripe * 0.5f),
                new Vector3(area.width, 0.006f, stripe), HazardYellow, SurfaceKind.Metal);
            StormdrainKit.Box(root, "Hazard S", new Vector3(area.center.x, 0.016f, area.yMin + stripe * 0.5f),
                new Vector3(area.width, 0.006f, stripe), HazardYellow, SurfaceKind.Metal);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

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
    }
}
