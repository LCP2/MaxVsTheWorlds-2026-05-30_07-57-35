using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
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
    /// If any RIG category is still locked, this director drops a visible <see cref="PickupKind.Device"/> —
    /// now a Morphing Module — pickup at the shed's spot (MV-382, reinstating the walk-over collectible
    /// MV-357/358 had reduced to an instant invisible grant) — no pause, no screen, the fight keeps
    /// going. Walking over it draws THE RIG's locked-category pool immediately and routes straight to
    /// the outcome (MV-424, replacing the old bank-then-BUILD-ABILITY step; MV-457 replaced the node draw
    /// with a family draw): 0 candidates consumes the module, 1 unlocks it directly, 2 opens THE RIG
    /// board's draft overlay to choose between them. Nothing left locked falls back to a part plus a
    /// bigger "cell cache" instead, same as before.
    /// </summary>
    [DisallowMultipleComponent]
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

        /// <summary>MV-626, change 2: uncollected power cells allowed live on the ground at once,
        /// oldest recycled first once this is hit — the actual bound on the accumulation this ticket
        /// fixes, independent of the reserve-full gate in <see cref="SpawnDrop"/> (which only stops
        /// growth once the wallet itself is full; a fast enough kill streak could otherwise still pile
        /// cells up below that ceiling). 24 sits well under the ~70-cell pile Lee's report measured at
        /// 19fps, and change 4 below removes the per-frame render cost each surviving cell pays anyway.
        /// Same bounded-population idiom as <c>AmbienceVfx.maxDecals</c> (40) and
        /// <c>DissolveVfx.maxGhosts</c> (12).</summary>
        private const int MaxLiveCells = 24;

        /// <summary>MV-626, change 3: how long an uncollected power cell survives before expiring back
        /// to the pool. A backstop, not the mechanism — <see cref="MaxLiveCells"/> above is what
        /// actually bounds the population, since steady state at Domination kill rates (drop-rate ×
        /// lifetime) can exceed it on its own. Generous on purpose: a player still working their way
        /// toward a cell should not see it vanish out from under them.</summary>
        private const float CellLifetimeSeconds = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<PickupDirector>() != null) return;
            new GameObject("PickupDirector").AddComponent<PickupDirector>();
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

        /// <summary>Live power cells in spawn order, oldest first (MV-626). <see
        /// cref="RecycleOldestCellIfAtCap"/> needs O(1) oldest-lookup, and an ordinary walk-over
        /// collect can remove any cell — not just the oldest — so <see cref="_cellNodes"/> gives O(1)
        /// removal from the middle too, which <see cref="_live"/> alone can't without an index scan
        /// per kill.</summary>
        private readonly LinkedList<Pickup> _cellOrder = new LinkedList<Pickup>();
        private readonly Dictionary<Pickup, LinkedListNode<Pickup>> _cellNodes = new Dictionary<Pickup, LinkedListNode<Pickup>>(32);

        /// <summary>Seconds since each live power cell was dropped (MV-626, change 3). Cells only —
        /// Supercell/Device grants never fail to collect (see <see cref="SpawnDrop"/>), so they never
        /// pile up and are a different population (this ticket's own "Relationship" note).</summary>
        private readonly Dictionary<Pickup, float> _cellAge = new Dictionary<Pickup, float>(32);

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
                SpawnDrop(PickupKind.PowerCell, pos + off);
            }

            // MV-401: exactly one Supercell per arena, from the last Bruiser destroyed in it — not every
            // large kind, and not a periodic count (see IsLastBruiserInArea). MV-427: granted at most
            // once EVER, even across a death that wipes and respawns this same area's robots — without
            // DeathRunState's flag, a restored area's fresh last Bruiser would mint another Supercell and
            // suicide-farming would be the optimal strategy.
            if (kind == EnemyKind.Bruiser && IsLastBruiserInArea()
                && MaxWorlds.Arena.DeathRunState.TryGrantAreaPart(ResolveCurrentArea()))
                SpawnDrop(PickupKind.Supercell, pos, DecorativeKind());
        }

        /// <summary>How many cells drop for the large kill just reported (MV-375). Prefers the
        /// authored per-area budget (<see cref="CellEconomyTuning.CellsForArea"/>), spread evenly
        /// across the area's actual solved large-kill count via a fractional accumulator so the run's
        /// cell total for that area lands on exactly the authored line rather than a compounding
        /// per-kill rate. Falls back to the flat <see cref="CellEconomyTuning.DefaultCellsPerLargeKill"/>
        /// rate when no area context is available (a headless test scene) or a dev-tuning override is
        /// active, since neither carries an actual solved kill count to normalise against.</summary>
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

            _cellAccum += CellEconomyTuning.CellsForArea(areaIndex) / largeCountForArea;
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
        private void OnFactoryDestroyed(Vector3 pos)
        {
            bool anyLocked = false;
            foreach (var _ in RigState.LockedCategoryIds()) { anyLocked = true; break; }

            if (!anyLocked)
            {
                SpawnDrop(PickupKind.Supercell, pos, DecorativeKind());
                for (int i = 0; i < ShedCellCacheAmount; i++)
                {
                    float ang = i * (Mathf.PI * 2f / ShedCellCacheAmount);
                    Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                    SpawnDrop(PickupKind.PowerCell, pos + off);
                }
                return;
            }

            SpawnDrop(PickupKind.Device, pos);
        }

        private void SpawnDrop(PickupKind kind, Vector3 pos, MaxWorlds.Upgrades.PartKind part = default,
                               AbilityKind ability = default)
        {
            if (kind == PickupKind.PowerCell)
            {
                // MV-626, change 1: a cell dropped once the reserve is already full can never be
                // collected — AddPowerCell refuses it in Collect below, and nothing ever removed a
                // refused pickup from _live, so every subsequent cell sat on the lawn forever. Not
                // spawning it costs the player nothing they could have had anyway.
                if (PickupWallet.PowerCells >= PickupWallet.Capacity) return;
                RecycleOldestCellIfAtCap();
            }

            Stack<Pickup> pool = kind switch
            {
                PickupKind.Supercell => _supercellPool,
                PickupKind.Device => _devicePool,
                _ => _cellPool,
            };
            Pickup p = pool.Count > 0 ? pool.Pop() : Pickup.Create(kind);
            p.Part = part;
            p.Ability = ability;
            p.transform.SetParent(transform, worldPositionStays: false);
            p.Place(pos);
            _live.Add(p);

            if (kind == PickupKind.PowerCell)
            {
                _cellNodes[p] = _cellOrder.AddLast(p);
                _cellAge[p] = 0f;
            }
        }

        /// <summary>MV-626, change 2: evicts the single oldest live cell when the cap is about to be
        /// exceeded — the actual accumulation bound, independent of the reserve-full gate above.</summary>
        private void RecycleOldestCellIfAtCap()
        {
            if (_cellOrder.Count < MaxLiveCells) return;
            Pickup oldest = _cellOrder.First.Value;
            int index = _live.IndexOf(oldest);
            if (index >= 0) RetireCell(index, oldest);
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
            _cellAge.Remove(p);
        }

        /// <summary>Forcibly returns a live cell to the pool without collecting it — used by the cap
        /// eviction and the lifetime backstop below, neither of which is a walk-over (see
        /// <see cref="Collect"/> for that path).</summary>
        private void RetireCell(int index, Pickup p)
        {
            UntrackCell(p);
            _reserveFullTold.Remove(p);
            p.gameObject.SetActive(false);
            _live.RemoveAt(index);
            _cellPool.Push(p);
        }

        /// <summary>MV-626, change 3: ages every live power cell and expires the ones past
        /// <see cref="CellLifetimeSeconds"/>. Pulled out of <see cref="Update"/>'s main collect/Magneto
        /// loop (which only concerns itself with cells near Max) so it always runs regardless of
        /// whether Max has been found yet, and so it's directly testable with an explicit
        /// <paramref name="dt"/> — same idiom as <c>DissolveVfx.TickGhosts</c>. Walks <see cref="_live"/>
        /// backward so RemoveAt during the walk is safe.</summary>
        private void TickCellLifetimes(float dt)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Pickup p = _live[i];
                if (p.Kind != PickupKind.PowerCell) continue;
                if (!_cellAge.TryGetValue(p, out float age)) continue;

                age += dt;
                if (age >= CellLifetimeSeconds) { RetireCell(i, p); continue; }
                _cellAge[p] = age;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            TickCellLifetimes(dt);

            if (_max == null)
            {
                var g = GameObject.FindGameObjectWithTag("Player");
                if (g != null) _max = g.transform;
            }
            if (_max == null || _live.Count == 0) return;

            Vector3 m = _max.position;
            float r2 = CollectRadius * CollectRadius;
            float magnetoRadius = MaxWorlds.Weapons.AbilityTuning.MagnetoPullRadius(
                MaxWorlds.Weapons.RigState.Level("e_mag"),
                MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullRadiusBase,
                MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullRadiusPerLevel);

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Pickup p = _live[i];
                float dx = p.transform.position.x - m.x;
                float dz = p.transform.position.z - m.z;
                float d2 = dx * dx + dz * dz;
                if (d2 <= r2) { Collect(i, p); continue; }
                _reserveFullTold.Remove(p);   // out of the radius — the next entry gets a fresh tell

                // Magneto (MV-422, e_mag): a caught power cell flies to Max from range instead of
                // waiting for a manual walk-over. Only power cells — parts/devices stay a deliberate
                // walk-over pickup. MV-439: never pulls once the reserve is full — an owned ability
                // must not actively destroy the player's resources.
                if (MagnetoShouldPull(p.Kind, magnetoRadius, d2))
                {
                    Vector3 pos = p.transform.position;
                    Vector3 toMax = new Vector3(m.x - pos.x, 0f, m.z - pos.z);
                    float step = MaxWorlds.Weapons.AbilityTuning.DefaultMagnetoPullSpeed * dt;
                    if (step * step >= d2) p.transform.position = new Vector3(m.x, pos.y, m.z);
                    else p.transform.position = pos + toMax.normalized * step;
                }
            }
        }

        /// <summary>Whether Magneto should reel this pickup in this frame (MV-422/MV-439) — pulled out
        /// as a pure function so the reserve-full guard is testable without a live scene. Public: the
        /// EditMode test assembly has no <c>InternalsVisibleTo</c> back to Gameplay.</summary>
        public static bool MagnetoShouldPull(PickupKind kind, float magnetoRadius, float squaredDistance) =>
            kind == PickupKind.PowerCell && magnetoRadius > 0f
            && squaredDistance <= magnetoRadius * magnetoRadius
            && PickupWallet.PowerCells < PickupWallet.Capacity;

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
            p.gameObject.SetActive(false);
            _live.RemoveAt(index);
            Stack<Pickup> pool = p.Kind switch
            {
                PickupKind.Supercell => _supercellPool,
                PickupKind.Device => _devicePool,
                _ => _cellPool,
            };
            pool.Push(p);
            UntrackCell(p);   // MV-626: drop cap/lifetime bookkeeping — a no-op for a non-cell kind
        }
    }
}
