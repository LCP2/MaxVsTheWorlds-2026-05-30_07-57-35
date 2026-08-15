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
    /// every-<see cref="CellEconomyTuning.DefaultPartsPerLargeKills"/>-large-kills trigger, which
    /// could fire more than once inside a populous arena; that mechanic (and its
    /// <see cref="CellEconomyTuning.PartsForArea"/> curve / <see cref="MaxWorlds.Core.DevTuning.PartsPerLargeKills"/>
    /// dial) is dead for the part decision now — left in place only because the Settings panel's
    /// "Parts/large kill" dev slider still reads/writes it (out of this ticket's scope to remove).
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
    /// If any ability is still unowned, this director drops a visible <see cref="PickupKind.Device"/>
    /// pickup at the shed's spot (MV-382, reinstating the walk-over collectible MV-357/358 had reduced to
    /// an instant invisible grant) — no pause, no screen, the fight keeps going. Walking over it banks one
    /// <see cref="AbilityCreditBank"/> credit exactly like every other walk-over pickup, and the player
    /// later spends it from the Abilities screen's BUILD ABILITY button, which is what actually draws
    /// candidates via <see cref="AbilityDraft"/>. None left falls back to a part plus a bigger "cell
    /// cache" instead, same as before.
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<PickupDirector>() != null) return;
            new GameObject("PickupDirector").AddComponent<PickupDirector>();
        }

        private readonly List<Pickup> _live = new List<Pickup>(32);
        private readonly Stack<Pickup> _cellPool = new Stack<Pickup>(16);
        private readonly Stack<Pickup> _partPool = new Stack<Pickup>(8);
        private readonly Stack<Pickup> _devicePool = new Stack<Pickup>(4);
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

            // MV-401: exactly one part per arena, from the last Bruiser destroyed in it — not every
            // large kind, and not a periodic count (see IsLastBruiserInArea).
            if (kind == EnemyKind.Bruiser && IsLastBruiserInArea())
                SpawnDrop(PickupKind.Part, pos, DecorativeKind());
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

        private int ResolveLargeCountForArea(int areaIndex) =>
            _areaDirector != null ? _areaDirector.LargeCountForArea(areaIndex) : 0;

        private int ResolveBruiserCountForArea(int areaIndex) =>
            _areaDirector != null ? _areaDirector.BruiserCountForArea(areaIndex) : 0;

        /// <summary>A cosmetic-only flavour for a dropped part (WV-228) — parts carry no gameplay
        /// identity anymore, but <c>PickupArtDirector</c> still swaps in the Hydro device's art for
        /// <see cref="MaxWorlds.Upgrades.PartKind.Hydro"/>, so cycling through the old catalog keeps
        /// that variety alive instead of every part looking identical forever.</summary>
        private MaxWorlds.Upgrades.PartKind DecorativeKind() =>
            MaxWorlds.Upgrades.UpgradeCatalog.AllKinds[_largeKills % MaxWorlds.Upgrades.UpgradeCatalog.AllKinds.Length];

        /// <summary>A shed's the unlock mechanic now (WV-229, spec §4/§6; draft-pick MV-357; a visible
        /// walk-over pickup again as of MV-382): if any ability is still unowned, drop one
        /// <see cref="PickupKind.Device"/> pickup — no pause, no screen, the fight isn't interrupted; the
        /// credit itself only banks once Max walks over it (<see cref="Collect"/>), same as any other
        /// drop. Once every ability is owned there is nothing left to build, so it falls back to a part +
        /// a cell cache instead — the reward the shed no longer has a use for the ability pool to give.</summary>
        private void OnFactoryDestroyed(Vector3 pos)
        {
            bool anyUnacquired = false;
            foreach (var _ in WeaponSystemState.Unacquired) { anyUnacquired = true; break; }

            if (!anyUnacquired)
            {
                SpawnDrop(PickupKind.Part, pos, DecorativeKind());
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
            Stack<Pickup> pool = kind switch
            {
                PickupKind.Part => _partPool,
                PickupKind.Device => _devicePool,
                _ => _cellPool,
            };
            Pickup p = pool.Count > 0 ? pool.Pop() : Pickup.Create(kind);
            p.Part = part;
            p.Ability = ability;
            p.transform.SetParent(transform, worldPositionStays: false);
            p.Place(pos);
            _live.Add(p);
        }

        private void Update()
        {
            if (_max == null)
            {
                var g = GameObject.FindGameObjectWithTag("Player");
                if (g != null) _max = g.transform;
            }
            if (_max == null || _live.Count == 0) return;

            Vector3 m = _max.position;
            float r2 = CollectRadius * CollectRadius;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Pickup p = _live[i];
                float dx = p.transform.position.x - m.x;
                float dz = p.transform.position.z - m.z;
                if (dx * dx + dz * dz <= r2) Collect(i, p);
            }
        }

        private void Collect(int index, Pickup p)
        {
            switch (p.Kind)
            {
                case PickupKind.PowerCell:
                    PickupWallet.AddPowerCell();
                    HudSignals.EmitPickup(p.transform.position, "+1 CELL", new Color(0.31f, 0.86f, 0.98f));
                    break;
                case PickupKind.Device:
                    // MV-382: walking over the shed's device banks a buildable credit — same
                    // AbilityCreditBank the BUILD ABILITY button spends later, not a direct grant. Which
                    // ability it becomes isn't decided until that later draw (AbilityDraft).
                    AbilityCreditBank.Bank();
                    HudSignals.EmitPickup(p.transform.position, "ABILITY DEVICE +1",
                        MaxWorlds.VFX.PickupArtDirector.CollectibleGlow);
                    break;
                default:
                    PickupWallet.AddPart();   // a fungible token now, no identity to bank (WV-228)
                    HudSignals.EmitPickup(p.transform.position, "+1 PART", MaxWorlds.VFX.PickupArtDirector.CollectibleGlow);
                    break;
            }

            p.gameObject.SetActive(false);
            _live.RemoveAt(index);
            Stack<Pickup> pool = p.Kind switch
            {
                PickupKind.Part => _partPool,
                PickupKind.Device => _devicePool,
                _ => _cellPool,
            };
            pool.Push(p);
        }
    }
}
