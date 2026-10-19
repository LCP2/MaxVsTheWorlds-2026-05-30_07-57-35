using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Arena
{
    /// <summary>Which hero set piece a <see cref="ShipSetPieceRoot"/> built — one per hero area (Lee,
    /// 19 Oct 2026: "I want interest ... make this incredible").</summary>
    public enum ShipSetPieceKind { HangarBay, AssemblyLine, SpecimenVault, ReactorRing, Bridge }

    /// <summary>Tags one hero set-piece root MV-1261 built for a Mothership hero area, so an EditMode
    /// test can resolve "did this hero area land its own landmark" as a RESOLVED value (testing policy
    /// Tier 2) rather than re-deriving it from names/hierarchy shape — same idiom as
    /// <see cref="ReefSetPieceRoot"/>.</summary>
    public sealed class ShipSetPieceRoot : MonoBehaviour
    {
        public ShipSetPieceKind Kind;
        public string AreaId;
        public int AreaIndex;
    }

    /// <summary>
    /// MV-1261 (Lee, 19 Oct 2026: "I want interest ... make this incredible"): one hero set piece for
    /// every Mothership hero area — Hangar Bay a1, Assembly Line a8, Specimen Vault a15, Reactor Ring
    /// a22, The Bridge a30, the same "one representative area per deck" set <see cref="MothershipDecks"/>
    /// and the MV-1249 capture preset already use — plus a themed dressing pass for every other
    /// ("template") area so no two adjacent areas read alike. This class decides WHERE each one lands;
    /// <see cref="ShipKit"/> is WHAT each one looks like — same WHERE/WHAT split
    /// <see cref="ReefSetPieces"/>/<see cref="ReefKit"/> already keep.
    ///
    /// Runs after <see cref="ShipDressing.DressCover"/>/<see cref="ShipAreaKit.BuildWallDressing"/> (needs
    /// their already-built cover geometry to find a conveyor/glass-case/console footprint to dress, and
    /// a resolved wall run for the Bridge's own viewport) — see <see cref="ShipDressing.Dress"/>.
    /// </summary>
    public static class ShipSetPieces
    {
        public static void Build(Transform host, WorldConfig cfg, IReadOnlyList<CoverPiece> cover,
            List<StructuralWall> walls, float wallHeight)
        {
            if (host == null || cfg?.areas == null) return;

            var root = new GameObject("Ship Set Pieces");
            root.transform.SetParent(host, false);
            root.AddComponent<KeepsOwnMaterial>();

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null) continue;

                switch (area.index)
                {
                    case 1: BuildHangarBay(root.transform, area); break;
                    case 8: BuildAssemblyLine(root.transform, area, cover); break;
                    case 15: BuildSpecimenVault(root.transform, area, cover); break;
                    case 22: BuildReactorRing(root.transform, area); break;
                    case 30: BuildBridge(root.transform, area, cover); break;
                    default: BuildTemplateArea(root.transform, area); break;
                }
            }

            StaticBatchingUtility.Combine(root.gameObject);
        }

        private static void Tag(GameObject go, ShipSetPieceKind kind, WorldArea area)
        {
            if (go == null) return;
            var tag = go.AddComponent<ShipSetPieceRoot>();
            tag.Kind = kind;
            tag.AreaId = area.id;
            tag.AreaIndex = area.index;
        }

        private static CoverPiece? FindCover(IReadOnlyList<CoverPiece> cover, WorldArea area, CoverDressing dressing)
        {
            if (cover == null) return null;
            foreach (CoverPiece piece in cover)
            {
                if (piece.Cover.Dressing != dressing) continue;
                if (!area.Footprint.Contains(piece.Cover.CenterXz)) continue;
                return piece;
            }
            return null;
        }

        private static List<CoverPiece> FindAllCover(IReadOnlyList<CoverPiece> cover, WorldArea area, CoverDressing dressing)
        {
            var found = new List<CoverPiece>();
            if (cover == null) return found;
            foreach (CoverPiece piece in cover)
            {
                if (piece.Cover.Dressing != dressing) continue;
                if (!area.Footprint.Contains(piece.Cover.CenterXz)) continue;
                found.Add(piece);
            }
            return found;
        }

        // ---- Hangar Bay (a1): the north wall's hangar mouth + the deck's own painted launch ring ------

        private static void BuildHangarBay(Transform host, WorldArea area)
        {
            var group = new GameObject("Hangar Bay Hero");
            group.transform.SetParent(host, false);

            float mouthXMin = Mathf.Max(area.XMin, 14f);
            float mouthXMax = Mathf.Min(area.XMax, 36f);
            if (mouthXMax > mouthXMin)
            {
                var a = new Vector2(mouthXMin, area.WallCoord(Wall.N));
                var b = new Vector2(mouthXMax, area.WallCoord(Wall.N));
                GameObject mouth = ShipKit.BuildHangarMouth(group.transform, a, b, Vector3.back, MapData.DefaultWallHeight);
                Tag(mouth, ShipSetPieceKind.HangarBay, area);
            }

            Vector2 ringCenter = new Vector2(Mathf.Clamp(area.XMin + 20f, area.XMin, area.XMax), area.ZMin + 22f);
            GameObject ring = ShipKit.BuildLaunchRingMarking(group.transform, new Vector3(ringCenter.x, 0f, ringCenter.y), 6f);
            Tag(ring, ShipSetPieceKind.HangarBay, area);
        }

        // ---- Assembly Line (a8): the hero conveyor dressing + a weld spark at every cradle -------------

        private static void BuildAssemblyLine(Transform host, WorldArea area, IReadOnlyList<CoverPiece> cover)
        {
            var group = new GameObject("Assembly Line Hero");
            group.transform.SetParent(host, false);

            CoverPiece? conveyor = FindCover(cover, area, CoverDressing.ShipConveyor);
            Vector2 cXz = conveyor.HasValue ? conveyor.Value.Cover.CenterXz : area.CenterXz;
            Vector3 conveyorGround = new Vector3(cXz.x, 0f, cXz.y);
            Vector3 conveyorSize = conveyor.HasValue ? conveyor.Value.Cover.Size : new Vector3(10f, 0.6f, 2f);

            GameObject hero = ShipKit.BuildAssemblyLineHero(group.transform, conveyorGround, conveyorSize, area.Footprint);
            Tag(hero, ShipSetPieceKind.AssemblyLine, area);

            foreach (WorldCradle cradle in area.cradles ?? System.Array.Empty<WorldCradle>())
            {
                if (cradle == null) continue;
                GameObject spark = ShipKit.BuildCradleWeldSpark(group.transform, new Vector3(cradle.x, 0f, cradle.z));
                Tag(spark, ShipSetPieceKind.AssemblyLine, area);
            }
        }

        // ---- Specimen Vault (a15): inlaid floor rings + a trophy and light pool per authored case ------

        private static void BuildSpecimenVault(Transform host, WorldArea area, IReadOnlyList<CoverPiece> cover)
        {
            var group = new GameObject("Specimen Vault Hero");
            group.transform.SetParent(host, false);

            Vector2 c = area.CenterXz;
            float radius = Mathf.Min(area.size?.w ?? 10f, area.size?.d ?? 10f) * 0.28f;
            GameObject rings = ShipKit.BuildVaultFloorRings(group.transform, new Vector3(c.x, 0f, c.y), radius);
            Tag(rings, ShipSetPieceKind.SpecimenVault, area);

            // ShipKit (Rendering) builds "Floating Gold Ring" but cannot spin it itself — the Rendering
            // assembly has no reference to MaxWorlds.VFX's motion components (same split ReefSetPieces
            // already keeps for ReefKit.BuildReactorCore's own rings) — so this Arena-assembly caller
            // finds it by name and attaches the spin.
            Transform floatingRing = rings.transform.Find("Floating Gold Ring");
            if (floatingRing != null) ReefSetPieceSpin.Attach(floatingRing, 18f);

            List<CoverPiece> cases = FindAllCover(cover, area, CoverDressing.ShipGlassCase);
            for (int i = 0; i < cases.Count; i++)
            {
                CoverPiece piece = cases[i];
                GameObject trophy = ShipKit.BuildVaultTrophyCase(group.transform, piece.Cover.Center, piece.Cover.Size, i);
                Tag(trophy, ShipSetPieceKind.SpecimenVault, area);
            }
        }

        // ---- Reactor Ring (a22): the core + one glowing channel per authored plasma vent ---------------

        private static void BuildReactorRing(Transform host, WorldArea area)
        {
            var group = new GameObject("Reactor Ring Hero");
            group.transform.SetParent(host, false);

            Vector2 c = area.CenterXz;
            Vector3 coreGround = new Vector3(c.x, 0f, c.y);
            float radius = Mathf.Min(area.size?.w ?? 12f, area.size?.d ?? 12f) * 0.18f;
            GameObject core = ShipKit.BuildShipReactorCore(group.transform, coreGround, radius, 2.2f);
            Tag(core, ShipSetPieceKind.ReactorRing, area);

            foreach (WorldVent vent in area.vents ?? System.Array.Empty<WorldVent>())
            {
                if (vent == null) continue;
                Vector2 ventCentre = area.WorldRectOf(vent.x, vent.z, vent.w, vent.d).center;
                GameObject channel = ShipKit.BuildReactorVentChannel(group.transform, coreGround, new Vector3(ventCentre.x, 0f, ventCentre.y));
                if (channel != null) Tag(channel, ShipSetPieceKind.ReactorRing, area);
            }
        }

        // ---- The Bridge (a30): the throne-wall viewport + holo table + a screen per console bank -------

        private static void BuildBridge(Transform host, WorldArea area, IReadOnlyList<CoverPiece> cover)
        {
            var group = new GameObject("Bridge Hero");
            group.transform.SetParent(host, false);

            Span span = area.WallSpan(Wall.N);
            var a = new Vector2(span.Min, area.WallCoord(Wall.N));
            var b = new Vector2(span.Max, area.WallCoord(Wall.N));
            Vector2 c = area.CenterXz;

            GameObject hero = ShipKit.BuildBridgeHero(group.transform, a, b, Vector3.back,
                MapData.DefaultWallHeight, new Vector3(c.x, 0f, c.y), area.Footprint);
            Tag(hero, ShipSetPieceKind.Bridge, area);

            Transform planet = hero.transform.Find("Planet Hologram");
            if (planet != null) ReefSetPieceSpin.Attach(planet, 12f);

            foreach (CoverPiece console in FindAllCover(cover, area, CoverDressing.ShipConsoleBank))
            {
                Vector2 cc = console.Cover.CenterXz;
                GameObject screen = ShipKit.BuildConsoleScreen(group.transform, new Vector3(cc.x, 0f, cc.y), console.Cover.Size);
                Tag(screen, ShipSetPieceKind.Bridge, area);
            }
        }

        // ---- Every other area: one of the ticket's own 6 template themes, cycled by area index ---------

        /// <summary>The ticket's own 6 template themes cycled by area index (Change item 6) — two areas
        /// 1 apart in index are never the same theme (a 6-long cycle), which is the ticket's own "no two
        /// consecutive areas share a theme colour" by construction.</summary>
        private static void BuildTemplateArea(Transform host, WorldArea area)
        {
            int themeIndex = ((area.index - 1) % 6 + 6) % 6;
            var theme = (ShipKit.TemplateTheme)themeIndex;
            Vector2 c = area.CenterXz;
            ShipKit.BuildTemplateTheme(host, theme, new Vector3(c.x, 0f, c.y), area.Footprint);
        }
    }
}
