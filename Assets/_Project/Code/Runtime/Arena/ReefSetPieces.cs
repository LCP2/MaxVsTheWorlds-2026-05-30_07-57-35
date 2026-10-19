using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Arena
{
    /// <summary>Which hero set piece a <see cref="ReefSetPieceRoot"/> built — one per room type, plus
    /// the a30-only anchor chain (Change item 7), which carries no room type of its own beyond the
    /// cargo-hold landmark it stands alongside.</summary>
    public enum ReefSetPieceKind
    {
        ReactorCore, PropellerShaft, ContainerWall, ObservationDome, VaultDoor, GrowLampCanopy, AnchorChain,
    }

    /// <summary>Tags one hero set-piece root MV-1258 built for a Reef room type, so an EditMode test can
    /// resolve "did every room type land its own landmark" as a RESOLVED value (testing policy Tier 2)
    /// rather than re-deriving it from names or hierarchy shape — same idiom as
    /// <see cref="MaxWorlds.VFX.ReefAmbientEmitter"/>.</summary>
    public sealed class ReefSetPieceRoot : MonoBehaviour
    {
        public ReefSetPieceKind Kind;
        public string AreaId;
        public string RoomType;
    }

    /// <summary>Retracts (hides) a pressure-lock gate's vault-door locking bolts the instant the gate
    /// it's wired to opens, and shows them again if it re-closes — subscribes directly to
    /// <see cref="AreaGate.Opened"/>/<see cref="AreaGate.Closed"/> rather than polling
    /// <see cref="AreaGate.IsOpen"/> every frame (same "cheap by construction" idiom
    /// <see cref="MaxWorlds.VFX.ReefAmbientBurstEmitter"/>'s own doc keeps). Lives in this assembly
    /// (not <c>MaxWorlds.VFX</c>) because it references <see cref="AreaGate"/> directly — the VFX
    /// assembly has no reference back to Arena types (see <c>ReefAmbientLife.cs</c>'s own doc comment).
    /// Visual only, never EditMode-tested (testing policy Tier 3).</summary>
    public sealed class ReefVaultDoorBolts : MonoBehaviour
    {
        private GameObject[] _bolts;
        private AreaGate _gate;

        public static ReefVaultDoorBolts Attach(Transform root, GameObject[] bolts, AreaGate gate)
        {
            if (root == null || gate == null) return null;

            var comp = root.gameObject.AddComponent<ReefVaultDoorBolts>();
            comp._bolts = bolts;
            comp._gate = gate;
            comp.SetBoltsVisible(!gate.IsOpen);
            gate.Opened += comp.Retract;
            gate.Closed += comp.Extend;
            return comp;
        }

        private void Retract() => SetBoltsVisible(false);
        private void Extend() => SetBoltsVisible(true);

        private void SetBoltsVisible(bool visible)
        {
            if (_bolts == null) return;
            foreach (GameObject bolt in _bolts)
                if (bolt != null) bolt.SetActive(visible);
        }

        private void OnDestroy()
        {
            if (_gate == null) return;
            _gate.Opened -= Retract;
            _gate.Closed -= Extend;
        }
    }

    /// <summary>
    /// MV-1258 (Lee, 19 Oct 2026: "I want interest"): one hero set piece per Reef room type, decided by
    /// <see cref="WorldArea.roomType"/> (Lee's 17 Oct table) — a reactor core, a propeller shaft, a
    /// container wall with a crane rail and hook, an observation dome, a vault-door frame and a
    /// grow-lamp canopy. This class decides WHERE each one lands (which area, which already-built cover
    /// piece or wall it stands on or replaces); <see cref="ReefKit"/> is WHAT each one looks like, same
    /// WHERE/WHAT split every other Reef dressing pass above it keeps.
    ///
    /// Runs after <see cref="ReefDressing.DressCover"/> and every <see cref="ReefAreaKit"/> pass (needs
    /// their already-built cover/wall geometry, and the already-built <see cref="AreaGate"/> instances,
    /// to find a footprint to stand on or a gate to dress), and before <see cref="ReefAmbientLife.Build"/>
    /// (which reads back the finished dressing by name) — see <c>BackyardPath.ApplyReefKit</c>.
    /// </summary>
    public static class ReefSetPieces
    {
        private const float CraneRigDefaultSpan = 6f;
        private const float CanopyClearance = 0.15f;
        private const float ReactorRingSpinDegreesPerSecond = 10f;
        private const float PropellerCollarSpinDegreesPerSecond = 40f;
        private const float CraneHookSwayDegrees = 6f;

        /// <summary><see cref="ReefKit.BuildReactorCore"/> builds pure geometry only (the Rendering
        /// assembly has no reference to <see cref="MaxWorlds.VFX"/>'s motion components) — this finds
        /// its "Ring 0"/"Ring 1"/"Ring 2" children by hierarchy and spins them in alternating
        /// directions, the same "Rendering builds it, Gameplay finds it by name and animates it" split
        /// <see cref="ReefAmbientLife"/> already keeps for <see cref="MaxWorlds.VFX.ReefDrumCorePulse"/>.
        /// </summary>
        private static void AttachReactorRingSpin(GameObject root)
        {
            int ringIndex = 0;
            foreach (Transform child in root.transform)
            {
                if (!child.name.StartsWith("Ring ")) continue;
                float direction = ringIndex % 2 == 0 ? 1f : -1f;
                ReefSetPieceSpin.Attach(child, direction * ReactorRingSpinDegreesPerSecond);
                ringIndex++;
            }
        }

        private static void AttachPropellerCollarSpin(GameObject root)
        {
            Transform collar = root.transform.Find("Coupling Collar");
            ReefSetPieceSpin.Attach(collar, PropellerCollarSpinDegreesPerSecond);
        }

        private static void AttachCraneHookSway(GameObject craneRoot, Vector3 seedPosition)
        {
            Transform hookPivot = craneRoot.transform.Find("Hook Pivot");
            int seed = unchecked((int)(seedPosition.x * 1000f + seedPosition.z));
            ReefSetPieceSway.Attach(hookPivot, CraneHookSwayDegrees, seed);
        }

        public static void Build(Transform host, WorldConfig cfg, IReadOnlyList<CoverPiece> cover,
            List<StructuralWall> walls, float wallHeight, float wallThickness)
        {
            if (host == null || cfg?.areas == null) return;

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null || string.IsNullOrEmpty(area.roomType)) continue;

                switch (area.roomType)
                {
                    case "reactor_bay": BuildReactorBay(host, area, cover); break;
                    case "engine_room": BuildEngineRoom(host, area); break;
                    case "cargo_hold": BuildCargoHold(host, area, cover); break;
                    case "gallery": BuildGallery(host, area, walls, wallHeight); break;
                    case "pressure_lock": BuildPressureLock(host, area, cfg); break;
                    case "crew_hydroponics": BuildCrewHydroponics(host, area, cover); break;
                }
            }
        }

        private static void Tag(GameObject root, ReefSetPieceKind kind, WorldArea area)
        {
            if (root == null) return;
            var tag = root.AddComponent<ReefSetPieceRoot>();
            tag.Kind = kind;
            tag.AreaId = area.id;
            tag.RoomType = area.roomType;
        }

        /// <summary>The area's largest hull-fill (<see cref="CoverDressing.ReefBulkhead"/>) piece —
        /// "the area's largest hull-fill mass becomes a[the hero piece]" (ticket, Change items 1/3).
        /// Null if the area authors no hull-fill cover at all.</summary>
        private static CoverPiece? LargestBulkhead(IReadOnlyList<CoverPiece> cover, WorldArea area)
        {
            if (cover == null) return null;

            CoverPiece? best = null;
            float bestFootprint = 0f;
            foreach (CoverPiece piece in cover)
            {
                if (piece.Cover.Dressing != CoverDressing.ReefBulkhead) continue;
                if (!area.Footprint.Contains(piece.Cover.CenterXz)) continue;

                float footprint = piece.Cover.Size.x * piece.Cover.Size.z;
                if (footprint > bestFootprint) { bestFootprint = footprint; best = piece; }
            }
            return best;
        }

        /// <summary>Hides whatever structure-kit visual <see cref="ReefDressing"/> already built for
        /// <paramref name="piece"/> (tagged with its own <see cref="ReefSourcePiece.PieceId"/>) so the
        /// hero piece about to stand in its place doesn't double up geometry at the same footprint. The
        /// piece's own invisible collider body (<see cref="CoverPiece.Body"/>) is left exactly as
        /// <see cref="ReefDressing.DressCover"/> already left it — "collision ... unchanged" (ticket).
        /// </summary>
        private static void HideExistingDressing(Transform host, CoverPiece piece)
        {
            foreach (ReefSourcePiece tagged in host.GetComponentsInChildren<ReefSourcePiece>(true))
            {
                if (tagged.PieceId != piece.Cover.Name) continue;
                tagged.gameObject.SetActive(false);
                break;
            }
        }

        /// <summary>The footprint a room-type builder should stand its hero piece on: the area's
        /// largest hull-fill piece when one is authored, else a fallback footprint at the room's own
        /// centre — so every area of a room type lands its landmark even where the V8 layout hasn't
        /// authored hull-fill cover for it yet.</summary>
        private static (Vector3 center, Vector3 size) FootprintFor(IReadOnlyList<CoverPiece> cover, WorldArea area, Transform host)
        {
            CoverPiece? chosen = LargestBulkhead(cover, area);
            if (chosen.HasValue)
            {
                HideExistingDressing(host, chosen.Value);
                return (chosen.Value.Cover.Center, chosen.Value.Cover.Size);
            }

            Vector2 c = area.CenterXz;
            float w = Mathf.Min(area.size?.w ?? 6f, 6f);
            float d = Mathf.Min(area.size?.d ?? 6f, 6f);
            return (new Vector3(c.x, 1.5f, c.y), new Vector3(w, 3f, d));
        }

        // ---- Reactor bay: the area's largest hull-fill mass becomes a reactor core -------------------

        private static void BuildReactorBay(Transform host, WorldArea area, IReadOnlyList<CoverPiece> cover)
        {
            (Vector3 center, Vector3 size) = FootprintFor(cover, area, host);
            GameObject root = ReefKit.BuildReactorCore(host, center, size);
            AttachReactorRingSpin(root);
            Tag(root, ReefSetPieceKind.ReactorCore, area);
        }

        // ---- Engine room: a propeller-shaft section along the area's own longest wall ----------------

        private static void BuildEngineRoom(Transform host, WorldArea area)
        {
            Wall longestWall = Wall.N;
            float longestLength = -1f;
            foreach (Wall w in new[] { Wall.N, Wall.E, Wall.S, Wall.W })
            {
                float len = area.WallSpan(w).Length;
                if (len > longestLength) { longestLength = len; longestWall = w; }
            }

            bool runsAlongX = area.WallRunsAlongX(longestWall);
            float coord = area.WallCoord(longestWall);
            Span span = area.WallSpan(longestWall);
            float inset = Mathf.Min(2.5f, span.Length * 0.3f);

            Vector3 center = runsAlongX ? new Vector3(span.Mid, 1.1f, coord) : new Vector3(coord, 1.1f, span.Mid);
            Vector3 size = runsAlongX
                ? new Vector3(span.Length * 0.6f, 2.2f, inset)
                : new Vector3(inset, 2.2f, span.Length * 0.6f);

            GameObject root = ReefKit.BuildPropellerShaft(host, center, size, runsAlongX);
            AttachPropellerCollarSpin(root);
            Tag(root, ReefSetPieceKind.PropellerShaft, area);
        }

        // ---- Cargo hold: the area's largest hull-fill mass becomes a stacked container wall, with a
        // ceiling crane rail and a swaying hook/chain above the open floor (a30 also gets the anchor). --

        private static void BuildCargoHold(Transform host, WorldArea area, IReadOnlyList<CoverPiece> cover)
        {
            (Vector3 center, Vector3 size) = FootprintFor(cover, area, host);
            GameObject root = ReefKit.BuildContainerWall(host, center, size);
            Tag(root, ReefSetPieceKind.ContainerWall, area);

            Vector2 c = area.CenterXz;
            Vector3 craneCenter = new Vector3(c.x, 0f, c.y);
            float craneSpan = Mathf.Min(area.size?.w ?? CraneRigDefaultSpan, area.size?.d ?? CraneRigDefaultSpan) * 0.8f;
            GameObject craneRoot = ReefKit.BuildCraneRig(host, craneCenter, craneSpan);
            AttachCraneHookSway(craneRoot, craneCenter);

            if (area.id == "a30")
                Tag(ReefKit.BuildAnchorChain(host, craneCenter, craneSpan), ReefSetPieceKind.AnchorChain, area);
        }

        // ---- Gallery: the area's own window wall widens into a curved observation dome ---------------

        private static void BuildGallery(Transform host, WorldArea area, List<StructuralWall> walls, float wallHeight)
        {
            StructuralWall match = ReefAreaKit.FindAreaWall(walls, area, Wall.N, requireOuterEdge: false);

            Vector3 center;
            Vector3 size;
            if (match != null)
            {
                Vector3 scale = match.transform.lossyScale;
                center = match.transform.position + Vector3.up * (wallHeight * 0.5f);
                size = new Vector3(scale.x, wallHeight, Mathf.Max(scale.z, 0.5f));
            }
            else
            {
                Vector2 c = area.CenterXz;
                center = new Vector3(c.x, wallHeight * 0.5f, c.y);
                size = new Vector3(6f, wallHeight, 1f);
            }

            GameObject root = ReefKit.BuildObservationDome(host, center, size);
            Tag(root, ReefSetPieceKind.ObservationDome, area);
        }

        // ---- Pressure lock: every gate into these areas gets a vault-door frame -----------------------

        private static void BuildPressureLock(Transform host, WorldArea area, WorldConfig cfg)
        {
            if (cfg?.gates == null) return;

            AreaGate[] builtGates = host.GetComponentsInChildren<AreaGate>(true);

            foreach (WorldGate gate in cfg.gates)
            {
                if (gate?.from == null || gate.to == null) continue;
                if (gate.from.area != area.id && gate.to.area != area.id) continue;

                AreaGate built = FindGateByName(builtGates, gate.id);
                if (built == null) continue;

                GameObject root = BuildVaultDoorAroundGate(built);
                if (root != null) Tag(root, ReefSetPieceKind.VaultDoor, area);
            }
        }

        private static AreaGate FindGateByName(AreaGate[] gates, string id)
        {
            foreach (AreaGate g in gates)
                if (g != null && g.name == id) return g;
            return null;
        }

        /// <summary>Same "inverse-scale child root cancels the gate's own (width,height,depth) scale"
        /// idiom <see cref="AreaGate.ApplyCitySkin"/>/<see cref="AreaGate.ApplyReefSkin"/> already use —
        /// <see cref="ReefKit.BuildVaultDoorFrame"/> builds in real metres, and this root's local scale
        /// undoes the gate's own scale so those metres land correctly regardless of doorway size or
        /// which wall the gate sits on. A sibling of <c>AreaGate.BuildReefGateFrame</c>'s own frame root
        /// (every Reef gate already carries that reinforced surround via <see cref="AreaGate.ApplyReefSkin"/>),
        /// not a replacement for it — the vault door is the pressure lock's OWN, bigger landmark on top.
        /// </summary>
        private static GameObject BuildVaultDoorAroundGate(AreaGate gate)
        {
            float width = gate.transform.localScale.x;
            float height = gate.transform.localScale.y;
            float depth = gate.transform.localScale.z;
            if (width <= 0f || height <= 0f || depth <= 0f) return null;

            var root = new GameObject(gate.gameObject.name + " (Vault Door)");
            root.transform.SetParent(gate.transform, worldPositionStays: false);
            root.transform.localScale = new Vector3(1f / width, 1f / height, 1f / depth);
            root.AddComponent<KeepsOwnMaterial>();

            GameObject[] bolts = ReefKit.BuildVaultDoorFrame(root.transform, width, height, depth);
            ReefVaultDoorBolts.Attach(root.transform, bolts, gate);

            return root;
        }

        // ---- Crew/hydroponics: a grow-lamp canopy over every planter rack in the area -----------------

        private static void BuildCrewHydroponics(Transform host, WorldArea area, IReadOnlyList<CoverPiece> cover)
        {
            bool placedAny = false;

            if (cover != null)
            {
                foreach (CoverPiece piece in cover)
                {
                    if (piece.Cover.Dressing != CoverDressing.ReefPlanter) continue;
                    if (!area.Footprint.Contains(piece.Cover.CenterXz)) continue;

                    Vector3 center = piece.Cover.Center + Vector3.up * (piece.Cover.Size.y * 0.5f + CanopyClearance);
                    GameObject root = ReefKit.BuildGrowLampCanopy(host, center, piece.Cover.Size);
                    Tag(root, ReefSetPieceKind.GrowLampCanopy, area);
                    placedAny = true;
                }
            }

            if (placedAny) return;

            // No authored planter in this area yet — still land the room type's own landmark at its
            // centre, so Lee's 17 Oct table always resolves a set piece per area (ticket AC1), the same
            // "never skip a room type" guarantee FootprintFor keeps for the bulkhead-based pieces.
            Vector2 c = area.CenterXz;
            GameObject fallback = ReefKit.BuildGrowLampCanopy(host, new Vector3(c.x, 2.2f, c.y), new Vector3(1.5f, 1f, 1.5f));
            Tag(fallback, ReefSetPieceKind.GrowLampCanopy, area);
        }
    }
}
