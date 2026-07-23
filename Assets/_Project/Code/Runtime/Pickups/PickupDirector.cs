using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// Turns robot deaths into drops and collects them (YT-131) — a self-installing director, the
    /// project idiom (<c>GroundAnchorVfx</c>, <c>HoseDirector</c>), so it needs no scene wiring.
    ///
    /// It owns the drop policy: only the tough tier — the <see cref="EnemyKind.Bruiser"/>, the closest
    /// thing the slice has to a "medium" robot — leaves an upgrade part, so parts stay an occasional
    /// event rather than a carpet under the rusher swarm. On a bruiser death it also scatters a few
    /// power cells, guaranteed. Each frame it does the walk-over collection itself: one Max lookup, one
    /// pool, a planar distance test per live pickup. Banking goes through <see cref="PickupWallet"/>;
    /// the HUD reacts to that.
    ///
    /// Power cells alone also drop off the common rusher swarm (YT-171): once Hydro burns cells as
    /// fuel (YT-137), a supply tied only to the rare bruiser leaves Max starved for most of a fight, so
    /// every rusher death rolls a tunable chance for a single cell — "not every robot need drop one,"
    /// per the ticket, hence a roll rather than a second guarantee.
    ///
    /// The specific part identities and the guaranteed-unique drop table are YT-133 — here a part is
    /// generic, and <see cref="OnRobotDied"/> is where that table will slot in.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PickupDirector : MonoBehaviour
    {
        /// <summary>Walk-over magnet radius, metres — planar distance from Max at which a pickup is
        /// collected. Generous: this is a phone game, you shouldn't have to thread a needle.</summary>
        public const float CollectRadius = 1.4f;

        /// <summary>Power cells per bruiser drop. The part is always exactly one.</summary>
        public const int CellsPerDrop = 3;

        /// <summary>Default tough-robot kills between part drops (YT-143). A part every 3rd bruiser
        /// spreads the five across a level instead of handing them over in the first five kills; power
        /// cells keep dropping every kill. Tunable via <see cref="DevTuning.PartDropInterval"/>.</summary>
        public const float DefaultPartInterval = 3f;

        /// <summary>Default chance [0,1] that a rusher's death drops a single power cell (YT-171) — a
        /// trickle from the common kill so the Hydro reserve doesn't depend solely on the rare bruiser.
        /// Tunable via <see cref="DevTuning.PowerCellDropChance"/>.</summary>
        public const float DefaultCellDropChance = 0.3f;

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
        // The unique drop table (YT-133): the five parts, each dispensed exactly once across the level.
        private readonly MaxWorlds.Upgrades.PartDropTable _table = new MaxWorlds.Upgrades.PartDropTable();
        private Transform _max;
        private int _bruiserKills;

        private void OnEnable() => DropSignals.RobotDied += OnRobotDied;
        private void OnDisable() => DropSignals.RobotDied -= OnRobotDied;

        private void OnRobotDied(Vector3 pos, EnemyKind kind)
        {
            if (kind != EnemyKind.Bruiser)
            {
                // The common tier (YT-171): no part, but a rolled chance at a single power cell so the
                // Hydro reserve has a trickle that doesn't wait on the rare bruiser.
                float chance = Mathf.Clamp01(DevTuning.Or(DevTuning.PowerCellDropChance, DefaultCellDropChance));
                if (Random.value < chance) SpawnDrop(PickupKind.PowerCell, pos);
                return;
            }

            _bruiserKills++;

            // Pace the parts (YT-143): one every Nth tough kill, so the five spread across the level
            // instead of arriving in the first five kills. Cells (below) still drop every kill. Once
            // all five parts are out, only cells drop.
            int interval = Mathf.Max(1, Mathf.RoundToInt(
                DevTuning.Or(DevTuning.PartDropInterval, DefaultPartInterval)));
            if (_bruiserKills % interval == 0
                && _table.TryNext(out MaxWorlds.Upgrades.PartKind part))
                SpawnDrop(PickupKind.Part, pos, part);

            for (int i = 0; i < CellsPerDrop; i++)
            {
                float ang = i * (Mathf.PI * 2f / CellsPerDrop);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ScatterRadius;
                SpawnDrop(PickupKind.PowerCell, pos + off);
            }
        }

        private void SpawnDrop(PickupKind kind, Vector3 pos, MaxWorlds.Upgrades.PartKind part = default)
        {
            Stack<Pickup> pool = kind == PickupKind.Part ? _partPool : _cellPool;
            Pickup p = pool.Count > 0 ? pool.Pop() : Pickup.Create(kind);
            p.Part = part;
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
            if (p.Kind == PickupKind.PowerCell)
            {
                PickupWallet.AddPowerCell();
                HudSignals.EmitPickup(p.transform.position, "+1 CELL", new Color(0.31f, 0.86f, 0.98f));
            }
            else
            {
                PickupWallet.AddPart(p.Part);   // banked with which of the five it is (YT-133)
                var part = MaxWorlds.Upgrades.UpgradeCatalog.For(p.Part);
                HudSignals.EmitPickup(p.transform.position, part.Name, part.Accent);
            }

            p.gameObject.SetActive(false);
            _live.RemoveAt(index);
            (p.Kind == PickupKind.Part ? _partPool : _cellPool).Push(p);
        }
    }
}
