using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Wires a loaded <see cref="WorldConfig"/>'s origination engine (MV-269) into the scene
    /// <see cref="MapRuntime"/> actually built (MV-270): the boss gate stays <see cref="AreaGate.Locked"/>
    /// until every shed is destroyed. Owns a <see cref="SupplyLineNetwork"/> — the pure engine class
    /// (MV-269) reads no live scene state itself, so something has to poll the built
    /// <see cref="MowerHutch"/> instances and report their deaths into it; that caller is this runner.
    ///
    /// MV-427: also the death-continues-the-run orchestrator. It already owns the boss-gate identity
    /// and (via the map) every other gate's, which is exactly the context a respawn needs to pick the
    /// right door to re-close and never touch the one that isn't allowed to.
    /// </summary>
    public sealed class WorldRunner : MonoBehaviour
    {
        /// <summary>Metres behind the gate a respawn lands — clear of the doorway's own collider and
        /// the room beyond it, but not so far back it reads as a different spot from "at the gate".</summary>
        private const float RespawnMarginFromGate = 2.5f;

        private SupplyLineNetwork _supply;
        private AreaGate _bossGate;
        private readonly List<(string areaId, MowerHutch hutch)> _sheds = new List<(string, MowerHutch)>(3);

        private WorldConfig _cfg;
        private MapData _map;
        private AreaAccumulationDirector _areaDirector;
        private PickupDirector _pickupDirector;
        private PlayerHealth _playerHealth;
        private Transform _player;

        /// <summary>The combat gate leading INTO each 1-based area — the one a death in that area
        /// re-closes (unless it's the boss gate; see <see cref="RespawnPlan.RecloseGate"/>) and the one
        /// a respawn lands behind.</summary>
        private readonly Dictionary<int, AreaGate> _gateIntoArea = new Dictionary<int, AreaGate>();

        public void Configure(WorldConfig cfg, MapData map, MapBuild build, AreaAccumulationDirector areaDirector)
        {
            _cfg = cfg;
            _map = map;
            _areaDirector = areaDirector;
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

            BuildGateIntoAreaMap(build);

            _playerHealth = FindFirstObjectByType<PlayerHealth>();
            if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;
        }

        private void BuildGateIntoAreaMap(MapBuild build)
        {
            if (_map.links == null) return;

            foreach (MapLink link in _map.links)
            {
                if (link == null || string.IsNullOrEmpty(link.gate)) continue;

                int intoArea = AreaAccumulationDirector.AreaIndexOf(link.to);
                if (intoArea <= 0) continue;   // e.g. area18 -> boss: keyed separately, below

                if (!build.Actors.TryGetValue(link.gate, out GameObject gateGo) || gateGo == null) continue;
                AreaGate gate = gateGo.GetComponent<AreaGate>();
                if (gate != null) _gateIntoArea[intoArea] = gate;
            }

            // The boss room itself is never named "area<N>" (AreaIndexOf returns 0 for "boss"), so the
            // loop above can't key it — key it explicitly at areaCount + 1, the same index
            // RespawnPlanner.Resolve uses for the boss room.
            if (_bossGate != null && _cfg?.dials != null)
                _gateIntoArea[_cfg.dials.areaCount + 1] = _bossGate;
        }

        private void OnDestroy()
        {
            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
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

        /// <summary>Max fell (MV-427). The run doesn't end: the arena he died in resets to its
        /// authored composition, its gate re-closes (unless it's the boss gate — that one never does),
        /// and he lands back at that gate, alive, one area behind.</summary>
        private void OnPlayerDied()
        {
            if (_areaDirector == null || _cfg?.dials == null) return;

            EnsurePlayer();
            int deathArea = ResolveDeathArea();
            if (deathArea <= 0) return;   // no live area context (a headless fixture) — nothing to respawn into

            RespawnPlan plan = RespawnPlanner.Resolve(deathArea, _cfg.dials.areaCount);

            DeathRunState.RecordDeath();

            // Wipe and respawn the death arena's robots to its authored composition. Sheds and the
            // area's own part-grant flag are untouched by this — a destroyed shed's DestructibleHealth
            // never revives, and DeathRunState.TryGrantAreaPart is keyed by area index, not by
            // "how many times this area has been filled".
            _areaDirector.RestoreArea(plan.RestoreAreaIndex);

            if (_pickupDirector == null) _pickupDirector = FindFirstObjectByType<PickupDirector>();
            _pickupDirector?.ResetBruiserCountdown(plan.RestoreAreaIndex);

            if (plan.RecloseGate && _gateIntoArea.TryGetValue(plan.RestoreAreaIndex, out AreaGate gate) && gate != null)
                gate.Reclose();

            // Sentinels never travel between areas (MV-362/396) — a death is exactly as much an area
            // change as walking through a gate is.
            Sentinel.DestroyAllActive();

            RespawnPlayer(plan);
            _areaDirector.SetCurrentArea(plan.RespawnAreaIndex);
        }

        private void EnsurePlayer()
        {
            if (_player != null) return;
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }

        /// <summary>Where Max is standing RIGHT NOW, in <see cref="RespawnPlanner.Resolve"/> terms — a
        /// normal area's 1-based index, or <c>areaCount + 1</c> for the boss room. Deliberately reads
        /// live position (<see cref="MapData.ZoneAt"/>), not <see cref="AreaAccumulationDirector.CurrentArea"/>:
        /// that tracker is advanced ahead of the player for population purposes (MV-245) and, more to
        /// the point, never reaches the boss room at all — the boss zone's id is "boss", not
        /// "area&lt;N&gt;", so <see cref="AreaAccumulationDirector.AreaIndexOf"/> can't parse it and
        /// nothing ever calls <c>EnterArea</c> for it (<c>BackyardPath.WireAreaGatesToPopulation</c>
        /// explicitly skips it). A death fought against the boss needs its own zone-kind check.</summary>
        private int ResolveDeathArea()
        {
            if (_player == null || _map == null) return 0;

            MapZone zone = _map.ZoneAt(_player.position.x, _player.position.z);
            if (zone == null) return 0;
            if (zone.Kind == ZoneKind.Boss) return _cfg.dials.areaCount + 1;
            return AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        private void RespawnPlayer(in RespawnPlan plan)
        {
            if (_player == null) return;

            Vector3 point = RespawnPoint(plan);

            // Same collider-disable/teleport/re-enable shape MapRuntime.Adopt uses to place Max at
            // level start — a CharacterController caches its own position and would otherwise undo
            // the teleport.
            var cc = _player.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            _player.position = point;
            if (cc != null) cc.enabled = was;

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
