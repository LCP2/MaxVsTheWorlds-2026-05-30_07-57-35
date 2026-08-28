using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Drives the gated arena's AMBIENT robot population (v0.5 recut spec §2, MV-242): the roaming
    /// crowd every one of the 10 areas carries regardless of a factory. The first time each area is
    /// entered, it queues that area's full population (<see cref="AreaPopulation.ComposeForArea"/> +
    /// <see cref="AreaPopulation.ToughSplitForArea"/>) into one field-wide <see cref="AreaSpawnQueue"/>
    /// and immediately releases everything that fits under the <c>maxActiveRobots</c> concurrent cap —
    /// the room's crowd is standing and active before the player can see it, not trickling in while
    /// they watch (MV-245). Only a roster that overflows the cap stays queued, releasing one at a time
    /// as active robots die (<see cref="Update"/>), the same as before.
    ///
    /// Entry is now GATE-event-driven (<see cref="EnterArea"/>, wired to each <see cref="AreaGate"/>'s
    /// <see cref="AreaGate.Opened"/> by <see cref="BackyardPath"/>): breaking a gate is what actually
    /// grants access to the next room, and it happens a few steps before the player walks through the
    /// doorway — exactly the window MV-245 needs to have the room populated ahead of arrival. This
    /// reverses MV-223/224's original call (position-based, "simpler and exactly as responsive") now
    /// that responsiveness alone is not what the ticket needs — position-crossing is kept in
    /// <see cref="Update"/> purely as a fallback for a zone entered without its gate ever firing
    /// (e.g. area 1, which nothing gates).
    ///
    /// A second, independent source from the three hutches: <see cref="EnemySpawner"/> keeps producing
    /// each factory's own local stream exactly as before (spec: "otherwise unchanged").
    /// </summary>
    public sealed class AreaAccumulationDirector : MonoBehaviour
    {
        /// <summary>Seconds between ambient releases — spaces overflow (over-the-cap) robots out as
        /// slots free, instead of dumping a whole area's death-freed backlog on the player at once.</summary>
        public const float ReleaseInterval = 0.35f;

        /// <summary>Kept clear of a room's walls so a robot never spawns inside geometry.</summary>
        private const float EdgeMargin = 3f;

        /// <summary>Minimum gap kept between a newly placed robot and any other already-active one, or
        /// a cover prop's footprint — placement retries rather than allowing an overlap.</summary>
        private const float PlacementSpacing = 1.5f;

        /// <summary>Candidate points tried for the ideal spot — clear of cover/robots AND outside the
        /// camera's view — before widening the search (see <see cref="MaxOffScreenAttempts"/>).</summary>
        private const int MaxPlacementAttempts = 12;

        /// <summary>Candidate points tried, ignoring cover/robot overlap, once <see cref="MaxPlacementAttempts"/>
        /// fails to find a spot that is both clear AND off-screen. Never popping into view matters more
        /// than a clean gap from cover or another robot (MV-273) — a robot briefly overlapping cover is
        /// a minor visual glitch; one materialising in front of the player is the bug this exists to
        /// prevent. Only once this ALSO fails (no off-screen point exists in the room at all) does
        /// placement fall back to whatever candidate was last tried.</summary>
        private const int MaxOffScreenAttempts = 24;

        /// <summary>Distinct distance-from-gate tiers a room's spawns cycle through (MV-324) — see
        /// <see cref="SpawnBias.StaggerBand"/>. Enough to break up a simultaneous-arrival mob without
        /// fragmenting a small room's far-side band into slivers.</summary>
        private const int StaggerBandCount = 5;

        /// <summary>How many of a fresh area's ambient spawns start concealed behind cover instead
        /// of joining the fight immediately (MV-363) — one small knot per qualifying room, not a
        /// blanket policy: "not every robot in an area should be hidden".</summary>
        private const int ConcealedGroupSize = 2;

        /// <summary>A room's total ambient population must be at least this before it spares a knot
        /// of robots the player never has to actively find — a 2-3 robot room reads as empty rather
        /// than staged if a third of it is hiding.</summary>
        private const int MinCompositionForConcealment = 4;

        [SerializeField] private RobotEnemy prefab;

        private MapData _map;
        private WorldConfig _worldCfg;
        private IReadOnlyList<CoverPiece> _cover = System.Array.Empty<CoverPiece>();
        private Transform _target;
        private Transform _bodies;
        private AreaSpawnQueue _queue;
        private readonly HashSet<int> _filledAreas = new HashSet<int>();
        private readonly Dictionary<int, int> _largeCountByArea = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _bruiserCountByArea = new Dictionary<int, int>();
        private readonly Dictionary<EnemyKind, Stack<RobotEnemy>> _pools = new Dictionary<EnemyKind, Stack<RobotEnemy>>();
        private Collider[] _playerColliders;
        private float _timer;

        /// <summary>Total Rushers this director has queued so far this run — enforces
        /// <see cref="RusherCap.PerLevel"/> (<see cref="RusherCap.Apply"/>). Reset in
        /// <see cref="Configure"/>, a fresh run.</summary>
        private int _rushersQueuedThisLevel;

        /// <summary>How many robots this director has placed in each area so far, keyed by area
        /// index. Feeds <see cref="SpawnBias.StaggerBand"/> so each one lands in a different
        /// distance-from-gate tier (MV-324). Per-area (MV-417), not a single running counter — an
        /// overflow robot released long after its own area was last filled must still continue that
        /// area's own stagger sequence, not whichever area happens to be filling right now. Seeded to
        /// 0 for an area in <see cref="FillArea"/> and advanced by <see cref="NextSpawnIndex"/> on
        /// every placement into it, instant-fill or later overflow alike.</summary>
        private readonly Dictionary<int, int> _spawnIndexByArea = new Dictionary<int, int>();

        /// <summary>Robots left to place concealed for the area currently being filled (MV-363) —
        /// decremented by <see cref="Spawn"/> as each one lands. Set fresh per area in
        /// <see cref="FillArea"/>; 0 for a room too small to spare a hidden knot.</summary>
        private int _concealedRemainingThisArea;

        /// <summary>Robots pre-placed for an area not yet entered (MV-514) — already standing at their
        /// authored <see cref="Garrison.SeedPositions"/>, dormant, the moment the PREVIOUS area was
        /// filled (see <see cref="PlacePendingGarrison"/>), awaiting <see cref="ActivateGarrisonFor"/>
        /// when this area's own gate actually breaks. Keyed by area; an area with none pending (no
        /// garrisonDensity, or reached without ever being pre-placed - e.g. area 1) has no entry, which
        /// is exactly what tells <see cref="ActivateGarrisonFor"/> to fall back to the old immediate
        /// <see cref="SeedGarrison"/> behaviour.</summary>
        private readonly Dictionary<int, List<RobotEnemy>> _pendingGarrisonByArea = new Dictionary<int, List<RobotEnemy>>();

        /// <summary>Areas whose garrison has already been given its MV-514 head start — guards
        /// <see cref="PlacePendingGarrison"/> so a given area is only ever pre-placed once, the same
        /// one-shot guarantee <see cref="_filledAreas"/> gives the rest of an area's population.</summary>
        private readonly HashSet<int> _garrisonPlacedAreas = new HashSet<int>();

        /// <summary>Which area each currently-active robot this director spawned was placed for (MV-417)
        /// — looked up in <see cref="OnEnemyDied"/> so <see cref="AreaSpawnQueue.ReportDestroyed(int)"/>
        /// frees the right area's slot, now that the cap is per-area rather than field-wide. Entries are
        /// added by <see cref="Spawn"/>/<see cref="SeedGarrison"/> and removed once reported.</summary>
        private readonly Dictionary<RobotEnemy, int> _areaByRobot = new Dictionary<RobotEnemy, int>();

        /// <summary>Consecutive placement failures (every candidate this pass read as on-screen) for one
        /// area (MV-417) — see <see cref="TryFindSpawnPoint"/>. Reset to 0 on any successful placement
        /// or door fallback; once it reaches <see cref="MaxConsecutivePlacementFailures"/> the next
        /// attempt for that area arrives through the door instead of deferring again.</summary>
        private readonly Dictionary<int, int> _consecutivePlacementFailuresByArea = new Dictionary<int, int>();

        /// <summary>How many release intervals (0.35 s each, ~1 s total) a single area is allowed to
        /// keep deferring a placement before it gives up finding an off-screen spot and arrives through
        /// the door instead (MV-417). A robot standing still in the far-side band the whole time would
        /// otherwise exhaust every candidate forever — arriving through a doorway reads as intentional;
        /// never spawning at all does not.</summary>
        private const int MaxConsecutivePlacementFailures = 3;

        /// <summary>The 1-based area the player is currently standing in (or last stood in, once past
        /// the final "area&lt;N&gt;" zone — the compost clearing does not advance it further).</summary>
        public int CurrentArea { get; private set; } = 1;

        /// <summary>Position-only tracking of the area Max is physically standing in, independent of
        /// <see cref="CurrentArea"/> — which <see cref="EnterArea"/> advances early, off the gate
        /// breaking, purely to give a room's population a head start (MV-245). <see cref="PlayerCrossedIntoArea"/>
        /// must NOT fire off that same early signal (MV-396: a sentinel cleared the instant a gate broke,
        /// before Max had actually walked through it), so this is tracked separately and only ever
        /// advances from the live <see cref="MapZone"/> under Max's feet in <see cref="Update"/>.</summary>
        private int _physicalArea = 1;

        /// <summary>Fired the instant Max's actual position crosses into a new area — unlike
        /// <see cref="EnterArea"/> (gate-open-driven, ahead of the player for population purposes), this
        /// reflects where Max physically is right now. What <see cref="MaxWorlds.Arena.Sentinel.DestroyAllActive"/>
        /// subscribes to instead of <see cref="AreaGate.Opened"/> (MV-396).</summary>
        public event System.Action<int> PlayerCrossedIntoArea;

        /// <summary>Robots this director currently considers live on the field.</summary>
        public int ActiveCount => _queue?.ActiveCount ?? 0;

        /// <summary>Robots still queued for the current (or a past) area, not yet released.</summary>
        public int QueuedCount => _queue?.QueuedCount ?? 0;

        /// <summary>The large-robot count <see cref="FillArea"/> solved for 1-based
        /// <paramref name="areaIndex"/> (MV-375) — 0 if that area hasn't been filled yet (or was the
        /// empty entry room). <see cref="MaxWorlds.Pickups.PickupDirector"/> divides its authored
        /// per-area cell/part budget by this so the drop curve is a designed line instead of an
        /// emergent side effect of how many robots this area's population solver happened to place.</summary>
        public int LargeCountForArea(int areaIndex) =>
            _largeCountByArea.TryGetValue(areaIndex, out int count) ? count : 0;

        /// <summary>The Bruiser count <see cref="FillArea"/> solved for 1-based
        /// <paramref name="areaIndex"/> (MV-401) — 0 if that area hasn't been filled yet, or if it was
        /// solved/authored with no Bruisers at all (e.g. world1_config's Area 4 ranged-pressure room,
        /// which is Rusher+Gunner only). <see cref="MaxWorlds.Pickups.PickupDirector"/> counts this
        /// down as Bruisers in the current area die so it can drop the arena's one guaranteed part on
        /// the last one, instead of the old periodic every-N-kills trigger.</summary>
        public int BruiserCountForArea(int areaIndex) =>
            _bruiserCountByArea.TryGetValue(areaIndex, out int count) ? count : 0;

        /// <summary>The live world config this director solves composition against, once
        /// <see cref="ConfigureWorld"/> has been called — exposed so the Settings panel can show the
        /// World &amp; Difficulty dials' authored values as their 100% reference, the same way every
        /// other knob reads its authored default off a live object.</summary>
        public WorldConfig ActiveWorldConfig => _worldCfg;

        /// <summary>Wires this director to a built map and starts Area 1's population. Call once, right
        /// after <see cref="MapRuntime.Build"/>. <paramref name="cover"/> is the cover the same build
        /// actually placed — used so a robot is never spawned on top of a hedge or planter.</summary>
        public void Configure(MapData map, IReadOnlyList<CoverPiece> cover)
        {
            _map = map;
            _cover = cover ?? System.Array.Empty<CoverPiece>();
            _queue = new AreaSpawnQueue(Mathf.RoundToInt(
                DevTuning.Or(DevTuning.MaxActiveRobots, RobotCompositionTuning.DefaultMaxActiveRobots)));
            _filledAreas.Clear();
            _largeCountByArea.Clear();
            _areaByRobot.Clear();
            _consecutivePlacementFailuresByArea.Clear();
            _pendingGarrisonByArea.Clear();
            _garrisonPlacedAreas.Clear();
            _rushersQueuedThisLevel = 0;
            CurrentArea = 1;
            _physicalArea = 1;
            FillArea(1);
        }

        /// <summary>Feeds this director a live world config (MV-270): once set, <see cref="FillArea"/>
        /// sources an area's composition from the difficulty engine's own budget solver
        /// (<see cref="WorldConfig.SolveComposition"/>) instead of the old hand-tuned
        /// <see cref="AreaPopulation"/> formula — "drive its enemies through the difficulty engine"
        /// (World &amp; Difficulty Framework §5/§10 step 4). Call any time before the area in question
        /// is first filled; areas already filled are not retroactively re-composed.</summary>
        public void ConfigureWorld(WorldConfig cfg) => _worldCfg = cfg;

        /// <summary>Grants a room's population a head start: called the instant the gate into it breaks
        /// (<see cref="AreaGate.Opened"/>), which is before the player has actually walked through the
        /// doorway. Idempotent — <see cref="FillArea"/> only ever queues a given area once, so a late
        /// position-crossing fallback call can never double-populate it.</summary>
        public void EnterArea(int areaIndex)
        {
            if (areaIndex <= CurrentArea) return;
            CurrentArea = areaIndex;
            FillArea(areaIndex);
        }

        /// <summary>The 1-based area number of an "area&lt;N&gt;" zone id, or 0 for anything else
        /// (the compost clearing, an unrecognised id, standing in the void).</summary>
        public static int AreaIndexOf(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId) || !zoneId.StartsWith("area")) return 0;
            return int.TryParse(zoneId.Substring(4), out int n) ? n : 0;
        }

        /// <summary>Wipe <paramref name="areaIndex"/>'s live/queued robots and re-solve a fresh
        /// instance of its authored composition (MV-427: the arena Max died in fully resets). Every
        /// robot currently standing inside the area's zone bounds is <see cref="RobotEnemy.Despawn"/>'d
        /// (no kill credit, no loot — it was never defeated) rather than left to keep fighting a
        /// player who is no longer there; anything still queued but not yet released for this area is
        /// dropped too (<see cref="AreaSpawnQueue.RemoveQueued"/>), so the restored roster is never
        /// topped up on top of a stale backlog. Composition solving is a pure function of the area
        /// index (<see cref="WorldConfig.SolveComposition"/>/<see cref="AreaPopulation.ComposeForArea"/>),
        /// so re-running <see cref="FillArea"/> gives back the identical authored roster — only the
        /// "already filled" guard and this area's own spawn/concealment counters reset. A no-op for
        /// the entry stub or an unrecognised area (nothing to restore).
        ///
        /// Scans <see cref="Object.FindObjectsByType{T}(FindObjectsSortMode)"/> rather than
        /// <see cref="RobotEnemy.Active"/> (MV-417) — the latter is populated from <c>OnEnable</c>,
        /// which Unity does not invoke on a plain MonoBehaviour outside Play mode, so it stays empty for
        /// every EditMode test and this despawn pass would silently do nothing, leaving a restored
        /// area's old roster alive and its queue slots stuck occupied. Both enumerate the same live set
        /// at runtime, since every robot's <c>OnEnable</c> does run there.</summary>
        public void RestoreArea(int areaIndex)
        {
            if (areaIndex <= 0 || _map == null || _queue == null) return;

            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone == null) return;

            foreach (RobotEnemy robot in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                if (robot == null || !robot.IsAlive) continue;
                Vector3 p = robot.transform.position;
                MapZone at = _map.ZoneAt(p.x, p.z);
                if (at != null && at.id == zone.id) robot.Despawn();
            }

            _queue.RemoveQueued(areaIndex);
            _filledAreas.Remove(areaIndex);
            FillArea(areaIndex);
        }

        /// <summary>Force <see cref="CurrentArea"/> and the physical-position tracker back to
        /// <paramref name="areaIndex"/> (MV-427) — called once, right after a death respawn places Max
        /// back in an earlier arena, so walking forward into the area he died in again re-fires
        /// <see cref="PlayerCrossedIntoArea"/> and the normal gate-open population hand-off exactly as
        /// it would on a first approach. Only ever rewinds — never raises either tracker, since
        /// <see cref="EnterArea"/> and <see cref="Update"/>'s own position check already own forward
        /// advancement.</summary>
        public void SetCurrentArea(int areaIndex)
        {
            if (areaIndex <= 0) return;
            if (areaIndex < CurrentArea) CurrentArea = areaIndex;
            if (areaIndex < _physicalArea) _physicalArea = areaIndex;
        }

        private void Update()
        {
            if (_map == null || _queue == null) return;

            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _target = p.transform;
                if (_target == null) return;
            }

            MapZone zone = _map.ZoneAt(_target.position.x, _target.position.z);
            int area = zone == null ? 0 : AreaIndexOf(zone.id);

            // The real, physical area-crossing signal (MV-396) — advances only off Max's own position,
            // never off a gate merely breaking. Sentinel.DestroyAllActive hangs off this, not EnterArea
            // below, so a deployed sentinel survives an open-but-uncrossed gate.
            if (area > _physicalArea)
            {
                _physicalArea = area;
                PlayerCrossedIntoArea?.Invoke(area);
            }

            // Fallback only — the real trigger is EnterArea, fired off the gate that guards this zone.
            // Kept for area 1 (nothing gates it) and as a safety net should a gate event ever be missed.
            if (area > CurrentArea)
            {
                CurrentArea = area;
                FillArea(area);
            }

            // Overflow only, by now — FillArea already released everything a fresh room could fit
            // under its own area's cap. This is what lets the rest in as that area's live count drops,
            // spaced out rather than dumped all at once. Gated on the queue's per-area cap only (MV-417)
            // — no longer on RobotEnemy.ActiveCount < EnemySpawner.GlobalMaxLiveEnemies, a field-wide
            // count that let a robot alive three rooms back starve the room the player is standing in.
            _timer += Time.deltaTime;
            if (_timer < ReleaseInterval) return;
            _timer = 0f;

            if (_queue.TryRelease(out int releaseArea, out EnemyKind kind))
                Spawn(releaseArea, kind);
        }

        private void FillArea(int areaIndex)
        {
            if (areaIndex <= 0 || !_filledAreas.Add(areaIndex)) return;
            _spawnIndexByArea[areaIndex] = 0;
            _consecutivePlacementFailuresByArea[areaIndex] = 0;

            // The lead-in/entry room (area1's "Patio & Back Door") is where Max spawns — it must stay
            // empty so a fresh run has a safe beat to orient before meeting a robot (MV-256). Marked
            // filled above so nothing re-queues it later; just never queues anything into it now.
            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone != null && zone.Kind == ZoneKind.Entry)
            {
                // MV-514: even the empty entry stub gives the NEXT area's garrison its head start —
                // otherwise the very first gated room (area 2 in world1) would still pop its garrison
                // in only once its own gate broke, exactly the bug this ticket exists to fix.
                PlacePendingGarrison(areaIndex + 1);
                return;
            }

            int totalForArea;
            if (_worldCfg != null)
            {
                // MV-442 (Lee, 2026-08-19): an authored composition (MV-365) is queued exactly as
                // designed, all seven kinds, never trimmed — RusherCap only ever applies to a
                // dial-derived solve. _rushersQueuedThisLevel still advances by an authored area's
                // Rusher count either way, so a later dial-derived area is still capped correctly.
                DifficultyEngine.Composition solved = _worldCfg.SolveComposition(areaIndex);
                bool authored = _worldCfg.AreaByIndex(areaIndex)?.composition?.IsAuthored == true;
                DifficultyEngine.Composition composition = authored ? CountRushers(solved) : ClampRusherCap(solved);
                _largeCountByArea[areaIndex] = composition.LargeCount;
                _bruiserCountByArea[areaIndex] = composition.Bruiser;
                _queue.FillExact(composition, areaIndex);
                totalForArea = composition.TotalCount;
            }
            else
            {
                var (large, small) = AreaPopulation.ComposeForArea(areaIndex,
                    DevTuning.Or(DevTuning.StartLargeCount, RobotCompositionTuning.DefaultStartLargeCount),
                    DevTuning.Or(DevTuning.StartSmallCount, RobotCompositionTuning.DefaultStartSmallCount),
                    DevTuning.Or(DevTuning.AreaGrowthPct, RobotCompositionTuning.DefaultAreaGrowthPct),
                    DevTuning.Or(DevTuning.LargeToSmallRatio, RobotCompositionTuning.DefaultLargeToSmallRatio),
                    DevTuning.Or(DevTuning.LargeShareDriftPerArea, RobotCompositionTuning.DefaultLargeShareDriftPerArea));

                float heavyIntroArea = DevTuning.Or(DevTuning.HeavyIntroArea, RobotCompositionTuning.DefaultHeavyIntroArea);
                float bruteIntroArea = DevTuning.Or(DevTuning.BruteIntroArea, RobotCompositionTuning.DefaultBruteIntroArea);
                float toughSubstitutionPct = DevTuning.Or(DevTuning.ToughSubstitutionPct, RobotCompositionTuning.DefaultToughSubstitutionPct);

                _largeCountByArea[areaIndex] = large;
                var (bruiserCount, _, _) = AreaPopulation.ToughSplitForArea(
                    areaIndex, large, heavyIntroArea, bruteIntroArea, toughSubstitutionPct);
                _bruiserCountByArea[areaIndex] = bruiserCount;
                _queue.FillForArea(areaIndex, large, small, heavyIntroArea, bruteIntroArea, toughSubstitutionPct);
                totalForArea = large + small;
            }

            // MV-417: a garrison is placed synchronously, before and independent of the queue's own
            // cap — the only thing that can guarantee this room is populated the instant the player
            // walks into it, rather than depending on the queue/interval/cap timing lining up. Seeded
            // from THIS area's own just-queued composition (deducted from what's queued, not added on
            // top of it), so RestoreArea (which re-runs this same method) gets exactly the same
            // guarantee on a post-death re-entry that first entry does.
            //
            // MV-514: usually this garrison already exists — placed dormant back when the PREVIOUS
            // area was filled (see PlacePendingGarrison) — so ActivateGarrisonFor just wakes and
            // toughens it instead of creating it fresh. Falls back to the original immediate
            // SeedGarrison when nothing was pre-placed (area 1, or a RestoreArea re-fill).
            ActivateGarrisonFor(areaIndex, _worldCfg?.AreaByIndex(areaIndex));

            // MV-363: a big enough room spares a small knot of robots to start concealed behind
            // cover instead of joining the fight the instant it fills — see Spawn() and
            // ConcealedSpawnPointInArea(). One knot per room, not a blanket policy. MV-603: each
            // member wakes purely off its own AmbushWake tick now — there is no group wiring left to
            // wake the rest of the knot early.
            _concealedRemainingThisArea = totalForArea >= MinCompositionForConcealment ? ConcealedGroupSize : 0;

            // Instantly, not paced — this room's population must already be standing by the time the
            // player can see it (MV-245). Targeted at this area specifically (MV-417) rather than plain
            // FIFO release, so a stale backlog left over from an area the player has already passed
            // can't sit ahead of the room just filled. Only what does not fit under this area's own cap
            // stays queued; a placement that can't find a legal spot this tick requeues itself and stops
            // the loop rather than spinning on the same entry (see Spawn/TryFindSpawnPoint).
            while (_queue.TryReleaseArea(areaIndex, out EnemyKind kind))
            {
                if (!Spawn(areaIndex, kind)) break;
            }

            // MV-514: NOW that this area's own composition is solved and queued (and, for a dial-derived
            // area, this area's own Rusher-cap running total has already advanced) — give the NEXT area's
            // garrison its head start. Deliberately last, not first: PlacePendingGarrison must never
            // solve a later area's composition before this one's own has been counted, or the cumulative
            // Rusher cap would clamp areas out of their true chronological order.
            PlacePendingGarrison(areaIndex + 1);
        }

        /// <summary>Gives <paramref name="areaIndex"/>'s garrison a head start the moment the area
        /// BEFORE it is filled (MV-514): placed and visible right away, at the exact same
        /// <see cref="Garrison.SeedPositions"/> spots <see cref="ActivateGarrisonFor"/> always used, but
        /// held <see cref="RobotEnemy.BeginDormant"/> — no movement, no aggro, no firing — and, because
        /// nothing here touches <see cref="_queue"/>, no contribution to the live-enemy cap either. This
        /// is the fix: previously a garrison was placed (and toughened) only once THIS area's own gate
        /// broke, so it visibly popped into existence in a room the player could already see through the
        /// still-closed gate (Lee, 2026-08-21, reproduced in area 2).
        ///
        /// Deliberately does not solve or queue this area's ambient composition (that stays exactly
        /// where it always was, in <see cref="FillArea"/>, at gate-break time) — only which KINDS the
        /// garrison's own <see cref="Garrison.SeedCount"/> slots will be, previewed on a throwaway
        /// <see cref="AreaSpawnQueue"/> using this area's own composition solved read-only (never
        /// mutating <see cref="_rushersQueuedThisLevel"/>: that must only ever advance once per area,
        /// which <see cref="FillArea"/> still does, later, for real). Toughness is deliberately NOT
        /// applied yet — <see cref="ActivateGarrisonFor"/> re-applies it with whatever
        /// <see cref="DifficultyDirector.ToughnessMultiplier"/> is live at the moment this area's own
        /// gate actually breaks, not the one in effect back when this merely ran (MV-514's trap: freezing
        /// it here would silently ease every area after the first as the run escalates).</summary>
        private void PlacePendingGarrison(int areaIndex)
        {
            if (areaIndex <= 0 || !_garrisonPlacedAreas.Add(areaIndex)) return;
            if (_worldCfg == null) return;   // legacy (no world config) path never garrisons ahead of time

            WorldArea area = _worldCfg.AreaByIndex(areaIndex);
            if (area == null) return;   // past the world's own end (e.g. the area after the boss room)

            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone != null && zone.Kind == ZoneKind.Entry) return;   // the empty lead-in room never garrisons

            int seedCount = Garrison.SeedCount(areaIndex, _worldCfg);
            if (seedCount <= 0) return;

            DifficultyEngine.Composition solved = _worldCfg.SolveComposition(areaIndex);
            bool authored = area.composition?.IsAuthored == true;
            // Read-only preview of the same clamp FillArea will apply for real later — Apply() itself
            // mutates nothing (see RusherCap's own doc comment), so calling it here is safe; only
            // ClampRusherCap/CountRushers (which advance _rushersQueuedThisLevel) are ever allowed to
            // run for a given area more than the one time FillArea does it.
            DifficultyEngine.Composition preview = authored ? solved : RusherCap.Apply(solved, _rushersQueuedThisLevel);

            var previewQueue = new AreaSpawnQueue(1);
            previewQueue.FillExact(preview, areaIndex);

            Garrison.Seed[] slots = Garrison.SeedSlots(area, seedCount);
            var pending = new List<RobotEnemy>(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                if (!previewQueue.TryTakeForGarrison(areaIndex, slots[i].Kind, out EnemyKind kind)) break;

                EnemyArchetype archetype = EnemyArchetype.Of(kind)
                    .WithHealthMultiplier(DevTuning.Or(DevTuning.RobotHealthMultiplier, EnemySpawner.DefaultRobotHealthMultiplier));

                RobotEnemy e = Take(kind, archetype);
                Vector3 pos = slots[i].Position;
                pos.y = archetype.SpawnHeight;
                e.transform.position = pos;
                e.transform.rotation = Quaternion.identity;
                e.gameObject.SetActive(true);
                _areaByRobot[e] = areaIndex;

                // BeginDormant() must run AFTER SetActive(true): OnEnable() calls ResetState(), which
                // would otherwise stamp this robot back to a fresh Chase state.
                e.BeginDormant();
                pending.Add(e);

                LetThePlayerThrough(e.gameObject);
            }

            if (pending.Count > 0) _pendingGarrisonByArea[areaIndex] = pending;
        }

        /// <summary>Wakes and toughens <paramref name="areaIndex"/>'s pre-placed garrison (see
        /// <see cref="PlacePendingGarrison"/>) the moment this area's own gate breaks, using whatever
        /// <see cref="DifficultyDirector.ToughnessMultiplier"/> is live RIGHT NOW — never the one that
        /// was live back when the garrison was merely placed (MV-514). Still drains
        /// <see cref="AreaSpawnQueue.TryTakeForGarrison"/> once per pre-placed member, exactly as the
        /// immediate-placement path always did, so this area's live-cap accounting stays correct even
        /// though the robot itself already existed. Falls back to the original <see cref="SeedGarrison"/>
        /// (solve, place and toughen immediately) when nothing was pre-placed for this area — area 1
        /// (nothing precedes it) or a post-death <see cref="RestoreArea"/> re-fill.</summary>
        private void ActivateGarrisonFor(int areaIndex, WorldArea area)
        {
            if (!_pendingGarrisonByArea.TryGetValue(areaIndex, out List<RobotEnemy> pending))
            {
                SeedGarrison(areaIndex, area);
                return;
            }
            _pendingGarrisonByArea.Remove(areaIndex);

            foreach (RobotEnemy e in pending)
            {
                if (!_queue.TryTakeForGarrison(areaIndex, out _)) break;

                if (e == null || !e.IsAlive)
                {
                    // Splash/AoE reached a dormant member before this area's gate ever broke - the
                    // queue slot just drained above must not leak as a permanently phantom "active" one.
                    _queue.ReportDestroyed(areaIndex);
                    continue;
                }

                EnemyArchetype archetype = EnemyArchetype.Of(e.Kind)
                    .WithHealthMultiplier(DevTuning.Or(DevTuning.RobotHealthMultiplier, EnemySpawner.DefaultRobotHealthMultiplier))
                    .Toughened(DifficultyDirector.ToughnessMultiplier);
                e.Retoughen(archetype);
                e.Activate();
            }
        }

        /// <summary>Places <see cref="Garrison.SeedCount"/> robots from <paramref name="areaIndex"/>'s
        /// just-queued composition at <see cref="Garrison.SeedPositions"/> — authored, deterministic
        /// positions, standing there already rather than popping in (MV-269/MV-417). Bypasses the
        /// queue's concurrent cap entirely via <see cref="AreaSpawnQueue.TryTakeForGarrison"/>: a
        /// garrison must be guaranteed present regardless of whatever cap/timing state the ambient
        /// top-up queue happens to be in. A no-op without a live world config (no <see cref="WorldArea"/>
        /// to read <c>garrisonDensity</c>/positions from) or when <paramref name="area"/> is null (area
        /// never filled / unrecognised).
        ///
        /// MV-478: every robot this places starts <see cref="RobotEnemy.BeginDormant"/> — each one
        /// wakes purely off its own <c>AmbushWake</c> tick (MV-603 retired the shared-group chain-wake
        /// that used to pop the whole ring the instant one member was seen).</summary>
        private void SeedGarrison(int areaIndex, WorldArea area)
        {
            if (_worldCfg == null || area == null) return;

            int seedCount = Garrison.SeedCount(areaIndex, _worldCfg);
            if (seedCount <= 0) return;

            Garrison.Seed[] slots = Garrison.SeedSlots(area, seedCount);
            for (int i = 0; i < slots.Length; i++)
            {
                if (!_queue.TryTakeForGarrison(areaIndex, slots[i].Kind, out EnemyKind kind)) break;

                EnemyArchetype archetype = EnemyArchetype.Of(kind)
                    .WithHealthMultiplier(DevTuning.Or(DevTuning.RobotHealthMultiplier, EnemySpawner.DefaultRobotHealthMultiplier))
                    .Toughened(DifficultyDirector.ToughnessMultiplier);

                RobotEnemy e = Take(kind, archetype);
                Vector3 pos = slots[i].Position;
                pos.y = archetype.SpawnHeight;
                e.transform.position = pos;
                e.transform.rotation = Quaternion.identity;
                e.gameObject.SetActive(true);
                _areaByRobot[e] = areaIndex;

                // BeginDormant() must run AFTER SetActive(true): OnEnable() calls ResetState(), which
                // would otherwise stamp this robot back to a fresh Chase state and wipe the call below.
                e.BeginDormant();

                LetThePlayerThrough(e.gameObject);
            }
        }

        /// <summary>Applies <see cref="RusherCap.Apply"/> against this director's running total, then
        /// advances that total by however many Rushers actually made it through. Only ever called for
        /// a dial-derived composition (MV-442) — an authored one goes through <see cref="CountRushers"/>
        /// instead, which advances the same running total without trimming.</summary>
        private DifficultyEngine.Composition ClampRusherCap(DifficultyEngine.Composition composition)
        {
            DifficultyEngine.Composition clamped = RusherCap.Apply(composition, _rushersQueuedThisLevel);
            _rushersQueuedThisLevel += clamped.Rusher;
            return clamped;
        }

        /// <summary>Advances the running Rusher total by an authored (MV-365) area's exact count,
        /// untouched — so a later dial-derived area is still capped correctly by
        /// <see cref="ClampRusherCap"/>, without this area's own count ever being trimmed (MV-442).</summary>
        private DifficultyEngine.Composition CountRushers(DifficultyEngine.Composition composition)
        {
            _rushersQueuedThisLevel += composition.Rusher;
            return composition;
        }

        /// <summary>True if <paramref name="areaIndex"/>'s authored scenario tag (MV-365,
        /// <see cref="WorldArea.scenario"/>) is <c>"centerDenial"</c> — false (ordinary placement) if
        /// there is no live world config, no matching area, or no scenario authored.</summary>
        private bool IsCenterDenialScenario(int areaIndex) =>
            _worldCfg?.AreaByIndex(areaIndex)?.scenario == "centerDenial";

        /// <summary>Spawns one robot into <paramref name="areaIndex"/> — the area it was actually
        /// queued for (MV-417), which the caller must pass through from <see cref="AreaSpawnQueue.TryRelease(out int, out EnemyKind)"/>
        /// rather than assuming <see cref="CurrentArea"/>. An overflow robot released after the player
        /// has moved on to a later area no longer materialises wherever the field happens to be right
        /// now — it lands back in the room it was meant for, even if that is behind the player.
        ///
        /// Returns false, WITHOUT placing anything, if a non-concealed placement couldn't find a legal
        /// spot this tick and the starvation guard hasn't tripped yet (MV-417) — the caller's queue
        /// entry has already been put back via <see cref="AreaSpawnQueue.Requeue"/> and will be retried
        /// on the next release interval. A pooled instance is never taken for a spawn that isn't going
        /// to be placed.</summary>
        private bool Spawn(int areaIndex, EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind)
                .WithHealthMultiplier(DevTuning.Or(DevTuning.RobotHealthMultiplier, EnemySpawner.DefaultRobotHealthMultiplier))
                .Toughened(DifficultyDirector.ToughnessMultiplier);

            // MV-363: never the centreDenial Launcher barrage — that cluster is meant to read as
            // visible, denied ground, not a hidden knot.
            bool concealed = _concealedRemainingThisArea > 0 && kind != EnemyKind.Launcher;
            Vector3 position;

            if (concealed)
            {
                position = ConcealedSpawnPointInArea(areaIndex, archetype.SpawnHeight);
            }
            else if (TryFindSpawnPoint(areaIndex, kind, archetype.SpawnHeight, NextSpawnIndex(areaIndex), out position))
            {
                _consecutivePlacementFailuresByArea[areaIndex] = 0;
            }
            else if (IncrementPlacementFailure(areaIndex) >= MaxConsecutivePlacementFailures)
            {
                // MV-417 starvation guard: every candidate this pass read as on-screen (e.g. the player
                // standing in the far-side band the whole time). Rather than keep deferring forever,
                // arrive through the door — reads as intentional, unlike popping into open lawn.
                position = DoorPoint(areaIndex, archetype.SpawnHeight);
                _consecutivePlacementFailuresByArea[areaIndex] = 0;
            }
            else
            {
                _queue.Requeue(areaIndex, kind);
                return false;
            }

            RobotEnemy e = Take(kind, archetype);
            e.transform.position = position;
            e.transform.rotation = Quaternion.identity;
            e.gameObject.SetActive(true);
            _areaByRobot[e] = areaIndex;

            // BeginDormant() must run AFTER SetActive(true): OnEnable() calls ResetState(), which
            // would otherwise stamp this robot back to a fresh Chase state and wipe the call below.
            if (concealed)
            {
                _concealedRemainingThisArea--;
                e.BeginDormant();
            }

            // Re-applied on every spawn, not just on creation — Unity drops an ignored collider pair
            // when the collider is disabled, and pooling disables it on every death.
            LetThePlayerThrough(e.gameObject);
            return true;
        }

        private int IncrementPlacementFailure(int areaIndex)
        {
            _consecutivePlacementFailuresByArea.TryGetValue(areaIndex, out int count);
            count++;
            _consecutivePlacementFailuresByArea[areaIndex] = count;
            return count;
        }

        /// <summary>Approximates where <paramref name="areaIndex"/>'s door is: the midpoint of the wall
        /// on the near side from <see cref="MapRuntime.EntryDirection"/> (MV-417's starvation-guard
        /// fallback). Not the gate's exact geometry — this class only has the zone's own bounds to work
        /// with — but close enough that a robot arriving here reads as coming through the doorway rather
        /// than materialising in open lawn. Falls back to the room's centre when no entry direction is
        /// known (e.g. area 1, entered from outside the map rather than through an authored gate).</summary>
        private Vector3 DoorPoint(int areaIndex, float height)
        {
            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone == null) return _target != null ? _target.position : Vector3.zero;

            float xMin = zone.XMin + EdgeMargin, xMax = zone.XMax - EdgeMargin;
            float zMin = zone.ZMin + EdgeMargin, zMax = zone.ZMax - EdgeMargin;
            float cx = (xMin + xMax) * 0.5f, cz = (zMin + zMax) * 0.5f;

            Vector3 awayFromDoor = MapRuntime.EntryDirection(_map, zone.id);
            if (awayFromDoor.sqrMagnitude < 1e-6f) return new Vector3(cx, height, cz);

            return Mathf.Abs(awayFromDoor.x) >= Mathf.Abs(awayFromDoor.z)
                ? new Vector3(awayFromDoor.x >= 0f ? xMin : xMax, height, cz)
                : new Vector3(cx, height, awayFromDoor.z >= 0f ? zMin : zMax);
        }

        /// <summary>The next stagger-band ordinal for a placement into <paramref name="areaIndex"/>
        /// (MV-324, MV-417) — tracked per area so an overflow robot released well after its own area
        /// last filled still continues that area's own sequence instead of whichever area is
        /// currently being filled.</summary>
        private int NextSpawnIndex(int areaIndex)
        {
            _spawnIndexByArea.TryGetValue(areaIndex, out int index);
            _spawnIndexByArea[areaIndex] = index + 1;
            return index;
        }

        /// <summary>Picks a point inside the room, clear of walls, cover and other active robots, and —
        /// when a camera exists to ask — outside its view: a robot must never be seen popping into
        /// existence, whether it's part of a room's instant fill or an overflow robot let in once the
        /// player has already been fighting there for a while (MV-245, MV-273). Never being seen matters
        /// more than a clean gap from cover or another robot, so a second, wider pass ignores overlap
        /// once the ideal search comes up empty (see <see cref="MaxOffScreenAttempts"/>) rather than
        /// falling straight back to an on-screen spawn.
        ///
        /// Returns false (not a candidate) only when a camera exists AND every candidate across both
        /// passes read as on-screen (MV-417) — e.g. the player standing in the middle of the far-side
        /// band, where the visible footprint covers most of the room. The old behaviour silently placed
        /// the last candidate anyway, which is exactly the "robots just appearing out of nowhere" defect
        /// this exists to fix; the caller (<see cref="Spawn"/>) now defers or falls back to the door
        /// instead. Without a camera to ask (e.g. EditMode), placement still always succeeds.</summary>
        private bool TryFindSpawnPoint(int areaIndex, EnemyKind kind, float height, int spawnIndex, out Vector3 point)
        {
            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone == null || zone.width <= EdgeMargin * 2f || zone.depth <= EdgeMargin * 2f)
            {
                point = _target != null ? _target.position : Vector3.zero;
                return true;
            }

            // MV-365: a centreDenial scenario's Launcher-kind spawns bias toward the room's middle
            // instead of the usual far-side-from-door band — "a barrage of missiles in the middle,
            // surrounded by robots" is a placement fact, not just a count.
            if (kind == EnemyKind.Launcher && IsCenterDenialScenario(areaIndex))
            {
                point = RandomPointIn(SpawnBias.CenterBand(zone, EdgeMargin), height);
                return true;
            }

            // MV-323: bias candidates to the side of the room opposite the door robots/Max just came
            // through, so the ambient fight tends to stay off the entrance rather than piling up on it.
            Vector3 awayFromDoor = MapRuntime.EntryDirection(_map, zone.id);
            Rect farSide = SpawnBias.FarSideBounds(zone, awayFromDoor, EdgeMargin);

            // MV-324: within that far side, cycle each spawn through its own distance-from-gate tier so
            // the room's robots don't all close the gap on Max at once.
            Rect bounds = SpawnBias.StaggerBand(farSide, awayFromDoor, spawnIndex, StaggerBandCount);

            Camera cam = Camera.main;
            Vector3 candidate = default;

            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                candidate = RandomPointIn(bounds, height);
                if (OverlapsCoverOrRobot(candidate)) continue;
                if (cam != null && IsOnScreen(cam, candidate)) continue;

                point = candidate;
                return true;
            }

            if (cam == null)
            {
                // No camera to ask (EditMode/tests) — on-screen can't be evaluated, so the un-biased
                // last candidate is as good as any; this path never fails.
                point = candidate;
                return true;
            }

            for (int attempt = 0; attempt < MaxOffScreenAttempts; attempt++)
            {
                candidate = RandomPointIn(bounds, height);
                if (!IsOnScreen(cam, candidate))
                {
                    point = candidate;
                    return true;
                }
            }

            point = default;
            return false;
        }

        /// <summary>Where a concealed robot lands (MV-363): behind the room's own deepest authored
        /// cover piece — the one farthest from the door — so a dormant knot is genuinely hidden, not
        /// merely placed on the ordinary far-side band everything else uses. Falls back to that far
        /// band's own deepest stagger tier when the room carries no cover of its own, so "real
        /// distance from the gate" still holds even without a prop to hide behind.</summary>
        private Vector3 ConcealedSpawnPointInArea(int areaIndex, float height)
        {
            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone == null || zone.width <= EdgeMargin * 2f || zone.depth <= EdgeMargin * 2f)
                return _target != null ? _target.position : Vector3.zero;

            Vector3 awayFromDoor = MapRuntime.EntryDirection(_map, zone.id);
            Camera cam = Camera.main;

            var coverInZone = new List<ArenaCover>();
            foreach (CoverPiece piece in _cover)
                if (ConcealmentBias.InsideZone(zone, piece.Cover.CenterXz))
                    coverInZone.Add(piece.Cover);

            if (ConcealmentBias.TryBehindDeepestCover(coverInZone, zone, awayFromDoor, EdgeMargin, out Vector2 anchor))
            {
                for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
                {
                    Vector2 jitter = anchor + Random.insideUnitCircle * ConcealmentBias.JitterRadius;
                    var candidate = new Vector3(jitter.x, height, jitter.y);
                    if (OverlapsCoverOrRobot(candidate)) continue;
                    if (cam != null && IsOnScreen(cam, candidate)) continue;
                    return candidate;
                }
            }

            Rect farSide = SpawnBias.FarSideBounds(zone, awayFromDoor, EdgeMargin);
            Rect deepest = SpawnBias.StaggerBand(farSide, awayFromDoor, StaggerBandCount - 1, StaggerBandCount);

            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                Vector3 candidate = RandomPointIn(deepest, height);
                if (OverlapsCoverOrRobot(candidate)) continue;
                if (cam != null && IsOnScreen(cam, candidate)) continue;
                return candidate;
            }

            return RandomPointIn(deepest, height);
        }

        private static Vector3 RandomPointIn(Rect bounds, float height)
        {
            float x = Random.Range(bounds.xMin, bounds.xMax);
            float z = Random.Range(bounds.yMin, bounds.yMax);
            return new Vector3(x, height, z);
        }

        private bool OverlapsCoverOrRobot(Vector3 point)
        {
            var flat = new Vector2(point.x, point.z);

            foreach (CoverPiece piece in _cover)
                if (piece.Cover.DistanceTo(flat) < MapValidation.SpawnClearance)
                    return true;

            foreach (RobotEnemy robot in RobotEnemy.Active)
            {
                if (robot == null) continue;
                Vector3 p = robot.transform.position;
                if ((new Vector2(p.x, p.z) - flat).sqrMagnitude < PlacementSpacing * PlacementSpacing)
                    return true;
            }

            return false;
        }

        // MV-527: reused every call instead of the allocating GeometryUtility.CalculateFrustumPlanes(Camera)
        // overload's fresh Plane[6] — this runs up to MaxPlacementAttempts times per spawn placement.
        private static readonly Plane[] s_frustumPlanes = new Plane[6];

        private static bool IsOnScreen(Camera cam, Vector3 point)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, s_frustumPlanes);
            return GeometryUtility.TestPlanesAABB(s_frustumPlanes, new Bounds(point, Vector3.one));
        }

        private RobotEnemy Take(EnemyKind kind, in EnemyArchetype archetype)
        {
            if (_pools.TryGetValue(kind, out var pool) && pool.Count > 0)
            {
                RobotEnemy pooled = pool.Pop();
                pooled.Apply(archetype);
                return pooled;
            }
            return CreateInstance(archetype);
        }

        private RobotEnemy CreateInstance(in EnemyArchetype a)
        {
            RobotEnemy e;
            if (prefab != null)
            {
                e = Instantiate(prefab, Bodies());
            }
            else
            {
                var go = GameObject.CreatePrimitive(
                    a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
                go.name = $"RobotEnemy {a.Kind} (area spawn)";
                go.transform.SetParent(Bodies(), false);
                go.transform.localScale = a.BodyScale;

                var cc = go.AddComponent<CharacterController>();
                float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
                cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
                cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
                cc.center = Vector3.zero;

                e = go.AddComponent<RobotEnemy>();
            }

            // MV-350 diagnostic tag — see RobotSpawnSource. Stamped once, here, regardless of which
            // branch above built the instance.
            e.gameObject.AddComponent<RobotSpawnSource>().Mark("AreaAccumulationDirector");

            e.Apply(a);                 // stats/Kind — MUST land before RobotRig attaches (MV-535):
                                         // RobotRig.Awake() reads _enemy.Kind synchronously to build its
                                         // body, so attaching it before Apply() builds every robot as
                                         // the default Kind (Rusher).

            // MV-527: dressing moved here from RobotRigDirector's per-frame FindObjectsByType<RobotEnemy>
            // sweep — attached once, here, at the one place a NEW instance is actually built (a pooled
            // Take() reuses the same GameObject and skips CreateInstance entirely, so this never runs
            // twice for one robot). See EnemySpawner.CreateInstance's identical comment.
            e.gameObject.AddComponent<RobotRig>();
            e.gameObject.AddComponent<RobotSkinDiagnostics>();
            e.Died += OnEnemyDied;
            e.gameObject.SetActive(false);
            return e;
        }

        private void OnEnemyDied(RobotEnemy e)
        {
            int area = _areaByRobot.TryGetValue(e, out int a) ? a : 0;
            _areaByRobot.Remove(e);
            _queue?.ReportDestroyed(area);
            if (!_pools.TryGetValue(e.Kind, out var pool))
            {
                pool = new Stack<RobotEnemy>();
                _pools[e.Kind] = pool;
            }
            pool.Push(e);
        }

        private Transform Bodies()
        {
            if (_bodies == null)
                _bodies = new GameObject("Area Robots").transform;
            return _bodies;
        }

        private void LetThePlayerThrough(GameObject enemy)
        {
            if (_playerColliders == null || _playerColliders.Length == 0)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _playerColliders = p.GetComponents<Collider>();
            }

            var enemyColliders = enemy.GetComponents<Collider>();
            foreach (var ec in enemyColliders)
            {
                if (ec == null) continue;
                foreach (var pc in _playerColliders)
                {
                    if (pc == null) continue;
                    Physics.IgnoreCollision(ec, pc, true);
                }
            }
        }
    }
}
