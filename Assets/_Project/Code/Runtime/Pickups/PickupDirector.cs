using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// Turns robot deaths into drops and collects them (YT-131) — a self-installing director, the
    /// project idiom (<c>GroundAnchorVfx</c>, <c>HoseDirector</c>), so it needs no scene wiring.
    ///
    /// The drop policy is a strict small/large split (WV-226): the small tier —
    /// <see cref="EnemyKind.Rusher"/> — drops nothing at all, no roll, no trickle. Only the large
    /// tier — bruiser, heavy and brute (<see cref="EnemyArchetype.IsLarge"/>, MV-224) — drops loot.
    /// Cells are an authored per-area total (<see cref="CellEconomyTuning.CellsForArea"/>, MV-375),
    /// spread across that area's actual solved large-kill count so the run's cell curve rises on a
    /// designed straight line instead of riding the enemy population's exponential growth — see
    /// <see cref="ResolveCellDrop"/>. Falls back to the flat <see cref="CellEconomyTuning.DefaultCellsPerLargeKill"/>
    /// rate outside a live area context (tests) or under a dev-tuning override.
    ///
    /// Parts drop exactly once per arena, from the last Bruiser destroyed in it (MV-401) — see
    /// <see cref="IsLastBruiserInArea"/>. This replaces MV-183/MV-226/MV-375's periodic
    /// every-N-large-kills trigger, which could fire more than once inside a populous arena; that
    /// mechanic's tuning (the per-area part curve and its Settings dev slider) was dead code left
    /// over from the old trigger and has been removed (MV-459).
    ///
    /// Each frame it does the walk-over collection itself: one Max lookup, one pool, a planar distance
    /// test per live pickup. Banking goes through <see cref="PickupWallet"/>; the HUD reacts to that.
    ///
    /// Parts are now universal upgrade tokens (WV-228): every paced drop banks, there is no longer a
    /// guaranteed-unique table to run dry against (YT-133's old <c>PartDropTable</c> is retired from
    /// this loop). A dropped part's <see cref="MaxWorlds.Upgrades.PartKind"/> is purely cosmetic now —
    /// it only steers <c>PickupArtDirector</c>'s occasional Hydro-device swap.
    ///
    /// Sheds are the ability-unlock mechanic (WV-229; draft-pick MV-357; moved off the mid-fight modal
    /// by MV-358): a destroyed <c>MowerHutch</c> reports through <see cref="HudSignals.FactoryDestroyed"/>.
    /// The first shed drops a visible <see cref="PickupKind.Device"/> — now a Morphing Module — pickup
    /// at the shed's spot if any RIG category is still locked (MV-382, reinstating the walk-over
    /// collectible MV-357/358 had reduced to an instant invisible grant) — no pause, no screen, the
    /// fight keeps going. After that, only every OTHER destroyed shed does (MV-948, see
    /// <see cref="IsDeviceShedOrdinal"/>) — spreading the unlocks out rather than opening every
    /// remaining category in the run's first few sheds. Walking over a Device draws THE RIG's
    /// locked-category pool immediately and routes straight to the outcome (MV-424, replacing the old
    /// bank-then-BUILD-ABILITY step; MV-457 replaced the node draw with a family draw): 0 candidates
    /// consumes the module, 1 unlocks it directly, 2 opens THE RIG board's draft overlay to choose
    /// between them. A shed that doesn't drop a Device — nothing left locked, or an even ordinal —
    /// falls back to a part plus a bigger "cell cache" instead.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("pickups")]
    public sealed class PickupDirector : MonoBehaviour
    {
        /// <summary>Walk-over magnet radius, metres — planar distance from Max at which a pickup is
        /// collected. Generous: this is a phone game, you shouldn't have to thread a needle.</summary>
        public const float CollectRadius = 1.4f;

        /// <summary>Power cells in the "cell cache" a shed drops once every ability is owned (WV-229) —
        /// bigger than a large kill's guaranteed drop (<see cref="CellEconomyTuning.DefaultCellsPerLargeKill"/>)
        /// since it's standing in for the ability device the shed can no longer hand out.</summary>
        public const int ShedCellCacheAmount = 6;

        private const float ScatterRadius = 0.9f;

        /// <summary>MV-1106 (Lee, 2026-10-06): "these Dredge Hulk robots ... don't drop anything" —
        /// every World 3 Brute (the Dredge Hulk skin, world3_config.json) killed drops this many parts
        /// ALWAYS, on top of whatever the per-area roll (<see cref="ResolveCellDrop"/>) already gives —
        /// a floor independent of that roll's own fractional accumulator, which can land a given kill's
        /// share at zero. See <see cref="OnRobotDied"/>.</summary>
        private const int DredgeHulkGuaranteedPartDrops = 2;

        /// <summary>MV-626, change 2: uncollected power cells allowed live on the ground at once,
        /// oldest recycled first once this is hit — the actual bound on the accumulation this ticket
        /// fixes, independent of the reserve-full gate in <see cref="SpawnDrop"/> (which only stops
        /// growth once the wallet itself is full; a fast enough kill streak could otherwise still pile
        /// cells up below that ceiling). 24 sits well under the ~70-cell pile Lee's report measured at
        /// 19fps, and change 4 below removes the per-frame render cost each surviving cell pays anyway.
        /// Same bounded-population idiom as <c>AmbienceVfx.maxDecals</c> (40) and
        /// <c>DissolveVfx.maxGhosts</c> (12).</summary>
        private const int MaxLiveCells = 24;

        /// <summary>MV-1101: how long a robot-dropped pickup (a Part or an Energy Cell) survives on the
        /// ground before expiring back to the pool — tightened from the old flat 30s (MV-626) to force a
        /// risk/reward choice instead of letting a drop sit forever. Only a drop <see
        /// cref="MarkAgesOnGround"/> actually marks ages at all — a shed's cell cache, a boss drop, the
        /// Weapon Core, a Morphing Module and a Rack Module are never marked and so never expire.</summary>
        private const float RobotDropLifetimeSeconds = 10f;

        /// <summary>MV-1101: for its last this-many seconds before expiring, a robot-dropped pickup
        /// blinks (see <see cref="BlinkHz"/>) instead of vanishing with no warning.</summary>
        private const float RobotDropBlinkWarningSeconds = 3f;

        /// <summary>MV-1101: the warning blink's toggle rate — the visible/hidden state flips this many
        /// times per second once a drop enters its last <see cref="RobotDropBlinkWarningSeconds"/>.</summary>
        private const float BlinkHz = 6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install() => EnsureInstalled();

        /// <summary>The live director, self-installing one if a caller needs to place a pickup before
        /// <see cref="Install"/>'s own AfterSceneLoad hook has run — an EditMode test (or any other
        /// caller) building a map straight through <c>MapRuntime</c> with no scene load in between
        /// (MV-644). Same find-or-create idiom <see cref="Install"/> always used.</summary>
        public static PickupDirector EnsureInstalled()
        {
            var existing = FindFirstObjectByType<PickupDirector>();
            return existing != null ? existing : new GameObject("PickupDirector").AddComponent<PickupDirector>();
        }

        private readonly List<Pickup> _live = new List<Pickup>(32);

        /// <summary>Pickups a refused-at-capacity tell has already fired for during the current
        /// walk-over (MV-439) — cleared the moment Max steps back out of <see cref="CollectRadius"/>,
        /// so a fresh entry gets one fresh tell rather than the frame-by-frame spam a naive re-emit
        /// on every <see cref="Collect"/> call inside the radius would cause.</summary>
        private readonly HashSet<Pickup> _reserveFullTold = new HashSet<Pickup>();
        private readonly Stack<Pickup> _cellPool = new Stack<Pickup>(16);
        private readonly Stack<Pickup> _supercellPool = new Stack<Pickup>(8);
        private readonly Stack<Pickup> _devicePool = new Stack<Pickup>(4);
        private readonly Stack<Pickup> _powerCellSecondaryPool = new Stack<Pickup>(8);
        private readonly Stack<Pickup> _weaponCorePool = new Stack<Pickup>(1);
        private readonly Stack<Pickup> _rackModulePool = new Stack<Pickup>(1);

        /// <summary>MV-727: true once this run's ONE Rack Module has dropped — gates every Replicator
        /// after the first. Run-scoped like every other per-run flag in this codebase (RigState,
        /// PickupWallet, ...): a fresh run always reaches this director through a scene reload
        /// (RunFlow.QuitToMenu / StartNextWorld / HomeScreen.OnWorld2 all reload before a new run's
        /// first frame), which destroys the old GameObject and recreates a fresh one, so no explicit
        /// reset call is needed the way RigState.Reset()/PickupWallet.Reset() need one.</summary>
        private bool _rackModuleDroppedThisRun;

        /// <summary>Live power cells in spawn order, oldest first (MV-626). <see
        /// cref="RecycleOldestCellIfAtCap"/> needs O(1) oldest-lookup, and an ordinary walk-over
        /// collect can remove any cell — not just the oldest — so <see cref="_cellNodes"/> gives O(1)
        /// removal from the middle too, which <see cref="_live"/> alone can't without an index scan
        /// per kill.</summary>
        private readonly LinkedList<Pickup> _cellOrder = new LinkedList<Pickup>();
        private readonly Dictionary<Pickup, LinkedListNode<Pickup>> _cellNodes = new Dictionary<Pickup, LinkedListNode<Pickup>>(32);

        /// <summary>Seconds since each live robot-dropped pickup hit the ground (MV-626 change 3;
        /// MV-1101 generalises it to Energy Cells and restricts it to robot-kill drops only — see <see
        /// cref="MarkAgesOnGround"/>). Supercell/Device/WeaponCore/RackModule grants never fail to
        /// collect (see <see cref="SpawnDrop"/>) and are never marked here either way, so they never
        /// pile up and are a different population (this ticket's own "Relationship" note).</summary>
        private readonly Dictionary<Pickup, float> _dropAge = new Dictionary<Pickup, float>(32);

        /// <summary>MV-1101 item 5: pickups the current <see cref="Tick"/> call's Magneto-pull pass is
        /// actively reeling in — a pull in flight pauses <see cref="TickCellLifetimes"/>'s ageing for
        /// that pickup; the clock resumes the moment the pull stops (the pickup is simply absent from
        /// this set on a later tick).</summary>
        private readonly HashSet<Pickup> _pullingThisTick = new HashSet<Pickup>();

        private Transform _max;
        private int _largeKills;

        /// <summary>Resolved lazily, same idiom as <see cref="_max"/> — re-searched each time it's null
        /// rather than cached-as-missing, so a director created after this one installs (map build order)
        /// is still picked up on the first kill that follows it. A headless test scene with no area
        /// director simply never finds one, and every kill falls back to the flat legacy rate.</summary>
        private AreaAccumulationDirector _areaDirector;

        /// <summary>The area <see cref="_cellAccum"/> is currently tracking (MV-375) — reset whenever a
        /// kill lands in a different area so a fresh area starts its cell budget from zero instead of
        /// carrying over the previous area's leftover fraction.</summary>
        private int _cellBudgetArea = -1;
        private float _cellAccum;

        /// <summary>MV-672: a running fractional accumulator for Power Cells (the new secondary
        /// currency) — same idiom as <see cref="_cellAccum"/>, but scoped to the whole run rather than
        /// per-area, since the drop ratio is a flat fraction of Parts drops, not an authored per-area
        /// budget. Accumulates <c>partsDropped * ratio</c> on every large kill; whenever it crosses a
        /// whole number, that many Power Cells drop and the fraction carries over — so a non-integer
        /// ratio (e.g. 0.1) still lands on the right long-run average instead of rounding away.</summary>
        private float _powerCellSecondaryAccum;

        /// <summary>The area <see cref="_bruiserRemaining"/> is currently counting down (MV-401) —
        /// reset whenever a Bruiser dies in a different area so a fresh area starts from that area's
        /// own solved Bruiser count instead of carrying over a stale one.</summary>
        private int _bruiserBudgetArea = -1;
        private int _bruiserRemaining;

        private void OnEnable()
        {
            DropSignals.RobotDied += OnRobotDied;
            HudSignals.FactoryDestroyed += OnFactoryDestroyed;
        }

        private void OnDisable()
        {
            DropSignals.RobotDied -= OnRobotDied;
            HudSignals.FactoryDestroyed -= OnFactoryDestroyed;
        }

        private void OnRobotDied(Vector3 pos, EnemyKind kind)
        {
            // WV-226: the small tier drops nothing at all — only large kills carry loot. Bruiser,
            // heavy and brute (MV-224) all count as "large" for economy purposes.
            if (!EnemyArchetype.IsLarge(kind)) return;

            _largeKills++;

            int cells = ResolveCellDrop();
            for (int i = 0; i < cells; i++)
            {
                float ang = i * (Mathf.PI * 2f / cells);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                Pickup dropped = SpawnDrop(PickupKind.PowerCell, pos + off);
                if (dropped != null) MarkAgesOnGround(dropped);   // MV-1101: a robot-kill Part ages
            }

            // MV-1106: a World 3 Brute (Dredge Hulk) always drops its own guaranteed parts too, whether
            // it came from a reactor (shed) or the area garrison — Die() reaches this listener through
            // the same DropSignals.EmitRobotDied call regardless of how the robot was placed, so this
            // is additive insurance against ResolveCellDrop's own roll landing at zero, not a fix to a
            // broken path.
            if (kind == EnemyKind.Brute && ResolvePlayedWorldIndex() >= 2)
            {
                for (int i = 0; i < DredgeHulkGuaranteedPartDrops; i++)
                {
                    float ang = i * (Mathf.PI * 2f / DredgeHulkGuaranteedPartDrops) + 0.5f; // offset off the ring above
                    Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                    Pickup dropped = SpawnDrop(PickupKind.PowerCell, pos + off);
                    if (dropped != null) MarkAgesOnGround(dropped);
                }
            }

            // MV-672: Power Cells (the new secondary currency) drop at a tunable fraction of the Parts
            // rate above — a running fractional accumulator, same idiom as _cellAccum, so a non-integer
            // ratio (the authored default, 0.1) still lands on the right long-run average instead of
            // rounding away every kill.
            _powerCellSecondaryAccum += cells * DevTuning.Or(DevTuning.PowerCellDropRatio, CellEconomyTuning.DefaultPowerCellDropRatio);
            int powerCellSecondaries = Mathf.FloorToInt(_powerCellSecondaryAccum);
            _powerCellSecondaryAccum -= powerCellSecondaries;
            for (int i = 0; i < powerCellSecondaries; i++)
            {
                float ang = i * (Mathf.PI * 2f / powerCellSecondaries);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                Pickup dropped = SpawnDrop(PickupKind.PowerCellSecondary, pos + off);
                if (dropped != null) MarkAgesOnGround(dropped);   // MV-1101: a robot-kill Energy Cell ages too
            }

            // MV-401: exactly one Supercell per arena, from the last Bruiser destroyed in it — not every
            // large kind, and not a periodic count (see IsLastBruiserInArea). MV-427: granted at most
            // once EVER, even across a death that wipes and respawns this same area's robots — without
            // DeathRunState's flag, a restored area's fresh last Bruiser would mint another Supercell and
            // suicide-farming would be the optimal strategy. MV-767: World 2+ only, every SECOND area
            // (see GrantsSupercellForArea) — World 2's Parts supply was running 2.33x its weapon-board
            // demand, and the per-area Supercell was 230 of the 828-part total.
            if (kind == EnemyKind.Bruiser && IsLastBruiserInArea() && GrantsSupercellForArea(ResolveCurrentArea())
                && MaxWorlds.Arena.DeathRunState.TryGrantAreaPart(ResolveCurrentArea()))
                SpawnDrop(PickupKind.Supercell, pos, DecorativeKind());
        }

        /// <summary>MV-767/MV-1142: grants a Supercell every <see cref="WorldDefinition.SupercellCadenceAreas"/>
        /// areas on the played world's own row — World 1's row cadence is 1 (every area, as before);
        /// World 2+'s row cadence is 2 (every SECOND area — halving its contribution to that world's
        /// Parts supply, 828 -&gt; 718, against a 711-part weapon board at the new
        /// <see cref="CellSpend"/> multiplier). Areas are 1-based (<see cref="ResolveCurrentArea"/>),
        /// so shifting to a 0-based index before checking cadence grants a cadence-2 world's Supercell
        /// on areas 1, 3, 5... — the first area of a World 2+ run is never shorted relative to World 1.
        ///
        /// MV-1078: reads <see cref="ResolvePlayedWorldIndex"/>, not <see cref="RigBoard.ActiveWorldIndex"/>
        /// — a world's finale clean-up morphs the board onto the NEXT world's the instant the Core is
        /// collected, well before the played world itself actually advances, so the old read would start
        /// treating a still-being-played World 1 area as if it were World 2+ the moment the Core landed.</summary>
        private bool GrantsSupercellForArea(int areaIndex) =>
            (areaIndex - 1) % WorldCatalog.Get(ResolvePlayedWorldIndex()).SupercellCadenceAreas == 0;

        /// <summary>MV-1078: the world actually being PLAYED right now (<see cref="AreaAccumulationDirector.ActiveWorldIndex"/>),
        /// as opposed to <see cref="RigBoard.ActiveWorldIndex"/> — which a finale's Weapon Core morph
        /// already switches onto the NEXT world the instant it's collected, before the played world
        /// itself advances. Same lazy-resolve idiom as <see cref="_areaDirector"/>'s other reads.</summary>
        private int ResolvePlayedWorldIndex()
        {
            if (_areaDirector == null) _areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            return _areaDirector != null ? _areaDirector.ActiveWorldIndex : 0;
        }

        /// <summary>How many cells drop for the large kill just reported (MV-375). Prefers the
        /// authored per-area budget (<see cref="CellEconomyTuning.CellsForArea"/>), spread evenly
        /// across the area's actual solved large-kill count via a fractional accumulator so the run's
        /// cell total for that area lands on exactly the authored line rather than a compounding
        /// per-kill rate. Falls back to the flat <see cref="CellEconomyTuning.DefaultCellsPerLargeKill"/>
        /// rate when no area context is available (a headless test scene) or a dev-tuning override is
        /// active, since neither carries an actual solved kill count to normalise against. MV-1029:
        /// the per-area budget scales by <see cref="CellEconomyTuning.WorldPartsMultiplier"/> for the
        /// active world before being spread across the kill count — 1.0 everywhere except World 3.</summary>
        private int ResolveCellDrop()
        {
            bool devOverride = DevTuning.CellsPerLargeKill.HasValue;
            int areaIndex = devOverride ? 0 : ResolveCurrentArea();
            int largeCountForArea = areaIndex > 0 ? ResolveLargeCountForArea(areaIndex) : 0;

            if (largeCountForArea <= 0)
            {
                return Mathf.Max(0, Mathf.RoundToInt(
                    DevTuning.Or(DevTuning.CellsPerLargeKill, CellEconomyTuning.DefaultCellsPerLargeKill)));
            }

            if (areaIndex != _cellBudgetArea)
            {
                _cellBudgetArea = areaIndex;
                _cellAccum = 0f;
            }

            // MV-1029: World 3's per-area budget alone scales up (CellEconomyTuning.WorldPartsMultiplier)
            // — World 1/2 read a flat 1.0 here, untouched.
            float worldMultiplier = CellEconomyTuning.WorldPartsMultiplier(_areaDirector.ActiveWorldIndex);
            _cellAccum += CellEconomyTuning.CellsForArea(areaIndex) * worldMultiplier / largeCountForArea;
            int cells = Mathf.FloorToInt(_cellAccum);
            _cellAccum -= cells;
            return cells;
        }

        /// <summary>True exactly once per arena: the moment the area's last Bruiser (per its solved
        /// composition, <see cref="AreaAccumulationDirector.BruiserCountForArea"/>) is destroyed
        /// (MV-401) — the sole trigger for that arena's one guaranteed part, replacing the old periodic
        /// per-kill-count mechanic that could fire more than once in a populous arena. An arena solved
        /// with zero Bruisers (e.g. world1_config's Area 4, a Rusher+Gunner ranged-pressure room) drops
        /// no part at all: the ticket's ask is literally "from the last Bruiser destroyed", and
        /// inventing a substitute trigger for a Bruiser-less arena is a design call this ticket doesn't
        /// make. No live area context (a headless test scene) is a different case, not the zero-Bruiser
        /// one — see the early-out below.</summary>
        private bool IsLastBruiserInArea()
        {
            int areaIndex = ResolveCurrentArea();

            // No live area context (a headless test scene) — there's no solved Bruiser count to count
            // down from, so the only sane flat-rate approximation (same idiom as ResolveCellDrop's flat
            // fallback) is "every Bruiser kill drops a part".
            if (areaIndex <= 0) return true;

            if (areaIndex != _bruiserBudgetArea)
            {
                _bruiserBudgetArea = areaIndex;
                _bruiserRemaining = ResolveBruiserCountForArea(areaIndex);
            }

            if (_bruiserRemaining <= 0) return false;
            _bruiserRemaining--;
            return _bruiserRemaining == 0;
        }

        private int ResolveCurrentArea()
        {
            if (_areaDirector == null)
                _areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            return _areaDirector != null ? _areaDirector.CurrentArea : 0;
        }

        /// <summary>Force <paramref name="areaIndex"/>'s last-Bruiser countdown to re-seed from a
        /// fresh solved count next time it's asked (MV-427) — called when a death wipes and respawns
        /// that area's robots, so the restored roster's own Bruisers count down from THEIR full number
        /// instead of picking up wherever the pre-death fight left off. The "already granted, ever"
        /// guard is separate (<see cref="MaxWorlds.Arena.DeathRunState"/>) — this only fixes the
        /// countdown's bookkeeping, not whether a part is still allowed to drop.</summary>
        public void ResetBruiserCountdown(int areaIndex)
        {
            if (_bruiserBudgetArea == areaIndex) _bruiserBudgetArea = -1;
        }

        private int ResolveLargeCountForArea(int areaIndex) =>
            _areaDirector != null ? _areaDirector.LargeCountForArea(areaIndex) : 0;

        private int ResolveBruiserCountForArea(int areaIndex) =>
            _areaDirector != null ? _areaDirector.BruiserCountForArea(areaIndex) : 0;

        /// <summary>A cosmetic-only flavour for a dropped part (WV-228) — parts carry no gameplay
        /// identity anymore, and <c>PickupArtDirector</c> does not read this at all: a dropped part's
        /// ground art comes from <see cref="MaxWorlds.VFX.WeaponPartArt.MachineInternalsKeys"/> alone
        /// (MV-430 — currently just the one gear design), independent of which <c>PartKind</c> this
        /// returns. What this cycles through is the HUD pickup toast's name/accent
        /// (<c>BossVictoryPayoff.CollectLanded</c>), not the ground prop.</summary>
        private MaxWorlds.Upgrades.PartKind DecorativeKind() =>
            MaxWorlds.Upgrades.UpgradeCatalog.AllKinds[_largeKills % MaxWorlds.Upgrades.UpgradeCatalog.AllKinds.Length];

        /// <summary>A shed's the unlock mechanic now (WV-229, spec §4/§6; draft-pick MV-357; a visible
        /// walk-over pickup again as of MV-382): if any RIG category is still locked, drop one
        /// <see cref="PickupKind.Device"/> pickup — no pause, no screen, the fight isn't interrupted; the
        /// credit itself only banks once Max walks over it (<see cref="Collect"/>), same as any other
        /// drop. MV-457: a shed unlocks a whole ability FAMILY now, not a single node — once every
        /// category is unlocked there is nothing left to open, so it falls back to a Supercell + a cell
        /// cache instead — the reward the shed no longer has a use for the family pool to give.</summary>
        /// <summary>Static, map-authored counterpart to a shed's cell-cache fallback (MV-644; fixed to
        /// never grant a Device by MV-646) — places the Supercell-plus-cell-ring reward at
        /// <paramref name="pos"/>, unconditionally, whatever <see cref="RigState.LockedCategoryIds"/>
        /// currently reports. This is the runtime hook for
        /// <see cref="MaxWorlds.Enemies.PowerupCadence.EnsureCoverage"/>'s guarantee: an area with no
        /// shed of its own to ever drop one still needs a reachable pickup once the cadence dial is
        /// otherwise exceeded, and there is no factory death here to hook — <c>MapRuntime</c> calls this
        /// directly while building the map's "pickup" entities instead. MV-646: this must NEVER spawn a
        /// <see cref="PickupKind.Device"/> — a shed-free area has no shed to justify handing out an
        /// ability FAMILY unlock, which is what a Device grants. Only a destroyed shed
        /// (<see cref="OnFactoryDestroyed"/>) may do that.</summary>
        public List<Pickup> PlacePartsCache(Vector3 pos) => SpawnCellCache(pos);

        /// <summary>Places World 1's finale drop (MV-698) at <paramref name="pos"/> — the one
        /// <see cref="PickupKind.WeaponCore"/> <c>BossVictoryPayoff</c> requests when the world's final
        /// boss area falls and there is a next world to send the player into. A thin public wrapper
        /// around <see cref="SpawnDrop"/> (private) for the same reason <see cref="PlacePartsCache"/>
        /// is one — the caller lives outside this director.</summary>
        public void SpawnWeaponCore(Vector3 pos)
        {
            SpawnDrop(PickupKind.WeaponCore, pos);
            HudSignals.EmitWeaponCoreDropped();
        }

        /// <summary>A thin public wrapper around <see cref="SpawnDrop"/> (private), same shape as
        /// <see cref="SpawnWeaponCore"/>/<see cref="PlacePartsCache"/>, for an EditMode test that needs a
        /// pickup at an EXACT position (MV-1099) rather than reflecting into <see cref="SpawnDrop"/>
        /// itself.</summary>
        public Pickup PlacePickupAt(PickupKind kind, Vector3 pos) => SpawnDrop(kind, pos);

        /// <summary>MV-964: banks any Weapon Core still on the ground exactly as if Max had walked over
        /// it — <see cref="MaxWorlds.Intro.WorldJoinSequence"/> calls this the instant Max crosses the
        /// world's finale door, so a core he never walked over in the open can't block progress into the
        /// corridor. A thin public wrapper around the same private <see cref="Collect"/> every ordinary
        /// walk-over pickup already resolves through — never a second collection path.</summary>
        public void CollectGroundedWeaponCore()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Pickup p = _live[i];
                if (p != null && p.Kind == PickupKind.WeaponCore) Collect(i, p);
            }
        }

        /// <summary>MV-948: shed #1 drops a Device (if any category's still locked), then only every
        /// OTHER destroyed shed after that (#3, #5, #7...) does — spreading ability-family unlocks out
        /// so a player spends more cells on PRIMARY/SECONDARY in between, instead of opening every
        /// remaining category in the run's first few sheds. The ordinal is the current total of every
        /// shed-equivalent factory destroyed this run so far (World 1's MowerHutch count plus World 2's
        /// Replicator count) — both are already reported to <see cref="FactoryCensus"/> synchronously
        /// with the kill that fires this very signal (<see cref="MaxWorlds.Factories.MowerHutch.OnDestroyed"/>,
        /// <see cref="MaxWorlds.Factories.Replicator.ApplyDestructionEffects"/>), so the shed THIS call is
        /// about is already counted and the result is a plain 1-based ordinal. Needs no new saved field:
        /// <see cref="FactoryCensus.Destroyed"/>/<see cref="FactoryCensus.ReplicatorsDestroyed"/> are
        /// exactly what MV-922/MV-776's checkpoint capture and restore, so a cold-boot RESUME re-derives
        /// the same ordinal a checkpoint rewind left off at, and a same-session death+CONTINUE (which
        /// never rebuilds the level, MV-941) never touches either count at all.</summary>
        private static bool IsDeviceShedOrdinal() =>
            (FactoryCensus.Destroyed + FactoryCensus.ReplicatorsDestroyed) % 2 == 1;

        private void OnFactoryDestroyed(Vector3 pos)
        {
            bool anyLocked = false;
            foreach (var _ in RigState.LockedCategoryIds()) { anyLocked = true; break; }

            if (!anyLocked || !IsDeviceShedOrdinal()) SpawnCellCache(pos);
            else SpawnDrop(PickupKind.Device, pos);

            // MV-727/MV-1142: the FIRST Replicator destroyed in the Rack Module's own world (today,
            // World 2 -- WorldCatalog.Get(...).RackModuleDropsHere) ALSO drops a Rack Module — once
            // per run, in ADDITION to the normal shed-equivalent drop above, never instead of it. Every
            // later Replicator (and every MowerHutch in a world that doesn't drop it) is untouched by
            // this branch. Offset by ScatterRadius (the same spacing SpawnCellCache's own ring already
            // uses) so the two drops never sit exactly co-located and read as one pickup.
            // MV-1078: ResolvePlayedWorldIndex(), not RigBoard.ActiveWorldIndex -- see that method's own
            // doc for why (a World 1 finale clean-up would otherwise read as "in World 2" the instant
            // the Core is collected, and wrongly drop a second Rack Module before World 1 even ends).
            if (WorldCatalog.Get(ResolvePlayedWorldIndex()).RackModuleDropsHere && !_rackModuleDroppedThisRun)
            {
                _rackModuleDroppedThisRun = true;
                SpawnDrop(PickupKind.RackModule, pos + Vector3.forward * ScatterRadius);
            }
        }

        /// <summary>The "nothing left to unlock" cell-cache reward — one Supercell plus a
        /// <see cref="ShedCellCacheAmount"/> ring of power cells — shared by <see cref="OnFactoryDestroyed"/>'s
        /// own fallback branch and <see cref="PlacePartsCache"/> (MV-646). Never spawns a Device.</summary>
        /// <summary>MV-972: returns every pickup it actually created — <see cref="MapRuntime.BuildProps"/>'s
        /// own map-authored <c>EntityKind.Pickup</c> case needs these to tag them with the area gate
        /// itself (the gate doesn't exist yet at that call time; see <see cref="SpawnDrop"/>'s own doc).</summary>
        private List<Pickup> SpawnCellCache(Vector3 pos)
        {
            var created = new List<Pickup>(1 + ShedCellCacheAmount);
            Pickup supercell = SpawnDrop(PickupKind.Supercell, pos, DecorativeKind());
            if (supercell != null) created.Add(supercell);
            for (int i = 0; i < ShedCellCacheAmount; i++)
            {
                float ang = i * (Mathf.PI * 2f / ShedCellCacheAmount);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                Pickup cell = SpawnDrop(PickupKind.PowerCell, pos + off);
                if (cell != null) created.Add(cell);
            }
            return created;
        }

        private Pickup SpawnDrop(PickupKind kind, Vector3 pos, MaxWorlds.Upgrades.PartKind part = default,
                               AbilityKind ability = default)
        {
            if (kind == PickupKind.PowerCell)
            {
                // MV-626, change 1: a cell dropped once the reserve is already full can never be
                // collected — AddPowerCell refuses it in Collect below, and nothing ever removed a
                // refused pickup from _live, so every subsequent cell sat on the lawn forever. Not
                // spawning it costs the player nothing they could have had anyway.
                if (PickupWallet.PowerCells >= PickupWallet.Capacity) return null;
                RecycleOldestCellIfAtCap();
            }

            Stack<Pickup> pool = kind switch
            {
                PickupKind.Supercell => _supercellPool,
                PickupKind.Device => _devicePool,
                PickupKind.PowerCellSecondary => _powerCellSecondaryPool,
                PickupKind.WeaponCore => _weaponCorePool,
                PickupKind.RackModule => _rackModulePool,
                _ => _cellPool,
            };
            // MV-1147: a pooled entry can have been destroyed out from under the director while sitting
            // in the pool — skip any such stale entries rather than handing one out as a live drop.
            Pickup p = null;
            while (pool.Count > 0 && p == null) p = pool.Pop();
            if (p == null) p = Pickup.Create(kind);
            p.Part = part;
            p.Ability = ability;
            p.transform.SetParent(transform, worldPositionStays: false);
            p.Place(pos);
            _live.Add(p);

            // MV-626 change 2's population cap tracks every live PowerCell regardless of origin — MV-1101
            // keeps that unconditional, but ground-lifetime ageing (_dropAge) is now opt-in per drop (see
            // MarkAgesOnGround), not implied by kind alone, so a shed's cell cache never expires.
            if (kind == PickupKind.PowerCell)
                _cellNodes[p] = _cellOrder.AddLast(p);

            // MV-972/MV-1038: registers (or RE-registers, for a pooled reuse) this drop's own zone tag
            // — same registration a robot-death/shed-destroyed/World-1-finale drop all funnel through
            // here for. RegisterPickup (not RegisterAtPosition — see its own doc for why a pooled
            // pickup needs a REPLACE, not an APPEND) is a no-op for a map-authored EntityKind.Pickup,
            // spawned while MapRuntime.Build is still running: the gate doesn't exist yet at that call
            // time, so MapRuntime.BuildProps tags those itself, straight off this method's own return
            // value.
            MapStaticBatchRoot.Active?.RegisterPickup(p, pos);

            return p;
        }

        /// <summary>MV-626, change 2: evicts the single oldest live cell when the cap is about to be
        /// exceeded — the actual accumulation bound, independent of the reserve-full gate above.
        /// MV-1147: the oldest tracked cell can have been destroyed out from under the director (its
        /// GameObject torn down by something other than this file's own pool/collect paths) — handing a
        /// destroyed Pickup straight to <see cref="RetireCell"/> dereferences it and throws. Same
        /// <c>!= null</c> idiom <see cref="CollectGroundedWeaponCore"/> already uses: drop the stale
        /// bookkeeping and keep looking for a live oldest instead.</summary>
        private void RecycleOldestCellIfAtCap()
        {
            while (_cellOrder.Count >= MaxLiveCells)
            {
                Pickup oldest = _cellOrder.First.Value;
                int index = _live.IndexOf(oldest);
                if (oldest == null)
                {
                    UntrackCell(oldest);
                    if (index >= 0) _live.RemoveAt(index);
                    continue;
                }
                if (index >= 0) RetireCell(index, oldest);
                else UntrackCell(oldest);
                return;
            }
        }

        /// <summary>Drops <paramref name="p"/>'s cap/lifetime bookkeeping — a no-op for a non-cell kind,
        /// and for a cell that's already untracked. Called from every path that removes a cell from
        /// <see cref="_live"/> (an ordinary walk-over <see cref="Collect"/>, a cap eviction, a lifetime
        /// expiry) so none of them can leave a stale entry pointing at a pooled-and-reused instance.</summary>
        private void UntrackCell(Pickup p)
        {
            if (_cellNodes.TryGetValue(p, out var node))
            {
                _cellOrder.Remove(node);
                _cellNodes.Remove(p);
            }
            _dropAge.Remove(p);
        }

        /// <summary>MV-1101: marks a freshly-dropped pickup as subject to the ground lifetime/blink —
        /// called only for a robot-death drop (a Part or an Energy Cell, see <see cref="OnRobotDied"/>).
        /// A shed's cell cache (<see cref="SpawnCellCache"/>/<see cref="PlacePartsCache"/>), a boss drop,
        /// the Weapon Core, a Morphing Module and a Rack Module are never marked and so never expire.</summary>
        private void MarkAgesOnGround(Pickup p) => _dropAge[p] = 0f;

        /// <summary>Forcibly returns a live cell to the pool without collecting it — used by the cap
        /// eviction and the lifetime backstop below, neither of which is a walk-over (see
        /// <see cref="Collect"/> for that path).</summary>
        private void RetireCell(int index, Pickup p)
        {
            UntrackCell(p);
            _reserveFullTold.Remove(p);
            // MV-1038: strip this pickup's renderers out of the area gate's bookkeeping BEFORE it goes
            // back in the pool — see MapStaticBatchRoot.Unregister's own doc for the stale-zone bug this
            // prevents on the pool's next SpawnDrop.
            MapStaticBatchRoot.Active?.Unregister(p.GetComponentsInChildren<Renderer>(true));
            p.gameObject.SetActive(false);
            _live.RemoveAt(index);
            _cellPool.Push(p);
        }

        /// <summary>MV-1101: expires a robot-dropped pickup once its ground lifetime elapses — same
        /// "pool, don't destroy" shape as <see cref="RetireCell"/>, but kind-aware about which pool to
        /// return to, since an Energy Cell's pool is <see cref="_powerCellSecondaryPool"/>, not
        /// <see cref="_cellPool"/>. Clears the blink first so a pooled-and-reused pickup never pops back
        /// out of the pool mid-blink.</summary>
        private void RetireAgedDrop(int index, Pickup p)
        {
            UntrackCell(p);
            _reserveFullTold.Remove(p);
            MapStaticBatchRoot.Active?.Unregister(p.GetComponentsInChildren<Renderer>(true));
            p.SetBlinkHidden(false);
            p.gameObject.SetActive(false);
            _live.RemoveAt(index);
            (p.Kind == PickupKind.PowerCellSecondary ? _powerCellSecondaryPool : _cellPool).Push(p);
        }

        /// <summary>MV-626 change 3; MV-1101 generalises it to Energy Cells and adds the warning blink:
        /// ages every live robot-dropped pickup (<see cref="_dropAge"/>) and expires the ones past
        /// <see cref="RobotDropLifetimeSeconds"/>, blinking for the last <see
        /// cref="RobotDropBlinkWarningSeconds"/> of that. A pickup <see cref="_pullingThisTick"/> marks
        /// as actively Magneto-pulled this tick does not age at all (item 5) — <see cref="Tick"/> builds
        /// that set immediately before calling this. Directly testable with an explicit
        /// <paramref name="dt"/> — same idiom as <c>DissolveVfx.TickGhosts</c>. Walks <see cref="_live"/>
        /// backward so RemoveAt during the walk is safe.</summary>
        private void TickCellLifetimes(float dt)
        {
            float warnStart = RobotDropLifetimeSeconds - RobotDropBlinkWarningSeconds;

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Pickup p = _live[i];
                // MV-1147: a tracked drop destroyed out from under the director — drop it rather than
                // dereference it below (SetBlinkHidden, RetireAgedDrop's own renderer lookup).
                if (p == null) { UntrackCell(p); _reserveFullTold.Remove(p); _live.RemoveAt(i); continue; }
                if (!_dropAge.TryGetValue(p, out float age)) continue;
                if (_pullingThisTick.Contains(p)) continue;   // MV-1101 item 5: clock paused mid-pull

                age += dt;
                if (age >= RobotDropLifetimeSeconds) { RetireAgedDrop(i, p); continue; }
                _dropAge[p] = age;

                bool hidden = age >= warnStart && (Mathf.FloorToInt((age - warnStart) * BlinkHz) % 2) == 1;
                p.SetBlinkHidden(hidden);
            }
        }

        /// <summary>The director's own per-frame tick, driven by <see cref="Update"/> with
        /// <c>Time.deltaTime</c> in play, and callable directly with an explicit <paramref name="dt"/>
        /// (MV-1101) so a test can drive deterministic ageing without reflecting into a private
        /// lifecycle method. Collect/Magneto-pull pass first (so <see cref="_pullingThisTick"/> reflects
        /// this tick's pulls before ageing reads it), then <see cref="TickCellLifetimes"/>, which always
        /// runs regardless of whether Max has been found yet.</summary>
        public void Tick(float dt)
        {
            if (_max == null)
            {
                var g = GameObject.FindGameObjectWithTag("Player");
                if (g != null) _max = g.transform;
            }

            _pullingThisTick.Clear();

            if (_max != null && _live.Count > 0)
            {
                Vector3 m = _max.position;
                MapData map = EnemyNavigation.Map;
                float r2 = CollectRadius * CollectRadius;
                float magnetoRadius = MaxWorlds.Weapons.AbilityTuning.MagnetoPullRadius(
                    MaxWorlds.Weapons.RigState.Level("e_mag"),
                    MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullRadiusBase,
                    MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullRadiusPerLevel);
                float cellMagnetoRadius = MaxWorlds.Weapons.AbilityTuning.MagnetoPullRadius(
                    MaxWorlds.Weapons.RigState.Level("e_cmg"),
                    MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullRadiusBase,
                    MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullRadiusPerLevel);

                for (int i = _live.Count - 1; i >= 0; i--)
                {
                    Pickup p = _live[i];
                    // MV-1147: a tracked drop destroyed out from under the director — drop it rather
                    // than dereference it below (transform, Collect, the Magneto pull).
                    if (p == null) { UntrackCell(p); _reserveFullTold.Remove(p); _live.RemoveAt(i); continue; }
                    Vector3 pPos = p.transform.position;
                    float dx = pPos.x - m.x;
                    float dz = pPos.z - m.z;
                    float d2 = dx * dx + dz * dz;
                    // MV-1001: both the walk-over collect and the Magneto pull below are planar (XZ-only)
                    // distance checks, so without this a Max on the floor could collect — or Magneto-pull —
                    // a drop sitting on the deck above him, and vice versa. CombatLevel.SameLevel is the
                    // same floor-vs-deck comparison MV-944 already gives every targeting/damage site.
                    bool sameLevel = CombatLevel.SameLevel(map, m, pPos);
                    if (d2 <= r2 && sameLevel) { Collect(i, p); continue; }
                    _reserveFullTold.Remove(p);   // out of the radius — the next entry gets a fresh tell

                    // Part Magneto (MV-422, e_mag) / Cell Magneto (MV-848, e_cmg): a caught pickup flies to
                    // Max from range instead of waiting for a manual walk-over. Only power cells — devices
                    // stay a deliberate walk-over pickup. MV-439: Part Magneto never pulls once the PARTS
                    // reserve is full — an owned ability must not actively destroy the player's resources.
                    // MV-1099: and only while every 0.5 m sample along the straight path to Max still
                    // reads as Max's OWN walkable surface (MagnetoPathClear) -- Lee's report was a pickup
                    // dragged across a deck-strip gap or into a parapet, left stuck there forever the
                    // instant it stopped reading as "same level" (sameLevel above is CombatLevel.SameLevel's
                    // own coarse floor-vs-deck check, which treats every deck as one abstract "deck"
                    // regardless of which physical strip). Checked BEFORE every step, so a pull that fails
                    // this frame simply never advances past wherever it already validly sat -- there is no
                    // separate "snap back" state to write.
                    if (sameLevel && (MagnetoShouldPull(p.Kind, magnetoRadius, d2) || CellMagnetoShouldPull(p.Kind, cellMagnetoRadius, d2))
                        && MagnetoPathClear(map, pPos, m))
                    {
                        _pullingThisTick.Add(p);   // MV-1101 item 5: pauses this pickup's ground lifetime below
                        Vector3 pos = p.transform.position;
                        Vector3 toMax = new Vector3(m.x - pos.x, 0f, m.z - pos.z);
                        float step = MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullSpeed * dt;
                        if (step * step >= d2) p.transform.position = new Vector3(m.x, pos.y, m.z);
                        else p.transform.position = pos + toMax.normalized * step;

                        // MV-1038 item 3: a Magneto-pulled pickup can cross a zone boundary mid-pull without
                        // ever going through SpawnDrop's own RegisterPickup call — cheap no-op on the common
                        // case (still the same zone), see ReregisterPickupIfZoneChanged's own doc.
                        MapStaticBatchRoot.Active?.ReregisterPickupIfZoneChanged(p, p.transform.position);
                    }
                }
            }

            TickCellLifetimes(dt);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>MV-1099: whether a Magneto pull may advance <paramref name="from"/> (the pickup)
        /// toward <paramref name="to"/> (Max) this frame — every 0.5 m sample along the straight line
        /// still reads as <paramref name="to"/>'s own walkable surface (<see cref="MapData.IsWalkable"/>).
        /// Data-only, deliberately: an earlier version of this gate also raycast the Cover layer, but a
        /// deck's parapet is deliberately kept OFF that layer (it blocks <c>CharacterController.Move</c>
        /// directly, not a raycast), so the raycast half could never catch the exact parapet/gap case this
        /// ticket is about, and it added a real <c>Physics.Raycast</c> into a scene this project
        /// deliberately never auto-syncs (<c>Physics.autoSyncTransforms</c> is off project-wide,
        /// <c>DynamicsManager.asset</c>) — a stale-collider-pose coupling to every other EditMode fixture's
        /// leftover geometry that is not worth paying for what <see cref="MapData.IsWalkable"/> already
        /// answers on its own: a zone's own footprint rect (floor) or a deck's own rect (deck) IS the
        /// wall/parapet boundary in this rect-based map model, so walking the path's samples through it
        /// already refuses a pull that would cross into a different room or off a deck's own strip.
        /// Degrades to true (never blocks, no live map) with no live <see cref="MapData"/> — the same
        /// degrade every other null-map helper in this file already uses.</summary>
        private static bool MagnetoPathClear(MapData map, Vector3 from, Vector3 to)
        {
            if (map == null) return true;

            const float SampleStep = 0.5f;
            float dist = Vector3.Distance(from, to);
            int samples = Mathf.Max(1, Mathf.CeilToInt(dist / SampleStep));
            for (int i = 0; i <= samples; i++)
            {
                Vector3 point = Vector3.Lerp(from, to, (float)i / samples);
                if (!map.IsWalkable(to, point)) return false;
            }
            return true;
        }

        /// <summary>Whether Part Magneto should reel this pickup in this frame (MV-422/MV-439) — pulled
        /// out as a pure function so the reserve-full guard is testable without a live scene. Public: the
        /// EditMode test assembly has no <c>InternalsVisibleTo</c> back to Gameplay.</summary>
        public static bool MagnetoShouldPull(PickupKind kind, float magnetoRadius, float squaredDistance) =>
            kind == PickupKind.PowerCell && magnetoRadius > 0f
            && squaredDistance <= magnetoRadius * magnetoRadius
            && PickupWallet.PowerCells < PickupWallet.Capacity;

        /// <summary>Whether Cell Magneto (MV-848, e_cmg) should reel this pickup in this frame — same
        /// shape as <see cref="MagnetoShouldPull"/> but for <see cref="PickupKind.PowerCellSecondary"/>
        /// (the MV-672 "Power Cells" rocket currency). No reserve-full guard: unlike the PARTS wallet
        /// <see cref="PickupWallet.PowerCellsSecondary"/> carries no authored capacity yet (see that
        /// field's own doc comment), so there is no cap to gate against — an unbounded reserve is always
        /// "below its cap".</summary>
        public static bool CellMagnetoShouldPull(PickupKind kind, float magnetoRadius, float squaredDistance) =>
            kind == PickupKind.PowerCellSecondary && magnetoRadius > 0f
            && squaredDistance <= magnetoRadius * magnetoRadius;

        private void Collect(int index, Pickup p)
        {
            switch (p.Kind)
            {
                case PickupKind.PowerCell:
                    if (!PickupWallet.AddPowerCell())
                    {
                        // MV-439: at capacity, walking over a cell must do nothing — leave it active
                        // and on the ground, no gain claimed, no per-frame spam while Max stands on it.
                        // TODO(MV-439/MV-429): dim this pickup's GroundRing to ~30% alpha while inert
                        // once MV-429 lands a ring on Pickup — it hasn't yet, so there is no ring to dim.
                        if (_reserveFullTold.Add(p))
                            HudSignals.EmitPickup(p.transform.position, "RESERVE FULL", new Color(0.9f, 0.35f, 0.25f));
                        return;
                    }
                    HudSignals.EmitPickup(p.transform.position, "+1 CELL", new Color(0.31f, 0.86f, 0.98f));
                    break;
                case PickupKind.Device:
                    // MV-424 drew THE RIG's candidate pool and routed straight to the draft outcome on
                    // walk-over. MV-425 stopped 2-3 candidates from force-opening the board mid-fight —
                    // that pool banks in PendingMorphingModule and waits for the player to tap WEAPONS on
                    // their own schedule (see that class's doc comment). MV-605: the 0/1-candidate
                    // auto-open branch this used to keep for "nothing to show/pick between" is gone too —
                    // MV-595 made a shed's own draw always exactly one locked CATEGORY id, so that branch
                    // had quietly become the ONLY path a shed pickup ever took, yanking the player into
                    // THE RIG every single time. Collecting a module now only ever banks it, unconditionally
                    // — the flash on the RIG mark is the invitation; the reveal is a ceremony that plays
                    // when the player opens THE RIG themselves, not a method this file calls directly.
                    var candidates = RigDraft.DrawCandidateCategories();
                    PendingMorphingModule.Set(candidates);
                    HudSignals.EmitPickup(p.transform.position, "MORPHING MODULE",
                        MaxWorlds.VFX.PickupArtDirector.CollectibleGlow);
                    break;
                case PickupKind.PowerCellSecondary:
                    // MV-672: a separate, scarcer currency — banks unconditionally (no reserve cap
                    // authored for it yet, unlike PickupKind.PowerCell above).
                    PickupWallet.AddPowerCellSecondary();
                    HudSignals.EmitPickup(p.transform.position, "+1 POWER CELL",
                        MaxWorlds.VFX.WeaponPartArt.PowerCellSecondaryGlow);
                    break;
                case PickupKind.WeaponCore:
                    // MV-689/MV-698: World 1's finale drop. Same "banks, doesn't force-open THE RIG"
                    // shape as PickupKind.Device above — the morph itself plays on THE RIG's next open
                    // (WeaponSystemState.OpenWeaponCoreMorphIfPending).
                    PendingMorphingModule.SetWeaponCore();
                    HudSignals.EmitPickup(p.transform.position, "WEAPON CORE",
                        MaxWorlds.VFX.PickupArtDirector.CollectibleGlow);
                    HudSignals.EmitWeaponCoreCollected();
                    break;
                case PickupKind.RackModule:
                    // MV-1090: banks only, same shape as a Device's PendingMorphingModule draft — MV-727
                    // used to unlock SECONDARY and grant s_rkt in this same instant, which let the
                    // Shoulder Rack start auto-firing before the player had ever opened THE RIG to see
                    // the reveal. The unlock + free grant now happen together when THE RIG's next open
                    // resolves the banked module (WeaponsScreen.Open), the same deferred moment every
                    // other ability family already waits for.
                    PendingMorphingModule.SetRackModule();
                    HudSignals.EmitPickup(p.transform.position, "SHOULDER RACK",
                        MaxWorlds.VFX.WeaponPartArt.RackModuleGlow);
                    break;
                default:
                    // MV-519: a Supercell grants its cells instantly, no bank/cash-in step — the HUD's
                    // own burst + "+10" flyup + readout count-up (HudSignals.EmitSupercellCollected) is
                    // the whole "definite pickup event" the ticket asks for; there is no separate toast.
                    int cellsBefore = PickupWallet.PowerCells;
                    PickupWallet.AddSupercell();
                    HudSignals.EmitSupercellCollected(p.transform.position, cellsBefore, PickupWallet.PowerCells);
                    break;
            }

            _reserveFullTold.Remove(p);
            // MV-1038: same pool-return unregistration as RetireCell — see that call's own comment.
            MapStaticBatchRoot.Active?.Unregister(p.GetComponentsInChildren<Renderer>(true));
            p.gameObject.SetActive(false);
            _live.RemoveAt(index);
            Stack<Pickup> pool = p.Kind switch
            {
                PickupKind.Supercell => _supercellPool,
                PickupKind.Device => _devicePool,
                PickupKind.PowerCellSecondary => _powerCellSecondaryPool,
                PickupKind.WeaponCore => _weaponCorePool,
                PickupKind.RackModule => _rackModulePool,
                _ => _cellPool,
            };
            pool.Push(p);
            UntrackCell(p);   // MV-626: drop cap/lifetime bookkeeping — a no-op for a non-cell kind
        }
    }
}
