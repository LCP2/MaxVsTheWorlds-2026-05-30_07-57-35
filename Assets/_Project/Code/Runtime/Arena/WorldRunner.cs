using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Wires a loaded <see cref="WorldConfig"/>'s origination engine (MV-269) into the scene
    /// <see cref="MapRuntime"/> actually built (MV-270). Owns a <see cref="SupplyLineNetwork"/> — the
    /// pure engine class (MV-269) reads no live scene state itself, so something has to poll the built
    /// <see cref="MowerHutch"/> instances and report their deaths into it; that caller is this runner.
    /// MV-665 removed the old role-driven gate lock; MV-703 reintroduces condition-gating driven off
    /// each gate's own parsed <see cref="GateCondition"/> instead (<see cref="RefreshGateLocks"/>) — a
    /// gate whose condition is Start/Primary/Sluice opens on ordinary combat alone, exactly as every
    /// World 1 gate does today.
    ///
    /// MV-427: also the death-continues-the-run orchestrator. It already owns every gate's identity
    /// (via the map), which is exactly the context a respawn needs to pick the right door to re-close.
    ///
    /// MV-438: the respawn itself no longer runs synchronously off the death. <see cref="OnPlayerDied"/>
    /// now only records the death and shows <see cref="DeathOverlay"/>; everything MV-427 used to do
    /// immediately — gate reclose, sentinel wipe, teleport — waits in <see cref="_pendingRespawn"/> for
    /// <see cref="Continue"/>, which the overlay's CONTINUE button calls. MV-941 dropped area restore
    /// from that list: a death no longer wipes/respawns the arena's robots (see <see cref="Continue"/>'s
    /// own doc comment).
    /// </summary>
    [MaxWorlds.Core.PerfSection("world")]
    public sealed class WorldRunner : MonoBehaviour
    {
        /// <summary>Metres behind the gate a respawn lands — clear of the doorway's own collider and
        /// the room beyond it, but not so far back it reads as a different spot from "at the gate".</summary>
        private const float RespawnMarginFromGate = 2.5f;

        private SupplyLineNetwork _supply;

        /// <summary>One entry per BUILT shed (MV-475, not per area — an area can carry several).
        /// <c>shedId</c> is the entity id <see cref="SupplyLineNetwork"/> tracks destruction against;
        /// <c>areaId</c> is only needed alongside it for <see cref="DestroyFactoriesBefore"/>.</summary>
        private readonly List<(string areaId, string shedId, MowerHutch hutch)> _sheds =
            new List<(string, string, MowerHutch)>(3);

        /// <summary>One entry per BUILT Replicator (MV-703/MV-706), polled the same "check IsAlive every
        /// tick" way <see cref="_sheds"/> is — a Replicator has no death event to subscribe to, only
        /// <see cref="Replicator.IsAlive"/>.</summary>
        private readonly List<Replicator> _replicators = new List<Replicator>(2);

        /// <summary>Each combat area's incoming gate, parsed into a <see cref="GateCondition"/> (MV-703)
        /// — built once in <see cref="BuildGateIntoAreaMap"/> alongside <see cref="_gateIntoArea"/>, from
        /// the same <see cref="WorldConfig.gates"/> entry <see cref="MapLink.gate"/> names. Only the
        /// three condition-gated kinds (<see cref="GateCondition.IsConditionGated"/>) do anything in
        /// <see cref="RefreshGateLocks"/> — every World 1 gate parses to Start/Primary and is untouched.</summary>
        private readonly Dictionary<int, GateCondition> _gateConditionIntoArea = new Dictionary<int, GateCondition>();

        /// <summary>MV-703 World 1 compatibility: logs once, the first time a boss-role area's incoming
        /// gate is found NOT condition-gated (every World 1 boss gate authors "primary") — see
        /// <see cref="IsConditionGatedArea"/>.</summary>
        private bool _loggedBossPrimaryCompat;

        /// <summary>Every built shed's <see cref="EnemySpawner"/>, paired with its own 1-based area
        /// index (MV-1093) — built once in <see cref="Configure"/>, alive or later destroyed alike.
        /// This runner is the only thing with a live view of which area Max is physically standing in,
        /// so it alone can gate EVERY shed to "producing only while Max stands in its own area" — live
        /// (Lee's "mower hutches are no threat" report: a shed several rooms away used to produce just
        /// as readily as the one Max was actually fighting) and, once destroyed, the MV-456 trickle
        /// (now just one case of this same gate rather than a separate destroyed-only code path).</summary>
        private readonly List<(int areaIndex, EnemySpawner spawner)> _shedSpawners =
            new List<(int, EnemySpawner)>(9);

        private WorldConfig _cfg;
        private MapData _map;
        private AreaAccumulationDirector _areaDirector;
        private PickupDirector _pickupDirector;
        private PlayerHealth _playerHealth;
        private Transform _player;
        private DeathOverlay _deathOverlay;

        /// <summary>Guards <see cref="HudSignals.EmitRunComplete"/> so the final area firing it is a
        /// one-shot (MV-591), the same one-shot shape every other terminal signal in this class uses.
        /// Reset alongside every other per-run death/respawn field would be wrong here — a run only
        /// ever completes once, so this is never cleared.</summary>
        private bool _runCompleteRaised;

        /// <summary>MV-438: the deferred respawn a death worked out but hasn't run yet — set the
        /// instant Max falls, cleared (and acted on) only when <see cref="Continue"/> runs. Null
        /// whenever the overlay isn't up, so <see cref="HasPendingRespawn"/> also answers "is a death
        /// overlay currently owed a Continue".</summary>
        private RespawnPlan? _pendingRespawn;

        /// <summary>Test-only access, same idiom as the rest of this project's screen classes.</summary>
        public bool HasPendingRespawn => _pendingRespawn.HasValue;

        /// <summary>The combat gate leading INTO each 1-based area — the one a death in that area
        /// re-closes (unless it's the boss gate; see <see cref="RespawnPlan.RecloseGate"/>) and the one
        /// a respawn lands behind.</summary>
        private readonly Dictionary<int, AreaGate> _gateIntoArea = new Dictionary<int, AreaGate>();

        /// <summary>Every built gate (MV-1096), keyed by its own authored id rather than by area —
        /// unlike <see cref="_gateIntoArea"/>, which can only ever remember ONE gate per area, this is
        /// what resolves a checkpoint's own <see cref="MaxWorlds.Save.SaveSlotData.CheckpointGateId"/>
        /// back to a specific gate instance for a two-level area visited through two different gates
        /// (World 2's a10/a11/a12, floor and deck alike).</summary>
        private readonly Dictionary<string, AreaGate> _gateById = new Dictionary<string, AreaGate>();

        /// <summary>Every built hatch (MV-829), paired with its own parsed <see cref="GateCondition"/>
        /// and its own area's 1-based index — keyed by the hatch itself rather than by "the area's
        /// incoming gate" (<see cref="_gateConditionIntoArea"/>'s shape) because an area can carry more
        /// than one hatch (World 2's a3/a6/a11 each author two). <see cref="RefreshGateLocks"/> resolves
        /// each one exactly like a wall gate's condition; a "primary"/"start"/"sluice" hatch (not
        /// <see cref="GateCondition.IsConditionGated"/>) is skipped there, same as a combat wall gate.</summary>
        private readonly List<(AreaGate gate, GateCondition condition, int areaIndex)> _hatchGates =
            new List<(AreaGate, GateCondition, int)>(4);

        public void Configure(WorldConfig cfg, MapData map, MapBuild build, AreaAccumulationDirector areaDirector)
        {
            _cfg = cfg;
            _map = map;
            _areaDirector = areaDirector;
            _supply = new SupplyLineNetwork(cfg);
            _runCompleteRaised = false;

            foreach (WorldArea area in cfg.areas)
            {
                WorldShed[] sheds = area.Sheds();
                for (int i = 0; i < sheds.Length; i++)
                {
                    string shedId = area.ShedId(i, sheds.Length);
                    if (!build.Actors.TryGetValue(shedId, out GameObject shedGo) || shedGo == null) continue;

                    MowerHutch hutch = shedGo.GetComponent<MowerHutch>();
                    if (hutch == null) continue;
                    // MV-922: stamps this shed's stable id onto the component itself, the same id this
                    // loop already computed to look itself up in build.Actors — so a mid-run checkpoint
                    // can persist and restore "which sheds are destroyed" by id, same as MV-776 already
                    // does for World 2's Replicator.
                    hutch.SetId(shedId);
                    _sheds.Add((area.id, shedId, hutch));

                    // MV-643: this shed may only ever emit a kind ITS OWN area's authored composition
                    // contains — every world1 area with a shed authors one (WorldComposition), so this
                    // is what actually replaces the old area-blind global cadence for every real shed.
                    // MV-1093: an area with NO authored composition (every World 3 area) now falls back
                    // to one derived from its own garrison instead of emitting nothing forever.
                    EnemySpawner spawner = hutch.GetComponent<EnemySpawner>();
                    if (spawner != null)
                    {
                        spawner.ConfigureAreaComposition(ResolveShedComposition(area));
                        spawner.ConfigureWorldConfig(cfg);   // MV-701: so this shed's own spawns can resolve enemyOverrides
                        _shedSpawners.Add((area.index, spawner));
                    }
                }

                // MV-703: register this area's Replicators (MV-706) into FactoryCensus by area id — the
                // same "the map builds them, this runner tells the census where" split as the shed loop
                // above, needed so a replicators-destroyed:<areas> gate condition can ask "destroyed in
                // THESE areas", not just "destroyed overall".
                WorldReplicator[] replicators = area.replicators ?? Array.Empty<WorldReplicator>();
                for (int i = 0; i < replicators.Length; i++)
                {
                    WorldReplicator r = replicators[i];
                    if (r == null) continue;
                    string replicatorId = string.IsNullOrEmpty(r.id) ? $"{area.id}_replicator{i + 1}" : r.id;
                    if (!build.Actors.TryGetValue(replicatorId, out GameObject repGo) || repGo == null) continue;

                    Replicator replicator = repGo.GetComponent<Replicator>();
                    if (replicator == null) continue;

                    // MV-756 Cause 3: the shed loop above configures its spawner's composition and
                    // world config; this loop skipped both, so an emitted twin resolved its archetype
                    // with a null _worldConfig and missed every MV-701 enemyOverrides entry.
                    EnemySpawner repSpawner = replicator.GetComponent<EnemySpawner>();
                    if (repSpawner != null)
                    {
                        repSpawner.ConfigureAreaComposition(area.composition);
                        repSpawner.ConfigureWorldConfig(cfg);
                    }

                    // MV-776: stamps this box's stable id onto the component itself so a mid-run
                    // checkpoint can persist and restore "which Replicators are destroyed" by id — the
                    // same id this loop already computed to look itself up in build.Actors, which used
                    // to be discarded the moment that lookup finished.
                    replicator.SetId(replicatorId);
                    FactoryCensus.RegisterReplicator(replicator, area.id);
                    // MV-828: area.id is the raw config id (e.g. "a3"), which AreaIndexOf can never
                    // parse (it only recognises the "area<N>" string WorldMapLoader translates zone ids
                    // to) — every World 2 Replicator stamped AreaIndex 0 this way, so NearestEligible
                    // matched no robot. area.index is the same 1-based number the zones and robots
                    // already key off (WorldMapLoader.TryLoad, AreaAccumulationDirector.SetAreaIndex).
                    int replicatorArea = area.index;
                    if (replicatorArea <= 0)
                        Debug.LogError($"[WorldRunner] Replicator '{replicatorId}' resolved AreaIndex " +
                                       $"{replicatorArea} from area '{area.id}' — a combat-area Replicator " +
                                       "must belong to a real (>=1) area index.");
                    replicator.SetAreaIndex(replicatorArea);
                    _replicators.Add(replicator);
                }
            }

            BuildGateIntoAreaMap(build);
            BuildHatchGateList(build);
            RefreshGateLocks(); // initial lock state before the first Update tick

            _playerHealth = FindFirstObjectByType<PlayerHealth>();
            if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;

            // MV-829: an area-entered:<id> hatch condition needs to know when Max's own position has
            // ever crossed into that area — the real physical-crossing signal, not the population
            // head-start EnterArea fires ahead of the player (see AreaAccumulationDirector's own doc
            // comment on the difference). Fired once immediately for whatever area Max starts in too,
            // since PlayerCrossedIntoArea only fires on a later CROSSING, never the starting area.
            if (_areaDirector != null)
            {
                _areaDirector.PlayerCrossedIntoArea += OnPlayerCrossedIntoArea;
                MarkAreaEntered(_areaDirector.CurrentArea);
            }
        }

        private void MarkAreaEntered(int areaIndex)
        {
            WorldArea area = _cfg?.AreaByIndex(areaIndex);
            if (area != null) AreaVisitCensus.MarkEntered(area.id);
        }

        private void OnPlayerCrossedIntoArea(int areaIndex) => MarkAreaEntered(areaIndex);

        /// <summary>Every hatch this world authors (MV-829), paired with its own parsed condition and
        /// its own area's index — built once, alongside <see cref="BuildGateIntoAreaMap"/>, from the
        /// same <paramref name="build"/> a hatch's id is a key into.</summary>
        private void BuildHatchGateList(MapBuild build)
        {
            _hatchGates.Clear();
            if (_cfg?.areas == null) return;

            foreach (WorldArea area in _cfg.areas)
            {
                foreach (WorldHatch h in area.hatches ?? Array.Empty<WorldHatch>())
                {
                    if (h == null) continue;
                    if (!build.Actors.TryGetValue(h.id, out GameObject hatchGo) || hatchGo == null) continue;

                    AreaGate gate = hatchGo.GetComponent<AreaGate>();
                    if (gate == null) continue;

                    if (GateCondition.TryParse(h.opensWith, out GateCondition condition, out _))
                        _hatchGates.Add((gate, condition, area.index));
                }
            }
        }

        private void BuildGateIntoAreaMap(MapBuild build)
        {
            if (_map.links == null) return;

            foreach (MapLink link in _map.links)
            {
                if (link == null || string.IsNullOrEmpty(link.gate)) continue;

                int intoArea = AreaAccumulationDirector.AreaIndexOf(link.to);
                if (intoArea <= 0) continue;   // a link into the entry stub itself — no death ever restores there

                if (!build.Actors.TryGetValue(link.gate, out GameObject gateGo) || gateGo == null) continue;
                AreaGate gate = gateGo.GetComponent<AreaGate>();
                // MV-575: this already covers boss areas too — WorldMapLoader translates a boss area's
                // id to "area<N>" exactly like every other combat area, so the loop above keys its gate
                // at its real index without needing a separate synthetic-index case here.
                if (gate != null)
                {
                    _gateIntoArea[intoArea] = gate;
                    _gateById[link.gate] = gate;
                }

                // MV-703: resolve the same gate's authored opensWith into a GateCondition, keyed the
                // same way, so RefreshGateLocks (and IsConditionGatedArea) can evaluate it per area
                // without re-deriving link.gate -> WorldGate every tick.
                WorldGate schemaGate = GateSchemaById(link.gate);
                if (schemaGate != null && GateCondition.TryParse(schemaGate.opensWith, out GateCondition condition, out _))
                    _gateConditionIntoArea[intoArea] = condition;
            }
        }

        private WorldGate GateSchemaById(string gateId)
        {
            if (_cfg?.gates == null) return null;
            foreach (WorldGate g in _cfg.gates)
                if (g != null && g.id == gateId) return g;
            return null;
        }

        /// <summary>This shed's emission cadence source (MV-1093): <paramref name="area"/>'s own
        /// authored <see cref="WorldComposition"/> when it has one, else one derived from the area's
        /// authored <see cref="WorldArea.garrison"/> counts, same proportions — every World 3 area ships
        /// with a garrison but no composition, which previously left <see cref="EnemyMix.AreaCadence"/>
        /// empty forever (the "door cycles, nothing ever comes out" report). Logs an error naming the
        /// area when NEITHER yields anything authored — that shed's door then stays shut
        /// (<see cref="EnemySpawner.WantsToEmit"/>) rather than silently inventing a kind nobody drew.</summary>
        private static WorldComposition ResolveShedComposition(WorldArea area)
        {
            if (area.composition != null && area.composition.IsAuthored) return area.composition;

            WorldComposition derived = CompositionFromGarrison(area);
            if (!derived.IsAuthored)
            {
                Debug.LogError($"[WorldRunner] MV-1093: area '{area.id}' has neither an authored " +
                                "composition nor any emit-able garrison kind for its shed — its door " +
                                "will stay shut.");
            }
            return derived;
        }

        /// <summary>Counts <paramref name="area"/>'s authored <see cref="WorldArea.garrison"/> entries by
        /// kind, skipping Lurker/Turret (MV-1093: placed at an authored grate/wall mount, never emitted
        /// through a shed's ambient release cadence — <see cref="EnemyMix.AreaCadence"/>'s own
        /// constructor already carries this same exemption for Lurker).</summary>
        private static WorldComposition CompositionFromGarrison(WorldArea area)
        {
            var composition = new WorldComposition();
            if (area.garrison == null) return composition;

            foreach (WorldGarrisonEntry g in area.garrison)
            {
                if (g == null || !EnemyKindNames.TryParse(g.kind, out EnemyKind kind)) continue;
                switch (kind)
                {
                    case EnemyKind.Rusher: composition.rusher++; break;
                    case EnemyKind.Bruiser: composition.bruiser++; break;
                    case EnemyKind.Heavy: composition.heavy++; break;
                    case EnemyKind.Brute: composition.brute++; break;
                    case EnemyKind.Gunner: composition.gunner++; break;
                    case EnemyKind.Launcher: composition.launcher++; break;
                    case EnemyKind.Blinker: composition.blinker++; break;
                    case EnemyKind.Bolter: composition.bolter++; break;
                    case EnemyKind.Sludger: composition.sludger++; break;
                    case EnemyKind.Charger: composition.charger++; break;
                    // Lurker/Turret: placed, not emitted — skipped on purpose.
                }
            }
            return composition;
        }

        private void OnDestroy()
        {
            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
            if (_areaDirector != null) _areaDirector.PlayerCrossedIntoArea -= OnPlayerCrossedIntoArea;
        }

        /// <summary>MV-524 part 2: iOS can suspend-then-terminate a backgrounded app with no further
        /// callback, so the checkpoint write has to happen here, not at quit.</summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) CapturePauseCheckpoint();
        }

        /// <summary>Same trigger as <see cref="OnApplicationPause"/>, from the other signal Unity gives
        /// a backgrounding app (MV-524's own ticket text names both) — <see cref="SaveSystem.CaptureActiveCheckpoint"/>
        /// is a plain overwrite, so a device that fires both costs nothing beyond a redundant write.</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) CapturePauseCheckpoint();
        }

        /// <summary>The actual write, pulled out of <see cref="OnApplicationPause"/>/<see cref="OnApplicationFocus"/>
        /// (MV-524 AC6's documented fallback): Unity never invokes either outside Play mode, so an
        /// EditMode test can't drive this method itself, only the plain <see cref="SaveSystem.CaptureActiveCheckpoint"/>
        /// call inside it — which it does, directly.</summary>
        private void CapturePauseCheckpoint()
        {
            if (_areaDirector == null) return;
            SaveSystem.CaptureActiveCheckpoint(_areaDirector.CurrentArea, _areaDirector.ActiveWorldIndex,
                _areaDirector.LastEnteredGateId);
        }

        /// <summary>RESUME tapped on the Home screen (MV-524 part 3): drop the player at
        /// <paramref name="areaIndex"/>'s entry using the exact same restore/reposition pipeline a
        /// death's <see cref="Continue"/> already uses — the checkpoint captures only an area index (no
        /// world snapshot, per the ticket's own decision), so landing back in that area means re-solving
        /// and re-populating its authored composition fresh, exactly like a death respawn into that same
        /// area would. Deliberately skips <see cref="DeathRunState.RecordDeath"/> and the
        /// <see cref="_pendingRespawn"/>/overlay machinery entirely — this is a cold boot landing mid-run,
        /// not a death. A no-op before <see cref="Configure"/> has wired this runner up, or for the entry
        /// stub (<paramref name="areaIndex"/> &lt;= 0 — nothing to restore/respawn into).
        ///
        /// MV-1065: resolves its predecessor through the exact same <see cref="ResolveRespawnPredecessor"/>
        /// route-graph lookup <see cref="OnPlayerDied"/> uses, deck-aware, rather than calling the 2-arg
        /// <see cref="RespawnPlanner.Resolve(int, bool)"/> overload — whose own <c>areaIndex - 1</c>
        /// fallback is only correct when a world's area INDEX order matches its PLAY order, which World
        /// 2's descending gantry-deck leg (a15 -&gt; a12 -&gt; a11 -&gt; a10) does not.
        ///
        /// MV-1096: <paramref name="gateId"/> is the id of the gate Max last entered through when this
        /// checkpoint was captured (<see cref="MaxWorlds.Save.SaveSlotData.CheckpointGateId"/>) — when it
        /// resolves to a real, built gate (<see cref="TryResolveEnteredGate"/>), it carries BOTH the area
        /// and the level/visit, which <paramref name="areaIndex"/> alone cannot for a two-level area
        /// (World 2's a10/a11/a12, each visited through two different gates). That path
        /// (<see cref="ResumeAtEnteredGate"/>) replaces the predecessor-area fallback below entirely.
        /// Null/empty (a save captured before this field existed) falls through to the unchanged
        /// area-index-only path.</summary>
        public void ResumeCheckpoint(int areaIndex, string gateId = null)
        {
            if (_areaDirector == null || _cfg?.dials == null || areaIndex <= 0) return;

            if (TryResolveEnteredGate(gateId, out int gateAreaIndex, out int gateLevel, out AreaGate enteredGate))
            {
                ResumeAtEnteredGate(gateAreaIndex, gateLevel, enteredGate);
                return;
            }

            bool gateIsConditionGated = IsConditionGatedArea(areaIndex);
            int predecessor = ResolveRespawnPredecessor(areaIndex);
            RespawnPlan plan = RespawnPlanner.Resolve(areaIndex, gateIsConditionGated, predecessor);

            // MV-951: Configure() (BackyardPath.Awake, before this Home-screen RESUME even ran) always
            // fills area 1 — and pre-places area 2's garrison — regardless of which area this checkpoint
            // is about to land in. Every area before the checkpoint must end up with no population at
            // all, not just the checkpoint area RestoreArea itself re-solves below.
            _areaDirector.ClearAreasBeforeCheckpoint(plan.RestoreAreaIndex);
            _areaDirector.RestoreArea(plan.RestoreAreaIndex);

            if (_pickupDirector == null) _pickupDirector = FindFirstObjectByType<PickupDirector>();
            _pickupDirector?.ResetBruiserCountdown(plan.RestoreAreaIndex);

            if (plan.RecloseGate && _gateIntoArea.TryGetValue(plan.RestoreAreaIndex, out AreaGate gate) && gate != null)
                gate.Reclose();

            Sentinel.DestroyAllActive();

            EnsurePlayer();
            RespawnPlayer(plan);
            _areaDirector.SetCurrentArea(plan.RespawnAreaIndex);
        }

        /// <summary>MV-1096: resolves <paramref name="gateId"/> to the area it leads into, that gate's
        /// own level (0 floor / 1 deck, <see cref="WorldMapLoader.GateLevel"/>), and the live built
        /// <see cref="AreaGate"/> instance — false for a null/empty id (no gate field on the save), an id
        /// this world's own <see cref="WorldConfig.gates"/> doesn't recognise, or a gate this runner
        /// never built (<see cref="_gateById"/>).</summary>
        private bool TryResolveEnteredGate(string gateId, out int areaIndex, out int level, out AreaGate gate)
        {
            areaIndex = 0;
            level = 0;
            gate = null;
            if (string.IsNullOrEmpty(gateId)) return false;

            WorldGate schemaGate = GateSchemaById(gateId);
            if (schemaGate?.to == null) return false;

            WorldArea toArea = _cfg?.Area(schemaGate.to.area);
            if (toArea == null) return false;

            if (!_gateById.TryGetValue(gateId, out gate) || gate == null) return false;

            areaIndex = toArea.index;
            level = WorldMapLoader.GateLevel(schemaGate);
            return true;
        }

        /// <summary>MV-1096: the gate-identified RESUME path — Max lands just INSIDE <paramref name="gate"/>,
        /// on <paramref name="level"/>'s own elevation, in <paramref name="areaIndex"/>, rather than the
        /// predecessor-area fallback <see cref="ResumeCheckpoint"/>'s area-index-only path uses. Population
        /// restore is keyed the same way: an in-place-deck area (World 2's a10/a11/a12) restores ONLY
        /// <paramref name="level"/>'s own authored garrison (<see cref="AreaAccumulationDirector.RestoreAreaAtLevel"/>)
        /// — the other level is left exactly as a fresh cold boot leaves it, since it is either already
        /// cleared (behind Max) or not yet reached (ahead of him). <paramref name="gate"/> is forced open
        /// (<see cref="AreaGate.ForceOpen"/>) since Max is landing past it, not behind it.</summary>
        private void ResumeAtEnteredGate(int areaIndex, int level, AreaGate gate)
        {
            _areaDirector.ClearAreasBeforeCheckpoint(areaIndex);
            _areaDirector.RestoreAreaAtLevel(areaIndex, level);

            if (_pickupDirector == null) _pickupDirector = FindFirstObjectByType<PickupDirector>();
            _pickupDirector?.ResetBruiserCountdown(areaIndex);

            gate.ForceOpen();

            Sentinel.DestroyAllActive();

            EnsurePlayer();
            RespawnJustInsideGate(gate, areaIndex, level);
            _areaDirector.SetCurrentArea(areaIndex);
        }

        /// <summary>MV-1057: the Home screen's DEV "FINAL AREA" entry point — jumps straight to this
        /// world's finale (its own final, boss-role area: <see cref="WorldConfig.AreaByIndex"/> of
        /// <see cref="WorldDials.areaCount"/>) with every prerequisite on the route already satisfied:
        /// every shed and Replicator strictly before it destroyed (<see cref="DestroyFactoriesBefore"/>,
        /// silently — no loot/VFX replay for a kill the player never made — then
        /// <see cref="FactoryCensus.RaiseCheckpointRestored"/> to catch the REPLICATORS/FACTORIES banner
        /// up to it, the same recompute a checkpoint RESUME already triggers for the same reason) and
        /// every condition-gated gate re-resolved open (<see cref="RefreshConditionGates"/>) before
        /// landing. The landing itself is <see cref="ResumeCheckpoint"/> verbatim, so Max arrives exactly
        /// as a RESUME into that area would — standing at its gate, in the area before it — with none of
        /// this written back to the slot's own save. A no-op (returns false) before <see cref="Configure"/>
        /// has wired this runner up, or if the config's own final area isn't boss-role (shouldn't happen
        /// for an authored world).</summary>
        public bool JumpToFinaleArea()
        {
            if (_areaDirector == null || _cfg?.dials == null) return false;

            int finaleIndex = _cfg.dials.areaCount;
            WorldArea finale = _cfg.AreaByIndex(finaleIndex);
            if (finale == null || !finale.IsBossRole) return false;

            DestroyFactoriesBefore(finaleIndex);
            FactoryCensus.RaiseCheckpointRestored();
            RefreshConditionGates();

            ResumeCheckpoint(finaleIndex);
            return true;
        }

        /// <summary>Every shed and Replicator strictly before <paramref name="targetAreaIndex"/>,
        /// destroyed — the same reach <see cref="MaxWorlds.Dev.DevModeController.TryJumpToArea"/>'s own
        /// replicator-only helper has (MV-940), extended to sheds since <see cref="JumpToFinaleArea"/>'s
        /// own prerequisite list (MV-1057) names both.</summary>
        private void DestroyFactoriesBefore(int targetAreaIndex)
        {
            foreach (Replicator r in _replicators)
                if (r != null && r.IsAlive && r.AreaIndex < targetAreaIndex)
                    r.ApplyCheckpointDestroyed();

            foreach (var (areaId, _, hutch) in _sheds)
            {
                if (hutch == null || !hutch.IsAlive) continue;
                WorldArea area = _cfg?.Area(areaId);
                if (area != null && area.index < targetAreaIndex)
                    hutch.ApplyCheckpointDestroyed();
            }
        }

        /// <summary>MV-665: reports every newly-dead shed into <see cref="_supply"/> — sheds no longer
        /// gate any door by role, but <see cref="SupplyLineNetwork"/> still needs to know a shed died for
        /// everything else it drives (supply lines). MV-703 adds
        /// the Replicator equivalent (no death event to subscribe to, so polled the same way) and then
        /// re-resolves every condition-gated gate's lock. Called every <see cref="Update"/> tick and
        /// public so anything else that needs a fresh resolution — a resume, a test — can force one
        /// directly.</summary>
        public void RefreshConditionGates()
        {
            if (_supply == null) return;

            for (int i = _sheds.Count - 1; i >= 0; i--)
            {
                (string _, string shedId, MowerHutch hutch) = _sheds[i];
                if (hutch != null && hutch.IsAlive) continue;

                _sheds.RemoveAt(i);
                _supply.DestroyShed(shedId);
            }

            for (int i = _replicators.Count - 1; i >= 0; i--)
            {
                Replicator replicator = _replicators[i];
                if (replicator != null && replicator.IsAlive) continue;

                _replicators.RemoveAt(i);
                if (replicator != null) FactoryCensus.ReportReplicatorDestroyed(replicator);
            }

            RefreshGateLocks();
        }

        /// <summary>Evaluates every condition-gated incoming gate (MV-703) and syncs
        /// <see cref="AreaGate.Locked"/>/<see cref="AreaGate.SetLockProgress"/> to it — the direct
        /// replacement for the role-driven lock this runner had before MV-665 stripped it, now keyed off
        /// the gate's own parsed <see cref="GateCondition"/> instead of the area's role. A gate whose
        /// condition is Start/Primary/Sluice is skipped entirely (<see cref="GateCondition.IsConditionGated"/>
        /// false) — exactly every World 1 gate today, so this is a no-op there. MV-829: every built
        /// hatch (<see cref="_hatchGates"/>) resolves through this exact same loop, right after.</summary>
        private void RefreshGateLocks()
        {
            if (_supply == null) return;

            foreach (KeyValuePair<int, GateCondition> kv in _gateConditionIntoArea)
            {
                GateCondition condition = kv.Value;
                if (!condition.IsConditionGated) continue;
                if (!_gateIntoArea.TryGetValue(kv.Key, out AreaGate gate) || gate == null) continue;

                // MV-571's "SHEDS 3 / 8" readout only makes sense for the two legacy shed kinds — a
                // replicators-destroyed gate has no progress count to show and falls back to the plain
                // "LOCKED" label (AreaGate.ReadoutName).
                if (condition.Kind == GateConditionKind.AllShedsDestroyed)
                {
                    _supply.ShedProgressBefore(int.MaxValue, out int destroyed, out int total);
                    gate.SetLockProgress(destroyed, total);
                }
                else if (condition.Kind == GateConditionKind.ShedsDestroyedBefore)
                {
                    _supply.ShedProgressBefore(kv.Key, out int destroyed, out int total);
                    gate.SetLockProgress(destroyed, total);
                }

                if (condition.IsSatisfied(_supply, kv.Key))
                {
                    bool wasLocked = gate.Locked;
                    gate.Locked = false;
                    if (wasLocked) gate.ForceOpen();
                }
                else
                {
                    gate.Locked = true;
                }
            }

            // MV-829: every hatch resolves through the exact same engine, one at a time rather than
            // one per area — see _hatchGates' own doc comment for why a single area-keyed dictionary
            // (the wall-gate shape above) doesn't fit a hatch.
            foreach (var (gate, condition, areaIndex) in _hatchGates)
            {
                if (!condition.IsConditionGated) continue;

                if (condition.IsSatisfied(_supply, areaIndex))
                {
                    bool wasLocked = gate.Locked;
                    gate.Locked = false;
                    if (wasLocked) gate.ForceOpen();
                }
                else
                {
                    gate.Locked = true;
                }
            }
        }

        /// <summary>Is the gate into <paramref name="areaIndex"/> condition-gated (MV-703) — the answer
        /// <see cref="ResumeCheckpoint"/>/<see cref="OnPlayerDied"/> need to decide whether a death
        /// re-closes it (never, for a condition-gated gate — re-closing it would be unreopenable).
        /// Prefers the gate's own parsed <see cref="GateCondition"/>; falls back to the area's role only
        /// when that gate is NOT condition-gated but the area is boss-role anyway (World 1 compatibility
        /// — every World 1 boss gate still authors "primary", not a condition string, logged once).</summary>
        private bool IsConditionGatedArea(int areaIndex)
        {
            if (_gateConditionIntoArea.TryGetValue(areaIndex, out GateCondition condition) && condition.IsConditionGated)
                return true;

            WorldArea area = _cfg?.AreaByIndex(areaIndex);
            if (area != null && area.IsBossRole)
            {
                if (!_loggedBossPrimaryCompat)
                {
                    _loggedBossPrimaryCompat = true;
                    Debug.Log($"MV-703: area '{area.id}' is boss-role but its incoming gate is not " +
                              "condition-gated (opensWith is still 'primary') — keeping the legacy " +
                              "role-driven reclose behaviour.");
                }
                return true;
            }

            return false;
        }

        private void Update()
        {
            RefreshConditionGates();

            UpdateShedAreaGating();

            // MV-591: the run ends when the FINAL area is empty — every robot dead, none still queued
            // to arrive, no boss alive. Not when a boss dies; a12 and a20 have bosses mid-run.
            if (!_runCompleteRaised && _areaDirector != null && _cfg?.dials != null)
            {
                int finalArea = _cfg.dials.areaCount;
                if (_areaDirector.CurrentArea >= finalArea
                    && _areaDirector.ActiveCount == 0
                    && _areaDirector.QueuedCount == 0
                    && !BossCensus.AnyLivingIn(finalArea))
                {
                    _runCompleteRaised = true;
                    HudSignals.EmitRunComplete();
                }
            }
        }

        /// <summary>MV-1093 (generalised from MV-456's destroyed-only version): a shed only produces
        /// while the player is PHYSICALLY standing in its own area — live, so a shed Max isn't fighting
        /// reads as dormant rather than a threat he can't see or answer, and once destroyed, so the
        /// field-wide <see cref="EnemySpawner.GlobalMaxLiveEnemies"/> cap is never starved by several
        /// OTHER destroyed sheds streaming unseen. Cheap to poll every frame — a world carries at most a
        /// handful of sheds.</summary>
        private void UpdateShedAreaGating()
        {
            if (_shedSpawners.Count == 0 || _map == null) return;
            EnsurePlayer();
            if (_player == null) return;

            MapZone zone = _map.ZoneAt(_player.position.x, _player.position.z);
            int playerArea = zone == null ? 0 : AreaAccumulationDirector.AreaIndexOf(zone.id);

            for (int i = 0; i < _shedSpawners.Count; i++)
            {
                (int areaIndex, EnemySpawner spawner) = _shedSpawners[i];
                if (spawner != null) spawner.SetAreaPaused(areaIndex != playerArea);
            }
        }

        /// <summary>Max fell (MV-427). The run doesn't end, but MV-438 stops it continuing silently:
        /// this only records the death, works out the plan, and shows the overlay — the actual area
        /// restore/gate-reclose/respawn (what this method did in full, pre-MV-438) waits in
        /// <see cref="Continue"/> for the player's own CONTINUE tap.</summary>
        private void OnPlayerDied()
        {
            if (_areaDirector == null || _cfg?.dials == null) return;

            EnsurePlayer();
            int deathArea = ResolveDeathArea();
            if (deathArea <= 0) return;   // no live area context (a headless fixture) — nothing to respawn into

            // MV-575: whether the gate into the death area re-closes is a property of the area (its
            // role), not of where it sits in the sequence — a boss area's gate opens on a shed
            // condition, never combat, and re-closing it would be unreopenable (a softlock).
            bool deathGateIsConditionGated = IsConditionGatedArea(deathArea);
            // MV-1091: which LEVEL Max actually died on — floor or deck — read straight off his real
            // position, the only honest signal for an in-place-deck area (one area id visited at both
            // elevations, World 2's a10/a11/a12): see ResolveRespawnPredecessor's onDeck param.
            bool onDeck = _map != null && _player != null &&
                          _map.IsOnDeck(_player.position.x, _player.position.y, _player.position.z);
            int predecessor = ResolveRespawnPredecessor(deathArea, onDeck);
            RespawnPlan plan = RespawnPlanner.Resolve(deathArea, deathGateIsConditionGated, predecessor);

            DeathRunState.RecordDeath();
            _pendingRespawn = plan;

            Time.timeScale = 0f;   // frozen until CONTINUE — nothing below this line until then
            ModalFrameRateGate.Enter();   // MV-574: idle the frame rate behind the death overlay

            WorldArea restoreArea = _cfg.AreaByIndex(plan.RestoreAreaIndex);
            string areaName = restoreArea != null ? restoreArea.name : $"Area {plan.RestoreAreaIndex}";

            if (_deathOverlay == null)
            {
                _deathOverlay = FindFirstObjectByType<DeathOverlay>();
                if (_deathOverlay == null) _deathOverlay = new GameObject("DeathOverlay").AddComponent<DeathOverlay>();
            }
            _deathOverlay.Show(areaName, plan.RecloseGate, DeathRunState.DeathsTaken, Continue);
        }

        /// <summary>CONTINUE was tapped (MV-438) — runs the deferred respawn sequence exactly as
        /// <see cref="OnPlayerDied"/> did in full before this ticket, then un-pauses. A no-op if there
        /// is nothing pending (e.g. a stray extra call), so this is always safe to wire straight to a
        /// button.
        ///
        /// MV-941: no longer calls <see cref="AreaAccumulationDirector.RestoreArea"/> on the death area.
        /// That used to wipe every live robot there — survivors included — and re-solve a brand-new
        /// authored composition, which directly contradicted Lee's rule ("if you destroyed those robots,
        /// they stay destroyed"): a robot still alive at the moment of death got despawned anyway, and
        /// every robot already destroyed before death came back. A death now leaves the arena's robot
        /// population exactly as it was — nothing here despawns or (re)spawns anything.
        /// <see cref="AreaAccumulationDirector.RestoreArea"/> itself is untouched and stays wired to
        /// <see cref="ResumeCheckpoint"/>, the cold-boot Home-screen RESUME path that lands into a
        /// freshly-built scene with no robots yet — a different situation this ticket doesn't touch.</summary>
        public void Continue()
        {
            if (!_pendingRespawn.HasValue) return;
            RespawnPlan plan = _pendingRespawn.Value;
            _pendingRespawn = null;

            if (plan.RecloseGate && _gateIntoArea.TryGetValue(plan.RestoreAreaIndex, out AreaGate gate) && gate != null)
                gate.Reclose();

            // Sentinels never travel between areas (MV-362/396) — a death is exactly as much an area
            // change as walking through a gate is.
            Sentinel.DestroyAllActive();

            RespawnPlayer(plan);
            _areaDirector.SetCurrentArea(plan.RespawnAreaIndex);

            Time.timeScale = 1f;
            ModalFrameRateGate.Exit();
        }

        private void EnsurePlayer()
        {
            if (_player != null) return;
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }

        /// <summary>Where Max is standing RIGHT NOW, in <see cref="RespawnPlanner.Resolve"/> terms —
        /// the area's real 1-based index, whether it's an ordinary area or a boss (MV-575: a boss area
        /// is a real numbered entry in <see cref="WorldConfig.areas"/>, translated to the same
        /// "area&lt;N&gt;" zone id as everything else by <see cref="WorldMapLoader"/> — there is no
        /// synthetic index past the end of the sequence for "the boss room"). Deliberately reads live
        /// position (<see cref="MapData.ZoneAt(float,float,float)"/>), not
        /// <see cref="AreaAccumulationDirector.CurrentArea"/>: that tracker is advanced ahead of the
        /// player for population purposes (MV-245) and never enters a boss zone at all
        /// (<c>BackyardPath.WireAreaGatesToPopulation</c> explicitly skips it), so a death fought
        /// against a boss still needs to read where Max actually is.
        ///
        /// MV-1002: the HEIGHT-AWARE 3-arg overload, not the 2-arg <c>ZoneAt(x,z)</c> this used to call
        /// — a deck overlay shares its exact XZ footprint with the floor it sits over (by construction,
        /// <see cref="WorldMapLoader"/> copies the floor's origin/size onto it), so the 2-arg lookup
        /// always resolved to whichever of the pair happens to iterate first (the floor, since it's
        /// authored first) regardless of Max's actual elevation — a death standing on World 2's a15
        /// deck was silently attributed to a13, the floor beneath it, never a15 itself.</summary>
        private int ResolveDeathArea()
        {
            if (_player == null || _map == null) return 0;

            MapZone zone = _map.ZoneAt(_player.position.x, _player.position.y, _player.position.z);
            return zone == null ? 0 : AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        /// <summary>MV-1002: the real physical-crossing predecessor of <paramref name="deathAreaIndex"/>
        /// (see <see cref="AreaAccumulationDirector.PredecessorOf"/>'s own doc comment for why raw
        /// index arithmetic is wrong), deck-aware — a death on a deck overlay
        /// (<see cref="WorldArea.overlays"/> non-null) uses its BASE FLOOR area's own predecessor
        /// instead of the deck's immediate one: dying anywhere in a floor/deck vertical pair (they
        /// share one footprint) behaves like dying at the floor itself, skipping past whatever setpiece
        /// area sits directly between the floor and its deck (World 2: a13's own predecessor, not a14
        /// "Replicator Nest", for a death on a15 "Trolley Yard (deck)"). Returns 0 ("unknown") when no
        /// crossing has been recorded yet, so <see cref="RespawnPlanner.Resolve"/>'s own
        /// <c>deathAreaIndex - 1</c> fallback applies.
        ///
        /// MV-1091: <paramref name="onDeck"/> is Max's real elevation at the moment this is asked —
        /// null from <see cref="ResumeCheckpoint"/> (unchanged: a cold-boot RESUME has no live death
        /// position to read one from, and MV-1065's own gantry-deck resume already resolves correctly
        /// without it, by the two visits having crossed through genuinely different gates), a real
        /// floor/deck reading from <see cref="OnPlayerDied"/>. Only changes the answer for an
        /// in-place-deck area (<see cref="AreaAccumulationDirector.PredecessorOf(int,bool)"/>'s own doc
        /// comment) — every other area ignores it.</summary>
        private int ResolveRespawnPredecessor(int deathAreaIndex, bool? onDeck = null)
        {
            if (_areaDirector == null || _cfg == null) return 0;

            int lookupArea = deathAreaIndex;
            WorldArea deathWorldArea = _cfg.AreaByIndex(deathAreaIndex);
            if (deathWorldArea != null && !string.IsNullOrEmpty(deathWorldArea.overlays))
            {
                WorldArea baseFloor = _cfg.Area(deathWorldArea.overlays);
                if (baseFloor != null) lookupArea = baseFloor.index;
            }

            return onDeck.HasValue
                ? _areaDirector.PredecessorOf(lookupArea, onDeck.Value)
                : _areaDirector.PredecessorOf(lookupArea);
        }

        private void RespawnPlayer(in RespawnPlan plan)
        {
            if (_player == null) return;

            TeleportPlayer(RespawnPoint(plan), "WorldRunner.RespawnPlayer");
            _playerHealth?.Revive();
        }

        /// <summary>MV-1096: the CC-safe teleport <see cref="RespawnPlayer"/> and
        /// <see cref="RespawnJustInsideGate"/> both need — same collider-disable/teleport/re-enable
        /// shape <see cref="MapRuntime.Adopt"/> uses to place Max at level start — a CharacterController
        /// caches its own position and would otherwise undo the teleport. MV-1021: same guard
        /// <see cref="MapRuntime.Adopt"/> now carries — see its own comment.</summary>
        private void TeleportPlayer(Vector3 point, string callerName)
        {
            var cc = _player.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            Vector3 before = _player.position;
            _player.position = point;
            if (cc != null && was)
            {
                if (CharacterControllerSafety.CanCreate(_player, cc, out string reason))
                {
                    cc.enabled = true;
                }
                else
                {
                    _player.position = before;
                    cc.enabled = true;
                    CharacterControllerSafety.LogRefusal(callerName, _player.name,
                        reason, point, _player.lossyScale);
                }
            }
            else if (cc != null)
            {
                cc.enabled = was;
            }
        }

        /// <summary>MV-1096: where Max lands for the gate-identified RESUME — just past
        /// <paramref name="gate"/>'s own door mouth, along its <see cref="AreaGate.AwayFromPlayerDirection"/>
        /// (which already points INTO <paramref name="areaIndex"/>, away from the room Max approached it
        /// from), at <paramref name="level"/>'s own elevation: the live-resolved deck height
        /// (<see cref="Garrison.ResolveLevelHeight"/>) for a deck gate, or the player's own current Y for
        /// a floor one — never a step back behind the gate the way a death's <see cref="RespawnPoint"/>
        /// lands, since this checkpoint was captured AFTER Max had already walked through it.</summary>
        private void RespawnJustInsideGate(AreaGate gate, int areaIndex, int level)
        {
            if (_player == null || gate == null) return;

            Vector3 dir = gate.AwayFromPlayerDirection;
            if (dir == Vector3.zero) dir = Vector3.forward;
            Vector3 point = gate.transform.position + dir.normalized * RespawnMarginFromGate;

            WorldArea area = _cfg?.AreaByIndex(areaIndex);
            point.y = level > 0 && area != null
                ? Garrison.ResolveLevelHeight(area, _cfg, new Vector2(point.x, point.z))
                : _player.position.y;

            TeleportPlayer(point, "WorldRunner.RespawnJustInsideGate");
            _playerHealth?.Revive();
        }

        /// <summary>Standing at the gate into the arena Max died in, from the near (respawn-area) side
        /// — the gate's own <see cref="AreaGate.AwayFromPlayerDirection"/> already points from that
        /// side toward the death arena, so stepping back along it lands Max at "the far end of the
        /// previous arena" the ticket asks for, whether that previous arena is a normal room or the
        /// entry stub.</summary>
        private Vector3 RespawnPoint(in RespawnPlan plan)
        {
            if (_gateIntoArea.TryGetValue(plan.RestoreAreaIndex, out AreaGate gate) && gate != null)
            {
                Vector3 dir = gate.AwayFromPlayerDirection;
                if (dir == Vector3.zero) dir = Vector3.forward;   // no map context wired in — shouldn't happen for a real world
                Vector3 p = gate.transform.position - dir.normalized * RespawnMarginFromGate;
                p.y = _player.position.y;
                return p;
            }

            string zoneId = plan.RespawnAreaIndex > 0 ? $"area{plan.RespawnAreaIndex}" : "stub";
            MapZone zone = _map.Zone(zoneId);
            return zone != null ? new Vector3(zone.x, _player.position.y, zone.z) : _player.position;
        }
    }
}
