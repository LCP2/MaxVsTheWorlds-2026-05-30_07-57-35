using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.Arena
{
    /// <summary>A cover prop as it ended up in the world: the authored data, and the primitive that
    /// carries its collider. The art pass (YT-75) needs both — the data to know what the piece is
    /// meant to be, the object to hide once a real model stands in its place.</summary>
    public readonly struct CoverPiece
    {
        public readonly ArenaCover Cover;
        public readonly GameObject Body;

        public CoverPiece(ArenaCover cover, GameObject body)
        {
            Cover = cover; Body = body;
        }
    }

    /// <summary>
    /// Loads the map and builds it (YT-89). This used to BE the level — a hand-written sequence of
    /// Box() calls, with its dimensions serialized into <c>Backyard_Slice.unity</c>, which is what
    /// made a layout change slow: the scene's copy of the numbers overrode the code's, the actors
    /// standing in the level were placed separately by hand, and an editor scaffold had to exist for
    /// the sole purpose of shoving the two back into agreement (Stage68).
    ///
    /// Now it is a host, not a level. The level is a JSON map under <c>Resources/Maps/</c>; this
    /// component names one, validates it, and hands it to <see cref="MapRuntime"/>. Reshaping the
    /// arena means editing a text file (or dragging a room in the map editor) — no scene edit, no
    /// recompile, no scaffold.
    ///
    /// It keeps its name and its place in the scene because the dressing, backdrop, minimap and map
    /// panel all find the level through it; <see cref="Layout"/> is now a view derived from the map
    /// (<see cref="MapLayoutBridge"/>) rather than a field a human typed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardPath : MonoBehaviour
    {
        [Tooltip("Dev override only (MV-687): which world to build, a JSON file under Resources/Worlds/ " +
                 "(MV-270). Left empty, the world resolves from the active save's WorldIndex instead — " +
                 "a value baked into the scene would silently shadow that the way BlasterTuning's old " +
                 "serialized fields once did.")]
        [SerializeField] private string worldKey = string.Empty;

        private static readonly List<CoverPiece> NoCover = new List<CoverPiece>(0);

        private MapData _map;
        private MapBuild _build;
        private BackyardPathLayout _layout = BackyardPathLayout.Default;
        private AreaAccumulationDirector _areaDirector;
        private WorldRunner _worldRunner;

        /// <summary>The map this level was built from. Null if it failed to load.</summary>
        public MapData Map => _map;

        /// <summary>Drives the gated arena's ambient population (v0.5 recut spec §2, MV-242). Null if
        /// the map failed to load — there is no run to populate.</summary>
        public AreaAccumulationDirector AreaDirector => _areaDirector;

        /// <summary>The map, described in the rooms-and-gate terms the minimap and the dressing pass
        /// read. Derived from <see cref="Map"/> — not a source of truth.</summary>
        public BackyardPathLayout Layout => _layout;

        public float ShedZ => MapLayoutBridge.ShedZ(_map);
        public float ShedSpawnRadius => MapValidation.SpawnRadius;

        /// <summary>The cover that actually got built — empty if the map failed to load or validate.
        /// The dressing layer reads this rather than the authored set, so it can never plant a tree
        /// where no cover was placed.</summary>
        public IReadOnlyList<CoverPiece> CoverPieces => _build?.Cover ?? (IReadOnlyList<CoverPiece>)NoCover;

        /// <summary>The world index this instance's <see cref="Awake"/> actually resolved (MV-766)
        /// — read by the world probe instead of calling <see cref="ActiveWorldIndex"/> a second
        /// time, so the probe reports what THIS run resolved rather than re-deriving it from
        /// whatever the active save currently says (which can have moved on by the time the probe
        /// is read).</summary>
        public int ResolvedWorldIndex { get; private set; }

        private void Awake()
        {
            int worldIndex = ActiveWorldIndex();
            ResolvedWorldIndex = worldIndex;
            ApplyPendingMorphAtRunStart(worldIndex);
            string key = string.IsNullOrWhiteSpace(worldKey) ? WorldLibrary.KeyForIndex(worldIndex) : worldKey;

            WorldConfig cfg = WorldLibrary.Load(key);
            if (cfg == null) return;   // WorldLibrary has already said why

            if (!WorldMapLoader.TryLoad(cfg, out _map, out string reason))
            {
                Debug.LogError($"[BackyardPath] world '{key}' is not playable: {reason}");
                _map = null;
                return;
            }

            _layout = MapLayoutBridge.ToLayout(_map);
            _build = MapRuntime.Build(_map, transform);

            // After the geometry exists, not before (MV-690): this is what actually sweeps the floor/
            // walls/props MapRuntime just built into this world's own biome — WorldMaterials.Install()'s
            // own AfterSceneLoad self-install runs later still, sees a WorldMaterials already here and
            // stands down, so this call is what decides the palette a run renders with, not that one.
            ApplyWorldMaterials(worldIndex, transform, _build.Cover);

            // ConfigureWorld MUST run before Configure (MV-311): Configure() fills area 1 synchronously,
            // so if the world config lands after that fill, area 1 permanently misses the budget-solver
            // path and is stuck on the legacy AreaPopulation fallback for the rest of the run.
            _areaDirector = new GameObject("Area Accumulation").AddComponent<AreaAccumulationDirector>();
            _areaDirector.transform.SetParent(transform, false);
            _areaDirector.ConfigureWorld(cfg);
            _areaDirector.Configure(_map, _build.Cover);

            // MV-579 (DECISION, Lee 26 Aug 2026 playtest): sentinels now persist across an area
            // crossing by default — a wedged, unrecallable sentinel used to also vanish and refund on
            // crossing (MV-362/MV-396), which was tolerable when it merely cost a redeploy; once it
            // could permanently block Max's only exit (the bug this ticket fixes), losing it on every
            // crossing stopped being a fair trade. This USED to be gated behind the f_skr (SKIRMISH)
            // fusion's IsForged("f_skr") check (MV-426: "Sentinels survive an area change" was its
            // whole perk) — removed here because it is now the default for every player, not a fusion
            // reward. f_skr keeps its OTHER effect (Teleport snaps to a live sentinel, see
            // PlayerAbilities.TryBlink) untouched; MV-582 is the linked follow-up proposing a
            // replacement for the half this deletes. Do not re-add a teardown here to give it
            // something to do again.

            _worldRunner = new GameObject("World Runner").AddComponent<WorldRunner>();
            _worldRunner.transform.SetParent(transform, false);
            _worldRunner.Configure(cfg, _map, _build, _areaDirector);

            WireAreaGatesToPopulation();
        }

        /// <summary>Which world the active save is up to (MV-687) — 0 with no active profile (tests,
        /// captures, a scene with no Home screen involved), matching the pre-MV-687 always-World-1
        /// behaviour.</summary>
        private static int ActiveWorldIndex()
        {
            int slot = SaveSystem.ActiveSlot;
            return slot >= 0 ? SaveSystem.Load(slot).WorldIndex : 0;
        }

        /// <summary>MV-753: RUN START's own trigger for a still-pending Weapon Core morph, called
        /// first thing in <see cref="Awake"/> — before this, the morph only ever ran lazily on THE
        /// RIG's first open (<c>WeaponsScreen.Open</c> -&gt; <see cref="WeaponSystemState.OpenWeaponCoreMorphIfPending"/>),
        /// which left <see cref="RigBoard.ActiveWorldIndex"/> on the OLD world for however long the
        /// player fought before first opening WEAPONS.
        /// <see cref="MaxWorlds.Pickups.PickupDirector.OnFactoryDestroyed"/> gates World 2's
        /// one-per-run Rack Module drop on that index already reading 1, so every Replicator destroyed
        /// in that window dropped no Rack Module (Lee: "something happened ... but I had no ability to
        /// pick up"). Calling this here closes the window entirely. Public and static, taking the
        /// already-resolved world index rather than reading the save itself, so an EditMode test can
        /// drive the exact run-start step with no scene/map involved.
        /// <see cref="WeaponSystemState.OpenWeaponCoreMorphIfPending"/> is already idempotent (a no-op
        /// once nothing is banked), so THE RIG's own open ceremony keeps calling it too — a fallback for
        /// anything that never runs a <see cref="BackyardPath"/> at all (the dev capture harness).</summary>
        public static void ApplyPendingMorphAtRunStart(int worldIndex) =>
            WeaponSystemState.OpenWeaponCoreMorphIfPending(worldIndex);

        /// <summary>Dresses the arena in the loaded world's own biome (MV-690) — World 1's lawn-green or
        /// World 2's wet concrete (<see cref="BiomePalette.ForWorld"/>). <see cref="WorldMaterials"/>
        /// self-installs at <c>AfterSceneLoad</c> with the Backyard default, which runs AFTER this
        /// Awake — so this call, not that one, is what actually decides the palette a World 2 run
        /// renders with; finding-or-creating it here rather than waiting for its own install means the
        /// floor/wall/prop sweep never runs twice with two different palettes.</summary>
        private void ApplyWorldMaterials(int worldIndex, Transform host, IReadOnlyList<CoverPiece> cover)
        {
            var wm = FindFirstObjectByType<WorldMaterials>();
            if (wm == null) wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
            wm.Apply(BiomePalette.ForWorld(worldIndex));

            // MV-713: the hydroponic reactor and power hatch are IDamageable, so the shape-classified
            // sweep above explicitly leaves them alone (WorldMaterials.IsWorldSurface) — gameplay owns
            // their tint the same way it owns every other damageable's. Reef is the one biome so far
            // that wants a cosmetic override of its own, applied here rather than at MapRuntime build
            // time so it can never race the sweep above or run before a hutch/gate's own Awake has set
            // up the renderer it recolours.
            if (worldIndex >= 2) ApplyReefKit(host, cover);
            // MV-755: World 2 never got a kit (MV-690's documented scope cut) and then lost the
            // garden props it was borrowing (MV-750) — this is what replaces both.
            else if (worldIndex == 1)
            {
                StormdrainDressing.Dress(host, _map, cover);
                // MV-759: every World 2 gate gets a round portal ring and sliding double doors instead
                // of its plain slab — same "re-skin what MapRuntime already built" idiom ApplyReefKit
                // uses for World 3's gates just above, scoped to this world only.
                foreach (var gate in FindObjectsByType<AreaGate>(FindObjectsSortMode.None))
                    gate.ApplyStormdrainGateSkin();
            }
        }

        /// <summary>World 3's Reef-only cosmetic pass — re-skins every hydroponic reactor
        /// (<see cref="MowerHutch"/>) and power hatch (<see cref="AreaGate"/>) already built in the
        /// scene, re-skins the floor/walls with the ticket's own named materials and lays the circuit
        /// spine along their base (MV-745: <see cref="ReefKit.DressHull"/> — the generic per-world
        /// sweep above only ever wore World 3 in a recoloured version of every other biome's ground/
        /// wall shader, never the dedicated Reef materials MV-713 shipped), builds the ocean backdrop
        /// behind its observation windows (MV-713), and dresses every authored "machinery" cover piece
        /// into a coolant turret (MV-744: <see cref="ReefDressing"/> is the routing this world's own
        /// kit was missing — <c>ReefKit.BuildCoolantTurret</c> existed since MV-713 but nothing ever
        /// called it). Called once per load, only when the active world is Reef (index 2+) — every
        /// other world leaves this untouched.</summary>
        private static void ApplyReefKit(Transform host, IReadOnlyList<CoverPiece> cover)
        {
            foreach (var hutch in FindObjectsByType<MowerHutch>(FindObjectsSortMode.None))
                hutch.ApplyReefSkin();
            foreach (var gate in FindObjectsByType<AreaGate>(FindObjectsSortMode.None))
                gate.ApplyReefSkin();

            ReefKit.DressHull(host);
            ReefKit.BuildOceanBackdrop(host);
            ReefDressing.DressCover(host, cover);
        }

        /// <summary>Gives each area a head start on its ambient population (MV-245): the moment the
        /// gate guarding it breaks, not the moment the player is later found standing inside it — the
        /// gap between those two is exactly the time the player still needs to walk through the
        /// doorway, which is what lets the room be fully populated before they can see it.</summary>
        private void WireAreaGatesToPopulation()
        {
            if (_map.links == null) return;

            foreach (MapLink link in _map.links)
            {
                if (link == null || string.IsNullOrEmpty(link.gate)) continue;

                int nextArea = AreaAccumulationDirector.AreaIndexOf(link.to);
                if (nextArea <= 0) continue;   // e.g. area10 -> compost: the boss arena, not gated by this

                if (!_build.Actors.TryGetValue(link.gate, out GameObject gateGo) || gateGo == null) continue;

                AreaGate gate = gateGo.GetComponent<AreaGate>();
                if (gate == null) continue;   // e.g. boss_gate is the adopted SubZoneGate, not an AreaGate

                gate.Opened += () => _areaDirector.EnterArea(nextArea);
            }
        }

        // --- MV-766: runtime probe -------------------------------------------------------------
        //
        // Five candidate causes for World 2 rendering as the Backyard were traced through the
        // source and all five were eliminated — on paper the merged code resolves World 2
        // correctly. This is what makes the running build state what it actually resolved, read
        // from the live objects, so nobody guesses again from a second static reading.

        private static bool s_probeLogged;

        /// <summary>Wired into <see cref="Bootstrap.WorldProbeLineProvider"/> once, from the one
        /// assembly that can see both the Arena types (this world index, the Stormdrain dressing
        /// host) and the Rendering types (the palette, the applied look) without Core reaching into
        /// either.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallWorldProbe() => Bootstrap.WorldProbeLineProvider = BuildWorldProbeLine;

        /// <summary>
        /// <c>W&lt;index&gt; &lt;paletteName&gt; / &lt;lookName&gt; fog&lt;density&gt; dress&lt;n&gt;</c>
        /// — every field read off the live object that actually resolved it, never recomputed from
        /// the world index. Static and self-contained (no instance required) so an EditMode test can
        /// drive it with nothing but <see cref="MaterialLibrary.Palette"/> set.
        /// </summary>
        public static string BuildWorldProbeLine()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            int worldIndex = path != null ? path.ResolvedWorldIndex : -1;

            string paletteName = NameOfPalette(MaterialLibrary.Palette);

            var lighting = FindFirstObjectByType<BackyardLighting>();
            string lookName = lighting != null ? NameOfLook(lighting.Look) : "none";

            float fogDensity = RenderSettings.fogDensity;

            GameObject dressing = GameObject.Find("Stormdrain Dressing");
            int dressingCount = dressing != null ? dressing.transform.childCount : 0;

            string line = $"W{worldIndex} {paletteName} / {lookName} fog{fogDensity:F3} dress{dressingCount}";

            if (!s_probeLogged && lighting != null)
            {
                s_probeLogged = true;
                Debug.Log($"[WorldProbe] {line}");
            }

            return line;
        }

        private static string NameOfPalette(BiomePalette p)
        {
            if (p.Equals(BiomePalette.Backyard)) return "Backyard";
            if (p.Equals(BiomePalette.Stormdrain)) return "Stormdrain";
            if (p.Equals(BiomePalette.Reef)) return "Reef";
            return "custom";
        }

        private static string NameOfLook(BackyardLook l)
        {
            if (l.Equals(BackyardLook.Default)) return "Default";
            if (l.Equals(BackyardLook.Stormdrain)) return "Stormdrain";
            return "custom";
        }
    }
}
