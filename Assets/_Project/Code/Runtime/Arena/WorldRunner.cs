using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;

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
    ///
    /// MV-438: the respawn itself no longer runs synchronously off the death. <see cref="OnPlayerDied"/>
    /// now only records the death and shows <see cref="DeathOverlay"/>; everything MV-427 used to do
    /// immediately — area restore, gate reclose, sentinel wipe, teleport — waits in
    /// <see cref="_pendingRespawn"/> for <see cref="Continue"/>, which the overlay's CONTINUE button
    /// calls.
    /// </summary>
    public sealed class WorldRunner : MonoBehaviour
    {
        /// <summary>Metres behind the gate a respawn lands — clear of the doorway's own collider and
        /// the room beyond it, but not so far back it reads as a different spot from "at the gate".</summary>
        private const float RespawnMarginFromGate = 2.5f;

        private SupplyLineNetwork _supply;
        private AreaGate _bossGate;

        /// <summary>Every OTHER gate authored with <c>opensWith: sheds-destroyed-before</c> (MV-560) —
        /// a mid-run boss's entry gate, which <see cref="_bossGate"/> above does not cover since that
        /// field is only ever the fixed <c>"bg"</c> actor id the FINAL boss's gate builds under. Locked
        /// at <see cref="Configure"/> exactly like <see cref="_bossGate"/> is, and force-opened the same
        /// way once its own condition — every shed strictly before the area it opens into — is met.
        /// <c>toAreaIndex</c> is that boundary <see cref="WorldArea.index"/>, what
        /// <see cref="SupplyLineNetwork.ShedsDestroyedBefore"/> checks against.</summary>
        private readonly List<(AreaGate gate, int toAreaIndex)> _shedsBeforeGates =
            new List<(AreaGate, int)>(2);

        /// <summary>One entry per BUILT shed (MV-475, not per area — an area can carry several).
        /// <c>shedId</c> is the entity id <see cref="SupplyLineNetwork"/> tracks destruction against;
        /// <c>areaId</c> is only needed alongside it for <see cref="TrackDestroyedShedStream"/>.</summary>
        private readonly List<(string areaId, string shedId, MowerHutch hutch)> _sheds =
            new List<(string, string, MowerHutch)>(3);

        /// <summary>Destroyed sheds' spawners, keyed by their 1-based area index (MV-456) — fed by
        /// <see cref="TrackDestroyedShedStream"/> as each shed dies. Never drained: a world is short
        /// enough that this never grows past a handful of entries.</summary>
        private readonly List<(int areaIndex, EnemySpawner spawner)> _destroyedShedSpawners =
            new List<(int, EnemySpawner)>(9);

        private WorldConfig _cfg;
        private MapData _map;
        private AreaAccumulationDirector _areaDirector;
        private PickupDirector _pickupDirector;
        private PlayerHealth _playerHealth;
        private Transform _player;
        private DeathOverlay _deathOverlay;

        /// <summary>Guards <see cref="HudSignals.EmitRunComplete"/> so the final area firing it is a
        /// one-shot (MV-591), the same one-shot shape every other terminal signal in this class uses.
        /// Reset alongside every other per-run death/respawn field would be wrong here — a run only
        /// ever completes once, so this is never cleared.</summary>
        private bool _runCompleteRaised;

        /// <summary>MV-438: the deferred respawn a death worked out but hasn't run yet — set the
        /// instant Max falls, cleared (and acted on) only when <see cref="Continue"/> runs. Null
        /// whenever the overlay isn't up, so <see cref="HasPendingRespawn"/> also answers "is a death
        /// overlay currently owed a Continue".</summary>
        private RespawnPlan? _pendingRespawn;

        /// <summary>Test-only access, same idiom as the rest of this project's screen classes.</summary>
        public bool HasPendingRespawn => _pendingRespawn.HasValue;

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
            _runCompleteRaised = false;

            foreach (WorldArea area in cfg.areas)
            {
                WorldShed[] sheds = area.Sheds();
                for (int i = 0; i < sheds.Length; i++)
                {
                    string shedId = area.ShedId(i, sheds.Length);
                    if (!build.Actors.TryGetValue(shedId, out GameObject shedGo) || shedGo == null) continue;

                    MowerHutch hutch = shedGo.GetComponent<MowerHutch>();
                    if (hutch != null) _sheds.Add((area.id, shedId, hutch));
                }
            }

            if (build.Actors.TryGetValue("bg", out GameObject bossGateGo) && bossGateGo != null)
                _bossGate = bossGateGo.GetComponent<AreaGate>();

            // Locked from the start: the boss gate's own HP would otherwise let sustained primary fire
            // break it early, exactly the "boss you can walk in on before the sheds fall" bug the
            // opensWith='all-sheds-destroyed' rule (MapValidation.WorldBossGate) exists to make
            // structurally impossible. A world authored with zero sheds never unlocks it here — that
            // is a content bug for the world config to fix, not something this runner should paper over.
            if (_bossGate != null) _bossGate.Locked = true;

            // MV-571: push the boss gate's starting count immediately — Update()'s polling loop below
            // only recomputes when a shed dies, which would otherwise leave the gate reading LOCKED
            // (zero/zero) rather than its real total until the first shed anywhere falls.
            PushBossGateProgress();

            // MV-560: every OTHER gate authored with sheds-destroyed-before — a mid-run boss's entry,
            // which the fixed "bg" lookup above never finds. Same lock-until-condition-met shape as the
            // final boss gate, just resolved generically off the gate's own opensWith/to-area instead
            // of a hardcoded id.
            if (cfg.gates != null)
            {
                foreach (WorldGate g in cfg.gates)
                {
                    if (g?.id == null || g.id == "bg") continue;
                    if (!string.Equals(g.opensWith, "sheds-destroyed-before", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!build.Actors.TryGetValue(g.id, out GameObject gateGo) || gateGo == null) continue;

                    AreaGate gate = gateGo.GetComponent<AreaGate>();
                    if (gate == null) continue;

                    WorldArea toArea = cfg.Area(g.to?.area);
                    if (toArea == null) continue;

                    gate.Locked = true;
                    _shedsBeforeGates.Add((gate, toArea.index));

                    // MV-571: same starting-count push as the boss gate above, keyed to this gate's
                    // own local boundary rather than the whole world.
                    _supply.ShedProgressBefore(toArea.index, out int destroyed, out int total);
                    gate.SetLockProgress(destroyed, total);
                }
            }

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
                if (intoArea <= 0) continue;   // a link into the entry stub itself — no death ever restores there

                if (!build.Actors.TryGetValue(link.gate, out GameObject gateGo) || gateGo == null) continue;
                AreaGate gate = gateGo.GetComponent<AreaGate>();
                // MV-575: this already covers boss areas too — WorldMapLoader translates a boss area's
                // id to "area<N>" exactly like every other combat area, so the loop above keys its gate
                // at its real index without needing a separate synthetic-index case here.
                if (gate != null) _gateIntoArea[intoArea] = gate;
            }
        }

        private void OnDestroy()
        {
            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
        }

        /// <summary>MV-524 part 2: iOS can suspend-then-terminate a backgrounded app with no further
        /// callback, so the checkpoint write has to happen here, not at quit.</summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) CapturePauseCheckpoint();
        }

        /// <summary>Same trigger as <see cref="OnApplicationPause"/>, from the other signal Unity gives
        /// a backgrounding app (MV-524's own ticket text names both) — <see cref="SaveSystem.CaptureActiveCheckpoint"/>
        /// is a plain overwrite, so a device that fires both costs nothing beyond a redundant write.</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) CapturePauseCheckpoint();
        }

        /// <summary>The actual write, pulled out of <see cref="OnApplicationPause"/>/<see cref="OnApplicationFocus"/>
        /// (MV-524 AC6's documented fallback): Unity never invokes either outside Play mode, so an
        /// EditMode test can't drive this method itself, only the plain <see cref="SaveSystem.CaptureActiveCheckpoint"/>
        /// call inside it — which it does, directly.</summary>
        private void CapturePauseCheckpoint()
        {
            if (_areaDirector == null) return;
            SaveSystem.CaptureActiveCheckpoint(_areaDirector.CurrentArea);
        }

        /// <summary>RESUME tapped on the Home screen (MV-524 part 3): drop the player at
        /// <paramref name="areaIndex"/>'s entry using the exact same restore/reposition pipeline a
        /// death's <see cref="Continue"/> already uses — the checkpoint captures only an area index (no
        /// world snapshot, per the ticket's own decision), so landing back in that area means re-solving
        /// and re-populating its authored composition fresh, exactly like a death respawn into that same
        /// area would. Deliberately skips <see cref="DeathRunState.RecordDeath"/> and the
        /// <see cref="_pendingRespawn"/>/overlay machinery entirely — this is a cold boot landing mid-run,
        /// not a death. A no-op before <see cref="Configure"/> has wired this runner up, or for the entry
        /// stub (<paramref name="areaIndex"/> &lt;= 0 — nothing to restore/respawn into).</summary>
        public void ResumeCheckpoint(int areaIndex)
        {
            if (_areaDirector == null || _cfg?.dials == null || areaIndex <= 0) return;

            bool gateIsConditionGated = _cfg.AreaByIndex(areaIndex)?.IsBossRole ?? false;
            RespawnPlan plan = RespawnPlanner.Resolve(areaIndex, gateIsConditionGated);

            _areaDirector.RestoreArea(plan.RestoreAreaIndex);

            if (_pickupDirector == null) _pickupDirector = FindFirstObjectByType<PickupDirector>();
            _pickupDirector?.ResetBruiserCountdown(plan.RestoreAreaIndex);

            if (plan.RecloseGate && _gateIntoArea.TryGetValue(plan.RestoreAreaIndex, out AreaGate gate) && gate != null)
                gate.Reclose();

            Sentinel.DestroyAllActive();

            EnsurePlayer();
            RespawnPlayer(plan);
            _areaDirector.SetCurrentArea(plan.RespawnAreaIndex);
        }

        /// <summary>Pushes the final boss gate's shed count (MV-571) — every shed in the world, the
        /// same set <see cref="SupplyLineNetwork.AllShedsDestroyed"/> counts, unlike a mid-run gate's
        /// local <see cref="SupplyLineNetwork.ShedsDestroyedBefore"/> boundary. Reuses
        /// <see cref="SupplyLineNetwork.ShedProgressBefore"/> with an area-index bound past every
        /// authored area (<c>_cfg.dials.areaCount + 1</c>) so nothing before it is excluded — an
        /// exclusive bound, unrelated to any area's own index.</summary>
        private void PushBossGateProgress()
        {
            if (_bossGate == null) return;
            int worldAreaBound = _cfg?.dials != null ? _cfg.dials.areaCount + 1 : int.MaxValue;
            _supply.ShedProgressBefore(worldAreaBound, out int destroyed, out int total);
            _bossGate.SetLockProgress(destroyed, total);
        }

        private void Update()
        {
            if (_supply != null)
            {
                for (int i = _sheds.Count - 1; i >= 0; i--)
                {
                    (string areaId, string shedId, MowerHutch hutch) = _sheds[i];
                    if (hutch != null && hutch.IsAlive) continue;

                    _sheds.RemoveAt(i);
                    _supply.DestroyShed(shedId);
                    TrackDestroyedShedStream(areaId, hutch);

                    if (_bossGate != null)
                    {
                        PushBossGateProgress();   // <-- MV-571, before AllShedsDestroyed opens it

                        if (_supply.AllShedsDestroyed)
                        {
                            _bossGate.Locked = false;
                            _bossGate.ForceOpen();
                        }
                    }

                    // MV-560: a shed just fell, so it's the moment any mid-run boss's own condition
                    // could newly be met — same event-driven check as the final boss gate above, just
                    // per-gate against its own boundary area index instead of the whole world.
                    for (int j = _shedsBeforeGates.Count - 1; j >= 0; j--)
                    {
                        (AreaGate gate, int toAreaIndex) = _shedsBeforeGates[j];

                        _supply.ShedProgressBefore(toAreaIndex, out int done, out int total);
                        gate.SetLockProgress(done, total);          // <-- MV-571, before the early-continue

                        if (!_supply.ShedsDestroyedBefore(toAreaIndex)) continue;

                        gate.Locked = false;
                        gate.ForceOpen();
                        _shedsBeforeGates.RemoveAt(j);
                    }
                }
            }

            UpdatePostDestructionStreamGating();

            // MV-591: the run ends when the FINAL area is empty — every robot dead, none still queued
            // to arrive, no boss alive. Not when a boss dies; a12 and a20 have bosses mid-run.
            if (!_runCompleteRaised && _areaDirector != null && _cfg?.dials != null)
            {
                int finalArea = _cfg.dials.areaCount;
                if (_areaDirector.CurrentArea >= finalArea
                    && _areaDirector.ActiveCount == 0
                    && _areaDirector.QueuedCount == 0
                    && !BossCensus.AnyLivingIn(finalArea))
                {
                    _runCompleteRaised = true;
                    HudSignals.EmitRunComplete();
                }
            }
        }

        /// <summary>Remember a destroyed shed's <see cref="EnemySpawner"/> against its 1-based area
        /// index (MV-456), so <see cref="UpdatePostDestructionStreamGating"/> can pause/resume its
        /// post-destruction trickle purely off which area the player is standing in. A no-op if the
        /// area id doesn't resolve to a combat area index or the factory carries no spawner — neither
        /// should happen (every shed's area carries one, and RequireComponent guarantees the other).</summary>
        private void TrackDestroyedShedStream(string areaId, MowerHutch hutch)
        {
            if (hutch == null || _cfg == null) return;
            WorldArea area = _cfg.Area(areaId);
            if (area == null || area.index <= 0) return;
            EnemySpawner spawner = hutch.GetComponent<EnemySpawner>();
            if (spawner == null) return;
            _destroyedShedSpawners.Add((area.index, spawner));
        }

        /// <summary>The risk MV-456 flags by name: areas are never unloaded, so with several destroyed
        /// sheds all streaming, the field-wide <see cref="EnemySpawner.GlobalMaxLiveEnemies"/> cap
        /// could starve spawns in the room the player is actually in. Mitigation: only the destroyed
        /// shed whose area the player is CURRENTLY, PHYSICALLY standing in keeps streaming; every
        /// other destroyed shed pauses. Cheap to poll every frame — a world carries at most a handful
        /// of sheds.</summary>
        private void UpdatePostDestructionStreamGating()
        {
            if (_destroyedShedSpawners.Count == 0 || _map == null) return;
            EnsurePlayer();
            if (_player == null) return;

            MapZone zone = _map.ZoneAt(_player.position.x, _player.position.z);
            int playerArea = zone == null ? 0 : AreaAccumulationDirector.AreaIndexOf(zone.id);

            for (int i = 0; i < _destroyedShedSpawners.Count; i++)
            {
                (int areaIndex, EnemySpawner spawner) = _destroyedShedSpawners[i];
                if (spawner != null) spawner.SetAreaPaused(areaIndex != playerArea);
            }
        }

        /// <summary>Max fell (MV-427). The run doesn't end, but MV-438 stops it continuing silently:
        /// this only records the death, works out the plan, and shows the overlay — the actual area
        /// restore/gate-reclose/respawn (what this method did in full, pre-MV-438) waits in
        /// <see cref="Continue"/> for the player's own CONTINUE tap.</summary>
        private void OnPlayerDied()
        {
            if (_areaDirector == null || _cfg?.dials == null) return;

            EnsurePlayer();
            int deathArea = ResolveDeathArea();
            if (deathArea <= 0) return;   // no live area context (a headless fixture) — nothing to respawn into

            // MV-575: whether the gate into the death area re-closes is a property of the area (its
            // role), not of where it sits in the sequence — a boss area's gate opens on a shed
            // condition, never combat, and re-closing it would be unreopenable (a softlock).
            bool deathGateIsConditionGated = _cfg.AreaByIndex(deathArea)?.IsBossRole ?? false;
            RespawnPlan plan = RespawnPlanner.Resolve(deathArea, deathGateIsConditionGated);

            DeathRunState.RecordDeath();
            _pendingRespawn = plan;

            Time.timeScale = 0f;   // frozen until CONTINUE — nothing below this line until then
            ModalFrameRateGate.Enter();   // MV-574: idle the frame rate behind the death overlay

            WorldArea restoreArea = _cfg.AreaByIndex(plan.RestoreAreaIndex);
            string areaName = restoreArea != null ? restoreArea.name : $"Area {plan.RestoreAreaIndex}";

            if (_deathOverlay == null)
            {
                _deathOverlay = FindFirstObjectByType<DeathOverlay>();
                if (_deathOverlay == null) _deathOverlay = new GameObject("DeathOverlay").AddComponent<DeathOverlay>();
            }
            _deathOverlay.Show(areaName, plan.RecloseGate, DeathRunState.DeathsTaken, Continue);
        }

        /// <summary>CONTINUE was tapped (MV-438) — runs the deferred respawn sequence exactly as
        /// <see cref="OnPlayerDied"/> did in full before this ticket, then un-pauses. A no-op if there
        /// is nothing pending (e.g. a stray extra call), so this is always safe to wire straight to a
        /// button.</summary>
        public void Continue()
        {
            if (!_pendingRespawn.HasValue) return;
            RespawnPlan plan = _pendingRespawn.Value;
            _pendingRespawn = null;

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

            Time.timeScale = 1f;
            ModalFrameRateGate.Exit();
        }

        private void EnsurePlayer()
        {
            if (_player != null) return;
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }

        /// <summary>Where Max is standing RIGHT NOW, in <see cref="RespawnPlanner.Resolve"/> terms —
        /// the area's real 1-based index, whether it's an ordinary area or a boss (MV-575: a boss area
        /// is a real numbered entry in <see cref="WorldConfig.areas"/>, translated to the same
        /// "area&lt;N&gt;" zone id as everything else by <see cref="WorldMapLoader"/> — there is no
        /// synthetic index past the end of the sequence for "the boss room"). Deliberately reads live
        /// position (<see cref="MapData.ZoneAt"/>), not <see cref="AreaAccumulationDirector.CurrentArea"/>:
        /// that tracker is advanced ahead of the player for population purposes (MV-245) and never
        /// enters a boss zone at all (<c>BackyardPath.WireAreaGatesToPopulation</c> explicitly skips
        /// it), so a death fought against a boss still needs to read where Max actually is.</summary>
        private int ResolveDeathArea()
        {
            if (_player == null || _map == null) return 0;

            MapZone zone = _map.ZoneAt(_player.position.x, _player.position.z);
            return zone == null ? 0 : AreaAccumulationDirector.AreaIndexOf(zone.id);
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
