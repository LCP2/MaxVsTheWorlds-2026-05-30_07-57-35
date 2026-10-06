using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.CameraRig;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Feel;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The world's own finale exit (MV-915) — the trigger for the doorway out of the world's LAST boss
    /// area (role "boss", index == <c>dials.areaCount</c>), separate from <see cref="BackyardExitGate"/>
    /// on purpose.
    ///
    /// <see cref="BackyardExitGate"/>/<c>BossVictoryPayoff</c> are a single scene-wide instance built
    /// once, positioned at whichever boss zone <see cref="MaxWorlds.Arena.Map.MapLayoutBridge"/> finds
    /// FIRST (in practice, a mid-run area like a12) and armed by the FIRST <see cref="HudSignals.BossDefeated"/>
    /// anywhere in the run — a single-boss-arena assumption from before World 1 authored bosses mid-run
    /// (a12, a20) ahead of the real finale (a30). Generalising that class to move/re-arm per area would
    /// change what a12's own boss death already does, which this ticket must not touch. This is a new,
    /// independent object instead: it only ever exists for a world's own actual finale (its final boss
    /// area), and it is built at that area's own zone.
    ///
    /// MV-956: used to open on <see cref="HudSignals.BossDefeated"/> for its OWN final area (see
    /// <see cref="IsFinalBossAreaDefeat"/>), the same event <c>BossVictoryPayoff</c> drops the Weapon
    /// Core on. MV-1078 reverses that: the orb (collecting the Core) is what matters now, not the boss
    /// dying — Lee, 2026-10-03, "I can't help thinking that the orb means nothing." The boss dying only
    /// latches <see cref="_finalBossDefeated"/>; the exit stays shut until every living robot in the
    /// final area is dead too (see <see cref="OnWeaponCoreCollected"/>/<see cref="TickCleanup"/>) — the
    /// 25s failsafe in <see cref="TickCleanup"/> is what stops one stuck robot stalling the ending
    /// forever, the actual risk the 2026-09-26 ruling this reverses was guarding against.
    ///
    /// MV-964: no longer a barrier that sinks in place — opening now hands off to
    /// <see cref="WorldJoinSequence"/> to cut the REAL door <see cref="WorldTransitions"/> authors for
    /// this world in its actual exit wall and build the corridor behind it, in the same frame the Weapon
    /// Core drops. Max keeps full control until he actually walks through it; <see cref="WorldJoinSequence"/>'s
    /// own crossing check takes over from there, so this class no longer tracks Max's position at all.
    ///
    /// MV-1013: the last world (no <see cref="WorldTransitions"/> row — nowhere to build a corridor
    /// toward) still gets a real, closed exit door authored the same way (<see cref="WorldTransitions.ApplyExitDoorway"/>'s
    /// finale-only table) — <see cref="Open"/> just force-opens it directly instead of handing off to
    /// <see cref="WorldJoinSequence"/>, and THIS class tracks Max's own crossing of it (see <see cref="Update"/>),
    /// since there is no corridor sequence to do that job for a world with nowhere further to go.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("map/gate")]
    public sealed class WorldFinaleGate : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<WorldFinaleGate>() != null) return;

            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Cfg?.dials == null || path.Map == null) return;

            new GameObject("WorldFinaleGate").AddComponent<WorldFinaleGate>();
        }

        /// <summary>The gate's own resolved state — open once this area's own final boss has actually
        /// died, not an authored flag (MV-915 AC4; MV-956 changed the trigger from RunComplete).</summary>
        public bool IsOpen { get; private set; }

        private WorldConfig _cfg;
        private MapData _map;
        private WorldTransitionEntry _entry;
        private int _fromWorldIndex;
        private AreaGate _exitGate;

        /// <summary>MV-1013: this world's own exit doorway geometry (wall/coord/span), read straight off
        /// <see cref="MapData.exitDoorway"/> — set for every world now (<see cref="WorldTransitions.ApplyExitDoorway"/>'s
        /// finale-only table covers the last world, which has no <see cref="_entry"/>). Used only by
        /// <see cref="Update"/>'s own crossing check, for the corridor-less last-world case.</summary>
        private ExitDoorway? _doorway;

        private Transform _max;
        private bool _crossed;

        /// <summary>MV-1078: this gate's own final boss has died — latched by
        /// <see cref="OnBossDefeated"/>, which no longer opens the exit directly. Independent of
        /// <see cref="_cleanupActive"/>: a boss can die well before Max ever walks back to collect the
        /// Core it dropped.</summary>
        private bool _finalBossDefeated;

        /// <summary>MV-1078: true from the moment the Core is collected until the final area's last
        /// living robot is dead and the exit actually opens.</summary>
        private bool _cleanupActive;

        /// <summary>MV-1078: the final area index (<c>cfg.dials.areaCount</c>), resolved fresh in
        /// <see cref="BeginCleanup"/> rather than reused from <see cref="Awake"/>'s own geometry (null in
        /// the geometry-independent tests, same reasoning as <see cref="IsFinalBossAreaDefeat"/>).</summary>
        private int _finalAreaIndex;

        private int _lastRobotsLeft;
        private float _lastRobotDeathRealtime;

        /// <summary>MV-1078: a stuck robot (nav-trapped, off a destroyed path) must never stall the
        /// ending forever — the same risk the 2026-09-26 "neither the orb nor the exit may require
        /// killing all robots" ruling was guarding against, now handled here instead of by dropping the
        /// requirement.</summary>
        private const float CleanupFailsafeSeconds = 25f;

        // ---------------------------------------------------------------- MV-1079: Beat A, WEAPON TAKEN

        private const float WeaponBeatDuration = 2.5f;
        private const float WeaponBeatMorphTime = 0.5f;
        private const float WeaponBeatFlashEnd = 0.7f;
        private const float WeaponBeatFlashDiameter = 1.5f;
        private const float WeaponBeatRingStart = 0.5f;
        private const float WeaponBeatRingEnd = 1.3f;
        private const float WeaponBeatRingMaxRadius = 3f; // 6 m across
        private const float WeaponBeatBannerStart = 0.6f;
        private const float WeaponBeatBannerFadeInEnd = 0.8f;   // start + 0.2 s
        private const float WeaponBeatBannerFadeOutStart = 2.2f; // end - 0.3 s

        private bool _weaponBeatActive;
        private float _weaponBeatTime;
        private bool _weaponBeatMorphApplied;
        private int _weaponBeatPlayedWorld;
        private GroundRing _weaponBeatFlashRing;
        private GroundRing _weaponBeatRing;

        // ---------------------------------------------------------------- MV-1079: Beat B, EXIT OPEN

        private const float ExitBeatDuration = 3.0f;
        private const float ExitBeatDoorOpenTime = 1.0f;
        private const float ExitBeatHoldUntil = 2.2f;
        private const float ExitBeatBurstFadeSeconds = 0.3f;
        private const float ExitBeatBurstRadius = 1.5f; // 3 m across
        private const float ExitBeatNearDoorSkipDistance = 8f;
        private const float ExitBeatBoltSpeedFloor = 1f;

        private bool _exitBeatActive;
        private float _exitBeatTime;
        private bool _exitBeatSkipTravel;
        private bool _exitBeatDoorOpened;
        private Vector3 _exitBeatDoorPosition;
        private CameraTargetRig _exitBeatCameraRig;
        private SentinelBolt _exitBeatBolt;
        private GroundRing _exitBeatBurstRing;

        private FinaleBanner _banner;
        private readonly List<RobotEnemy> _frozenRobots = new List<RobotEnemy>(16);
        private PlayerController _frozenPlayer;

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Cfg?.dials == null || path.Map == null) { enabled = false; return; }

            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            if (areaDirector == null) { enabled = false; return; }

            _cfg = path.Cfg;
            _map = path.Map;
            _fromWorldIndex = areaDirector.ActiveWorldIndex;
            _entry = WorldTransitions.For(_fromWorldIndex);
            // MV-997: the real, closed map gate MapRuntime already built for this world's exit door --
            // Open() forces THIS open rather than handing WorldJoinSequence a runtime CutWallGap.
            _exitGate = path.ExitGate;
            _doorway = _map.exitDoorway;
        }

        private void OnEnable()
        {
            HudSignals.BossDefeated += OnBossDefeated;
            HudSignals.WeaponCoreCollected += OnWeaponCoreCollected;
        }

        private void OnDisable()
        {
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.WeaponCoreCollected -= OnWeaponCoreCollected;

            // MV-1079: a gate torn down mid-beat (scene reload, test teardown) must not leak its own
            // scratch VFX/UI or leave gameplay stuck suspended.
            if (_weaponBeatActive || _exitBeatActive) RestoreGameplayForBeat();
            HideWeaponBeatVisuals();
            HideExitBeatVisuals();
            if (_banner != null) { _banner.DestroySelf(); _banner = null; }
        }

        /// <summary>MV-956: <see cref="HudSignals.BossDefeated"/> fires for the last living boss of ANY
        /// area emptying (a12, a20, a30 alike) — only latch for THIS gate's own final area. MV-1078:
        /// no longer opens the exit — see the class doc comment for why.</summary>
        private void OnBossDefeated()
        {
            if (!IsFinalBossAreaDefeat()) return;
            _finalBossDefeated = true;
        }

        /// <summary>MV-1078: the orb is what starts the WEAPON TAKEN/CLEAN-UP beat — never before the
        /// final boss has actually died (a Weapon Core only ever drops off that death, but this guard
        /// keeps the two concerns independent), and never twice for the same finale. MV-1079: the beat
        /// that actually starts here is now Beat A (<see cref="BeginWeaponBeat"/>), the player-visible
        /// "orb becomes the weapon" moment — CLEAN-UP itself only begins once that beat finishes.</summary>
        private void OnWeaponCoreCollected()
        {
            if (!_finalBossDefeated || _cleanupActive || _weaponBeatActive || IsOpen) return;
            BeginWeaponBeat();
        }

        /// <summary>MV-1078, WEAPON TAKEN -&gt; CLEAN-UP: (a) the final area's own sheds/Replicators stop
        /// producing and anything still queued for it is discarded. (b) every living robot already in the
        /// final area wakes and hunts, whatever its dormancy state. Then CLEAN-UP begins:
        /// <see cref="TickCleanup"/> drives the rest. MV-1079: the weapon morph itself no longer applies
        /// here — <see cref="TickWeaponBeat"/> applies it mid-beat, at its own authored instant, and this
        /// method only ever runs once that beat (or its no-next-weapon skip) has already finished.</summary>
        private void BeginCleanup()
        {
            _cleanupActive = true;

            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            WorldConfig cfg = areaDirector != null ? areaDirector.ActiveWorldConfig : null;
            _finalAreaIndex = cfg?.dials != null ? cfg.dials.areaCount : 0;

            if (_finalAreaIndex > 0)
            {
                areaDirector?.DiscardQueuedForArea(_finalAreaIndex);
                StopFinalAreaProduction(_finalAreaIndex);
                WakeFinalAreaRobots(_finalAreaIndex);
            }

            _lastRobotsLeft = -1;
            _lastRobotDeathRealtime = Time.unscaledTime;
            TickCleanup();
        }

        /// <summary>MV-1079, Beat A entry point: the player-visible "orb becomes the weapon" beat (2.5 s)
        /// — gameplay suspends, the Core flies into the gadget, the morph applies at its own authored
        /// instant mid-beat (<see cref="TickWeaponBeat"/>), and CLEAN-UP (<see cref="BeginCleanup"/>)
        /// only starts once it finishes. Skipped entirely — straight to CLEAN-UP, nothing to animate or
        /// apply — when this world has no next weapon (the last world).</summary>
        private void BeginWeaponBeat()
        {
            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            int playedWorld = areaDirector != null ? areaDirector.ActiveWorldIndex : 0;

            if (playedWorld + 1 >= WorldLibrary.Count)
            {
                BeginCleanup();
                return;
            }

            _weaponBeatActive = true;
            _weaponBeatTime = 0f;
            _weaponBeatMorphApplied = false;
            _weaponBeatPlayedWorld = playedWorld;
            SuspendGameplayForBeat();
        }

        /// <summary>Advance Beat A by <paramref name="dt"/> seconds. Public — same "an EditMode test can
        /// drive this deterministically" contract <see cref="WorldJoinSequence.Tick"/> and
        /// <see cref="SentinelBolt.Tick"/> already give their own scripted beats.</summary>
        public void TickWeaponBeat(float dt)
        {
            if (!_weaponBeatActive) return;
            _weaponBeatTime += dt;

            if (!_weaponBeatMorphApplied && _weaponBeatTime >= WeaponBeatMorphTime)
            {
                _weaponBeatMorphApplied = true;
                // MV-1079: the weapon change lands at THIS instant, not before — BeginCleanup used to
                // apply it the moment the Core was collected; now the beat itself is what applies it,
                // inside the flash below.
                PendingMorphingModule.TakeWeaponCore();
                WeaponSystemState.ApplyWeaponCoreMorph(_weaponBeatPlayedWorld + 1);
            }

            UpdateWeaponBeatFlash();
            UpdateWeaponBeatRing();
            UpdateWeaponBeatBanner();

            if (_weaponBeatTime >= WeaponBeatDuration)
            {
                _weaponBeatActive = false;
                HideWeaponBeatVisuals();
                RestoreGameplayForBeat();
                BeginCleanup();
            }
        }

        /// <summary>MV-1079, Beat A step 2: a white-cyan additive flash about 1.5 m across at the gadget,
        /// 0.50-0.70 s — the morph itself already applied by the time this window opens.</summary>
        private void UpdateWeaponBeatFlash()
        {
            if (_weaponBeatTime < WeaponBeatMorphTime || _weaponBeatTime > WeaponBeatFlashEnd)
            {
                _weaponBeatFlashRing?.Hide();
                return;
            }

            if (_weaponBeatFlashRing == null) _weaponBeatFlashRing = GroundRing.Create("MV-1079 Weapon Flash", additive: true);
            float t = Mathf.InverseLerp(WeaponBeatMorphTime, WeaponBeatFlashEnd, _weaponBeatTime);
            Vector3 pos = GadgetPosition();
            float radius = Mathf.Sin(t * Mathf.PI) * (WeaponBeatFlashDiameter * 0.5f);
            Color c = new Color(0.85f, 0.98f, 1f, 1f - t);
            _weaponBeatFlashRing.Show(pos, Mathf.Max(0.05f, radius), c);
        }

        /// <summary>MV-1079, Beat A step 3: a ground ring expands from Max to 6 m across and fades,
        /// 0.50-1.30 s. Visual only — no gameplay effect.</summary>
        private void UpdateWeaponBeatRing()
        {
            if (_weaponBeatTime < WeaponBeatRingStart || _weaponBeatTime > WeaponBeatRingEnd)
            {
                _weaponBeatRing?.Hide();
                return;
            }

            Transform max = MaxTransform();
            if (max == null) return;

            if (_weaponBeatRing == null) _weaponBeatRing = GroundRing.Create("MV-1079 Weapon Ring", additive: true);
            float t = Mathf.InverseLerp(WeaponBeatRingStart, WeaponBeatRingEnd, _weaponBeatTime);
            float radius = Mathf.Lerp(0.1f, WeaponBeatRingMaxRadius, t);
            Color c = new Color(0.4f, 0.95f, 1f, 1f - t);
            _weaponBeatRing.Show(max.position, radius, c);
        }

        /// <summary>MV-1079, Beat A step 4: the centre banner — "NEW WEAPON" small, the new primary's
        /// long name large, cyan — 0.60-2.50 s, fading in over 0.2 s and out over the last 0.3 s.</summary>
        private void UpdateWeaponBeatBanner()
        {
            if (_weaponBeatTime < WeaponBeatBannerStart || _weaponBeatTime > WeaponBeatDuration)
            {
                _banner?.Hide();
                return;
            }

            float alpha;
            if (_weaponBeatTime < WeaponBeatBannerFadeInEnd)
                alpha = Mathf.InverseLerp(WeaponBeatBannerStart, WeaponBeatBannerFadeInEnd, _weaponBeatTime);
            else if (_weaponBeatTime > WeaponBeatBannerFadeOutStart)
                alpha = 1f - Mathf.InverseLerp(WeaponBeatBannerFadeOutStart, WeaponBeatDuration, _weaponBeatTime);
            else
                alpha = 1f;

            if (_banner == null) _banner = FinaleBanner.Create();
            string longName = WeaponCatalog.DisplayName(WeaponSystemState.ActivePrimary);
            _banner.Show("NEW WEAPON", longName, alpha);
        }

        private void HideWeaponBeatVisuals()
        {
            _weaponBeatFlashRing?.Hide();
            _weaponBeatRing?.Hide();
        }

        /// <summary>Where Beat A's flash plays — Max's own gadget, when his rig exists, falling back to
        /// Max's capsule position for a fixture/test with no <see cref="MaxRig"/> built.</summary>
        private static Vector3 GadgetPosition()
        {
            if (MaxRig.Instance != null) return MaxRig.Instance.GunWorldPosition;
            GameObject g = GameObject.FindGameObjectWithTag("Player");
            return g != null ? g.transform.position : Vector3.zero;
        }

        /// <summary>MV-1079: freezes the field for a finale beat — every active robot and Max's own
        /// control, so neither can act or be acted on while a scripted beat plays. The HUD is
        /// deliberately left alone (both beats keep it up). MV-1081: freezes via
        /// <see cref="RobotEnemy.SetCutsceneFrozen"/> rather than disabling — disabling fired OnEnable's
        /// own ResetState on restore, reverting every robot to a fresh full-health Chase after each beat
        /// and un-converting anything captured. Same snapshot idiom <see cref="WorldJoinSequence.SuspendGameplay"/>
        /// already uses.</summary>
        private void SuspendGameplayForBeat()
        {
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) { player.enabled = false; _frozenPlayer = player; }

            _frozenRobots.Clear();
            _frozenRobots.AddRange(RobotEnemy.Active);
            foreach (RobotEnemy r in _frozenRobots)
                if (r != null) r.SetCutsceneFrozen(true);
        }

        private void RestoreGameplayForBeat()
        {
            if (_frozenPlayer != null) { _frozenPlayer.enabled = true; _frozenPlayer = null; }
            foreach (RobotEnemy r in _frozenRobots) if (r != null) r.SetCutsceneFrozen(false);
            _frozenRobots.Clear();
        }

        /// <summary>MV-1078 (b): this gate's own final area's sheds (<see cref="MowerHutch"/>) and
        /// Replicators stop producing — found by position (<see cref="MowerHutch"/> carries no area tag
        /// of its own, unlike <see cref="Replicator.AreaIndex"/>), since <see cref="_map"/> is null in
        /// the geometry-independent tests, making this a no-op there exactly like <see cref="Open"/>'s
        /// own WorldJoinSequence hand-off.</summary>
        private void StopFinalAreaProduction(int areaIndex)
        {
            foreach (MowerHutch hutch in FactoryCensus.All)
            {
                if (hutch == null || !hutch.IsAlive || !IsInArea(hutch.transform.position, areaIndex)) continue;
                EnemySpawner spawner = hutch.GetComponent<EnemySpawner>();
                if (spawner != null) spawner.Stop();
            }

            foreach (Replicator replicator in FactoryCensus.RegisteredReplicators)
            {
                if (replicator == null || !replicator.IsAlive || replicator.AreaIndex != areaIndex) continue;
                EnemySpawner spawner = replicator.GetComponent<EnemySpawner>();
                if (spawner != null) spawner.Stop();
            }
        }

        private bool IsInArea(Vector3 pos, int areaIndex)
        {
            if (_map == null) return false;
            MapZone zone = _map.ZoneAt(pos.x, pos.z);
            return zone != null && MapEnums.AreaIndexOf(zone.id) == areaIndex;
        }

        /// <summary>MV-1078 (c): every living robot already placed in the final area wakes and joins the
        /// hunt, regardless of dormancy — <see cref="RobotEnemy.Activate"/> is idempotent and a no-op on
        /// anything already awake, the same "call it unconditionally" shape
        /// <see cref="AreaAccumulationDirector.ActivateGarrisonFor"/> already relies on. Scans
        /// <see cref="Object.FindObjectsByType{T}(FindObjectsInactive, FindObjectsSortMode)"/> rather
        /// than <see cref="RobotEnemy.Active"/> — see <see cref="AreaAccumulationDirector.ActiveCountForArea"/>'s
        /// own doc for why.</summary>
        private static void WakeFinalAreaRobots(int areaIndex)
        {
            foreach (RobotEnemy r in FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r != null && r.IsAlive && r.AreaIndex == areaIndex) r.Activate();
        }

        /// <summary>MV-1078, CLEAN-UP: the bottom HUD line reads "ROBOTS LEFT n" (n = living robots in
        /// the final area, counted directly off <see cref="RobotEnemy.Active"/>/<see cref="RobotEnemy.AreaIndex"/>
        /// — the same per-robot source <see cref="WakeFinalAreaRobots"/> already scans, independent of
        /// how a given robot got there (the ambient queue, a pre-placed garrison, or a hand-built test
        /// fixture) — rather than field-wide <see cref="AreaAccumulationDirector.ActiveCount"/>, which
        /// would never reach zero while an earlier area still has survivors, see the class doc comment's
        /// Observation). A stuck robot force-dies after <see cref="CleanupFailsafeSeconds"/> with no
        /// death in the area. Reaching zero opens the exit exactly as <see cref="Open"/> always has.</summary>
        private void TickCleanup()
        {
            int left = RobotsLeftInFinalArea();

            if (left != _lastRobotsLeft)
            {
                _lastRobotsLeft = left;
                _lastRobotDeathRealtime = Time.unscaledTime;
                HudSignals.EmitArenaLabelOverride(left > 0 ? $"ROBOTS LEFT {left}" : null);
            }
            else if (left > 0 && Time.unscaledTime - _lastRobotDeathRealtime >= CleanupFailsafeSeconds)
            {
                ForceKillStragglers(_finalAreaIndex);
                _lastRobotDeathRealtime = Time.unscaledTime;
                return;
            }

            if (left <= 0)
            {
                _cleanupActive = false;
                BeginExitBeat(ResolveDoorPosition());
            }
        }

        private int RobotsLeftInFinalArea() =>
            FindFirstObjectByType<AreaAccumulationDirector>()?.ActiveCountForArea(_finalAreaIndex) ?? 0;

        private static void ForceKillStragglers(int areaIndex)
        {
            // A snapshot, not a live view -- killing a robot deactivates its GameObject, and this must
            // not mutate the very collection FindObjectsByType handed back while this loop walks it.
            var stragglers = new List<RobotEnemy>(
                FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            foreach (RobotEnemy r in stragglers)
            {
                if (r == null || !r.IsAlive || r.AreaIndex != areaIndex) continue;
                r.TakeDamage(new DamageInfo(999999f, r.transform.position, Vector3.forward, Team.Player));
            }
        }

        /// <summary>The exact same "was the area that just cleared the world's own final boss area"
        /// check <c>BossVictoryPayoff.IsFinalBossAreaDefeat</c> makes before dropping the Weapon Core —
        /// kept local rather than shared (two lines, and the two classes must never depend on one
        /// another). Deliberately independent of this component's own <see cref="Awake"/>-resolved
        /// geometry: a test driving pure boss-death/seal logic has no reason to build a real scene.</summary>
        private static bool IsFinalBossAreaDefeat()
        {
            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            WorldConfig cfg = areaDirector != null ? areaDirector.ActiveWorldConfig : null;
            if (cfg?.dials == null) return false;

            int areaIndex = BossCensus.LastDefeatedAreaIndex;
            WorldArea area = cfg.AreaByIndex(areaIndex);
            return area != null && area.IsBossRole && areaIndex == cfg.dials.areaCount;
        }

        private void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            // Geometry-independent tests (Mv915/MV956/MV959/Mv921) build this component with no
            // BackyardPath in the scene at all -- Awake already bailed on those, leaving these null, so
            // there is nothing to hand off to and IsOpen alone is the whole observable effect, same as
            // before this ticket.
            if (_entry != null && _cfg != null && _map != null)
                WorldJoinSequence.OpenExitDoor(_cfg, _map, _entry, _fromWorldIndex, _exitGate);
            else if (_exitGate != null)
                // MV-1013: the last world's own finale -- a real door (see _doorway), but nowhere to
                // build a corridor toward, so there is no WorldJoinSequence to hand off to. Force it
                // open directly; Update() below watches for Max walking through it.
                _exitGate.ForceOpen();
        }

        /// <summary>MV-1079: the door's own resolved world position, for Beat B's camera travel and
        /// bolt target — <see cref="_exitGate"/> when Awake resolved real geometry (every production
        /// run), falling back to Max's own position (a geometry-independent fixture/test with nothing
        /// built at all, where the beat then measures as "already at the door" and skips its travel).</summary>
        private Vector3 ResolveDoorPosition()
        {
            if (_exitGate != null) return _exitGate.transform.position;
            Transform max = MaxTransform();
            return max != null ? max.position : Vector3.zero;
        }

        /// <summary>MV-1079, Beat B entry point: the player-visible "exit blows open" beat (3.0 s) —
        /// gameplay suspends, a bolt travels from the gadget to the door, the camera travels with it
        /// (<see cref="CameraTargetRig.ApplyFocusOverride"/>), the door itself opens mid-beat
        /// (<see cref="TickExitBeat"/> moves the existing <see cref="Open"/> call to that instant), and
        /// gameplay resumes once it ends. Public — same "an EditMode test can drive this deterministically,
        /// no scene required" contract as <see cref="WorldJoinSequence.Initialize"/>: a real run always
        /// arrives here through <see cref="TickCleanup"/>, which resolves <paramref name="doorPosition"/>
        /// itself (<see cref="ResolveDoorPosition"/>); a test can call this directly with an explicit
        /// position instead.</summary>
        public void BeginExitBeat(Vector3 doorPosition)
        {
            if (_exitBeatActive || IsOpen) return;

            _exitBeatActive = true;
            _exitBeatTime = 0f;
            _exitBeatDoorOpened = false;
            _exitBeatDoorPosition = doorPosition;

            Transform max = MaxTransform();
            Vector3 maxPos = max != null ? max.position : Vector3.zero;
            float dist = Vector2.Distance(new Vector2(maxPos.x, maxPos.z), new Vector2(doorPosition.x, doorPosition.z));
            _exitBeatSkipTravel = dist < ExitBeatNearDoorSkipDistance;

            _exitBeatCameraRig = FindFirstObjectByType<CameraTargetRig>();

            SuspendGameplayForBeat();

            // MV-1079 step 1: the bolt and its ground glow fly for exactly the travel window
            // (0.00-1.00 s) regardless of the 8 m skip — "the bolt and burst still play" even when the
            // camera itself doesn't travel.
            Vector3 origin = GadgetPosition();
            float speed = Mathf.Max(ExitBeatBoltSpeedFloor, Vector3.Distance(origin, doorPosition) / ExitBeatDoorOpenTime);
            _exitBeatBolt = SentinelBolt.Fire(origin, doorPosition, speed, ExitBeatColor());
        }

        /// <summary>Advance Beat B by <paramref name="dt"/> seconds. Public — same test-driving contract
        /// as <see cref="TickWeaponBeat"/>.</summary>
        public void TickExitBeat(float dt)
        {
            if (!_exitBeatActive) return;
            _exitBeatTime += dt;

            _exitBeatBolt?.Tick(dt);

            if (!_exitBeatSkipTravel && _exitBeatCameraRig != null)
            {
                float travel;
                if (_exitBeatTime <= ExitBeatDoorOpenTime)
                    travel = AnimSequence.OutQuad(Mathf.Clamp01(_exitBeatTime / ExitBeatDoorOpenTime));
                else if (_exitBeatTime <= ExitBeatHoldUntil)
                    travel = 1f;
                else
                    travel = 1f - Mathf.Clamp01((_exitBeatTime - ExitBeatHoldUntil) / (ExitBeatDuration - ExitBeatHoldUntil));

                _exitBeatCameraRig.ApplyFocusOverride(_exitBeatDoorPosition, travel);
            }

            if (!_exitBeatDoorOpened && _exitBeatTime >= ExitBeatDoorOpenTime)
            {
                _exitBeatDoorOpened = true;
                Open(); // MV-1079: moved here from the instant the final robot died — see class doc.
                PlayExitBeatBurst();
            }

            UpdateExitBeatBurst();
            UpdateExitBeatBanner();

            if (_exitBeatTime >= ExitBeatDuration)
            {
                _exitBeatActive = false;
                if (_exitBeatCameraRig != null) _exitBeatCameraRig.EndFocusOverride();
                HideExitBeatVisuals();
                RestoreGameplayForBeat();
            }
        }

        /// <summary>MV-1079, Beat B step 2: an additive burst about 3 m across at the door, the instant
        /// it opens.</summary>
        private void PlayExitBeatBurst()
        {
            if (_exitBeatBurstRing == null) _exitBeatBurstRing = GroundRing.Create("MV-1079 Exit Burst", additive: true);
            _exitBeatBurstRing.Show(_exitBeatDoorPosition, ExitBeatBurstRadius, ExitBeatColor());
        }

        private void UpdateExitBeatBurst()
        {
            if (_exitBeatBurstRing == null || !_exitBeatBurstRing.Visible) return;
            float sinceOpen = _exitBeatTime - ExitBeatDoorOpenTime;
            float t = Mathf.Clamp01(sinceOpen / ExitBeatBurstFadeSeconds);
            if (t >= 1f) { _exitBeatBurstRing.Hide(); return; }
            Color c = ExitBeatColor();
            c.a = 1f - t;
            _exitBeatBurstRing.Show(_exitBeatDoorPosition, ExitBeatBurstRadius, c);
        }

        /// <summary>MV-1079, Beat B step 3: the centre banner reads "EXIT OPEN" from the instant the door
        /// opens (1.00 s) to 2.40 s.</summary>
        private void UpdateExitBeatBanner()
        {
            const float BannerStart = ExitBeatDoorOpenTime;
            const float BannerEnd = 2.4f;
            if (_exitBeatTime < BannerStart || _exitBeatTime > BannerEnd)
            {
                _banner?.Hide();
                return;
            }

            if (_banner == null) _banner = FinaleBanner.Create();
            _banner.Show("EXIT OPEN", string.Empty, 1f);
        }

        private void HideExitBeatVisuals()
        {
            _exitBeatBurstRing?.Hide();
            _banner?.Hide();
        }

        /// <summary>The new primary's own glow colour — the bolt and the door-open burst both play in it
        /// (ticket: "in the new weapon's glow colour"). Falls back to the LPPE/RCDA lens family
        /// (<see cref="MaxRig.LensGlass"/>) for a primary with no distinct glow of its own.</summary>
        private static Color ExitBeatColor() => MaxRig.LensGlass;

        /// <summary>MV-1013: the last world's own crossing check — every OTHER world hands this job to
        /// <see cref="WorldJoinSequence"/>'s own <c>AwaitingCrossing</c> phase (armed via <see cref="_entry"/>
        /// in <see cref="Open"/>), so this only ever runs when <see cref="_entry"/> is null and there is
        /// a real doorway (<see cref="_doorway"/>) to watch. Raises the same
        /// <see cref="HudSignals.FinaleGateCrossed"/> signal <c>WorldJoinSequence</c> raises once its
        /// corridor walk ends, so <c>RunTracker</c> cannot tell the two cases apart.</summary>
        private void Update()
        {
            if (_weaponBeatActive) TickWeaponBeat(Time.unscaledDeltaTime);
            if (_cleanupActive && !IsOpen) TickCleanup();
            if (_exitBeatActive) TickExitBeat(Time.unscaledDeltaTime);

            if (_entry != null || _crossed || !IsOpen || !_doorway.HasValue) return;

            Transform max = MaxTransform();
            if (max == null) return;

            ExitDoorway d = _doorway.Value;
            Vector2 doorCenter = d.AlongX ? new Vector2(d.Hole.Mid, d.Coord) : new Vector2(d.Coord, d.Hole.Mid);
            if (!IsBeyondFence(max.position, d.Wall, d.Coord, doorCenter, d.Hole.Length * 0.5f)) return;

            _crossed = true;
            HudSignals.EmitFinaleGateCrossed();
        }

        private Transform MaxTransform()
        {
            if (_max == null)
            {
                GameObject g = GameObject.FindGameObjectWithTag("Player");
                if (g != null) _max = g.transform;
            }
            return _max;
        }

        /// <summary>True once <paramref name="pos"/> is standing past a wall's own doorway — within its
        /// half-width of the door's centre and beyond the wall's own line, outward. Pure so a test (or
        /// <see cref="WorldJoinSequence"/>'s own crossing check) can drive the geometry directly, no
        /// scene required (MV-915 AC5; generalised to any wall, MV-964).</summary>
        public static bool IsBeyondFence(Vector3 pos, Wall wall, float wallCoord, Vector2 doorCenter, float halfWidth)
        {
            switch (wall)
            {
                case Wall.N: return pos.z >= wallCoord && Mathf.Abs(pos.x - doorCenter.x) <= halfWidth;
                case Wall.S: return pos.z <= wallCoord && Mathf.Abs(pos.x - doorCenter.x) <= halfWidth;
                case Wall.E: return pos.x >= wallCoord && Mathf.Abs(pos.z - doorCenter.y) <= halfWidth;
                case Wall.W: return pos.x <= wallCoord && Mathf.Abs(pos.z - doorCenter.y) <= halfWidth;
                default: return false;
            }
        }
    }
}
