using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Arena
{
    /// <summary>What a map build produced. The dressing pass needs the cover it actually got, not the
    /// cover that was authored — those differ the moment a piece fails validation.</summary>
    public sealed class MapBuild
    {
        public readonly List<CoverPiece> Cover = new List<CoverPiece>(8);
        public readonly Dictionary<string, GameObject> Actors = new Dictionary<string, GameObject>();

        /// <summary>The factories this map built, in the order it authored them.</summary>
        public readonly List<MowerHutch> Factories = new List<MowerHutch>(2);

        /// <summary>The Replicators this map built (MV-706), in the order it authored them.</summary>
        public readonly List<Replicator> Replicators = new List<Replicator>(2);

        /// <summary>The bosses this map built (MV-561), in the order it authored them.</summary>
        public readonly List<BigBermudaBoss> Bosses = new List<BigBermudaBoss>(2);
    }

    /// <summary>MV-882: combines every GameObject <see cref="MapRuntime.Build"/> hands it into one draw
    /// call per material, via the (gos, staticBatchRoot) overload — which leaves every object exactly
    /// where it was already parented (this map's own floor, walls, cover and static deck pieces stay
    /// direct children of the map root, unmoved; only used as the coordinate origin baked into the
    /// combined mesh), so no test or caller that navigates this hierarchy by path sees any difference.
    ///
    /// Deferred to <see cref="Start"/> rather than called inline at the end of Build: <c>BackyardPath.
    /// Awake</c> still has to run <c>ApplyWorldMaterials</c> (and, for World 2, <c>StormdrainDressing.
    /// Dress</c>; for World 3, <c>ReefKit.ApplyReefKit</c>) AFTER Build returns, to give this geometry
    /// its real biome material — and Unity's own static-batching caveat is that a material swapped onto
    /// a renderer AFTER it has been combined does not reliably render (the same hazard
    /// <see cref="MaxWorlds.Arena.BackyardDressing"/> avoids by dressing its kit props before calling
    /// <c>StaticBatchingUtility.Combine</c> itself). <see cref="Start"/> is guaranteed by Unity to run
    /// after every object's <c>Awake</c> has already fired this scene — the same guarantee
    /// <see cref="MaxWorlds.VFX.RuntimeSurfaceDirector"/>'s own one-shot sweep relies on — so every
    /// world's biome dressing has already landed by the time this fires. Public (not internal) so an
    /// EditMode test can read <see cref="Statics"/> straight off the built hierarchy (Rule 2: a
    /// resolved value, not an authored constant) — this project's EditMode test assembly carries no
    /// <c>InternalsVisibleTo</c> back to Gameplay.</summary>
    public sealed class MapStaticBatchRoot : MonoBehaviour
    {
        private GameObject[] _statics;
        private MapData _map;
        private Dictionary<Renderer, List<string>> _rendererZones;
        private AreaAccumulationDirector _areaDirector;
        private Transform _target;

        /// <summary>MV-925: the zone id <see cref="ApplyAreaGate"/> last gated to — the readout's own
        /// "which zone is the gate actually current on" figure (see <see cref="RecordRendererCensus"/>).</summary>
        private string _currentGateZoneId;

        /// <summary>MV-925: the active set (current zone + linked neighbours + footprint-sharing
        /// overlays) <see cref="ApplyAreaGate"/> last computed — kept so <see cref="Update"/> can ask
        /// "is the zone under Max's own feet actually lit right now" every frame without recomputing it.</summary>
        private HashSet<string> _activeZoneIds;

        /// <summary>MV-890: every tagged renderer that was ALREADY disabled the first time this class
        /// ever looks (snapshotted in <see cref="Start"/>, before the first <see cref="ApplyAreaGate"/>
        /// call) — a cover piece's own box, retired by <c>StormdrainDressing.Dress</c> (and
        /// <c>BackyardDressing</c>/<c>ReefDressing</c>'s own copies of the same "collider stays, art
        /// swaps" contract) via <c>Renderer.enabled = false</c> during the SAME <c>Awake</c>, before this
        /// component's own <c>Start</c> ever runs. Before this fix, <see cref="ApplyAreaGate"/> re-enabled
        /// one of these the moment its zone became current — a dressed box has no art of its own left, so
        /// turning it back on is exactly the reported "pipes encased in grey blocks" regression: the
        /// full-size collider box drawing again, swallowing the (still-enabled, never-gated) dressing prop
        /// sitting inside its footprint. A renderer in this set is never touched by the gate again, for
        /// the same reason <see cref="ApplyAreaGate"/>'s own doc already gives for an UNTAGGED renderer:
        /// once something else has taken over deciding whether it draws, the gate must leave it alone.</summary>
        private HashSet<Renderer> _dressedHidden;

        /// <summary>MV-937 diagnostic: how many times <see cref="ApplyAreaGate"/> has actually run for
        /// this instance — read by a test (or a future debug readout) to prove the self-heal in
        /// <see cref="Update"/> is not re-applying the gate needlessly while Max merely stands or walks
        /// inside a zone that is already active. Never consulted for a gameplay decision.</summary>
        public int ApplyAreaGateCallCount { get; private set; }

        /// <summary>Exactly what was handed to <see cref="StaticBatchingUtility.Combine"/> — every
        /// GameObject this build classified as never moving.</summary>
        public IReadOnlyList<GameObject> Statics => _statics;

        public void Configure(GameObject[] statics, MapData map, Dictionary<Renderer, List<string>> rendererZones)
        {
            _statics = statics;
            _map = map;
            _rendererZones = rendererZones;
        }

        private void Start()
        {
            if (_statics != null && _statics.Length > 0)
                StaticBatchingUtility.Combine(_statics, gameObject);

            // MV-887 change 5, widened by MV-904: the dressing kit (StormdrainDressing) is a SIBLING of
            // this map's own root, built later in the same Awake as MapRuntime.Build — by the time
            // Start() fires, Unity guarantees every Awake this frame has already run (the same guarantee
            // this class's own doc comment already leans on for the static-batch combine above), so its
            // whole "Stormdrain Dressing" host already exists and can be folded into the same per-zone
            // index MapRuntime.Build populated. MV-887 originally tagged only the "Sludge" child of that
            // host, so every OTHER thing StormdrainDressing.Dress builds — wall kerbs/pipes/lamps/
            // soffits, the overhead structure, the drain-machinery props that replace cover boxes, panel
            // joints/bays/stains, hazard-bulkhead fittings — stayed untagged and therefore always
            // enabled, everywhere, forever (this ticket's own measured defect: 0 of 16,106 non-sludge
            // dressing renderers tagged on the live build). Tagging the whole subtree by each renderer's
            // own resolved position is exactly the same idiom MapRuntime.TagStatic already uses for
            // ordinary props/cover/sludge/ramps/grates built directly under the map root.
            TagDressingSludge();

            // MV-890: snapshot BEFORE the first ApplyAreaGate call ever runs — see _dressedHidden's own
            // doc for why a renderer already disabled at this exact point must never be touched again.
            _dressedHidden = new HashSet<Renderer>();
            if (_rendererZones != null)
            {
                foreach (Renderer r in _rendererZones.Keys)
                    if (r != null && !r.enabled) _dressedHidden.Add(r);
            }

            // The area the gate starts with — Max's own physical area at map-build time, read off
            // AreaAccumulationDirector rather than assumed, so this never has to hard-code "area1"
            // separately from Configure()'s own convention.
            _areaDirector = Object.FindFirstObjectByType<AreaAccumulationDirector>();
            int startArea = _areaDirector != null ? _areaDirector.PhysicalArea : 1;
            ApplyAreaGate($"area{startArea}");

            // Re-evaluate on area change only (AC4) — never per frame. This is the exact signal
            // Replicator.OnAreaEntered already consumes for the same reason (see the ticket).
            if (_areaDirector != null)
                _areaDirector.PlayerCrossedIntoArea += OnPlayerCrossedIntoArea;
        }

        private void OnDestroy()
        {
            if (_areaDirector != null)
                _areaDirector.PlayerCrossedIntoArea -= OnPlayerCrossedIntoArea;
        }

        private void OnPlayerCrossedIntoArea(int area) => ApplyAreaGate($"area{area}");

        /// <summary>MV-925: the gate's own self-heal. <see cref="AreaAccumulationDirector"/>'s tracker
        /// (and the <see cref="OnPlayerCrossedIntoArea"/> event it fires) only ever advances between
        /// zones an authored <see cref="MapLink"/> actually joins — right for the ambient-population
        /// tracker's own accumulation rules, wrong for this gate, which must never leave Max standing
        /// somewhere its own walls and deck went dark. A Blink over a wall, or <see cref="MapData.ZoneAt(float,float,float)"/>
        /// resolving to an overlay that carries no <see cref="MapLink"/> of its own (World 2's a15/a17,
        /// this ticket's own measured trigger), both leave the tracker refusing to move while Max's real
        /// position has already jumped — <see cref="AreaAccumulationDirector.Update"/> logs "blocked an
        /// area-tracker jump" and holds, and nothing before this fix ever re-checked the gate again.
        ///
        /// Deliberately independent of the tracker entirely: this asks only "is the zone under Max's own
        /// feet part of the active set <see cref="ApplyAreaGate"/> last computed" — regardless of why it
        /// might not be — and re-applies the gate to THAT zone the instant it isn't. Never touches
        /// <see cref="AreaAccumulationDirector"/> or any of its garrison/accumulation state.</summary>
        private void Update()
        {
            if (_map == null || _rendererZones == null) return;

            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _target = p.transform;
            }

            MapZone zone = _map.ZoneAt(_target.position.x, _target.position.y, _target.position.z);
            if (zone == null) return;
            if (_activeZoneIds != null && _activeZoneIds.Contains(zone.id)) return;

            ApplyAreaGate(zone.id);
        }

        /// <summary>MV-887: enables every renderer this build tagged as belonging to
        /// <paramref name="currentZoneId"/> or to one of its gate-connected neighbours (read straight off
        /// <see cref="MapData.links"/> — the map's own zone/gate graph, not a separately invented
        /// adjacency source), disables every other tagged one. A renderer this build never tagged (the
        /// single map-spanning floor slab, and anything gameplay-owned — robots, replicators, factories,
        /// bosses, area gates, all explicitly out of scope per the ticket's own "renderers only, and not
        /// gameplay's renderers either" rule) is never touched here and keeps whatever state it already
        /// had.
        ///
        /// MV-890, fix 2: a renderer in <see cref="_dressedHidden"/> is skipped outright — see that
        /// field's own doc for why a dressed-away cover box must never come back.
        ///
        /// MV-890, fix 3: <paramref name="currentZoneId"/>'s own gate-linked neighbours are only half of
        /// what "active" means. A deck/hatch overlay (<see cref="MapZone.level"/> &gt; 0) shares its exact
        /// XZ footprint with the level-0 zone it roofs (<see cref="MapZone.ShareFootprint"/>, MV-697/
        /// MV-832) but is reached by a ramp, not a doorway — so it carries NO <see cref="MapLink"/> of its
        /// own to that floor zone, and the loop above alone never adds it to <c>active</c> no matter which
        /// floor zone is current. World 2's own data proves this is real, not hypothetical: "area17"
        /// (Junction Hall deck) links only sideways to area16/area18, never down to "area3" (Junction Hall
        /// floor) that it overlays — so a17's own renderers went dark for every player standing in a3
        /// looking up at them, exactly the reported "a17's upper floor is gone". Every zone sharing a
        /// footprint with an already-active one is folded in below, symmetrically (floor activates its
        /// deck and a deck, if ever current itself, keeps its own floor lit) — the same footprint test,
        /// not a second hand-authored adjacency list to drift out of sync with the first.</summary>
        public void ApplyAreaGate(string currentZoneId)
        {
            if (_rendererZones == null || string.IsNullOrEmpty(currentZoneId)) return;

            ApplyAreaGateCallCount++;

            var active = new HashSet<string> { currentZoneId };
            if (_map?.links != null)
            {
                foreach (MapLink link in _map.links)
                {
                    if (link == null) continue;
                    if (link.from == currentZoneId) active.Add(link.to);
                    else if (link.to == currentZoneId) active.Add(link.from);
                }
            }

            if (_map?.zones != null)
            {
                var activeZones = new List<MapZone>();
                foreach (MapZone z in _map.zones)
                    if (z != null && active.Contains(z.id)) activeZones.Add(z);

                foreach (MapZone z in _map.zones)
                {
                    if (z == null || active.Contains(z.id)) continue;
                    foreach (MapZone match in activeZones)
                    {
                        if (!MapZone.ShareFootprint(z, match)) continue;
                        active.Add(z.id);
                        break;
                    }
                }
            }

            foreach (KeyValuePair<Renderer, List<string>> pair in _rendererZones)
            {
                Renderer r = pair.Key;
                if (r == null) continue;
                if (_dressedHidden != null && _dressedHidden.Contains(r)) continue;
                r.enabled = pair.Value.Exists(active.Contains);
            }

            // MV-925 item 4: re-recorded on every call (not just once in Start), and item 2's own
            // self-heal check (Update, above) reads _activeZoneIds every frame — both need this call's
            // own result, not whatever the last call computed.
            _currentGateZoneId = currentZoneId;
            _activeZoneIds = active;
            RecordRendererCensus();
        }

        // MV-904: walks the WHOLE "Stormdrain Dressing" host, not just its "Sludge" child — see this
        // method's own call site (above) for why the narrower Sludge-only walk left every other dressing
        // renderer permanently enabled regardless of the area gate.
        //
        // MV-925 (lower-severity item 3): a wall panel run (StormdrainDressing.DressWallPanels' own
        // "Wall Panels" host, one "Wall Run" per StructuralWall it replaces) sits exactly ON a shared
        // boundary the same way the wall box itself does — a single-point probe can resolve it to only
        // ONE of the two zones it actually borders (MapData.ZoneAt is inclusive at both ends of a shared
        // edge), going permanently dark on the side that single point didn't land on. Every renderer
        // under a given "Wall Run" is tagged with the SAME two-sided zone ids TagWallZones already
        // resolves for the StructuralWall it replaces, not its own individual position.
        private void TagDressingSludge()
        {
            if (_map == null || _rendererZones == null) return;

            Transform areaRoot = transform.parent != null ? transform.parent : transform;
            Transform dressing = areaRoot.Find("Stormdrain Dressing");
            if (dressing == null) return;

            Transform wallPanels = dressing.Find("Wall Panels");
            List<WallSegment> wallSegments = wallPanels != null ? MapGeometry.Walls(_map) : null;

            foreach (Renderer r in dressing.GetComponentsInChildren<Renderer>(true))
            {
                List<string> ids = wallPanels != null && r.transform.IsChildOf(wallPanels)
                    ? WallPanelZoneIds(r.transform, wallPanels, wallSegments)
                    : null;

                if (ids == null)
                {
                    Vector3 p = r.transform.position;
                    MapZone zone = _map.ZoneAt(p.x, p.y, p.z) ?? MapRuntime.NearestFloorZone(_map, p.x, p.z);
                    if (zone == null) continue;
                    ids = new List<string>(1) { zone.id };
                }

                if (!_rendererZones.TryGetValue(r, out List<string> zones))
                    _rendererZones[r] = zones = new List<string>(ids.Count);
                foreach (string id in ids)
                    if (!zones.Contains(id)) zones.Add(id);
            }
        }

        /// <summary>MV-925: the two-sided zone ids for the <see cref="WallSegment"/> that
        /// <paramref name="rendererTransform"/>'s own "Wall Run" ancestor was built for — found by nearest
        /// centre match against <see cref="MapGeometry.Walls"/> (a pure function of the map, so it
        /// reproduces the exact same segments <c>MapRuntime.Build</c>'s own wall loop already built;
        /// <c>StormdrainKit.BuildWallPanels</c> plants each "Wall Run" at that segment's own
        /// <see cref="WallSegment.Center"/> with no transform in between). Null (falls back to the
        /// ordinary single-point probe) if <paramref name="rendererTransform"/> isn't under a "Wall Run"
        /// or no segment matches — should never happen for a real build, but never worth an exception if
        /// a future change to the dressing kit's own hierarchy ever breaks the assumption.</summary>
        private List<string> WallPanelZoneIds(Transform rendererTransform, Transform wallPanels, List<WallSegment> wallSegments)
        {
            if (wallSegments == null) return null;

            Transform run = rendererTransform;
            while (run != null && run.parent != wallPanels) run = run.parent;
            if (run == null) return null;

            WallSegment? best = null;
            float bestDistSqr = float.MaxValue;
            foreach (WallSegment seg in wallSegments)
            {
                float distSqr = (seg.Center - run.position).sqrMagnitude;
                if (distSqr < bestDistSqr) { bestDistSqr = distSqr; best = seg; }
            }
            if (best == null) return null;

            List<string> ids = MapRuntime.ResolveWallZoneIds(_map, best.Value);
            return ids.Count > 0 ? ids : null;
        }

        /// <summary>MV-886 item 2: a census of this area's own Renderer population, taken at the same
        /// "everything this area starts with already exists" point the static-batch combine above relies
        /// on (see class comment), and handed to <see cref="FrameCost"/> to cache and display — never
        /// recomputed per frame (AC3). MV-925: re-run on every <see cref="ApplyAreaGate"/> call (not just
        /// once, as originally built), so the readout's own "enabled" figure and gate zone id can never go
        /// stale the way the fixed-at-Start reading used to whenever the gate re-applied later. Walks up
        /// to this map's own parent (the transform
        /// <see cref="BackyardPath"/>/each world's own area script builds both the map and, for World 2,
        /// <c>StormdrainDressing</c>'s "Stormdrain Dressing" root under) rather than just this GameObject's
        /// own children, because dressing is a SIBLING of the map root, not nested under it — MapRuntime.
        /// Build and StormdrainDressing.Dress are both called with the same parent transform.
        ///
        /// Robots are not built here at all (they spawn later, from a factory/Replicator's own timer), so
        /// the robots bucket below genuinely read 0 the one time this ever ran (right after <c>Build</c>,
        /// before any robot existed) — before MV-925 made this a repeating call. Called again on a later
        /// gate change, it counts whatever is actually alive under <c>areaRoot</c> at that moment, same as
        /// every other bucket here — an honest live reading, not a second, competing source of truth for
        /// population (that stays <c>PopulationReadout</c>'s own job, its own line).</summary>
        private void RecordRendererCensus()
        {
            Transform areaRoot = transform.parent != null ? transform.parent : transform;

            Transform sludgeHost = null;
            Transform dressing = areaRoot.Find("Stormdrain Dressing");
            if (dressing != null) sludgeHost = dressing.Find("Sludge");

            int mapGeometry = 0, replicators = 0, robots = 0, sludge = 0, opaque = 0, transparent = 0, enabledCount = 0;

            // MV-887: counts every renderer regardless of Renderer.enabled now (the area gate disables
            // most of them outside the current area+neighbours) — the census's own job is to report the
            // WORLD's total population (AC3), with enabledCount reporting the gate's live effect on top,
            // not to silently shrink to "whatever happens to be on right now" the way the pre-gate skip
            // here used to (harmless before this ticket, since nothing but two invisible blockers ever
            // disabled a renderer).
            foreach (Renderer r in areaRoot.GetComponentsInChildren<Renderer>(false))
            {
                bool isSludge = sludgeHost != null && r.transform.IsChildOf(sludgeHost);
                bool isRobot = !isSludge && r.GetComponentInParent<RobotEnemy>() != null;
                bool isReplicator = !isSludge && !isRobot &&
                    (r.GetComponentInParent<Replicator>() != null ||
                     r.GetComponentInParent<MowerHutch>() != null ||
                     r.GetComponentInParent<BigBermudaBoss>() != null);

                if (isSludge) sludge++;
                else if (isRobot) robots++;
                else if (isReplicator) replicators++;
                else mapGeometry++;

                if (r.enabled) enabledCount++;

                Material mat = r.sharedMaterial;
                if (mat != null && mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent) transparent++;
                else opaque++;
            }

            FrameCost.RecordAreaRendererCensus(mapGeometry, replicators, robots, sludge, opaque, transparent, enabledCount, _currentGateZoneId);
        }
    }

    /// <summary>
    /// Builds a playable arena out of a <see cref="MapData"/> (YT-89): the floor, the walls, the cover
    /// and props — and then puts the actors where the map says they go, and wires them to each other
    /// the way the map says they connect.
    ///
    /// That second half is the point. The level's shape used to live in the scene YAML and the actors
    /// standing in it were placed separately by hand, so the two drifted apart and an editor scaffold
    /// existed for the sole purpose of shoving them back into agreement every time a number moved
    /// (Stage68). Now the map is the only thing that says where the factories stand, where the gate is,
    /// and which factories open it — so there is nothing left to keep in sync.
    ///
    /// Two kinds of actor, and the difference is exactly the one the level cares about:
    ///
    ///   * The ONE-OF actors — Max, the gate — are ADOPTED. The scene owns one of each and the map
    ///     moves and wires it.
    ///
    ///   * FACTORIES ARE BUILT (YT-92), and so is the BOSS (MV-561). A level can have as many of
    ///     either as it likes, so there is nothing for the scene to own one of, and the hand-placed
    ///     hutch (and boss) it used to own stand down. This is not just plumbing for a second factory:
    ///     the scene's copy of the hutch had been quietly carrying a body colour from three tickets
    ///     ago, overriding the code's, which is precisely the failure mode the code-driven-scenes rule
    ///     exists to stop. Built factories (and bosses) cannot disagree with each other or with the
    ///     code, because there is only one recipe each.
    ///
    /// Only shapes are set here. The stylised look is applied automatically by the rendering layer
    /// (flat → ground, tall → wall, short → prop), so nothing here touches a material — and so nothing
    /// here can ship magenta.
    /// </summary>
    public static class MapRuntime
    {
        public static MapBuild Build(MapData map, Transform parent)
        {
            var built = new MapBuild();
            if (map == null) return built;

            RetireLegacyGround();

            // Before anything registers: the scene's hand-placed hutch stands down, and the census
            // forgets whatever it may already have counted (its Awake may or may not have run — the
            // order of two Awakes in one scene load is nobody's to promise).
            RetireSceneFactories();
            FactoryCensus.Reset();

            // MV-829: a fresh level starts with no area-entered history — see AreaVisitCensus's own
            // doc comment for why "the last level's (or the last test's) areas" can never be trusted.
            AreaVisitCensus.Reset();

            // MV-561: the scene's hand-placed Big Bermuda (Stage27BossScaffold) stands down the same
            // way the hutch does above — the map builds its own boss(es) now, so a scene copy is not a
            // spare, it is an extra boss standing in the wrong place with no area of its own.
            RetireSceneBosses();

            // MV-542: same reasoning, one level up — a level loaded a second time must count its own
            // bosses, not the previous level's (or the previous test's) ghosts.
            BossCensus.Reset();

            // A new level is a new Invasion Level clock (YT-181) — a run's escalation must not carry
            // over from whatever the last level (or the last test) left it at.
            DifficultyDirector.Reset();

            // MV-774: same reasoning, for the World 2 FLOOD bar's own clock — a fresh run starts dry,
            // and reads this world's own authored combat-area count so its band-by-index stand-in
            // (BandForAreaIndex) never carries over a previous world's route length.
            StormdrainFlood.Reset();
            StormdrainFlood.Configure(CountCombatAreas(map));

            // Same reasoning, for the pump-housing count the flood's own drain term reads (MV-794) — a
            // world with no drain dressing at all (World 1, World 3) must read 0, not the last World 2
            // level's count. StormdrainDressing.Dress (called later, from this world's own dressing
            // sweep) overwrites this for real when there is a drain to dress.
            StormdrainDressing.Reset();

            // Same reasoning for the Blinker squad jump's cooldown (MV-366) — a fresh run starts its
            // own clock rather than inheriting whatever the last level left mid-countdown.
            BlinkerSquadDirector.Reset();

            // Belt-and-braces against a robot whose OnDisable hasn't run yet when the next level (or
            // test) starts counting toward the field-wide spawn budget (YT-186).
            RobotEnemy.ResetRegistry();

            // A stale attack-token count must never survive into the next level (MV-428) — the last
            // level's Rushers/Blinkers are all being wiped by the reset above without ever reaching
            // Recover to hand their tokens back.
            LungeTokenPool.Reset();

            // Sentinels aren't pooled — a fresh level must tear any leftover ones down outright, not
            // just forget them (MV-362).
            Sentinel.DestroyAllActive();

            // A new level is a new set of directions. The robots cache the map they navigate (YT-93),
            // and a cache that outlives its level would route this yard's robots around the last one's
            // walls.
            EnemyNavigation.Reset();

            // MV-773: a fresh map owns its own grates — the last level's (or the last test's) registered
            // GrateShudder instances are gone, so a stale entry can never eat a RobotEnemy's trigger.
            GrateShudder.ClearRegistry();

            var root = new GameObject($"Map: {map.name}").transform;
            root.SetParent(parent, false);

            // MV-882: every renderer built below that never moves once the map is up is collected here
            // and combined into one draw call per material (MapStaticBatchRoot.Start, above) instead of
            // submitting its own draw call every frame — measured at plausibly thousands per frame in a
            // dense World 2 area, since MapRuntime never batched anything (World 1's hand-authored
            // dressing layers — BackyardBackdrop/BackyardDressing/BackyardHomeShed/BackyardEntryDoor —
            // always did). Collecting into a list rather than reparenting under a new child root: the
            // (gos, staticBatchRoot) Combine overload doesn't require or create any such parenting, so
            // every object this method builds keeps the exact same parent it always had.
            var staticGeometry = new List<GameObject>(64);

            // MV-887: every renderer this build tags with the zone(s) it belongs to, so
            // MapStaticBatchRoot can enable only the ones in Max's current area and its gate-connected
            // neighbours (see that class's own ApplyAreaGate). A renderer never added here — the single
            // map-spanning "Map Floor" below, or anything gameplay-owned (robots, replicators, factories,
            // bosses, area gates) — is never gated and keeps behaving exactly as before.
            var rendererZones = new Dictionary<Renderer, List<string>>(512);

            FloorSlab floor = MapGeometry.Floor(map);
            // blocksSight: false — you cannot hide behind the ground, and a ground collider on the
            // cover layer would have every sight-line ray graze it. Never tagged (see rendererZones'
            // own doc above) — one slab spans the whole map, so there is no single zone to gate it by,
            // and it is cheap enough (one renderer) that leaving it always on costs nothing.
            AddStatic(Box(root, "Map Floor", floor.Center, floor.Size, blocksSight: false, isStatic: true), staticGeometry);

            foreach (WallSegment w in MapGeometry.Walls(map))
            {
                // MV-742: mark it a wall explicitly — WorldMaterials.KindOf's height heuristic alone
                // cannot tell a boundary wall from cover once a world authors walls shorter than its
                // own cover (World 2's wallHeight 1.5 m vs cover up to 1.6 m), and this box is built
                // knowing exactly what it is, so it says so rather than making KindOf guess from shape.
                GameObject wallGo = Box(root, w.Name, w.Center, w.Size, blocksSight: true, isStatic: true);
                ApplyBevelledBoxMesh(wallGo, w.Size);
                wallGo.AddComponent<StructuralWall>();
                TagWallZones(map, wallGo, w, rendererZones);
                AddStatic(map, wallGo, staticGeometry, rendererZones, autoTag: false);
            }

            BuildProps(map, root, staticGeometry, built, rendererZones);
            PlaceActors(map, root, built);
            WireGates(map, built);

            // MV-706: "REPLICATORS n/N" for a world with replicators and no sheds; "FACTORIES" wording
            // stays for a world (or a legacy fixture) that only ever has sheds — a world with neither
            // never shows this at all (HudModel's own count-driven visibility is unaffected either way).
            bool hasReplicators = built.Replicators.Count > 0;
            bool hasSheds = built.Factories.Count > 0;
            HudSignals.EmitWorldFactoryWording(hasReplicators && !hasSheds);

            // MV-741: this world's own Invasion Dial wording (empty means "use World 1's default"),
            // fired the same way and at the same point as the factory wording just above.
            HudSignals.EmitPressureWording(map.pressureNoun, map.pressureCaption);

            root.gameObject.AddComponent<MapStaticBatchRoot>().Configure(staticGeometry.ToArray(), map, rendererZones);

            return built;
        }

        /// <summary>MV-887: which zone(s) <paramref name="w"/> actually borders, found by probing just
        /// inside each side of the wall's own line rather than testing the wall's boundary coordinate
        /// itself — <see cref="MapZone.Contains"/> is inclusive at both ends, so a point exactly on a
        /// shared edge would resolve to whichever zone happens to come first in <c>map.zones</c>, not
        /// necessarily either (or both) of the zones the wall actually separates. Sampled at both ends of
        /// the run and its middle, not just the centre, because a long merged run (MapGeometry.Walls'
        /// own <c>Solids</c> pass) can span more than one zone's edge when consecutive rooms share a line
        /// with no doorway cut between them. A side with no room at all (<see cref="WallSegment.RoomLower"/>/
        /// <see cref="WallSegment.RoomUpper"/> false, or the map boundary) simply resolves to null and is
        /// skipped, never invented.</summary>
        private static void TagWallZones(MapData map, GameObject wallGo, WallSegment w,
            Dictionary<Renderer, List<string>> rendererZones)
        {
            Renderer renderer = wallGo.GetComponent<Renderer>();
            if (renderer == null) return;

            List<string> ids = ResolveWallZoneIds(map, w);
            if (ids.Count == 0) return;

            if (!rendererZones.TryGetValue(renderer, out List<string> zones))
                rendererZones[renderer] = zones = new List<string>(ids.Count);
            foreach (string id in ids)
                if (!zones.Contains(id)) zones.Add(id);
        }

        /// <summary>MV-925: <see cref="TagWallZones"/>'s own two-sided probe, extracted so a wall's OWN
        /// dressing (<see cref="MapStaticBatchRoot.WallPanelZoneIds"/>) can tag every piece of a wall's
        /// panel/rib/pilaster/coping/kerb run with the SAME zone ids the wall box itself carries, rather
        /// than resolving each small piece by its own single-point position — the "lower severity" defect
        /// this ticket also reports: a panel sitting exactly on a shared boundary can resolve to only one
        /// of the two zones it actually borders, going dark on the side that single point didn't land on.</summary>
        internal static List<string> ResolveWallZoneIds(MapData map, WallSegment w)
        {
            var ids = new List<string>(2);

            float length = w.AlongX ? w.Size.x : w.Size.z;
            float inset = Mathf.Max(map.wallThickness, 0.5f);
            float halfLength = length * 0.5f - 0.05f;
            float[] offsets = halfLength > 0f ? new[] { -halfLength, 0f, halfLength } : new[] { 0f };

            foreach (float offset in offsets)
            {
                Vector3 along = w.AlongX
                    ? new Vector3(w.Center.x + offset, w.Center.y, w.Center.z)
                    : new Vector3(w.Center.x, w.Center.y, w.Center.z + offset);

                Vector3 sideLower = w.AlongX
                    ? new Vector3(along.x, along.y, along.z - inset)
                    : new Vector3(along.x - inset, along.y, along.z);
                Vector3 sideUpper = w.AlongX
                    ? new Vector3(along.x, along.y, along.z + inset)
                    : new Vector3(along.x + inset, along.y, along.z);

                AddZoneId(map, sideLower, ids);
                AddZoneId(map, sideUpper, ids);
            }

            return ids;
        }

        private static void AddZoneId(MapData map, Vector3 worldPos, List<string> ids)
        {
            MapZone zone = map.ZoneAt(worldPos.x, worldPos.y, worldPos.z);
            if (zone != null && !ids.Contains(zone.id)) ids.Add(zone.id);
        }

        /// <summary>MV-887: resolves the zone at <paramref name="worldPos"/> and, if one exists, records
        /// <paramref name="renderer"/> as belonging to it — the shared primitive both <see cref="TagWallZones"/>
        /// and <see cref="TagStatic"/> build on.</summary>
        private static void TagRendererAt(MapData map, Renderer renderer, Vector3 worldPos,
            Dictionary<Renderer, List<string>> rendererZones)
        {
            MapZone zone = map.ZoneAt(worldPos.x, worldPos.y, worldPos.z);
            if (zone == null) return;

            if (!rendererZones.TryGetValue(renderer, out List<string> zones))
                rendererZones[renderer] = zones = new List<string>(2);
            if (!zones.Contains(zone.id)) zones.Add(zone.id);
        }

        /// <summary>MV-887: tags every renderer under <paramref name="go"/> by its own resolved position
        /// — correct for anything that sits comfortably inside one room (cover, props, sludge, a deck's
        /// own pieces), unlike a wall which sits exactly on a shared boundary and needs <see cref="TagWallZones"/>'s
        /// two-sided probe instead.
        ///
        /// MV-904: unlike <see cref="TagRendererAt"/> (used by <see cref="TagWallZones"/>, where a side
        /// with no room really does border nothing and must stay untagged — see that method's own "never
        /// invented" doc), a piece this method tags is documented to always sit inside SOME room. A few
        /// measurably don't (e.g. a sludge tile's own scattered bubble prop landing outside every zone's
        /// footprint) — those fall back to the nearest floor zone by rect distance rather than leaking
        /// through the area gate as permanently-enabled, the same fallback <see cref="TagDressingSludge"/>
        /// uses for the same reason.</summary>
        private static void TagStatic(MapData map, GameObject go, Dictionary<Renderer, List<string>> rendererZones)
        {
            if (go == null) return;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 p = r.transform.position;
                MapZone zone = map.ZoneAt(p.x, p.y, p.z) ?? NearestFloorZone(map, p.x, p.z);
                if (zone == null) continue;

                if (!rendererZones.TryGetValue(r, out List<string> zones))
                    rendererZones[r] = zones = new List<string>(2);
                if (!zones.Contains(zone.id)) zones.Add(zone.id);
            }
        }

        /// <summary>MV-904: the nearest zone to (<paramref name="px"/>, <paramref name="pz"/>) by distance
        /// to its own footprint rect (0 if the point already falls inside it) — the shared fallback both
        /// <see cref="TagStatic"/> and <see cref="TagDressingSludge"/> use when
        /// <see cref="MapData.ZoneAt(float,float,float)"/>'s exact containment test resolves to no zone
        /// at all (a kerb/pipe/overhead run hugging a long elevated corridor's OWN walls, where no floor
        /// zone sits underneath it — measured on World 2's own "area16", a 106 m deck with no underlying
        /// room), so it gates with whichever zone it visually reads as belonging to instead of leaking
        /// through the area gate as permanently-enabled forever.
        ///
        /// Every zone is a candidate, deck (<see cref="MapZone.level"/> &gt; 0) included: a standalone
        /// elevated corridor with nothing beneath it is the exact case this fallback exists for, and
        /// excluding it would silently reattribute its own dressing to whichever ordinary room happened
        /// to sit nearest instead — wrong both visually (it would light up in the wrong area) and for the
        /// gate's own measured effect (see this ticket's Jira comment for the count that caught it). Rect
        /// distance rather than centre distance so a long or irregularly-shaped zone (the same 106 m
        /// deck) is judged by how close the point actually sits to ITS footprint, not to its far-away
        /// centroid. Deliberately NOT used by <see cref="TagWallZones"/>: a wall genuinely bordering no
        /// room must stay untagged on that side, not backfilled to whatever zone happens to be nearest.</summary>
        internal static MapZone NearestFloorZone(MapData map, float px, float pz)
        {
            if (map?.zones == null) return null;

            MapZone best = null;
            float bestDistSqr = float.MaxValue;
            foreach (MapZone z in map.zones)
            {
                if (z == null) continue;
                float dx = Mathf.Max(0f, Mathf.Max(z.XMin - px, px - z.XMax));
                float dz = Mathf.Max(0f, Mathf.Max(z.ZMin - pz, pz - z.ZMax));
                float distSqr = dx * dx + dz * dz;
                if (distSqr < bestDistSqr) { bestDistSqr = distSqr; best = z; }
            }
            return best;
        }

        /// <summary>Everything the map creates from nothing: cover to fight around, and scenery with a
        /// body.</summary>
        private static void BuildProps(MapData map, Transform root, List<GameObject> staticGeometry, MapBuild built,
            Dictionary<Renderer, List<string>> rendererZones)
        {
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;

                switch (e.Kind)
                {
                    case EntityKind.Cover:
                    {
                        // MV-882: cover never moves — YT-78 already removed the only thing that ever
                        // swayed it (AmbienceVfx's old prop-sway pass, which the class's own doc
                        // comment records as having swayed nothing since YT-75, and was deleted rather
                        // than fixed) — so it batches with the floor/walls instead of costing its own
                        // draw call.
                        CoverPiece piece = BuildCover(root, e);
                        built.Cover.Add(piece);
                        AddStatic(map, piece.Body, staticGeometry, rendererZones);
                        break;
                    }

                    case EntityKind.Prop:
                    {
                        // Scenery with a body: it reads, and it breaks a sight-line, but the fight is
                        // not designed around it (the posts flanking the shed). Never moves — batches.
                        GameObject propGo = Box(root, e.id, e.GroundedCenter, e.Size, blocksSight: true, isStatic: true);
                        AddStatic(map, propGo, staticGeometry, rendererZones);
                        break;
                    }

                    case EntityKind.Pickup:
                        // MV-644: currently only PowerupCadence.EnsureCoverage's guaranteed parts cache
                        // authors this kind — the same walk-over reward a shed drop already knows how to
                        // give (PickupDirector.PlacePartsCache), placed statically since there is no
                        // factory death here to hook it off.
                        PickupDirector.EnsureInstalled().PlacePartsCache(e.GroundedCenter);
                        break;

                    case EntityKind.Sludge:
                    {
                        // MV-882: excluded from the static batch — it flows (SludgeFlow scrolls its
                        // own material's UVs every frame).
                        GameObject sludgeGo = BuildSludge(map, root, e);
                        TagStatic(map, sludgeGo, rendererZones);
                        break;
                    }

                    case EntityKind.Deck:
                        BuildDeck(map, root, staticGeometry, e, rendererZones);
                        break;

                    case EntityKind.Ramp:
                    {
                        // MV-882: excluded from the static batch per the ticket's own instruction.
                        GameObject rampGo = BuildRamp(map, root, e);
                        TagStatic(map, rampGo, rendererZones);
                        break;
                    }

                    case EntityKind.Hatch:
                        // MV-882: excluded from the static batch — a hatch is an AreaGate and opens.
                        // MV-887: an AreaGate is gameplay (HP, hinge, opens on destruction), so it is
                        // never tagged/gated here — same exemption as BuildAreaGate below.
                        BuildHatch(map, e, root, built);
                        break;

                    case EntityKind.Grate:
                    {
                        // MV-882: excluded from the static batch — GrateShudder moves each grille bar's
                        // local position when a garrisoned Lurker rises through it (MV-773), so a grate
                        // combined into a static mesh would freeze mid-shudder or shudder in place while
                        // the (baked) visual stayed put.
                        GameObject grateGo = StormdrainKit.BuildGrate(root, e.id, new Vector2(e.x, e.z));
                        TagStatic(map, grateGo, rendererZones);
                        break;
                    }
                }
            }
        }

        /// <summary>MV-785: retoned onto <see cref="StormdrainKit.Sludge"/> — closing the divergence
        /// MV-783 documented on that constant (it retoned the dressing kit's own copy but explicitly left
        /// this tile fill out of scope). No longer acid-green (MV-692's original value).</summary>
        private static readonly Color SludgeColor = new Color(0.200f, 0.300f, 0.115f);

        /// <summary>W2 TEAL (MV-690, palette bridge #2fa3b0) — what the sludge grades toward within
        /// <see cref="OutfallGradeRadius"/> of the outfall gate, per the palette bridge's "teal in,
        /// acid-green out": seawater pushing in through the broken outfall before the flow has had
        /// time to turn.</summary>
        private static readonly Color SludgeTealColor = new Color(0.18f, 0.64f, 0.69f);

        /// <summary>How fast a sludge tile's own material scrolls (MV-690) — closing the "[ART]
        /// follow-up gives it a flowing UV scroll" note this file used to carry on
        /// <see cref="SludgeColor"/>.</summary>
        private static readonly Vector2 SludgeScrollSpeed = new Vector2(0f, 0.12f);

        /// <summary>The id a world authors on the one <see cref="EntityKind.AreaGate"/> the sludge grades
        /// toward (MV-690) — e.g. World 2's a1 west wall, "the sludge rises where the sea gets in".
        /// Authoring convention, not a schema field: a gate is marked the outfall purely by giving it
        /// this id, the same way <c>"big_bermuda"</c> names a boss without a dedicated flag.</summary>
        private const string OutfallGateId = "outfall";

        /// <summary>Metres from the outfall gate a sludge tile still grades toward teal (MV-690, the
        /// ticket's own number).</summary>
        private const float OutfallGradeRadius = 12f;

        /// <summary>Flat grate grey (MV-692) — same "flat colour is acceptable" scope as
        /// <see cref="SludgeColor"/>.</summary>
        private static readonly Color DeckGrateColor = new Color(0.42f, 0.44f, 0.47f);

        private const float SludgeThickness = 0.05f;

        /// <summary>MV-821: the rail's replacement — a flat hazard edge at most this tall above the
        /// deck top (the ticket's own cap: "nothing on an edge may stand taller than 0.10 m"), striped
        /// with the approved <see cref="StormdrainKit.BuildHazardBanding"/> banding so the edge reads
        /// as a painted hazard line rather than a barrier.</summary>
        private const float DeckEdgeBandHeight = 0.10f;

        /// <summary>The ticket's own "0.12 m-wide painted stripe" figure — the banding plate's depth
        /// across the deck's outer edge.</summary>
        private const float DeckEdgeBandWidth = 0.12f;

        /// <summary>A visible structural beam under each deck edge (MV-821 change 2), built entirely
        /// OUTSIDE the deck's own footprint (hung off the outer face) so it never counts as infill under
        /// the walkway.</summary>
        private const float DeckEdgeBeamDepth = 0.15f;
        private const float DeckEdgeBeamThickness = 0.10f;

        /// <summary>Support posts (MV-821 change 2): 0.2 m square, floor to slab underside, at every
        /// corner and at no more than this spacing along each edge — the only thing allowed to occupy
        /// the open space under a deck.</summary>
        private const float DeckPostSize = 0.2f;
        private const float DeckPostSpacing = 4.0f;

        /// <summary>The ground shadow band under a deck's footprint (MV-821 change 3) — a flush decal,
        /// proud of the floor by a hair (same idiom as <see cref="StormdrainKit.HazardStripeProud"/>),
        /// so it reads as covered floor rather than as an obstruction under the walkway.</summary>
        private const float DeckShadowThickness = 0.02f;
        private const float DeckShadowProud = 0.01f;
        private const float DeckShadowDarken = 0.30f;

        /// <summary>MV-852 — a walled deck's parapet: 1.0 m tall, built in the same
        /// <see cref="StormdrainKit.BuildHazardBanding"/> look as the ordinary MV-821 edge band, but
        /// with a real, non-stripped collider (a plain invisible box coincident with the visual) that
        /// blocks Max/robots. Projectiles pass through untouched with no extra work: every raycast this
        /// game's weapons run against world geometry (<see cref="MaxWorlds.Weapons.HomingSteering.BlockedByGeometry"/>)
        /// is restricted to <see cref="CoverLayer"/>, which this parapet is never assigned to.</summary>
        private const float DeckParapetHeight = 1.0f;

        /// <summary>The parapet's own collision-only box thickness across the deck's outer edge — same
        /// figure as <see cref="DeckEdgeBandWidth"/>, just given its own name since the two no longer
        /// always share a wall (a walled deck skips the ordinary band/beam entirely).</summary>
        private const float DeckParapetThickness = 0.12f;

        private const float RampThickness = 0.15f;

        /// <summary>Roughly one bubble emitter's worth of bubbles per this many square metres of sludge
        /// (MV-769) — low density, so a lane reads as textured without turning into a fizzing bath.</summary>
        private const float SludgeBubbleDensityArea = 12f;

        /// <summary>A sludge slow-zone's ground overlay (MV-692) — visual only, no collider: a mover's
        /// slow is decided by <see cref="MapSlowZones"/> sampling its footprint, not by a physical
        /// trigger, so nothing here needs to catch anything. MV-690 gives it a flowing UV scroll
        /// (<see cref="SludgeFlow"/>) and grades its tone toward teal near the outfall. MV-769 adds the
        /// same rising-bubble treatment the Sludge Drone's own puddle now carries, at a low density, so
        /// every body of sludge in the world reads as one living material.</summary>
        private static GameObject BuildSludge(MapData map, Transform root, MapEntity e)
        {
            GameObject body = Spawn(root, e.id, PrimitiveType.Cube,
                new Vector3(e.x, SludgeThickness * 0.5f, e.z), new Vector3(e.width, SludgeThickness, e.depth));
            StripCollider(body);
            body.isStatic = true;

            Color tone = SludgeToneAt(map, e.CenterXz);
            Material material = MaterialLibrary.Tinted(SurfaceKind.Prop, tone);
            Tint(body, material);
            body.AddComponent<SludgeFlow>().Configure(material, SludgeScrollSpeed);

            int seed = Mathf.RoundToInt(e.x * 977f + e.z * 733f);
            int bubbleCount = Mathf.Max(1, Mathf.RoundToInt((e.width * e.depth) / SludgeBubbleDensityArea));
            SludgeBubbles.Attach(body.transform, "Bubbles", new Vector2(e.width * 0.5f, e.depth * 0.5f),
                SludgeThickness * 0.5f, seed, bubbleCount, tone);

            return body;
        }

        /// <summary>Plain acid green, graded toward <see cref="SludgeTealColor"/> the closer this tile
        /// stands to the map's outfall gate (MV-690) — "teal in, acid-green out". A map with no gate
        /// carrying <see cref="OutfallGateId"/> grades nothing, which is exactly what every non-Stormdrain
        /// map (and today's placeholder World 2 config, which authors no outfall gate yet) already
        /// does: plain acid green, same as MV-692 shipped.</summary>
        private static Color SludgeToneAt(MapData map, Vector2 at)
        {
            MapEntity outfall = map.Entity(OutfallGateId);
            if (outfall == null || outfall.Kind != EntityKind.AreaGate) return SludgeColor;

            float distance = Vector2.Distance(at, outfall.CenterXz);
            if (distance >= OutfallGradeRadius) return SludgeColor;

            float t = 1f - distance / OutfallGradeRadius;
            return Color.Lerp(SludgeColor, SludgeTealColor, t);
        }

        /// <summary>A deck's walkable top slab, built as an open raised walkway rather than a walled
        /// block (MV-821): a flat hazard edge (skipped on whichever wall a ramp/bridge actually climbs
        /// into, so the mouth stays open, same as the rails it replaces), a structural edge beam and
        /// corner/spacing posts holding it up with the floor beneath left visibly open, and a darker
        /// ground shadow so that open floor still reads as covered space Max can walk into. Carries
        /// <see cref="DeckVisibility"/> so the grate still fades out from directly under Max (readability
        /// rule, change item 5) — it now has no rails to hide.
        ///
        /// MV-882: the slab itself (<c>body</c>) is deliberately left OUT of the static batch — it
        /// carries <see cref="DeckVisibility"/>, which repaints it every frame via a
        /// MaterialPropertyBlock while Max stands underneath, and a combined static renderer does not
        /// reliably take a material/property-block change after it has been batched. Everything else
        /// this method builds (parapet, edge band, edge beam, posts, ground shadow) never moves or
        /// repaints, so those are added to <paramref name="staticGeometry"/> instead.</summary>
        private static void BuildDeck(MapData map, Transform root, List<GameObject> staticGeometry, MapEntity e,
            Dictionary<Renderer, List<string>> rendererZones)
        {
            DeckSlab slab = default;
            bool found = false;
            foreach (DeckSlab d in MapGeometry.Decks(map))
                if (d.Id == e.id) { slab = d; found = true; break; }
            if (!found) return;

            GameObject body = Spawn(root, e.id, PrimitiveType.Cube, slab.Center, slab.Size);
            ApplyBevelledBoxMesh(body, slab.Size);
            Tint(body, MaterialLibrary.Tinted(SurfaceKind.Metal, DeckGrateColor));
            body.isStatic = false; // MV-692: DeckVisibility repaints it every frame it's near Max
            TagStatic(map, body, rendererZones); // MV-887: excluded from AddStatic, tagged directly

            HashSet<Wall> mouths = DeckMouthWalls(map, e);
            foreach (Wall wall in AllWalls)
            {
                if (mouths.Contains(wall)) continue;
                if (e.walled)
                {
                    // MV-859: a [DECK] gate's own doorway can meet this exact edge. MV-899: a SECOND
                    // deck at the same height can also occupy the far side of this edge, at a corner or
                    // T-join where a walkway bends — either way the parapet must open a gap rather than
                    // standing solid across ground Max can already walk onto, the way MV-852 built every
                    // edge blind to both.
                    var holes = AbuttingDeckSpans(map, e, wall);
                    if (TryDeckGateSpan(map, e, wall, out Span gateSpan)) holes.Add(gateSpan);
                    BuildDeckParapetOpenAt(map, root, staticGeometry, e, wall, slab.TopY, holes, rendererZones);
                    continue;
                }
                BuildDeckEdgeBand(map, root, staticGeometry, e, wall, slab.TopY, rendererZones);
                BuildDeckEdgeBeam(map, root, staticGeometry, e, wall, slab.TopY, rendererZones);
            }

            BuildDeckPosts(map, root, staticGeometry, e, slab.TopY, rendererZones);
            BuildDeckGroundShadow(map, root, staticGeometry, e, slab.TopY, rendererZones);

            var footprint = new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
            body.AddComponent<DeckVisibility>().Configure(
                body.GetComponent<Renderer>(), System.Array.Empty<GameObject>(), footprint, slab.TopY);
        }

        private static readonly Wall[] AllWalls = { Wall.N, Wall.E, Wall.S, Wall.W };

        /// <summary>Which of this deck's own walls a ramp actually climbs into (MV-692) — the mirror of
        /// the ramp's own <see cref="RampSlab.ClimbsToward"/>, found by matching each ramp's resolved
        /// top point against this deck's footprint — PLUS, for a bridge deck (MV-711), the two short
        /// ends it meets its areas on, carried on the otherwise-ramp-only <see cref="MapEntity.facing"/>
        /// field as a comma-separated wall list (<see cref="WorldMapLoader"/>'s "reuse the shape, not
        /// the meaning" idiom) — a bridge has no ramp, but Max must still be able to walk straight onto
        /// and off it, so those two ends stay open exactly like a ramp mouth does.</summary>
        private static HashSet<Wall> DeckMouthWalls(MapData map, MapEntity deck)
        {
            var mouths = new HashSet<Wall>();
            const float tolerance = 0.05f;
            var expanded = new Rect(deck.x - deck.width * 0.5f - tolerance, deck.z - deck.depth * 0.5f - tolerance,
                deck.width + tolerance * 2f, deck.depth + tolerance * 2f);

            foreach (RampSlab ramp in MapGeometry.Ramps(map))
            {
                var top = new Vector2(ramp.TopCenter.x, ramp.TopCenter.z);
                if (expanded.Contains(top)) mouths.Add(WallEnums.Opposite(ramp.ClimbsToward));
            }

            if (!string.IsNullOrEmpty(deck.facing))
                foreach (string token in deck.facing.Split(','))
                    if (WallEnums.TryParse(token.Trim(), out Wall w)) mouths.Add(w);

            return mouths;
        }

        /// <summary>The flat hazard edge that replaces the old 1.5 m rail (MV-821 change 1): the
        /// approved <see cref="StormdrainKit.BuildHazardBanding"/> banding, capped at
        /// <see cref="DeckEdgeBandHeight"/> and rising from the deck top rather than blocking the view
        /// down onto (or off) the walkway. Positioned exactly on the deck's outer edge, same convention
        /// the rail it replaces used.</summary>
        private static void BuildDeckEdgeBand(MapData map, Transform root, List<GameObject> staticGeometry, MapEntity deck, Wall wall, float topY,
            Dictionary<Renderer, List<string>> rendererZones)
        {
            float halfW = deck.width * 0.5f, halfD = deck.depth * 0.5f;
            float bandCenterY = topY + DeckEdgeBandHeight * 0.5f;
            Vector3 centre;
            float length;
            bool alongX;
            switch (wall)
            {
                case Wall.N:
                    centre = new Vector3(deck.x, bandCenterY, deck.z + halfD);
                    length = deck.width; alongX = true;
                    break;
                case Wall.S:
                    centre = new Vector3(deck.x, bandCenterY, deck.z - halfD);
                    length = deck.width; alongX = true;
                    break;
                case Wall.E:
                    centre = new Vector3(deck.x + halfW, bandCenterY, deck.z);
                    length = deck.depth; alongX = false;
                    break;
                default: // Wall.W
                    centre = new Vector3(deck.x - halfW, bandCenterY, deck.z);
                    length = deck.depth; alongX = false;
                    break;
            }

            GameObject band = StormdrainKit.BuildHazardBanding(root, centre, length, DeckEdgeBandHeight, alongX, DeckEdgeBandWidth);
            band.name = $"{deck.id}_edge_{wall}";
            AddStatic(map, band, staticGeometry, rendererZones);
        }

        /// <summary>MV-899: the sub-span(s) of <paramref name="deck"/>'s <paramref name="wall"/> edge
        /// that ANOTHER deck rect authored in the SAME area already occupies on the far (outward) side
        /// of that line — two rects of one bending walkway meeting flush edge-to-edge (their footprints
        /// just touch), or one rect's edge running straight through a perpendicular rect's footprint at
        /// a corner or T-join (the bug this ticket reports — see the Jira comment for World 2's own
        /// abutting pairs), both show up here. A deck that merely touches the line without extending
        /// past it (its own far edge exactly on the line, same as <paramref name="deck"/>'s) does NOT
        /// count — that is two rects independently ending at the same place, not one continuing past the
        /// other.
        ///
        /// Deliberately scoped to decks in the SAME area (same <c>"&lt;areaId&gt;_deck..."</c> id prefix
        /// every world config's own decks already follow — MV-821's own test already relies on the same
        /// convention). A bending walkway is always authored as multiple rects inside ONE area's own
        /// <c>decks</c> list (there is no other way to author it); two DIFFERENT areas' decks meeting
        /// edge-to-edge (World 2's own a14/a15, MV-859's g32) are joined by an <see cref="AreaGate"/> on
        /// purpose — that boundary must stay a real wall except exactly at the gate's own span
        /// (<see cref="TryDeckGateSpan"/>), never fall open just because the two rects happen to touch.</summary>
        private static List<Span> AbuttingDeckSpans(MapData map, MapEntity deck, Wall wall)
        {
            var spans = new List<Span>();
            const float heightTolerance = 0.05f;
            const float lineTolerance = 0.05f;
            string areaPrefix = DeckAreaPrefix(deck.id);

            bool alongX = wall == Wall.N || wall == Wall.S;
            float halfW = deck.width * 0.5f, halfD = deck.depth * 0.5f;
            float edgeCoord = wall switch
            {
                Wall.N => deck.z + halfD,
                Wall.S => deck.z - halfD,
                Wall.E => deck.x + halfW,
                _ => deck.x - halfW, // W
            };
            float wallMin = alongX ? deck.x - halfW : deck.z - halfD;
            float wallMax = alongX ? deck.x + halfW : deck.z + halfD;
            bool outwardIsMax = wall == Wall.N || wall == Wall.E; // outward = +coord for N/E, -coord for S/W

            foreach (DeckSlab other in MapGeometry.Decks(map))
            {
                if (other.Id == deck.id) continue;
                if (DeckAreaPrefix(other.Id) != areaPrefix) continue;
                if (Mathf.Abs(other.TopY - deck.height) > heightTolerance) continue;

                float oHalfW = other.Size.x * 0.5f, oHalfD = other.Size.z * 0.5f;
                float perpMin = alongX ? other.Center.z - oHalfD : other.Center.x - oHalfW;
                float perpMax = alongX ? other.Center.z + oHalfD : other.Center.x + oHalfW;

                // The other deck must reach the line (no gap) AND genuinely extend past it on the
                // outward side — merely touching from the SAME side as this deck's own body is not an
                // abutment, it is two decks independently ending at the same coordinate.
                bool reachesAndCrosses = outwardIsMax
                    ? perpMin <= edgeCoord + lineTolerance && perpMax > edgeCoord + lineTolerance
                    : perpMax >= edgeCoord - lineTolerance && perpMin < edgeCoord - lineTolerance;
                if (!reachesAndCrosses) continue;

                float alongMin = alongX ? other.Center.x - oHalfW : other.Center.z - oHalfD;
                float alongMax = alongX ? other.Center.x + oHalfW : other.Center.z + oHalfD;
                float from = Mathf.Max(wallMin, alongMin);
                float to = Mathf.Min(wallMax, alongMax);
                if (to - from > lineTolerance) spans.Add(new Span(from, to));
            }
            return spans;
        }

        /// <summary>MV-899: the area a deck id belongs to, per the <c>"&lt;areaId&gt;_deck..."</c>
        /// convention every world config's own decks author (verified against all three shipped
        /// configs). Falls back to the whole id for anything that doesn't match — a bridge deck (e.g.
        /// <c>"b1"</c>) never collides with a real area id this way, so it simply never matches any
        /// area's own decks, exactly as it should (a bridge's own ends already open via
        /// <see cref="DeckMouthWalls"/>, never via this path).</summary>
        private static string DeckAreaPrefix(string deckId)
        {
            int idx = deckId.IndexOf("_deck", System.StringComparison.Ordinal);
            return idx >= 0 ? deckId.Substring(0, idx) : deckId;
        }

        /// <summary>MV-852 — a walled deck's parapet, replacing the ordinary open edge band/beam on
        /// every non-mouth wall: the SAME <see cref="StormdrainKit.BuildHazardBanding"/> visual, just
        /// <see cref="DeckParapetHeight"/> tall instead of <see cref="DeckEdgeBandHeight"/>, plus a
        /// separate, coincident, invisible box that keeps its default (non-stripped) collider — the one
        /// thing every other deck-edge primitive in this file deliberately strips. Never assigned to
        /// <see cref="CoverLayer"/>, so it blocks Max/robots (ordinary <c>CharacterController.Move</c>
        /// collision, layer-agnostic) while every projectile in the game passes straight through it.
        ///
        /// MV-899: <paramref name="holes"/> generalises MV-859's single gate-doorway opening to any
        /// number of spans to leave open — a gate's own span, another deck abutting this edge (see
        /// <see cref="AbuttingDeckSpans"/>), or both at once. Empty holes builds one continuous parapet,
        /// exactly as before.</summary>
        private static void BuildDeckParapetOpenAt(MapData map, Transform root, List<GameObject> staticGeometry, MapEntity deck, Wall wall, float topY,
            List<Span> holes, Dictionary<Renderer, List<string>> rendererZones)
        {
            bool alongX = wall == Wall.N || wall == Wall.S;
            float halfW = deck.width * 0.5f, halfD = deck.depth * 0.5f;
            float wallMin = alongX ? deck.x - halfW : deck.z - halfD;
            float wallMax = alongX ? deck.x + halfW : deck.z + halfD;
            float fixedCoord = wall switch
            {
                Wall.N => deck.z + halfD,
                Wall.S => deck.z - halfD,
                Wall.E => deck.x + halfW,
                _ => deck.x - halfW, // W
            };
            float centerY = topY + DeckParapetHeight * 0.5f;

            if (holes.Count == 0)
            {
                BuildParapetSpan(map, root, staticGeometry, deck, wall, alongX, fixedCoord, centerY, wallMin, wallMax, 0, rendererZones);
                return;
            }

            holes.Sort((a, b) => a.Min.CompareTo(b.Min));
            float cursor = wallMin;
            int index = 0;
            foreach (Span hole in holes)
            {
                float from = cursor, to = Mathf.Min(hole.Min, wallMax);
                if (to - from > 0.05f)
                    BuildParapetSpan(map, root, staticGeometry, deck, wall, alongX, fixedCoord, centerY, from, to, ++index, rendererZones);
                cursor = Mathf.Max(cursor, hole.Max);
            }
            if (wallMax - cursor > 0.05f)
                BuildParapetSpan(map, root, staticGeometry, deck, wall, alongX, fixedCoord, centerY, cursor, wallMax, ++index, rendererZones);
        }

        /// <summary>MV-859: does a <c>[DECK]</c> gate's own doorway meet this deck's edge on
        /// <paramref name="wall"/>? <see cref="MapGeometry.Doorway"/> is still used to CONFIRM which
        /// link/gate belongs on this wall (same resolution <see cref="MapGeometry.Walls"/> itself uses),
        /// but MV-919: its own hole is centred and sized against the two AREAS' full shared wall — for a
        /// floor-level gate that IS the deck's own edge, but a deck's edge is only a narrow slice of that
        /// wall (World 2's decks sit at one end of a much taller area rect), so trusting that hole
        /// verbatim could leave it straddling the deck's own bounds: mostly outside them at one end
        /// (wasted) and short of covering the doorway's own reach at the other, leaving a stray sliver of
        /// parapet neither the doorway nor the deck's own genuine edge needs (World 2's a15/a10/a11/a12/a16
        /// gate joins, confirmed on the live build - see the MV-919 Jira comment for the measured before/
        /// after). The fix re-centres the SAME authored doorway width on the gate's own resolved
        /// position, clamped into THIS deck's own span — never wider than the deck's own edge, and never
        /// escaping it — so the opening is always exactly what the gate needs and nothing the deck's own
        /// edge doesn't already own.</summary>
        private static bool TryDeckGateSpan(MapData map, MapEntity deck, Wall wall, out Span span)
        {
            span = default;
            if (map.links == null) return false;

            bool wallAlongX = wall == Wall.N || wall == Wall.S;
            float halfW = deck.width * 0.5f, halfD = deck.depth * 0.5f;
            float wallCoord = wall switch
            {
                Wall.N => deck.z + halfD,
                Wall.S => deck.z - halfD,
                Wall.E => deck.x + halfW,
                _ => deck.x - halfW, // W
            };
            float spanMin = wallAlongX ? deck.x - halfW : deck.z - halfD;
            float spanMax = wallAlongX ? deck.x + halfW : deck.z + halfD;

            foreach (MapLink link in map.links)
            {
                if (link == null) continue;
                MapEntity gate = map.Entity(link.gate);
                if (gate == null || gate.Kind != EntityKind.AreaGate || gate.level <= 0) continue;
                if (!MapGeometry.Doorway(map, link, out bool runsAlongX, out float coord, out Span hole)) continue;
                if (runsAlongX != wallAlongX || Mathf.Abs(coord - wallCoord) > 0.05f) continue;

                // The hole must actually fall along THIS deck's own span on the wall, not some other
                // gate sharing the same infinite line elsewhere in the level.
                if (hole.Max <= spanMin + 0.05f || hole.Min >= spanMax - 0.05f) continue;

                float gateCoord = wallAlongX ? gate.x : gate.z;
                float half = (link.doorway > 0f ? Mathf.Min(link.doorway, spanMax - spanMin) : spanMax - spanMin) * 0.5f;
                float centre = Mathf.Clamp(gateCoord, spanMin + half, spanMax - half);
                span = new Span(centre - half, centre + half);
                return true;
            }
            return false;
        }

        /// <summary>One stretch of a parapet (MV-859, generalised MV-899 by <see cref="BuildDeckParapetOpenAt"/>)
        /// — the same visual/collider pairing whether it is the single continuous run covering a whole
        /// edge or one piece either side of a hole, just clipped to <paramref name="from"/>..
        /// <paramref name="to"/>. A non-positive length means a hole reaches (or overruns) this end, so
        /// there is nothing left of this stretch to build.</summary>
        private static void BuildParapetSpan(MapData map, Transform root, List<GameObject> staticGeometry, MapEntity deck, Wall wall, bool alongX, float fixedCoord,
            float centerY, float from, float to, int index, Dictionary<Renderer, List<string>> rendererZones)
        {
            float length = to - from;
            if (length <= 0.05f) return;

            float mid = (from + to) * 0.5f;
            Vector3 centre = alongX ? new Vector3(mid, centerY, fixedCoord) : new Vector3(fixedCoord, centerY, mid);

            GameObject visual = StormdrainKit.BuildHazardBanding(root, centre, length, DeckParapetHeight, alongX, DeckParapetThickness);
            visual.name = $"{deck.id}_parapet_{wall}_{index}";
            AddStatic(map, visual, staticGeometry, rendererZones);

            Vector3 blockerSize = alongX
                ? new Vector3(length, DeckParapetHeight, DeckParapetThickness)
                : new Vector3(DeckParapetThickness, DeckParapetHeight, length);
            GameObject blocker = Spawn(root, $"{deck.id}_parapet_{wall}_{index}_collider", PrimitiveType.Cube, centre, blockerSize);
            foreach (Renderer r in blocker.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        /// <summary>A visible structural beam hung off each deck edge (MV-821 change 2) — built entirely
        /// outside the deck's own horizontal footprint (pushed outward by half its own thickness), so it
        /// reads as a beam bolted to the walkway's edge rather than as infill closing the open space
        /// underneath.</summary>
        private static void BuildDeckEdgeBeam(MapData map, Transform root, List<GameObject> staticGeometry, MapEntity deck, Wall wall, float topY,
            Dictionary<Renderer, List<string>> rendererZones)
        {
            float halfW = deck.width * 0.5f, halfD = deck.depth * 0.5f;
            float underside = topY - MapGeometry.DeckThickness;
            float beamCenterY = underside - DeckEdgeBeamDepth * 0.5f;
            float outward = DeckEdgeBeamThickness * 0.5f;
            Vector3 center;
            Vector3 size;
            switch (wall)
            {
                case Wall.N:
                    center = new Vector3(deck.x, beamCenterY, deck.z + halfD + outward);
                    size = new Vector3(deck.width, DeckEdgeBeamDepth, DeckEdgeBeamThickness);
                    break;
                case Wall.S:
                    center = new Vector3(deck.x, beamCenterY, deck.z - halfD - outward);
                    size = new Vector3(deck.width, DeckEdgeBeamDepth, DeckEdgeBeamThickness);
                    break;
                case Wall.E:
                    center = new Vector3(deck.x + halfW + outward, beamCenterY, deck.z);
                    size = new Vector3(DeckEdgeBeamThickness, DeckEdgeBeamDepth, deck.depth);
                    break;
                default: // Wall.W
                    center = new Vector3(deck.x - halfW - outward, beamCenterY, deck.z);
                    size = new Vector3(DeckEdgeBeamThickness, DeckEdgeBeamDepth, deck.depth);
                    break;
            }

            AddStatic(map, StormdrainKit.Box(root, $"{deck.id}_beam_{wall}", center, size, DeckGrateColor, SurfaceKind.Metal), staticGeometry, rendererZones);
        }

        /// <summary>Support posts at every corner and at no more than <see cref="DeckPostSpacing"/>
        /// along each edge (MV-821 change 2), floor to slab underside — the only thing built in the open
        /// space under a deck, so that space still reads as visibly open between them. Corners are added
        /// once each (two edges would otherwise both claim the same corner point).</summary>
        private static void BuildDeckPosts(MapData map, Transform root, List<GameObject> staticGeometry, MapEntity deck, float topY,
            Dictionary<Renderer, List<string>> rendererZones)
        {
            float halfW = deck.width * 0.5f, halfD = deck.depth * 0.5f;
            float underside = topY - MapGeometry.DeckThickness;
            float postHeight = Mathf.Max(0.01f, underside);
            float postCenterY = postHeight * 0.5f;

            var seen = new HashSet<Vector2Int>();
            var posts = new List<Vector2>();

            void Add(float x, float z)
            {
                var key = new Vector2Int(Mathf.RoundToInt(x * 100f), Mathf.RoundToInt(z * 100f));
                if (seen.Add(key)) posts.Add(new Vector2(x, z));
            }

            Add(deck.x - halfW, deck.z - halfD);
            Add(deck.x + halfW, deck.z - halfD);
            Add(deck.x + halfW, deck.z + halfD);
            Add(deck.x - halfW, deck.z + halfD);

            AddEdgePosts(Add, deck.x - halfW, deck.x + halfW, deck.z - halfD, alongX: true);
            AddEdgePosts(Add, deck.x - halfW, deck.x + halfW, deck.z + halfD, alongX: true);
            AddEdgePosts(Add, deck.z - halfD, deck.z + halfD, deck.x - halfW, alongX: false);
            AddEdgePosts(Add, deck.z - halfD, deck.z + halfD, deck.x + halfW, alongX: false);

            for (int i = 0; i < posts.Count; i++)
            {
                Vector3 center = new Vector3(posts[i].x, postCenterY, posts[i].y);
                AddStatic(map, StormdrainKit.Box(root, $"{deck.id}_post{i}", center,
                    new Vector3(DeckPostSize, postHeight, DeckPostSize), DeckGrateColor, SurfaceKind.Metal), staticGeometry, rendererZones);
            }
        }

        /// <summary>Interior posts along one edge, spaced no more than <see cref="DeckPostSpacing"/>
        /// apart — the corners themselves are added separately by the caller.</summary>
        private static void AddEdgePosts(System.Action<float, float> add, float from, float to, float fixedCoord, bool alongX)
        {
            float length = to - from;
            int intervals = Mathf.Max(1, Mathf.CeilToInt(length / DeckPostSpacing));
            for (int i = 1; i < intervals; i++)
            {
                float t = from + length * i / (float)intervals;
                if (alongX) add(t, fixedCoord); else add(fixedCoord, t);
            }
        }

        /// <summary>The ground under a deck's footprint reads as covered floor, not empty space (MV-821
        /// change 3): a thin decal, proud of the floor by a hair, tinted <see cref="DeckShadowDarken"/>
        /// darker than this world's own resolved ground tone.</summary>
        private static void BuildDeckGroundShadow(MapData map, Transform root, List<GameObject> staticGeometry, MapEntity deck,
            float topY, Dictionary<Renderer, List<string>> rendererZones)
        {
            Color ground = MaterialLibrary.Palette.GroundBase;
            Color tone = new Color(ground.r * (1f - DeckShadowDarken), ground.g * (1f - DeckShadowDarken),
                                    ground.b * (1f - DeckShadowDarken), 1f);

            float centerY = DeckShadowProud + DeckShadowThickness * 0.5f;
            GameObject shadow = Spawn(root, $"{deck.id}_ground_shadow", PrimitiveType.Cube,
                new Vector3(deck.x, centerY, deck.z), new Vector3(deck.width, DeckShadowThickness, deck.depth));
            StripCollider(shadow);
            Tint(shadow, MaterialLibrary.Tinted(SurfaceKind.Ground, tone));
            shadow.isStatic = true;
            AddStatic(shadow, staticGeometry);

            // MV-887: tagged at the DECK's own height, not the shadow's own near-floor Y — an open
            // (unwalled) deck lets Max look straight down through its edge at this patch (that is the
            // whole point of MV-821's open-walkway redesign), so it must gate with the deck above it,
            // not with whatever floor zone happens to sit underneath.
            Renderer shadowRenderer = shadow.GetComponent<Renderer>();
            if (shadowRenderer != null) TagRendererAt(map, shadowRenderer, new Vector3(deck.x, topY, deck.z), rendererZones);
        }

        /// <summary>A ramp's walkable slope (MV-692) — one box, tilted so its top face runs continuously
        /// from the floor to the deck it climbs to; Unity's own <c>CharacterController</c> slope-climb
        /// walks it without any code change on either mover's side.</summary>
        private static GameObject BuildRamp(MapData map, Transform root, MapEntity e)
        {
            RampSlab slab = default;
            bool found = false;
            foreach (RampSlab r in MapGeometry.Ramps(map))
                if (r.Id == e.id) { slab = r; found = true; break; }
            if (!found) return null; // no recognised facing — MapValidation should never let this through

            Vector3 bottom = slab.BottomCenter, top = slab.TopCenter;
            Vector3 mid = (bottom + top) * 0.5f;
            Vector3 along = top - bottom;
            float slopeLength = along.magnitude;
            Quaternion rot = along.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(along.normalized, Vector3.up)
                : Quaternion.identity;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = e.id;
            body.transform.SetParent(root, false);
            body.transform.localPosition = mid;
            body.transform.localRotation = rot;
            ApplyBevelledBoxMesh(body, new Vector3(slab.Width, RampThickness, slopeLength));
            Tint(body, MaterialLibrary.Tinted(SurfaceKind.Metal, DeckGrateColor));
            body.isStatic = true;
            return body;
        }

        /// <summary>MV-829: a locked hatch is a barrier standing across the ramp head it guards, not a
        /// flat panel lying on the deck — the old 0.3 m-thick slab a locked <see cref="AreaGate"/> still
        /// left walkable, the bug this ticket's own observation names. Built exactly like a wall gate
        /// mechanically (<see cref="BuildAreaGate"/>: its own HP, breakable by sustained primary fire,
        /// opens on destruction, <see cref="AreaGate.ForceOpen"/> for a condition), standing on TOP of
        /// the deck surface (<see cref="MapEntity.height"/> is that resolved Y — <see cref="WorldMapLoader"/>'s
        /// own doc comment on the field) instead of upright in a floor wall, at the map's own
        /// <see cref="MapData.wallHeight"/> so <c>WorldRunner.RefreshGateLocks</c>'s hatch loop and every
        /// wall gate agree on how tall "blocks Max" is.
        ///
        /// <see cref="AreaGate.StartHingeSwing"/> always pivots on local X — exactly like
        /// <see cref="BuildAreaGate"/>'s E/W-wall case, a hatch whose authored SPAN runs along Z (its
        /// depth bigger than its width — every hatch but a6's two) has to be built rotated 90° so the
        /// hinge pivots on the real span, not the 1 m-deep approach edge.</summary>
        private static void BuildHatch(MapData map, MapEntity e, Transform root, MapBuild built)
        {
            bool spansAlongX = e.width >= e.depth;
            float span = spansAlongX ? e.width : e.depth;
            float thickness = spansAlongX ? e.depth : e.width;

            float baseY = e.height; // the deck's own resolved surface height, not a size — see doc above
            var center = new Vector3(e.x, baseY + map.wallHeight * 0.5f, e.z);

            GameObject body = Spawn(root, e.id, PrimitiveType.Cube, center,
                new Vector3(span, map.wallHeight, thickness));
            if (!spansAlongX) body.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            Tint(body, MaterialLibrary.Tinted(SurfaceKind.Metal, DeckGrateColor));
            MarkDiscoverable(body);
            body.AddComponent<AreaGate>(); // Locked defaults false — WorldRunner.RefreshGateLocks sets it from opensWith

            built.Actors[e.id] = body;
        }

        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }

        private static void Tint(GameObject go, Material material)
        {
            if (material == null) return;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            if (go.GetComponent<KeepsOwnMaterial>() == null) go.AddComponent<KeepsOwnMaterial>();
        }

        private static CoverPiece BuildCover(Transform root, MapEntity e)
        {
            ArenaCover cover = e.ToCover();

            // The cylinder mesh is 2 units tall, so half its height goes into the Y scale.
            Vector3 scale = cover.Shape == CoverShape.Cylinder
                ? new Vector3(cover.Size.x, cover.Size.y * 0.5f, cover.Size.z)
                : cover.Size;

            // MV-882: the "ambience layer sways this" reasoning this comment used to give for leaving
            // cover isStatic-false is stale — AmbienceVfx's old prop-sway pass was deleted outright by
            // YT-78 (its own doc comment: it had swayed nothing since YT-75, every kit prop already
            // being static). Cover batches with the floor/walls now (see BuildProps); isStatic itself
            // was never load-bearing for that either way — see the ticket's own diagnosis that it
            // never produced batching on its own.
            GameObject body = Spawn(root, e.id,
                cover.Shape == CoverShape.Cylinder ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                cover.Center, scale);

            // MV-778: cylinders (hedges) are untouched — only the box shape gets a chamfered mesh.
            if (cover.Shape != CoverShape.Cylinder)
                ApplyBevelledBoxMesh(body, scale);

            // This is the line that turns a prop from scenery into a mechanic (YT-83) — except for a
            // hedge row (MV-400), a pipe barrier (MV-863, Lee's decision 2026-09-17), or anything
            // authored with an explicit see-through CoverKind (MV-917): all three keep blocking a
            // footstep (the collider Spawn() just built is untouched) while stopping blocking a
            // sight-line or a shot, so they are left off the Cover layer. LineOfSight, WaterBlaster's
            // spray and HomingMissile all cast against CoverLayer.Mask, so skipping the assign here is
            // the single point that makes robots see, and shoot, straight through a plant row, a pipe
            // run, or any piece authored see-through.
            //
            // MV-917 ORs its explicit cover.Kind check onto the existing dressing check rather than
            // replacing it: world2_config.json's 29 shipped "pipe" pieces carry no coverKind yet (that
            // data lands separately, in the design workbook re-author) and CoverKind defaults to Solid,
            // so replacing the dressing check outright would silently revert those pieces to blocking
            // sight and shots — undoing the MV-863 decision by accident, exactly what MV-917's own
            // schema default is meant to prevent.
            if (cover.Kind != CoverKind.SeeThrough
                && cover.Dressing != CoverDressing.Hedge && cover.Dressing != CoverDressing.Pipe)
                CoverLayer.Assign(body);

            return new CoverPiece(cover, body);
        }

        /// <summary>Put the actors where the map says they stand: adopt the one-of ones, build the
        /// factories and bosses. An adopted actor's Y is left alone — the map authors a floor plan, not
        /// heights — while a built one gets a Y from its own body, so it can never be authored
        /// half-buried.</summary>
        private static void PlaceActors(MapData map, Transform root, MapBuild built)
        {
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;

                switch (e.Kind)
                {
                    case EntityKind.PlayerSpawn:
                        Adopt(e, built, GameObject.FindGameObjectWithTag("Player"));
                        break;

                    case EntityKind.Factory:
                        built.Factories.Add(BuildFactory(map, e, root, built));
                        break;

                    case EntityKind.Replicator:
                        built.Replicators.Add(BuildReplicator(e, root, built));
                        break;

                    case EntityKind.AreaGate:
                        BuildAreaGate(map, e, root, built);
                        break;

                    case EntityKind.Gate:
                        GameObject gate = MarkDiscoverable(Adopt(e, built, Find<SubZoneGate>()));
                        // A gate is exactly as wide as the doorway it fills, plus the wall it seals
                        // against each side. Its width is NOT authored — it is read off the link, so a
                        // widened doorway can never leave a gap beside its gate.
                        if (gate != null)
                        {
                            gate.transform.localScale = new Vector3(SealWidth(map, e), e.height, e.depth);

                            // A shut gate stops sight, not just footsteps (YT-107). The walls around
                            // it already block (Box(blocksSight: true)), but the gate filling the
                            // doorway did not — so the boss could be discovered straight through a
                            // door that has never been opened, and a robot could watch Max through it.
                            // Nothing has to close this again: SubZoneGate.Open disables its collider
                            // the instant it starts sinking, so the sight-line opens with the gate.
                            CoverLayer.Assign(gate);

                            // Robots must not be routed at (and grind on) a gate that is still shut
                            // (MV-364, same reasoning as BuildAreaGate below) — this is what tells
                            // EnemyNavigation which live SubZoneGate a link's "gate" id actually points
                            // to, so a shut one counts as impassable instead of reading as open.
                            EnemyNavigation.RegisterGate(e.id, gate.GetComponent<SubZoneGate>(), map);
                        }
                        break;

                    case EntityKind.Boss:
                        built.Bosses.Add(BuildBoss(map, e, root, built));
                        break;
                }
            }
        }

        /// <summary>
        /// Say that this landmark has to be found before the map will admit it exists (YT-107).
        ///
        /// Here rather than in each landmark's own Awake because THIS is the code that knows what
        /// kind of thing it is placing — the boss and the gate are adopted from the scene, and asking
        /// them to mark themselves would mean a scene-authored gate quietly behaves differently from
        /// a map-authored one.
        /// </summary>
        private static GameObject MarkDiscoverable(GameObject landmark)
        {
            if (landmark != null && landmark.GetComponent<Discoverable>() == null)
                landmark.AddComponent<Discoverable>();
            return landmark;
        }

        /// <summary>
        /// A Mower Hutch, from data (YT-92). Every factory in every map is this one recipe, so a
        /// level's second factory cannot be a slightly different machine from its first.
        ///
        /// The body is built and SIZED first, and only then given its components, because
        /// <see cref="MowerHutch"/> reads its own scale in Awake to size a health bar in metres
        /// (YT-71) — and AddComponent runs that Awake there and then. Add the script to a
        /// default-sized cube and you get a bar built for a 1 m machine on a 3 m one.
        ///
        /// Nothing here sets a material. The body is damageable, so the rendering layer skins it as a
        /// Structure exactly as it skins the hutch the scene used to hold, and both directors leave a
        /// damageable renderer alone — which is what keeps a code-built factory off the magenta path.
        ///
        /// MV-683: also hands a mobile shed its own leash — the footprint of whichever zone it was
        /// authored inside, the same <c>map.ZoneAt(e.x, e.z).Footprint</c> convention <see cref="BuildBoss"/>
        /// already uses for a boss's wake area — so a static shed's <see cref="MowerHutch.TickMobility"/>
        /// (a permanent no-op for it) never even reads the field.
        /// </summary>
        private static MowerHutch BuildFactory(MapData map, MapEntity e, Transform root, MapBuild built)
        {
            GameObject body = Spawn(root, e.id, PrimitiveType.Cube, e.GroundedCenter, e.Size);

            // Before the hutch, not after: MowerHutch.Awake runs inside the AddComponent below and
            // asks whether it has been found yet, so that the name badge and the glowing core are
            // never built visible and hidden a frame later. A one-frame flash of "MOWER HUTCH" on
            // the horizon is exactly the telegraph this ticket exists to remove.
            MarkDiscoverable(body);

            // MV-548 (shed roadmap stage 3): a mobile shed pursues Max via the same
            // CharacterController.SafeMove pattern Big Bermuda uses (MV-386) — one physical shape only,
            // so the primitive's stray BoxCollider is stripped first, exactly as BuildBoss does below.
            // A static shed (the default) is untouched — it keeps the plain BoxCollider it always had.
            if (e.mobile)
            {
                var stray = body.GetComponent<BoxCollider>();
                if (stray != null) Object.DestroyImmediate(stray);
                CharacterController cc = body.AddComponent<CharacterController>();
                // LOCAL (unscaled) unit-cube extents, exactly like BigBermudaBoss.FitColliderToRenderedBody
                // — CharacterController.height/radius/center are in local space and Unity scales them by
                // transform.lossyScale automatically. Passing e.Size (already world-space) here would
                // double-scale into an oversized, geometrically invalid capsule (2*radius > height).
                cc.center = Vector3.zero;
                cc.height = 1f;
                cc.radius = 0.5f;
            }

            // RequireComponent brings the EnemySpawner with it — the factory's mouth is part of what a
            // factory IS, not something a scene has to remember to bolt on.
            var hutch = body.AddComponent<MowerHutch>();
            if (e.mobile)
            {
                hutch.ConfigureMobility(true);
                MapZone zone = map.ZoneAt(e.x, e.z);
                if (zone != null) hutch.SetAreaFootprint(zone.Footprint);
            }

            BuildShedFittings(body, e, hutch);

            built.Actors[e.id] = body;
            return hutch;
        }

        /// <summary>Roof-corner weapon fittings (MV-547, shed roadmap stage 2) — up to 4 small,
        /// independently destroyable turrets riding on this shed. Parented to the shed's own body, so a
        /// mobile shed's <see cref="MowerHutch.MoveBody"/> carries them along for free, with no separate
        /// motion code of their own. World position is set BEFORE parenting (<c>worldPositionStays: true</c>)
        /// so the corner offsets below are in plain metres regardless of the body's own
        /// <see cref="MapEntity.Size"/> scale — the same trap <see cref="MowerHutch.BuildHealthBar"/>'s
        /// own "cancel the parent's scale" comment documents.</summary>
        private static readonly Vector2[] FittingCornerSigns =
        {
            new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f),
        };

        private const float FittingInset = 0.85f;   // fraction of the half-extent, so a corner turret doesn't overhang
        private const float FittingSize = 0.5f;     // a small turret, not a shed-sized object

        private static void BuildShedFittings(GameObject body, MapEntity e, MowerHutch hutch)
        {
            ShedFittingKind kind = ShedFittingKindEnums.Parse(e.fittingKind);
            if (kind == ShedFittingKind.None) return;

            int count = Mathf.Clamp(e.fittingCount, 0, FittingCornerSigns.Length);
            Vector3 center = body.transform.position;
            float halfW = e.width * 0.5f * FittingInset;
            float halfD = e.depth * 0.5f * FittingInset;
            float roofY = center.y + e.height * 0.5f + FittingSize * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Vector2 sign = FittingCornerSigns[i];

                // MV-913: Missile gets MissileLauncherRig's generated-mesh body (built inside
                // ShedFitting.Bind) riding on a bare collider host — CreatePrimitive would leave a
                // stray Unity cube mesh/renderer behind the rig, which is exactly the primitive the
                // ticket's AC2 test asserts is gone. Spiker/Laser are untouched: their own tickets
                // convert them later, and until then they still need CreatePrimitive's box mesh AND
                // its collider in one call.
                GameObject go;
                if (kind == ShedFittingKind.Missile)
                {
                    go = new GameObject();
                    go.AddComponent<BoxCollider>();
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                }

                go.name = $"{e.id}_fitting{i + 1}";
                go.transform.position = new Vector3(center.x + sign.x * halfW, roofY, center.z + sign.y * halfD);
                go.transform.localScale = Vector3.one * FittingSize;
                go.transform.SetParent(body.transform, worldPositionStays: true);

                var fitting = go.AddComponent<ShedFitting>();
                fitting.Bind(hutch, kind);
            }
        }

        /// <summary>
        /// A Replicator, from data (MV-706) — World 2's factory, built the same "one recipe, however
        /// many a level authors" way <see cref="BuildFactory"/> already builds a shed. RequireComponent
        /// brings the EnemySpawner with it; <see cref="Replicator.Configure"/> is called AFTER
        /// AddComponent (same ordering as <see cref="MowerHutch.ConfigureMobility"/>) so the box reads
        /// its own scale in Awake before anything else touches it.
        /// </summary>
        private static Replicator BuildReplicator(MapEntity e, Transform root, MapBuild built)
        {
            GameObject body = Spawn(root, e.id, PrimitiveType.Cube, e.GroundedCenter, e.Size);
            MarkDiscoverable(body);

            var replicator = body.AddComponent<Replicator>();
            replicator.Configure(e.capacity);
            replicator.SetFacing(e.facing); // MV-860: which side is the IN face, "S" = today's behaviour

            built.Actors[e.id] = body;
            return replicator;
        }

        /// <summary>
        /// Big Bermuda, from data (MV-561) — same recipe <see cref="Stage27BossScaffold"/> used to hand-
        /// place once, now built per entity so an area can carry as many as it authors. The stray
        /// BoxCollider <c>CreatePrimitive</c> leaves behind is stripped before <c>AddComponent</c>,
        /// exactly as the scaffold always did — <see cref="BigBermudaBoss"/>'s required
        /// CharacterController is the sole physical shape a boss carries (MV-410), and Unity does not
        /// support both on one GameObject.
        ///
        /// MV-572: also hands the boss its own wake area — the footprint of whichever zone it was
        /// authored inside. <see cref="MapValidation"/> already requires every non-gate entity
        /// (including a boss) to sit inside a zone, so <c>ZoneAt</c> is resolved here, not defended
        /// against being null.
        /// </summary>
        private static BigBermudaBoss BuildBoss(MapData map, MapEntity e, Transform root, MapBuild built)
        {
            GameObject body = Spawn(root, e.id, PrimitiveType.Cube, e.GroundedCenter, e.Size);

            var stray = body.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);

            MarkDiscoverable(body);
            var boss = body.AddComponent<BigBermudaBoss>(); // RequireComponent adds the CharacterController
            boss.SetWakeArea(map.ZoneAt(e.x, e.z).Footprint);

            // MV-573: every boss gets its own rig, bound to it alone, right here — the old scene-load
            // singleton (BigBermudaRig.Install) built exactly one rig per scene and bailed the moment
            // any rig existed, so only the FIRST boss on a multi-boss map ever grew a body and every
            // other one stood there as the bare greybox cube above.
            BigBermudaRig.CreateFor(boss);

            built.Actors[e.id] = body;
            return boss;
        }

        /// <summary>
        /// A gated room boundary, from data (WV-222) — the reusable mechanic behind the recut's 10-area
        /// arena (spec §1). Unlike the scene-adopted <see cref="EntityKind.Gate"/> — one per scene,
        /// moved into place — a level can have as many area gates as it has rooms, so like a factory it
        /// is BUILT, not adopted.
        ///
        /// Its width is NOT authored (see <see cref="MapEntity.width"/>'s doc) — it fills the doorway
        /// of the link that names it, read off the exact same <see cref="SealWidth"/> the scene-adopted
        /// gate uses, so the two kinds can never leave a doorway with a gap beside its seal.
        /// </summary>
        /// <summary>MV-246: a gate's authored depth is exactly <c>wallThickness</c> (both 0.6 in
        /// backyard_slice.json), and <see cref="SealWidth"/> deliberately overlaps the gate into the
        /// wall on each side of the doorway by that same thickness — so over the overlap, the gate's
        /// front/back faces sit on the EXACT SAME plane as the wall's, and the two flicker (z-fight)
        /// against each other. 2 cm proud on each face, the same margin <c>BackyardDressingSet</c>
        /// gives its fence panels for the identical reason, is enough to always win the depth test
        /// without being visible as a gap at this camera angle.</summary>
        private const float AntiZFightMargin = 0.04f;

        private static GameObject BuildAreaGate(MapData map, MapEntity e, Transform root, MapBuild built)
        {
            // MV-697: a deck-level gate (opensWith carried "[DECK]") is built at deck height in the
            // wall instead of the floor — everything else about it (width, hinge, lock) is identical.
            float baseY = e.level > 0 ? map.deckHeight : 0f;
            var center = new Vector3(e.x, baseY + e.height * 0.5f, e.z);

            GameObject body = Spawn(root, e.id, PrimitiveType.Cube, center,
                new Vector3(SealWidth(map, e), e.height, e.depth + AntiZFightMargin));

            // The box is built for an N/S-wall doorway (width along local X, thickness along local Z —
            // same axes WallSegment's alongX case uses). An E/W-wall gate's doorway runs along Z instead
            // (MV-271: g1/g3/g5/g7 in world1_config), so the body is spun 90° to match — NOT rebuilt
            // with x/z swapped, because AreaGate.StartHingeSwing reads localScale.x as "the width" to
            // find its hinge pivot; swap the scale instead of rotating and the hinge would pivot on the
            // thin edge.
            if (!GateRunsAlongX(map, e))
                body.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            MarkDiscoverable(body);
            var gate = body.AddComponent<AreaGate>();

            // MV-320: tell the gate which way is "deeper into the level, away from Max" so its hinge
            // swing opens into the room ahead rather than the one the player is standing in — a chain
            // that doubles back (world1_config g3: area3 -> area4 runs -X, not +X like g1) needs the
            // real from/to direction, not a fixed assumption.
            gate.AwayFromPlayerDirection = AwayFromPlayerDirection(map, e);

            // Robots must not be routed at (and grind on) a gate that is still shut (MV-272) — this is
            // what tells EnemyNavigation which live AreaGate a link's "gate" id actually points to.
            EnemyNavigation.RegisterGate(e.id, gate, map);

            // Shut, an area gate blocks sight exactly like the scene-adopted one (YT-107). Cover goes on
            // the gate's THRESHOLD object, not the visible leaf (MV-386): the leaf's own collider no
            // longer disables when the gate opens (it stays solid and keeps following the hinge swing),
            // so putting Cover on it here would risk the sight-line staying blocked, or re-blocking
            // partway through the swing, depending on where the leaf ends up. The threshold still drops
            // the instant the gate breaks exactly like the old single collider did, so the sight-line
            // opens with the gate exactly as before.
            CoverLayer.Assign(gate.ThresholdObject);

            built.Actors[e.id] = body;
            return body;
        }

        /// <summary>
        /// The hand-placed Mower Hutch the slice scene has carried since the first scaffold stands
        /// down. The map owns the factories now, and a scene copy is not a spare — it is a second
        /// factory standing in the wrong room with its own stale serialized numbers.
        ///
        /// Inactive rather than destroyed, and BEFORE anything else runs: deactivating an object whose
        /// Awake has not fired yet means it never fires, so the retired hutch cannot register itself,
        /// emit a signal, or spawn a single robot.
        /// </summary>
        private static void RetireSceneFactories()
        {
            foreach (var hutch in Object.FindObjectsByType<MowerHutch>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hutch != null) hutch.gameObject.SetActive(false);
            }
        }

        /// <summary>The hand-placed Big Bermuda <see cref="Stage27BossScaffold"/> used to leave in
        /// <c>Backyard_Slice.unity</c> stands down (MV-561), same reasoning and same shape as
        /// <see cref="RetireSceneFactories"/> one level up: the map owns the boss(es) now, and a scene
        /// copy is not a spare boss, it is an extra one standing in the wrong room with no area of its
        /// own. Inactive rather than destroyed, and BEFORE anything else runs, for the same reason —
        /// deactivating an object whose Awake has not fired yet means it never registers with
        /// <see cref="BossCensus"/> or wakes on <see cref="FactoryCensus"/>'s cleared signal.</summary>
        private static void RetireSceneBosses()
        {
            foreach (var boss in Object.FindObjectsByType<BigBermudaBoss>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (boss != null) boss.gameObject.SetActive(false);
            }

            // MV-573: a boss now owns a real rig (BigBermudaRig.CreateFor), built as its own root
            // GameObject alongside it rather than one shared scene singleton — so retiring a boss above
            // must retire its rig too, or a map built a second time in the same scene (a later area, a
            // level reload, or a test) leaks the old boss's whole 30-part articulated body forever. A
            // retired rig has no boss left to follow or read tells off, so destroying it outright (not
            // just deactivating, unlike the boss above) is correct rather than just tidy.
            foreach (var rig in Object.FindObjectsByType<BigBermudaRig>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rig != null) Object.DestroyImmediate(rig.gameObject);
            }
        }

        private static GameObject Adopt(MapEntity e, MapBuild built, GameObject actor)
        {
            if (actor == null)
            {
                // Normal in a geometry-only test scene; a real problem in the game scene, so say it.
                Debug.LogWarning($"[MapRuntime] the map places '{e.id}' ({e.kind}), but the scene has " +
                                 "no such actor to place.");
                return null;
            }

            // A CharacterController caches its own position and will happily undo a teleport, so it
            // has to be switched off across the move (this is how Max gets to the map's start).
            var cc = actor.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            Vector3 at = actor.transform.position;
            actor.transform.position = new Vector3(e.x, at.y, e.z);

            if (cc != null) cc.enabled = was;

            // MV-503: `was` false means the controller arrived here already disabled, and this restores
            // it right back to disabled rather than to true — one of the two candidate mechanisms for
            // "Max rotates but never translates" on a fresh run. Was silent; now on the record. Not a
            // fix (this ticket is diagnostic-only) — just says when it happens.
            if (cc != null && !was)
            {
                Debug.LogWarning($"[MV-503] MapRuntime.Adopt restored '{e.id}' ({e.kind}) " +
                                  "CharacterController to disabled — it arrived already disabled.");
            }

            built.Actors[e.id] = actor;
            return actor;
        }

        /// <summary>Hand every gate the factories that open it. "Kill the sources and the way opens" is
        /// a property of the level data (<c>opensOn</c>) rather than a slot a human dragged an object
        /// into — a slot that silently comes undone the next time the object is rebuilt.
        ///
        /// A gate may name more than one factory (YT-92), and it opens on the LAST of them. Each key
        /// is announced to the gate as it is bound, so the gate knows how many it is waiting on before
        /// the player has broken any of them.</summary>
        private static void WireGates(MapData map, MapBuild built)
        {
            foreach (MapEntity gate in MapValidation.Kind(map, EntityKind.Gate))
            {
                if (!built.Actors.TryGetValue(gate.id, out GameObject gateGo) || gateGo == null) continue;

                var door = gateGo.GetComponent<SubZoneGate>();
                if (door == null) continue;

                foreach (string key in gate.Keys)
                {
                    if (!built.Actors.TryGetValue(key, out GameObject factoryGo) || factoryGo == null) continue;

                    var hutch = factoryGo.GetComponent<MowerHutch>();
                    if (hutch != null) hutch.Bind(door);   // Bind tells the gate it has another key
                }
            }
        }

        /// <summary>Width a gate needs to seal its doorway: the opening, plus the wall thickness each
        /// side, so there is no sliver to squeeze through.</summary>
        public static float SealWidth(MapData map, MapEntity gate)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null || link.gate != gate.id) continue;
                    if (MapGeometry.Doorway(map, link, out _, out _, out Span hole))
                        return hole.Length + map.wallThickness * 2f;
                }
            }
            return gate.width;   // an unlinked gate falls back to its authored width
        }

        /// <summary>Which way the wall this gate seals actually runs — true for an N/S-wall doorway
        /// (wall at a constant Z, spanning X), false for an E/W-wall one (constant X, spanning Z). Same
        /// link lookup as <see cref="SealWidth"/>; an unlinked gate defaults true, matching the box
        /// <see cref="BuildAreaGate"/> spawns before any rotation is applied.</summary>
        private static bool GateRunsAlongX(MapData map, MapEntity gate)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null || link.gate != gate.id) continue;
                    if (MapGeometry.Doorway(map, link, out bool runsAlongX, out _, out _))
                        return runsAlongX;
                }
            }
            return true;
        }

        /// <summary>World-space direction from this gate's "from" zone centre to its "to" zone centre
        /// (MV-320) — the room Max is standing in when he reaches the gate, to the room beyond it. Same
        /// link lookup as <see cref="SealWidth"/> and <see cref="GateRunsAlongX"/>; an unlinked gate (or
        /// one whose zones can't be resolved) falls back to <see cref="Vector3.zero"/>, which
        /// <see cref="AreaGate.SwingSign"/> reads as "no map context, keep the old swing". Public, like
        /// <see cref="SealWidth"/>, so a test can check it against a fixture map without instantiating
        /// the GameObjects <see cref="BuildAreaGate"/> would spawn.</summary>
        public static Vector3 AwayFromPlayerDirection(MapData map, MapEntity gate)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null || link.gate != gate.id) continue;
                    MapZone from = map.Zone(link.from);
                    MapZone to = map.Zone(link.to);
                    if (from == null || to == null) continue;
                    return new Vector3(to.x - from.x, 0f, to.z - from.z);
                }
            }
            return Vector3.zero;
        }

        /// <summary>World-space direction from the zone entered just before <paramref name="zoneId"/>
        /// to <paramref name="zoneId"/> itself — the same direction as <see cref="AwayFromPlayerDirection"/>,
        /// but looked up by the zone being entered rather than by its gate entity (MV-323: a caller that
        /// only has an area's zone id, like the ambient spawn director, still needs to know which side of
        /// the room the door is on so it can bias placement to the far side). Falls back to
        /// <see cref="Vector3.zero"/> when no link leads into this zone (e.g. area 1, entered from
        /// outside the map, not through any authored gate).</summary>
        public static Vector3 EntryDirection(MapData map, string zoneId)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null || link.to != zoneId) continue;
                    MapZone from = map.Zone(link.from);
                    MapZone to = map.Zone(link.to);
                    if (from == null || to == null) continue;
                    return new Vector3(to.x - from.x, 0f, to.z - from.z);
                }
            }
            return Vector3.zero;
        }

        /// <summary>The slice scene still carries a hand-placed 30 m ground plane from the very first
        /// scaffold. The map owns its floor now, and two coplanar floors at y=0 z-fight into a
        /// shimmering mess — so the old one stands down.</summary>
        private static void RetireLegacyGround()
        {
            GameObject ground = GameObject.Find("Ground");
            if (ground != null) ground.SetActive(false);
        }

        /// <summary>This world's authored combat-area count, read back off the built zones rather than
        /// threaded through as a separate <c>WorldConfig</c> parameter: <see cref="WorldMapLoader.TryLoad"/>
        /// already renames every combat area (1..<c>dials.areaCount</c>) to the "area&lt;N&gt;" convention
        /// and leaves the entry stub/boss room under their own authored ids (MV-774 reuses that same
        /// parse via <see cref="AreaAccumulationDirector.AreaIndexOf"/>), so the highest index found IS
        /// that count.</summary>
        private static int CountCombatAreas(MapData map)
        {
            int max = 0;
            if (map.zones != null)
                foreach (MapZone z in map.zones)
                    max = Mathf.Max(max, AreaAccumulationDirector.AreaIndexOf(z.id));
            return max;
        }

        private static GameObject Find<T>() where T : Component
        {
            var found = Object.FindFirstObjectByType<T>();
            return found == null ? null : found.gameObject;
        }

        /// <summary>MV-882: registers every renderer under <paramref name="go"/> for the deferred
        /// static-batch combine — itself, if it carries its own renderer, or (for a bare parent wrapper
        /// like <see cref="StormdrainKit.BuildHazardBanding"/>'s own plate-plus-stripes root) each of
        /// its children that does — so a call site never has to know which shape of GameObject whatever
        /// it just built returned.</summary>
        private static void AddStatic(GameObject go, List<GameObject> staticGeometry)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                staticGeometry.Add(r.gameObject);
        }

        /// <summary>MV-887: same as the two-argument overload, plus tags every one of those renderers by
        /// its own resolved position (<see cref="TagStatic"/>) — every caller below sits comfortably
        /// inside one room, unlike a wall (which sits exactly on a shared boundary and tags itself
        /// separately via <see cref="TagWallZones"/> before calling here with <paramref name="autoTag"/>
        /// false, so its own multi-probe result is never overwritten by a single ambiguous point).</summary>
        private static void AddStatic(MapData map, GameObject go, List<GameObject> staticGeometry,
            Dictionary<Renderer, List<string>> rendererZones, bool autoTag = true)
        {
            AddStatic(go, staticGeometry);
            if (autoTag) TagStatic(map, go, rendererZones);
        }

        private static GameObject Box(Transform root, string name, Vector3 center, Vector3 size,
                                      bool blocksSight, bool isStatic)
        {
            GameObject go = Spawn(root, name, PrimitiveType.Cube, center, size);
            go.isStatic = isStatic;
            if (blocksSight) CoverLayer.Assign(go);
            return go;
        }

        private static GameObject Spawn(Transform root, string name, PrimitiveType type,
                                        Vector3 center, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = center;
            go.transform.localScale = scale;
            return go;
        }

        /// <summary>Swaps a freshly spawned primitive cube's flat mesh for a chamfered one (MV-778) —
        /// walls, cover blocks, deck slabs and ramp slabs, never the floor or a Prop entity, which keep
        /// their flat sides. The box's true size now lives in the mesh rather than the transform's
        /// scale (reset to one here), so the collider's own size is set explicitly to match; the
        /// position, rotation, material, name and parent are all untouched.</summary>
        private static void ApplyBevelledBoxMesh(GameObject go, Vector3 size)
        {
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size));

            var collider = go.GetComponent<BoxCollider>();
            if (collider != null) collider.size = size;
        }
    }
}
