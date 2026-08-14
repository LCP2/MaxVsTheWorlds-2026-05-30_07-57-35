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
    /// How much is an authored per-area total (<see cref="CellEconomyTuning.CellsForArea"/> /
    /// <see cref="CellEconomyTuning.PartsForArea"/>, MV-375), spread across that area's actual solved
    /// large-kill count so the run's cell/part curve rises on a designed straight line instead of
    /// riding the enemy population's exponential growth — see <see cref="ResolveDrop"/>. Falls back to
    /// the flat <see cref="CellEconomyTuning.DefaultCellsPerLargeKill"/> / one-part-every-
    /// <see cref="CellEconomyTuning.DefaultPartsPerLargeKills"/>-kills rate outside a live area context
    /// (tests) or under a dev-tuning override. Each frame it does the walk-over collection itself: one
    /// Max lookup, one pool, a planar distance test per live pickup. Banking goes through
    /// <see cref="PickupWallet"/>; the HUD reacts to that.
    ///
    /// Parts are now universal upgrade tokens (WV-228): every paced drop banks, there is no longer a
    /// guaranteed-unique table to run dry against (YT-133's old <c>PartDropTable</c> is retired from
    /// this loop). A dropped part's <see cref="MaxWorlds.Upgrades.PartKind"/> is purely cosmetic now —
    /// it only steers <c>PickupArtDirector</c>'s occasional Hydro-device swap.
    ///
    /// Sheds are the ability-unlock mechanic (WV-229; draft-pick MV-357; moved off the mid-fight modal
    /// by MV-358): a destroyed <c>MowerHutch</c> reports through <see cref="HudSignals.FactoryDestroyed"/>.
    /// If any ability is still unowned, this director banks one <see cref="AbilityCreditBank"/> credit —
    /// no pause, no screen, the fight keeps going — and the player later spends it from the Abilities
    /// screen's BUILD ABILITY button, which is what actually draws candidates via
    /// <see cref="AbilityDraft"/>. None left falls back to a part plus a bigger "cell cache" instead,
    /// same as before.
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

        /// <summary>The area <see cref="_cellAccum"/>/<see cref="_partAccum"/> are currently tracking
        /// (MV-375) — reset whenever a kill lands in a different area so a fresh area starts its budget
        /// from zero instead of carrying over the previous area's leftover fraction.</summary>
        private int _budgetArea = -1;
        private float _cellAccum;
        private float _partAccum;

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

            (int cells, bool dropPart) = ResolveDrop();

            for (int i = 0; i < cells; i++)
            {
                float ang = i * (Mathf.PI * 2f / cells);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                SpawnDrop(PickupKind.PowerCell, pos + off);
            }

            if (dropPart)
                SpawnDrop(PickupKind.Part, pos, DecorativeKind());
        }

        /// <summary>How many cells and whether a part drops for the large kill just reported (MV-375).
        /// Prefers the authored per-area budget (<see cref="CellEconomyTuning.CellsForArea"/> /
        /// <see cref="CellEconomyTuning.PartsForArea"/>), spread evenly across the area's actual solved
        /// large-kill count via a fractional accumulator so the run's total for that area lands on
        /// exactly the authored line rather than a compounding per-kill rate. Falls back to the flat
        /// legacy rate — every kill drops <see cref="CellEconomyTuning.DefaultCellsPerLargeKill"/> cells,
        /// one part every <see cref="CellEconomyTuning.DefaultPartsPerLargeKills"/>-th kill — when no
        /// area context is available (a headless test scene) or a dev-tuning override is active, since
        /// neither carries an actual solved kill count to normalise against.</summary>
        private (int cells, bool dropPart) ResolveDrop()
        {
            bool devOverride = DevTuning.CellsPerLargeKill.HasValue || DevTuning.PartsPerLargeKills.HasValue;
            int areaIndex = devOverride ? 0 : ResolveCurrentArea();
            int largeCountForArea = areaIndex > 0 ? ResolveLargeCountForArea(areaIndex) : 0;

            if (largeCountForArea <= 0)
            {
                int flatCells = Mathf.Max(0, Mathf.RoundToInt(
                    DevTuning.Or(DevTuning.CellsPerLargeKill, CellEconomyTuning.DefaultCellsPerLargeKill)));
                int interval = Mathf.Max(1, Mathf.RoundToInt(
                    DevTuning.Or(DevTuning.PartsPerLargeKills, CellEconomyTuning.DefaultPartsPerLargeKills)));
                return (flatCells, _largeKills % interval == 0);
            }

            if (areaIndex != _budgetArea)
            {
                _budgetArea = areaIndex;
                _cellAccum = 0f;
                _partAccum = 0f;
            }

            _cellAccum += CellEconomyTuning.CellsForArea(areaIndex) / largeCountForArea;
            int cells = Mathf.FloorToInt(_cellAccum);
            _cellAccum -= cells;

            _partAccum += CellEconomyTuning.PartsForArea(areaIndex) / largeCountForArea;
            bool dropPart = _partAccum >= 1f;
            if (dropPart) _partAccum -= 1f;

            return (cells, dropPart);
        }

        private int ResolveCurrentArea()
        {
            if (_areaDirector == null)
                _areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            return _areaDirector != null ? _areaDirector.CurrentArea : 0;
        }

        private int ResolveLargeCountForArea(int areaIndex) =>
            _areaDirector != null ? _areaDirector.LargeCountForArea(areaIndex) : 0;

        /// <summary>A cosmetic-only flavour for a dropped part (WV-228) — parts carry no gameplay
        /// identity anymore, but <c>PickupArtDirector</c> still swaps in the Hydro device's art for
        /// <see cref="MaxWorlds.Upgrades.PartKind.Hydro"/>, so cycling through the old catalog keeps
        /// that variety alive instead of every part looking identical forever.</summary>
        private MaxWorlds.Upgrades.PartKind DecorativeKind() =>
            MaxWorlds.Upgrades.UpgradeCatalog.AllKinds[_largeKills % MaxWorlds.Upgrades.UpgradeCatalog.AllKinds.Length];

        /// <summary>A shed's the unlock mechanic now (WV-229, spec §4/§6; draft-pick MV-357; the pickup
        /// itself just banks a credit rather than granting anything, MV-358): if any ability is still
        /// unowned, bank one <see cref="AbilityCreditBank"/> credit — no pause, no screen, the fight
        /// isn't interrupted. Once every ability is owned there is nothing left to build, so it falls
        /// back to a part + a cell cache instead — the reward the shed no longer has a use for the
        /// ability pool to give.</summary>
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

            AbilityCreditBank.Bank();
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
                    // Idempotent (WeaponSystemState.Acquire no-ops if somehow already owned), but
                    // OnFactoryDestroyed only ever draws from Unacquired so this shouldn't happen.
                    WeaponSystemState.Acquire(p.Ability);
                    HudSignals.EmitPickup(p.transform.position, WeaponCatalog.DisplayName(p.Ability) + " UNLOCKED",
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
