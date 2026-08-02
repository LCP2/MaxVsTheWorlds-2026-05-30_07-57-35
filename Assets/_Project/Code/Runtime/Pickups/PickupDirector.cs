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
    /// <see cref="EnemyKind.Rusher"/> — drops nothing at all, no roll, no trickle. Only the large tier
    /// — <see cref="EnemyKind.Bruiser"/>, the closest thing the slice has to "large" until Heavy/Brute
    /// (WV-223/224) land — drops loot: a guaranteed <see cref="CellEconomyTuning.DefaultCellsPerLargeKill"/>
    /// power cells every kill, plus one part every
    /// <see cref="CellEconomyTuning.DefaultPartsPerLargeKills"/>-th large kill, so parts stay an
    /// occasional event rather than a carpet. Each frame it does the walk-over collection itself: one
    /// Max lookup, one pool, a planar distance test per live pickup. Banking goes through
    /// <see cref="PickupWallet"/>; the HUD reacts to that.
    ///
    /// Parts are now universal upgrade tokens (WV-228): every paced drop banks, there is no longer a
    /// guaranteed-unique table to run dry against (YT-133's old <c>PartDropTable</c> is retired from
    /// this loop). A dropped part's <see cref="MaxWorlds.Upgrades.PartKind"/> is purely cosmetic now —
    /// it only steers <c>PickupArtDirector</c>'s occasional Hydro-device swap.
    ///
    /// Sheds are the ability-unlock mechanic (WV-229): a destroyed <c>MowerHutch</c> reports through
    /// <see cref="HudSignals.FactoryDestroyed"/>, and this director draws a random entry from
    /// <see cref="WeaponSystemState.Unacquired"/> and drops a device pickup carrying it — or, once all
    /// six abilities are owned, a part plus a bigger "cell cache" instead.
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
            // WV-226: the small tier drops nothing at all — only large kills carry loot.
            if (kind != EnemyKind.Bruiser) return;

            _largeKills++;

            int cells = Mathf.Max(0, Mathf.RoundToInt(
                DevTuning.Or(DevTuning.CellsPerLargeKill, CellEconomyTuning.DefaultCellsPerLargeKill)));
            for (int i = 0; i < cells; i++)
            {
                float ang = i * (Mathf.PI * 2f / cells);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                SpawnDrop(PickupKind.PowerCell, pos + off);
            }

            // Pace the parts (WV-226): one every Nth large kill, so they spread across the level
            // instead of arriving all at once. Cells (above) still drop every kill. Parts are
            // universal tokens (WV-228) — there is no cap on how many can drop across a run, unlike the
            // old five-and-done unique table.
            int interval = Mathf.Max(1, Mathf.RoundToInt(
                DevTuning.Or(DevTuning.PartsPerLargeKills, CellEconomyTuning.DefaultPartsPerLargeKills)));
            if (_largeKills % interval == 0)
                SpawnDrop(PickupKind.Part, pos, DecorativeKind());
        }

        /// <summary>A cosmetic-only flavour for a dropped part (WV-228) — parts carry no gameplay
        /// identity anymore, but <c>PickupArtDirector</c> still swaps in the Hydro device's art for
        /// <see cref="MaxWorlds.Upgrades.PartKind.Hydro"/>, so cycling through the old catalog keeps
        /// that variety alive instead of every part looking identical forever.</summary>
        private MaxWorlds.Upgrades.PartKind DecorativeKind() =>
            MaxWorlds.Upgrades.UpgradeCatalog.AllKinds[_largeKills % MaxWorlds.Upgrades.UpgradeCatalog.AllKinds.Length];

        /// <summary>A shed's the unlock mechanic now (WV-229, spec §4/§6): destroying one drops a
        /// device granting one random ability Max doesn't already own. Once all six are owned there is
        /// nothing left to grant, so it falls back to a part + a cell cache instead — the reward the
        /// shed no longer has a use for the ability pool to give.</summary>
        private void OnFactoryDestroyed(Vector3 pos)
        {
            var unacquired = new List<AbilityKind>(WeaponSystemState.Unacquired);
            if (unacquired.Count > 0)
            {
                AbilityKind kind = unacquired[Random.Range(0, unacquired.Count)];
                SpawnDrop(PickupKind.Device, pos, ability: kind);
                return;
            }

            SpawnDrop(PickupKind.Part, pos, DecorativeKind());
            for (int i = 0; i < ShedCellCacheAmount; i++)
            {
                float ang = i * (Mathf.PI * 2f / ShedCellCacheAmount);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                SpawnDrop(PickupKind.PowerCell, pos + off);
            }
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
