using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Wires a loaded <see cref="WorldConfig"/>'s origination engine (MV-269) into the scene
    /// <see cref="MapRuntime"/> actually built (MV-270): the boss gate stays <see cref="AreaGate.Locked"/>
    /// until every shed is destroyed, and every world-authored parts cache stands from the moment the
    /// level loads. Owns a <see cref="SupplyLineNetwork"/> — the pure engine class (MV-269) reads no
    /// live scene state itself, so something has to poll the built <see cref="MowerHutch"/> instances
    /// and report their deaths into it; that caller is this runner.
    /// </summary>
    public sealed class WorldRunner : MonoBehaviour
    {
        private WorldConfig _cfg;
        private SupplyLineNetwork _supply;
        private AreaGate _bossGate;
        private readonly List<(string areaId, MowerHutch hutch)> _sheds = new List<(string, MowerHutch)>(3);

        public void Configure(WorldConfig cfg, MapBuild build)
        {
            _cfg = cfg;
            _supply = new SupplyLineNetwork(cfg);

            foreach (WorldArea area in cfg.areas)
            {
                if (!area.hasShed) continue;
                if (!build.Actors.TryGetValue($"{area.id}_shed", out GameObject shedGo) || shedGo == null) continue;

                MowerHutch hutch = shedGo.GetComponent<MowerHutch>();
                if (hutch != null) _sheds.Add((area.id, hutch));
            }

            if (build.Actors.TryGetValue("bg", out GameObject bossGateGo) && bossGateGo != null)
                _bossGate = bossGateGo.GetComponent<AreaGate>();

            // Locked from the start: the boss gate's own HP would otherwise let sustained primary fire
            // break it early, exactly the "boss you can walk in on before the sheds fall" bug the
            // opensWith='all-sheds-destroyed' rule (MapValidation.WorldBossGate) exists to make
            // structurally impossible. A world authored with zero sheds never unlocks it here — that
            // is a content bug for the world config to fix, not something this runner should paper over.
            if (_bossGate != null) _bossGate.Locked = true;
        }

        // Deferred to Start, not done inline in Configure (called from BackyardPath.Awake): PickupDirector
        // self-installs via [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] (MV-131), and nothing
        // guarantees that callback has already run relative to a scene object's own Awake — only that it
        // runs before the first Start, the same ordering guarantee MowerHutch's own doc comment leans on
        // for FactoryCensus/HUD. Seeding here, not in Configure, is what keeps this from silently no-oping
        // via the null-conditional on a PickupDirector that hasn't installed itself yet.
        private void Start()
        {
            if (_cfg == null) return;

            foreach (WorldArea area in _cfg.areas)
            {
                if (area.partsCache == null) continue;
                PickupDirector.Instance?.SeedPartsCache(new Vector3(area.partsCache.x, 0f, area.partsCache.z));
            }
        }

        private void Update()
        {
            if (_supply == null || _sheds.Count == 0) return;

            for (int i = _sheds.Count - 1; i >= 0; i--)
            {
                (string areaId, MowerHutch hutch) = _sheds[i];
                if (hutch != null && hutch.IsAlive) continue;

                _sheds.RemoveAt(i);
                _supply.DestroyShed(areaId);

                if (_supply.AllShedsDestroyed && _bossGate != null)
                {
                    _bossGate.Locked = false;
                    _bossGate.ForceOpen();
                }
            }
        }
    }
}
