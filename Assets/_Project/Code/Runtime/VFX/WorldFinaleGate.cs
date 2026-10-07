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
using MaxWorlds.Pickups;
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
    /// final area is dead too (see <see cref="OnWeaponCoreCollected"/>/<see cref="TickCleanup(float)"/>)
    /// — MV-1122's own four clean-up safety nets (see that ticket's own doc block, just above
    /// <see cref="NoProgressSeconds"/>) are what stop one stuck robot stalling the ending forever, the
    /// actual risk the 2026-09-26 ruling this reverses was guarding against.
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

        /// <summary>MV-1129: the most recently <see cref="Awake"/>-run gate — read by
        /// <see cref="MaxWorlds.Save.SaveSystem.CaptureCheckpoint"/> the same static-only way it reads
        /// every other checkpoint field (<c>BossCensus</c>, <c>FactoryCensus</c>, ...), since this class
        /// keeps no running tally of its own otherwise. Cleared in <see cref="OnDestroy"/>.</summary>
        public static WorldFinaleGate Active { get; private set; }

        /// <summary>The gate's own resolved state — open once this area's own final boss has actually
        /// died, not an authored flag (MV-915 AC4; MV-956 changed the trigger from RunComplete).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>MV-1129: true once this finale has moved past its weapon moment — the Core collected
        /// and, if there is a next world, its morph already applied (<see cref="TickWeaponBeat"/>) —
        /// into clean-up or later. Not simply <c>_cleanupActive || IsOpen</c>: <see cref="TickCleanup(float)"/>
        /// can hand straight off to <see cref="BeginExitBeat"/> inside the very same call, with no frame
        /// where <c>_cleanupActive</c> is true and <see cref="IsOpen"/> isn't yet — <c>_exitBeatActive</c>
        /// covers exactly that gap. What <see cref="MaxWorlds.Save.SaveSlotData.CheckpointFinaleWeaponGranted"/>
        /// persists.</summary>
        public bool WeaponMomentResolved => _cleanupActive || _exitBeatActive || IsOpen;

        /// <summary>MV-1122 test seam: whether Beat B (EXIT OPEN) has started — distinct from
        /// <see cref="IsOpen"/>, which only flips true <see cref="ExitBeatDoorOpenTime"/> seconds into
        /// that beat. What "the exit beat has begun" (the clean-up safety nets' own AC wording) means.</summary>
        public bool ExitBeatActive => _exitBeatActive;

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

        /// <summary>MV-1129: guards <see cref="ResumeFromCheckpoint"/> to exactly once per gate.</summary>
        private bool _resumedFromCheckpoint;

        /// <summary>MV-1078: true from the moment the Core is collected until the final area's last
        /// living robot is dead and the exit actually opens.</summary>
        private bool _cleanupActive;

        /// <summary>MV-1078: the final area index (<c>cfg.dials.areaCount</c>), resolved fresh in
        /// <see cref="BeginCleanup"/> rather than reused from <see cref="Awake"/>'s own geometry (null in
        /// the geometry-independent tests, same reasoning as <see cref="IsFinalBossAreaDefeat"/>).</summary>
        private int _finalAreaIndex;

        private int _lastRobotsLeft;

        // ---------------------------------------------------------------- MV-1122: clean-up safety nets
        //
        // Four independent layers so a robot the player can never deal with can never hold the exit
        // shut (Lee, 2026-10-07): (1) BigBermudaBoss.LandInFlightAdds lands an airborne add the instant
        // its boss dies, so nothing can stay suspended with its brain off forever. (2) TickUnreachableRemoval
        // removes a counted robot once a second if it's disabled/inactive, stuck off the floor, outside
        // the area's own footprint, or has had no route to Max for 2s. (3) TickNoProgressThinning — the
        // old 25s all-at-once CleanupFailsafeSeconds is GONE; after NoProgressSeconds (10s) with no
        // genuine kill, the remainder is thinned one every NoProgressThinInterval (0.3s). (4) the hard
        // HardLimitSeconds (45s) ceiling clears everyone left and opens the exit regardless. All four
        // are driven off _cleanupClock, an explicit elapsed-seconds accumulator advanced by TickCleanup's
        // own dt argument — never Time.unscaledTime directly — so an EditMode test can drive 45+ seconds
        // of clean-up deterministically in a tight loop, the same "a Tick(dt) the test can drive"
        // contract TickWeaponBeat/TickExitBeat already give their own beats.

        private const float NoProgressSeconds = 10f; // was CleanupFailsafeSeconds's 25s, single-shot
        private const float NoProgressThinInterval = 0.3f;
        private const float HardLimitSeconds = 45f;
        private const float UnreachableCheckInterval = 1f;
        private const float OffFloorTolerance = 0.5f;
        private const float OffFloorGraceSeconds = 1f;
        private const float NoRouteGraceSeconds = 2f;

        private const float CleanupRingRadius = 0.9f;
        private static readonly Color CleanupRingColor = new Color(1f, 0.231f, 0.188f, 0.9f); // #FF3B30 @ .9
        private static readonly Color CleanupBorderColor = new Color(1f, 0.231f, 0.188f);      // #FF3B30
        private const int MaxEdgeArrows = 6;

        private float _cleanupClock;
        private float _lastProgressClock;
        private float _lastThinClock;
        private float _lastUnreachableCheckClock;

        /// <summary>Robots currently counted toward clean-up — alive, stamped with the final area, and
        /// standing inside its own footprint. Rebuilt from scratch every <see cref="TickCleanup(float)"/>
        /// (same "scan FindObjectsByType, never RobotEnemy.Active" reasoning as <see cref="AreaAccumulationDirector.ActiveCountForArea"/>'s
        /// own doc comment — OnEnable never runs outside Play mode), so a removal this method makes is
        /// only ever a same-tick bookkeeping shortcut; the next refresh would exclude it anyway.</summary>
        private readonly List<RobotEnemy> _cleanupRobots = new List<RobotEnemy>(16);

        private readonly Dictionary<RobotEnemy, GroundRing> _cleanupRings = new Dictionary<RobotEnemy, GroundRing>();
        private readonly List<EdgeArrow> _cleanupArrows = new List<EdgeArrow>(MaxEdgeArrows);
        private readonly Dictionary<RobotEnemy, float> _offFloorSinceClock = new Dictionary<RobotEnemy, float>();
        private readonly Dictionary<RobotEnemy, float> _noRouteSinceClock = new Dictionary<RobotEnemy, float>();

        /// <summary>Robots this tick's own forced removal (footprint violation, unreachable, thinning,
        /// hard limit) has already killed — consumed once, at the top of the NEXT <see cref="TickCleanup(float)"/>,
        /// to tell a genuine player kill (resets <see cref="_lastProgressClock"/>) apart from one of
        /// clean-up's own safety-net kills (must NOT read as progress, or the no-progress thinner would
        /// perpetually postpone itself on the very kills it just made).</summary>
        private readonly HashSet<RobotEnemy> _forcedKillsPending = new HashSet<RobotEnemy>();

        /// <summary>MV-1122 test/HUD seam: every ring currently marking a counted robot, keyed by the
        /// robot it follows — resolved runtime state (position, scale), not a reflected field.</summary>
        public IReadOnlyDictionary<RobotEnemy, GroundRing> CleanupRings => _cleanupRings;

        /// <summary>MV-1122 test seam: how many off-screen edge arrows are showing right now.</summary>
        public int ActiveEdgeArrowCount { get; private set; }

        // ---------------------------------------------------------------- MV-1079: Beat A, WEAPON TAKEN

        private const float WeaponBeatDuration = 2.5f;
        private const float WeaponBeatMorphTime = 0.5f;

        /// <summary>MV-1131: the flare is now a <see cref="CameraFacingFlare"/> (was a flat
        /// <see cref="GroundRing"/> that never faced the camera — Lee's device observation, "my weapon
        /// changed with no indication"), 2.5 m across for 0.25 s.</summary>
        private const float WeaponBeatFlashEnd = WeaponBeatMorphTime + 0.25f;
        private const float WeaponBeatFlashDiameter = 2.5f;

        /// <summary>MV-1131: the Core's own 0.0-0.4 s flight arc into the gadget — the class doc's "the
        /// doc comment says the Core flies into the gadget; there is no code that does it" gap.</summary>
        private const float WeaponBeatCoreFlightDuration = 0.4f;
        private const float WeaponBeatCoreFlightArcHeight = 1.5f;
        private const float WeaponBeatCoreFlightStartDiameter = 1f;
        private const float WeaponBeatCoreFlightEndScale = 0.3f;

        /// <summary>MV-1131: the camera pushes in 20% toward Max, 0.0-0.5 s ease out, holds, eases back
        /// 2.1-2.5 s — <see cref="FixedAngleCameraRig.Distance"/> is "the follow distance (or equivalent
        /// zoom value)" the ticket's own AC names.</summary>
        private const float WeaponBeatZoomPushEnd = 0.5f;
        private const float WeaponBeatZoomHoldEnd = 2.1f;
        private const float WeaponBeatZoomPushFraction = 0.2f;

        /// <summary>MV-1131: eight additive shards radiate from the gun, 0.8-1.8 m long, 0.35 s.</summary>
        private const float WeaponBeatShardDuration = 0.35f;
        private const float WeaponBeatShardMinLength = 0.8f;
        private const float WeaponBeatShardMaxLength = 1.8f;

        private const float WeaponBeatRingStart = 0.5f;
        private const float WeaponBeatRingEnd = 1.3f;
        private const float WeaponBeatRingMaxRadius = 3.2f; // MV-1131: was 3f ("6 m across"), now 6.4 m
        private const float WeaponBeatRingOuterMaxRadius = 4f; // MV-1131: the new fainter second ring
        private const float WeaponBeatBannerStart = 0.6f;
        private const float WeaponBeatBannerFadeInEnd = 0.8f;   // start + 0.2 s
        private const float WeaponBeatBannerFadeOutStart = 2.2f; // end - 0.3 s

        private static readonly Color WeaponBeatGlowColor = new Color(0.85f, 0.98f, 1f);

        private bool _weaponBeatActive;
        private float _weaponBeatTime;
        private bool _weaponBeatMorphApplied;
        private int _weaponBeatPlayedWorld;
        private Vector3 _weaponBeatCoreOrigin;
        private CameraFacingFlare _weaponBeatFlashFlare;
        private CameraFacingFlare _weaponBeatCoreFlare;
        private ShardBurst _weaponBeatShards;
        private GroundRing _weaponBeatRing;
        private GroundRing _weaponBeatOuterRing;
        private FixedAngleCameraRig _weaponBeatCameraRig;
        private float _weaponBeatCameraRestDistance = -1f;

        // ---------------------------------------------------------------- MV-1131: the Core-on-ground beacon

        private const float CoreBeaconPillarWidth = 0.5f;
        private const float CoreBeaconPillarHeight = 6f;
        private const float CoreBeaconRingRadius = 1.2f;
        private static readonly Color CoreBeaconPillarColor = new Color(0.85f, 0.98f, 1f);
        private static readonly Color CoreBeaconRingColor = new Color(0.4f, 0.95f, 1f);

        private bool _coreBeaconActive;
        private Vector3 _lastCorePosition;
        private LightPillar _coreBeaconPillar;
        private GroundRing _coreBeaconRing;

        // ---------------------------------------------------------------- MV-1079: Beat B, EXIT OPEN

        private const float ExitBeatDuration = 3.0f;
        private const float ExitBeatDoorOpenTime = 1.0f;
        private const float ExitBeatHoldUntil = 2.2f;
        private const float ExitBeatBurstFadeSeconds = 0.3f;
        private const float ExitBeatBurstDiameter = 1.5f; // MV-1125: a camera-facing flare, not a flat disc
        private const float ExitBeatBoltSpeedFloor = 1f;

        private bool _exitBeatActive;
        private float _exitBeatTime;
        private bool _exitBeatDoorOpened;
        private Vector3 _exitBeatDoorPosition;
        private Vector3 _exitBeatWedgeDirection;
        private CameraTargetRig _exitBeatCameraRig;
        private SentinelBolt _exitBeatBolt;
        private CameraFacingFlare _exitBeatBurstFlare;

        private FinaleBanner _banner;
        private readonly List<RobotEnemy> _frozenRobots = new List<RobotEnemy>(16);
        private PlayerController _frozenPlayer;

        // ---------------------------------------------------------------- MV-1125: the door's own jamb
        // lamp (red while shut, green once open), the floor light wedge that marks the open doorway, and
        // the post-beat chevron trail leading Max to it.

        private const float ExitLampDiameter = 0.38f;
        private static readonly Color ExitLampLockedColor = new Color(0.90f, 0.15f, 0.10f);
        private static readonly Color ExitLampOpenColor = new Color(0.208f, 0.878f, 0.420f); // #35E06B
        private static readonly int ExitLampColorId = Shader.PropertyToID("_BaseColor");

        private GameObject _exitLampGo;
        private MeshRenderer _exitLampRenderer;
        private MaterialPropertyBlock _exitLampMpb;

        private const float ExitWedgeNearWidth = 3f;
        private const float ExitWedgeFarWidth = 6f;
        private const float ExitWedgeLength = 7f;
        private static readonly Color ExitWedgeColor = new Color(1f, 0.92f, 0.78f, 0.35f);
        private GroundWedge _exitWedge;

        private const float ExitTrailStepDistance = 2f;
        private const int ExitTrailMaxChevrons = 12;
        private static readonly Color ExitTrailChevronColor = new Color(0.616f, 1f, 0.710f, 0.9f); // #9DFFB5 @ .9
        private static readonly Color ExitTrailBorderColor = new Color(0.208f, 0.878f, 0.420f); // #35E06B

        private bool _trailActive;
        private Vector3 _trailDoorPosition;
        private readonly List<GroundChevron> _trailChevrons = new List<GroundChevron>(ExitTrailMaxChevrons);

        /// <summary>MV-1125 test seam: how many chevrons the trail is currently showing.</summary>
        public int ExitTrailChevronCount { get; private set; }

        /// <summary>MV-1125 test seam: the i'th live chevron's own resolved world position.</summary>
        public Vector3 ExitTrailChevronPosition(int i) => _trailChevrons[i].transform.position;

        /// <summary>MV-1125 test seam: the i'th live chevron's own resolved facing.</summary>
        public Vector3 ExitTrailChevronForward(int i) => _trailChevrons[i].transform.forward;

        /// <summary>MV-1125 test seam: the exit door lamp's own resolved rendered colour, read back off
        /// its renderer's MaterialPropertyBlock — not a cached field copy of whichever constant last set
        /// it.</summary>
        public Color ExitDoorLampColor
        {
            get
            {
                if (_exitLampRenderer == null) return default;
                var mpb = new MaterialPropertyBlock();
                _exitLampRenderer.GetPropertyBlock(mpb);
                return mpb.GetColor(ExitLampColorId);
            }
        }

        private void Awake()
        {
            Active = this;

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

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        private void OnEnable()
        {
            HudSignals.BossDefeated += OnBossDefeated;
            HudSignals.WeaponCoreCollected += OnWeaponCoreCollected;
            HudSignals.WeaponCoreDropped += OnWeaponCoreDropped;
            HudSignals.FinaleGateCrossed += OnFinaleGateCrossedForTrail;
        }

        private void OnDisable()
        {
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.WeaponCoreCollected -= OnWeaponCoreCollected;
            HudSignals.WeaponCoreDropped -= OnWeaponCoreDropped;
            HudSignals.FinaleGateCrossed -= OnFinaleGateCrossedForTrail;

            // MV-1079: a gate torn down mid-beat (scene reload, test teardown) must not leak its own
            // scratch VFX/UI or leave gameplay stuck suspended.
            if (_weaponBeatActive || _exitBeatActive) RestoreGameplayForBeat();
            HideWeaponBeatVisuals();
            HideExitBeatVisuals();
            HideCoreBeacon();
            HideCleanupVisuals();
            if (_exitLampGo != null) _exitLampGo.SetActive(false);
            _exitWedge?.Hide();
            foreach (GroundChevron c in _trailChevrons) c?.Hide();
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

        /// <summary>MV-1129: seeds this freshly-rebuilt gate from an already-restored checkpoint — call
        /// once, right after <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> has already run
        /// <c>BossCensus.ApplyCheckpointDefeatedAreas</c>, so <see cref="BossCensus.IsAreaDefeated"/>
        /// below reads the just-restored set.
        ///
        /// A cold-boot RESUME rebuilds this gate fresh with no memory that its own final boss ever died:
        /// <see cref="OnBossDefeated"/>'s own live trigger never fires for a restored defeat (see that
        /// census method's own doc comment — "this is restoring history, not scoring a fresh kill"), so
        /// without this call <see cref="_finalBossDefeated"/> stays false forever and the Weapon Core
        /// this area already dropped — collected or not before the app closed — never resumes anything.
        ///
        /// A no-op unless this gate's own final area is recorded defeated: nothing to resume into
        /// otherwise, and the ordinary live BossDefeated -&gt; Core -&gt; clean-up -&gt; exit chain runs
        /// exactly as before this ticket. Idempotent — a second call does nothing once the first already
        /// ran.</summary>
        /// <param name="weaponGranted">The checkpoint's own <c>CheckpointFinaleWeaponGranted</c> — true
        /// once the Core had already been collected (and, if there is a next world, its morph already
        /// applied) by the time the checkpoint was captured.</param>
        /// <param name="exitOpen">The checkpoint's own <c>CheckpointFinaleExitOpen</c>.</param>
        public void ResumeFromCheckpoint(bool weaponGranted, bool exitOpen)
        {
            if (_resumedFromCheckpoint) return;
            if (_cfg?.dials == null) return;

            int finalAreaIndex = _cfg.dials.areaCount;
            if (finalAreaIndex <= 0 || !BossCensus.IsAreaDefeated(finalAreaIndex)) return;

            _resumedFromCheckpoint = true;
            _finalBossDefeated = true;

            if (exitOpen) { Open(); return; }
            if (weaponGranted) { BeginCleanup(); return; }

            PickupDirector.EnsureInstalled().SpawnWeaponCore(ResolveFinalBossPosition(finalAreaIndex));
        }

        /// <summary>Where <see cref="ResumeFromCheckpoint"/> drops a resume-spawned Weapon Core: the
        /// final area's own boss, still standing Dormant in the rebuilt scene — <c>BossCensus.IsAreaDefeated</c>
        /// is seeded before any boss's own <c>Wake()</c> ever runs, so it silently self-destructs without
        /// ever registering (see <c>BigBermudaBoss.Wake</c>'s own doc comment) rather than vanishing the
        /// instant the scene builds, leaving its GameObject and position right there to read. Falls back
        /// to the door position for a geometry-independent fixture with no boss built at all, same
        /// fallback <see cref="ResolveDoorPosition"/> itself uses.</summary>
        private Vector3 ResolveFinalBossPosition(int areaIndex)
        {
            foreach (BigBermudaBoss boss in FindObjectsByType<BigBermudaBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (boss != null && IsInArea(boss.transform.position, areaIndex))
                    return boss.transform.position + Vector3.up * 1.4f;
            return ResolveDoorPosition();
        }

        /// <summary>MV-1078: the orb is what starts the WEAPON TAKEN/CLEAN-UP beat — never before the
        /// final boss has actually died (a Weapon Core only ever drops off that death, but this guard
        /// keeps the two concerns independent), and never twice for the same finale. MV-1079: the beat
        /// that actually starts here is now Beat A (<see cref="BeginWeaponBeat"/>), the player-visible
        /// "orb becomes the weapon" moment — CLEAN-UP itself only begins once that beat finishes.</summary>
        private void OnWeaponCoreCollected()
        {
            if (!_finalBossDefeated || _cleanupActive || _weaponBeatActive || IsOpen) return;
            HideCoreBeacon();
            BeginWeaponBeat();
        }

        /// <summary>MV-1131, Change B: while the Weapon Core waits on the ground — a camera-facing light
        /// pillar above it, a pulsing ground ring under it, and the objective strip reading "TAKE THE
        /// CORE" with a cyan border. Starts the instant <see cref="PickupDirector.SpawnWeaponCore"/> drops
        /// it (every world's finale, including a resume-spawned Core), ends the instant it's collected
        /// (<see cref="OnWeaponCoreCollected"/>/<see cref="HideCoreBeacon"/>).</summary>
        private void OnWeaponCoreDropped()
        {
            _coreBeaconActive = true;
            Pickup core = FindLiveWeaponCore();
            if (core != null) _lastCorePosition = core.transform.position;
            HudSignals.EmitObjective("TAKE THE CORE", CoreBeaconRingColor);
        }

        private static Pickup FindLiveWeaponCore()
        {
            foreach (Pickup p in FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                if (p != null && p.Kind == PickupKind.WeaponCore) return p;
            return null;
        }

        /// <summary>Tracks the live Weapon Core's own position every frame the beacon is up — both to
        /// place the pillar/ring where the Core actually is (it bobs, <see cref="Pickup"/>'s own float)
        /// and so <see cref="_lastCorePosition"/> is the real drop-off point for Beat A's own flight arc
        /// (<see cref="UpdateWeaponBeatCoreFlight"/>) even if nothing ticked <see cref="Update"/> between
        /// the drop and the collect (a scripted test driving the signals directly, with no frames).</summary>
        private void UpdateCoreBeacon()
        {
            if (!_coreBeaconActive) return;

            Pickup core = FindLiveWeaponCore();
            if (core == null) return; // collected already -- OnWeaponCoreCollected owns tearing this down

            _lastCorePosition = core.transform.position;

            if (_coreBeaconPillar == null) _coreBeaconPillar = LightPillar.Create("MV-1131 Core Pillar");
            if (_coreBeaconRing == null) _coreBeaconRing = GroundRing.Create("MV-1131 Core Ring", additive: true);

            _coreBeaconPillar.Show(_lastCorePosition, CoreBeaconPillarWidth, CoreBeaconPillarHeight, CoreBeaconPillarColor);

            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 3f);
            Color ringColor = CoreBeaconRingColor;
            ringColor.a *= pulse;
            _coreBeaconRing.Show(_lastCorePosition, CoreBeaconRingRadius, ringColor);
        }

        private void HideCoreBeacon()
        {
            _coreBeaconActive = false;
            _coreBeaconPillar?.Hide();
            _coreBeaconRing?.Hide();
            HudSignals.EmitObjective(null, default);
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
            _cleanupClock = 0f;
            _lastProgressClock = 0f;
            _lastThinClock = -NoProgressThinInterval;
            _lastUnreachableCheckClock = -UnreachableCheckInterval;
            _cleanupRobots.Clear();
            _cleanupRings.Clear();
            _offFloorSinceClock.Clear();
            _noRouteSinceClock.Clear();
            _forcedKillsPending.Clear();
            TickCleanup(0f);
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
            _weaponBeatCoreOrigin = _lastCorePosition;
            SuspendGameplayForBeat();

            _weaponBeatCameraRig = FindFirstObjectByType<FixedAngleCameraRig>();
            _weaponBeatCameraRestDistance = _weaponBeatCameraRig != null ? _weaponBeatCameraRig.Distance : -1f;

            FindFirstObjectByType<HudController>()?.SetControlsHidden(true);
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

            UpdateWeaponBeatCoreFlight();
            UpdateWeaponBeatZoom();
            UpdateWeaponBeatFlash();
            UpdateWeaponBeatShards();
            UpdateWeaponBeatRing();
            UpdateWeaponBeatBanner();

            if (_weaponBeatTime >= WeaponBeatDuration)
            {
                _weaponBeatActive = false;
                HideWeaponBeatVisuals();
                RestoreGameplayForBeat();

                // MV-1131: restore exactly, rather than trust the ease-back curve's own last step to
                // land on it -- the AC's "back within 1%" deserves exact, not "close enough by construction".
                if (_weaponBeatCameraRig != null && _weaponBeatCameraRestDistance >= 0f)
                    _weaponBeatCameraRig.SetDistance(_weaponBeatCameraRestDistance);
                _weaponBeatCameraRig = null;
                _weaponBeatCameraRestDistance = -1f;

                FindFirstObjectByType<HudController>()?.SetControlsHidden(false);
                BeginCleanup();
            }
        }

        /// <summary>MV-1131, Change C row 1: the Core flies from where it was to the gun on a 1.5 m arc,
        /// shrinking to 30%, 0.0-0.4 s — reuses <see cref="BossVictoryPayoff.Arc"/>'s own pure hop curve
        /// rather than a second copy of the same maths.</summary>
        private void UpdateWeaponBeatCoreFlight()
        {
            if (_weaponBeatTime > WeaponBeatCoreFlightDuration)
            {
                _weaponBeatCoreFlare?.Hide();
                return;
            }

            float t = Mathf.Clamp01(_weaponBeatTime / WeaponBeatCoreFlightDuration);
            Vector3 pos = BossVictoryPayoff.Arc(_weaponBeatCoreOrigin, GadgetPosition(), t, WeaponBeatCoreFlightArcHeight);
            float diameter = Mathf.Lerp(
                WeaponBeatCoreFlightStartDiameter,
                WeaponBeatCoreFlightStartDiameter * WeaponBeatCoreFlightEndScale,
                t);

            if (_weaponBeatCoreFlare == null) _weaponBeatCoreFlare = CameraFacingFlare.Create("MV-1131 Core Flight");
            _weaponBeatCoreFlare.Show(pos, diameter, WeaponBeatGlowColor);
        }

        /// <summary>MV-1131, Change C row 2: the camera pushes in 20% toward Max, 0.0-0.5 s ease out,
        /// holds, eases back 2.1-2.5 s — <see cref="FixedAngleCameraRig.SetDistance"/> is the same knob
        /// <see cref="MaxWorlds.CameraRig.TeleportZoomController"/> already drives for its own zoom beat.</summary>
        private void UpdateWeaponBeatZoom()
        {
            if (_weaponBeatCameraRig == null || _weaponBeatCameraRestDistance < 0f) return;

            // MV-1131: "pushes in 20%" reads as a 20% bigger apparent zoom, i.e. distance / 1.2 (~0.833x)
            // — not distance * 0.8 — matching the AC's own 0.83 (+-0.03) ratio with room either side of
            // it, rather than sitting right on the tolerance's edge.
            float pushedDistance = _weaponBeatCameraRestDistance / (1f + WeaponBeatZoomPushFraction);
            float t;
            if (_weaponBeatTime <= WeaponBeatZoomPushEnd)
                t = AnimSequence.OutQuad(Mathf.Clamp01(_weaponBeatTime / WeaponBeatZoomPushEnd));
            else if (_weaponBeatTime <= WeaponBeatZoomHoldEnd)
                t = 1f;
            else
                t = 1f - Mathf.Clamp01((_weaponBeatTime - WeaponBeatZoomHoldEnd) / (WeaponBeatDuration - WeaponBeatZoomHoldEnd));

            _weaponBeatCameraRig.SetDistance(Mathf.Lerp(_weaponBeatCameraRestDistance, pushedDistance, t));
        }

        /// <summary>MV-1131, Change C row 3 (the flare's own shards): eight additive shards radiate from
        /// the gun, 0.8-1.8 m long, for 0.35 s from the same 0.5 s instant the flare and the morph both
        /// land.</summary>
        private void UpdateWeaponBeatShards()
        {
            float shardEnd = WeaponBeatMorphTime + WeaponBeatShardDuration;
            if (_weaponBeatTime < WeaponBeatMorphTime || _weaponBeatTime > shardEnd)
            {
                _weaponBeatShards?.Hide();
                return;
            }

            if (_weaponBeatShards == null) _weaponBeatShards = ShardBurst.Create("MV-1131 Weapon Shards");
            float t = Mathf.InverseLerp(WeaponBeatMorphTime, shardEnd, _weaponBeatTime);
            _weaponBeatShards.Show(GadgetPosition(), WeaponBeatShardMinLength, WeaponBeatShardMaxLength,
                0.08f, WeaponBeatGlowColor, 1f - t);
        }

        /// <summary>MV-1131, Change C row 3: a camera-facing additive flare 2.5 m across at the gadget,
        /// 0.50-0.75 s — the morph itself already applied by the time this window opens. Was a flat
        /// <see cref="GroundRing"/> that never faced the camera (the class doc's own "not facing the
        /// camera" observation) — now a <see cref="CameraFacingFlare"/>.</summary>
        private void UpdateWeaponBeatFlash()
        {
            if (_weaponBeatTime < WeaponBeatMorphTime || _weaponBeatTime > WeaponBeatFlashEnd)
            {
                _weaponBeatFlashFlare?.Hide();
                return;
            }

            if (_weaponBeatFlashFlare == null) _weaponBeatFlashFlare = CameraFacingFlare.Create("MV-1131 Weapon Flash");
            float t = Mathf.InverseLerp(WeaponBeatMorphTime, WeaponBeatFlashEnd, _weaponBeatTime);
            Color c = WeaponBeatGlowColor; c.a = 1f - t;
            _weaponBeatFlashFlare.Show(GadgetPosition(), WeaponBeatFlashDiameter, c);
        }

        /// <summary>MV-1131, Change C row 4: a cyan ground ring at Max's feet grows from 0.1 to 3.2 m
        /// radius with a fainter second ring out to 4 m, 0.50-1.30 s. Visual only — no gameplay effect.</summary>
        private void UpdateWeaponBeatRing()
        {
            if (_weaponBeatTime < WeaponBeatRingStart || _weaponBeatTime > WeaponBeatRingEnd)
            {
                _weaponBeatRing?.Hide();
                _weaponBeatOuterRing?.Hide();
                return;
            }

            Transform max = MaxTransform();
            if (max == null) return;

            if (_weaponBeatRing == null) _weaponBeatRing = GroundRing.Create("MV-1079 Weapon Ring", additive: true);
            if (_weaponBeatOuterRing == null) _weaponBeatOuterRing = GroundRing.Create("MV-1131 Weapon Ring Outer", additive: true);

            float t = Mathf.InverseLerp(WeaponBeatRingStart, WeaponBeatRingEnd, _weaponBeatTime);
            float radius = Mathf.Lerp(0.1f, WeaponBeatRingMaxRadius, t);
            float outerRadius = Mathf.Lerp(0.1f, WeaponBeatRingOuterMaxRadius, t);
            Color c = new Color(0.4f, 0.95f, 1f, 1f - t);
            Color outerC = new Color(0.4f, 0.95f, 1f, (1f - t) * 0.4f);
            _weaponBeatRing.Show(max.position, radius, c);
            _weaponBeatOuterRing.Show(max.position, outerRadius, outerC);
        }

        /// <summary>MV-1131, Change C row 6: the centre banner — "NEW WEAPON" small (cyan), the new
        /// primary's own SHORT banner name large (white), one line of what it does (light grey) —
        /// 0.60-2.50 s, fading in over 0.2 s and out over the last 0.3 s. <see cref="BannerCopyFor"/>'s
        /// short name is deliberately NOT <see cref="WeaponCatalog.ShortName"/> ("LPPE") — the ticket's
        /// own copy ("PULSE EMITTER") is what reads as a weapon name at this size, "LPPE" does not.</summary>
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
            (string shortName, string effectLine) = BannerCopyFor(WeaponSystemState.ActivePrimary);
            _banner.Show("NEW WEAPON", shortName, effectLine, alpha);
        }

        /// <summary>The new-weapon banner's own short name + one-line effect description (ticket's
        /// explicit copy table) — distinct from <see cref="WeaponCatalog.ShortName"/>/<c>EffectLine</c>,
        /// which serve the weapons screen and abilities grid respectively, neither of which this banner
        /// is.</summary>
        private static (string shortName, string effectLine) BannerCopyFor(WeaponCatalog.PrimaryKind kind) =>
            kind switch
            {
                WeaponCatalog.PrimaryKind.Lppe => ("PULSE EMITTER", "Fires pulses that home in on robots"),
                WeaponCatalog.PrimaryKind.Undertow => ("UNDERTOW", "A beam that seeks robots and grips them"),
                _ => (WeaponCatalog.ShortName(kind), string.Empty),
            };

        private void HideWeaponBeatVisuals()
        {
            _weaponBeatFlashFlare?.Hide();
            _weaponBeatRing?.Hide();
            _weaponBeatOuterRing?.Hide();
            _weaponBeatCoreFlare?.Hide();
            _weaponBeatShards?.Hide();
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

        /// <summary>MV-1078/MV-1122, CLEAN-UP: the top HUD objective strip reads "CLEAR THE AREA
        /// ROBOTS LEFT n" (n = alive robots stamped with the final area AND standing inside its own
        /// footprint — see <see cref="RefreshCleanupRobots"/>). <paramref name="dt"/>-driven, not
        /// <c>Time.unscaledTime</c> (MV-1122) — see the class's own clean-up-safety-nets doc block for
        /// why. Public — same "an EditMode test can drive this deterministically" contract
        /// <see cref="TickWeaponBeat"/>/<see cref="TickExitBeat"/> already give their own beats; a real
        /// run always arrives here through <see cref="Update"/>, which passes <c>Time.unscaledDeltaTime</c>.</summary>
        public void TickCleanup(float dt)
        {
            if (!_cleanupActive) return;
            _cleanupClock += dt;

            List<RobotEnemy> previous = new List<RobotEnemy>(_cleanupRobots);
            RefreshCleanupRobots();
            int left = _cleanupRobots.Count;

            bool realProgress = _lastRobotsLeft < 0;
            if (!realProgress)
            {
                foreach (RobotEnemy prev in previous)
                {
                    if (prev == null || _cleanupRobots.Contains(prev) || _forcedKillsPending.Contains(prev)) continue;
                    realProgress = true;
                    break;
                }
            }
            _forcedKillsPending.Clear();
            if (realProgress) _lastProgressClock = _cleanupClock;

            if (left != _lastRobotsLeft) { _lastRobotsLeft = left; EmitCleanupObjective(left); }

            if (left > 0)
            {
                if (_cleanupClock >= HardLimitSeconds)
                {
                    KillAllCleanupRobots();
                }
                else
                {
                    TickUnreachableRemoval();
                    TickNoProgressThinning();
                }
                left = _cleanupRobots.Count;
                if (left != _lastRobotsLeft) { _lastRobotsLeft = left; EmitCleanupObjective(left); }
            }

            UpdateCleanupVisuals();

            if (left <= 0)
            {
                _cleanupActive = false;
                HideCleanupVisuals();
                BeginExitBeat(ResolveDoorPosition());
            }
        }

        /// <summary>Rebuilds <see cref="_cleanupRobots"/> from the live scene — alive, stamped with the
        /// final area, and standing inside its own footprint. A robot stamped with the final area but
        /// found OUTSIDE its footprint is destroyed on the spot, no drops (MV-1122 Change 2) — never
        /// added to the list, so it never shows up as "counted" even for one frame. Scans
        /// <see cref="Object.FindObjectsByType{T}(FindObjectsInactive, FindObjectsSortMode)"/> rather
        /// than <see cref="RobotEnemy.Active"/>, same reasoning as <see cref="WakeFinalAreaRobots"/>'s
        /// own doc comment.</summary>
        private void RefreshCleanupRobots()
        {
            _cleanupRobots.Clear();
            foreach (RobotEnemy r in FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r == null || !r.IsAlive || r.AreaIndex != _finalAreaIndex) continue;

                // MV-1122: IsInArea's own established "can't tell, so no" default (_map == null) is
                // right for the no-op callers it already had; it would be wrong here, where "no" means
                // "force-kill" — a geometry-independent fixture/test with no BackyardPath (every sibling
                // finale test's own convention, see WorldFinaleGate's own class doc) would then nuke
                // every robot it holds on the very first tick. Only a real, resolved "outside" counts.
                if (_map != null && !IsInArea(r.transform.position, _finalAreaIndex))
                {
                    _forcedKillsPending.Add(r);
                    r.KillWithoutDrops();
                    continue;
                }

                _cleanupRobots.Add(r);
            }
        }

        /// <summary>MV-1122 Change 7, layer 2: once a second, removes a counted robot that cannot
        /// actually be dealt with — disabled/inactive, outside the footprint (a straggler that wandered
        /// out after being counted), stuck more than <see cref="OffFloorTolerance"/> off the floor for
        /// <see cref="OffFloorGraceSeconds"/>, or with no route to Max for <see cref="NoRouteGraceSeconds"/>.
        /// No drops — the player never actually fought it.</summary>
        private void TickUnreachableRemoval()
        {
            if (_cleanupClock - _lastUnreachableCheckClock < UnreachableCheckInterval) return;
            _lastUnreachableCheckClock = _cleanupClock;

            Transform max = MaxTransform();
            List<RobotEnemy> toRemove = null;

            foreach (RobotEnemy r in _cleanupRobots)
            {
                if (r == null) continue;

                bool disabledOrInactive = !r.enabled || !r.gameObject.activeInHierarchy;
                bool outsideFootprint = _map != null && !IsInArea(r.transform.position, _finalAreaIndex);
                if (disabledOrInactive || outsideFootprint)
                {
                    (toRemove ??= new List<RobotEnemy>()).Add(r);
                    continue;
                }

                bool offFloorExpired = TrackTimer(_offFloorSinceClock, r, IsOffFloor(r), OffFloorGraceSeconds);
                bool noRouteExpired = max != null && TrackTimer(_noRouteSinceClock, r, !HasRouteTo(r, max.position), NoRouteGraceSeconds);
                if (offFloorExpired || noRouteExpired) (toRemove ??= new List<RobotEnemy>()).Add(r);
            }

            if (toRemove == null) return;
            foreach (RobotEnemy r in toRemove) RemoveCleanupRobot(r);
        }

        /// <summary>Tracks how long <paramref name="conditionTrue"/> has held continuously for
        /// <paramref name="r"/> against <see cref="_cleanupClock"/>; clears the timer the instant the
        /// condition lifts. Returns whether <paramref name="grace"/> has now elapsed.</summary>
        private bool TrackTimer(Dictionary<RobotEnemy, float> since, RobotEnemy r, bool conditionTrue, float grace)
        {
            if (!conditionTrue) { since.Remove(r); return false; }
            if (!since.TryGetValue(r, out float first)) { since[r] = _cleanupClock; return false; }
            return _cleanupClock - first >= grace;
        }

        /// <summary>Whether <paramref name="r"/>'s own pivot sits more than <see cref="OffFloorTolerance"/>
        /// from where its kind should rest on the floor under it (<see cref="MapData.SurfaceHeightAt"/>
        /// plus its own authored <see cref="EnemyArchetype.SpawnHeight"/> ground clearance — the same
        /// offset every spawn path already adds when placing a robot). False with no map registered
        /// (nothing to measure against).</summary>
        private bool IsOffFloor(RobotEnemy r)
        {
            if (_map == null) return false;
            float floorY = _map.SurfaceHeightAt(r.transform.position);
            float expectedY = floorY + EnemyArchetype.Of(r.Kind).SpawnHeight;
            return Mathf.Abs(r.transform.position.y - expectedY) > OffFloorTolerance;
        }

        /// <summary>Whether the room graph currently has any way at all from <paramref name="r"/>'s own
        /// zone to <paramref name="targetPos"/>'s zone — a plain room-graph reachability check
        /// (<see cref="MapRoutes.Rooms"/>), not a guarantee the live route is walkable this instant; this
        /// is a safety net against a genuinely sealed-off robot, not a navigation system. True (never the
        /// reason to remove anything) with no map, or either position resolving to no zone/the same zone.</summary>
        private bool HasRouteTo(RobotEnemy r, Vector3 targetPos)
        {
            if (_map == null) return true;
            MapZone from = _map.ZoneAt(r.transform.position.x, r.transform.position.z);
            MapZone to = _map.ZoneAt(targetPos.x, targetPos.z);
            if (from == null || to == null || from == to) return true;
            return MapRoutes.Rooms(_map, from, to, EnemyNavigation.IsGateOpen).Count > 0;
        }

        /// <summary>MV-1122 Change 7, layer 3: after <see cref="NoProgressSeconds"/> with no GENUINE
        /// kill (<see cref="_lastProgressClock"/> — a safety-net removal never counts as progress, see
        /// <see cref="_forcedKillsPending"/>'s own doc comment), the remainder is thinned one every
        /// <see cref="NoProgressThinInterval"/>. Replaces the old single-shot 25s CleanupFailsafeSeconds
        /// entirely.</summary>
        private void TickNoProgressThinning()
        {
            if (_cleanupRobots.Count == 0) return;
            if (_cleanupClock - _lastProgressClock < NoProgressSeconds) return;
            if (_cleanupClock - _lastThinClock < NoProgressThinInterval) return;
            _lastThinClock = _cleanupClock;

            RemoveCleanupRobot(_cleanupRobots[0]);
        }

        /// <summary>MV-1122 Change 7, layer 4: <see cref="HardLimitSeconds"/> after clean-up began, every
        /// robot still counted is removed on this one tick and the exit beat follows immediately — the
        /// exit can therefore never stay shut more than <see cref="HardLimitSeconds"/> past the weapon
        /// beat ending, whatever layers 1-3 did or didn't manage to clear.</summary>
        private void KillAllCleanupRobots()
        {
            // A snapshot: RemoveCleanupRobot mutates _cleanupRobots as it goes.
            foreach (RobotEnemy r in new List<RobotEnemy>(_cleanupRobots)) RemoveCleanupRobot(r);
        }

        private void RemoveCleanupRobot(RobotEnemy r)
        {
            _cleanupRobots.Remove(r);
            _offFloorSinceClock.Remove(r);
            _noRouteSinceClock.Remove(r);
            if (r == null || !r.IsAlive) return;
            _forcedKillsPending.Add(r);
            r.KillWithoutDrops();
        }

        /// <summary>MV-1122 Change 4: the top objective strip — "CLEAR THE AREA   ROBOTS LEFT n" with a
        /// red (<see cref="CleanupBorderColor"/>) border, "ROBOTS LEFT n" itself in yellow via an inline
        /// rich-text tag (<see cref="HudController.ObjectiveText"/>'s own doc comment explains why an
        /// EditMode test never sees the tag). Cleared (no strip at all) the instant the count is back to
        /// 0 — Change 3's "no strip, no wait" when clean-up opens on an already-empty area.</summary>
        private void EmitCleanupObjective(int left)
        {
            if (left <= 0) { HudSignals.EmitObjective(null, default); return; }
            HudSignals.EmitObjective($"CLEAR THE AREA   <color=#FFD23C>ROBOTS LEFT {left}</color>", CleanupBorderColor);
        }

        /// <summary>MV-1122 Change 5/6: a following red ground ring (<see cref="CleanupRingRadius"/>) on
        /// every counted robot, and up to <see cref="MaxEdgeArrows"/> off-screen edge arrows for whichever
        /// of them the camera can't currently see, nearest first.</summary>
        private void UpdateCleanupVisuals()
        {
            var stillCounted = new HashSet<RobotEnemy>(_cleanupRobots);
            List<RobotEnemy> staleRings = null;
            foreach (KeyValuePair<RobotEnemy, GroundRing> kv in _cleanupRings)
            {
                if (stillCounted.Contains(kv.Key)) continue;
                kv.Value?.Hide();
                (staleRings ??= new List<RobotEnemy>()).Add(kv.Key);
            }
            if (staleRings != null) foreach (RobotEnemy r in staleRings) _cleanupRings.Remove(r);

            foreach (RobotEnemy r in _cleanupRobots)
            {
                if (!_cleanupRings.TryGetValue(r, out GroundRing ring))
                    _cleanupRings[r] = ring = GroundRing.Create($"MV-1122 Cleanup Ring {r.GetInstanceID()}");
                ring.Show(r.transform.position, CleanupRingRadius, CleanupRingColor);
            }

            Camera cam = Camera.main;
            Transform max = MaxTransform();
            Vector3 origin = max != null ? max.position : Vector3.zero;

            var offScreen = cam != null ? new List<RobotEnemy>(_cleanupRobots.Count) : null;
            if (cam != null)
            {
                foreach (RobotEnemy r in _cleanupRobots)
                {
                    Vector3 vp = cam.WorldToViewportPoint(r.transform.position);
                    bool onScreen = vp.z >= 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                    if (!onScreen) offScreen.Add(r);
                }
                offScreen.Sort((a, b) => Vector3.SqrMagnitude(a.transform.position - origin)
                    .CompareTo(Vector3.SqrMagnitude(b.transform.position - origin)));
            }

            int shown = 0;
            int available = offScreen?.Count ?? 0;
            for (; shown < available && shown < MaxEdgeArrows; shown++)
            {
                if (shown >= _cleanupArrows.Count) _cleanupArrows.Add(EdgeArrow.Create($"MV-1122 Edge Arrow {shown}"));
                _cleanupArrows[shown].Show(offScreen[shown].transform.position, cam);
            }
            for (int i = shown; i < _cleanupArrows.Count; i++) _cleanupArrows[i].Hide();

            ActiveEdgeArrowCount = shown;
        }

        private void HideCleanupVisuals()
        {
            foreach (KeyValuePair<RobotEnemy, GroundRing> kv in _cleanupRings) kv.Value?.Hide();
            _cleanupRings.Clear();
            foreach (EdgeArrow a in _cleanupArrows) a.Hide();
            ActiveEdgeArrowCount = 0;
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

            // MV-1125: the camera always travels to the door now -- a short move is still a move (Lee,
            // 2026-10-03: "the exit opening must be an event the player sees"). The old near-door skip
            // (ExitBeatNearDoorSkipDistance) meant a player already standing close to the exit saw
            // nothing happen at all.
            _exitBeatCameraRig = FindFirstObjectByType<CameraTargetRig>();

            SuspendGameplayForBeat();
            FindFirstObjectByType<HudController>()?.SetControlsHidden(true);

            // MV-1079 step 1: the bolt and its ground glow fly for exactly the travel window
            // (0.00-1.00 s). MV-1125: externallyDriven -- this bolt is ticked explicitly below
            // (_exitBeatBolt?.Tick(dt)), so its own Update() must not ALSO tick it, or it arrives in
            // roughly half the intended 1.0 s (the double-tick the ticket's own hypothesis named).
            Vector3 origin = GadgetPosition();
            float speed = Mathf.Max(ExitBeatBoltSpeedFloor, Vector3.Distance(origin, doorPosition) / ExitBeatDoorOpenTime);
            _exitBeatBolt = SentinelBolt.Fire(origin, doorPosition, speed, ExitBeatColor(), externallyDriven: true);

            Vector3 toOrigin = origin - doorPosition; toOrigin.y = 0f;
            _exitBeatWedgeDirection = toOrigin.sqrMagnitude > 1e-4f ? toOrigin.normalized : Vector3.forward;

            ShowExitDoorLamp(doorPosition, ExitLampLockedColor);
        }

        /// <summary>Advance Beat B by <paramref name="dt"/> seconds. Public — same test-driving contract
        /// as <see cref="TickWeaponBeat"/>.</summary>
        public void TickExitBeat(float dt)
        {
            if (!_exitBeatActive) return;
            _exitBeatTime += dt;

            _exitBeatBolt?.Tick(dt);

            if (_exitBeatCameraRig != null)
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
                ShowExitDoorLamp(_exitBeatDoorPosition, ExitLampOpenColor);
                ShowExitLightWedge();
            }

            UpdateExitBeatBurst();
            UpdateExitBeatBanner();

            if (_exitBeatTime >= ExitBeatDuration)
            {
                _exitBeatActive = false;
                if (_exitBeatCameraRig != null) _exitBeatCameraRig.EndFocusOverride();
                HideExitBeatVisuals();
                RestoreGameplayForBeat();
                FindFirstObjectByType<HudController>()?.SetControlsHidden(false);
                BeginExitTrail(_exitBeatDoorPosition);
            }
        }

        /// <summary>MV-1079, Beat B step 2: a camera-facing additive flare 1.5 m across at the door, the
        /// instant it opens. MV-1125: replaces the old flat <see cref="GroundRing"/> disc (not on the
        /// ground, not facing the camera, per the class doc's own observation) with a
        /// <see cref="CameraFacingFlare"/>, the same fix Beat A's own flash already got (MV-1131).</summary>
        private void PlayExitBeatBurst()
        {
            if (_exitBeatBurstFlare == null) _exitBeatBurstFlare = CameraFacingFlare.Create("MV-1125 Exit Burst Flare");
            _exitBeatBurstFlare.Show(_exitBeatDoorPosition, ExitBeatBurstDiameter, ExitBeatColor());
        }

        private void UpdateExitBeatBurst()
        {
            if (_exitBeatBurstFlare == null || !_exitBeatBurstFlare.Visible) return;
            float sinceOpen = _exitBeatTime - ExitBeatDoorOpenTime;
            float t = Mathf.Clamp01(sinceOpen / ExitBeatBurstFadeSeconds);
            if (t >= 1f) { _exitBeatBurstFlare.Hide(); return; }
            Color c = ExitBeatColor();
            c.a = 1f - t;
            _exitBeatBurstFlare.Show(_exitBeatDoorPosition, ExitBeatBurstDiameter, c);
        }

        /// <summary>MV-1079, Beat B step 3: the centre banner reads "EXIT OPEN" from the instant the door
        /// opens (1.00 s) to 2.40 s. MV-1125: now the scaled HUD canvas's own large white treatment
        /// (<see cref="FinaleBanner.ShowExitOpen"/>) rather than Line1's small cyan slot.</summary>
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
            _banner.ShowExitOpen(1f);
        }

        private void HideExitBeatVisuals()
        {
            _exitBeatBurstFlare?.Hide();
            _banner?.Hide();
        }

        /// <summary>MV-1125: the door's own jamb lamp — a small emissive sphere, red while the door is
        /// locked and green (<see cref="ExitLampOpenColor"/>) once it's open. Built lazily, moved/retinted
        /// in place thereafter (never rebuilt) so it keeps reading as the SAME fixture across the beat.</summary>
        private void ShowExitDoorLamp(Vector3 doorPosition, Color color)
        {
            if (_exitLampGo == null)
            {
                _exitLampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _exitLampGo.name = "MV-1125 Exit Door Lamp";
                Collider col = _exitLampGo.GetComponent<Collider>();
                if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }

                _exitLampRenderer = _exitLampGo.GetComponent<MeshRenderer>();
                _exitLampRenderer.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
                _exitLampRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _exitLampRenderer.receiveShadows = false;
                _exitLampGo.transform.localScale = Vector3.one * ExitLampDiameter;
                _exitLampMpb = new MaterialPropertyBlock();
            }

            if (!_exitLampGo.activeSelf) _exitLampGo.SetActive(true);
            _exitLampGo.transform.position = doorPosition + Vector3.up * 1.2f;

            _exitLampRenderer.GetPropertyBlock(_exitLampMpb);
            _exitLampMpb.SetColor(ExitLampColorId, color);
            _exitLampRenderer.SetPropertyBlock(_exitLampMpb);
        }

        /// <summary>MV-1125: the flat additive light wedge marking the open doorway — shown once, the
        /// instant the door opens, and left up (it "stays while the door is open").</summary>
        private void ShowExitLightWedge()
        {
            if (_exitWedge == null)
                _exitWedge = GroundWedge.Create("MV-1125 Exit Light Wedge", ExitWedgeNearWidth, ExitWedgeFarWidth, ExitWedgeLength);
            _exitWedge.Show(_exitBeatDoorPosition, _exitBeatWedgeDirection, ExitWedgeColor);
        }

        /// <summary>MV-1125: after the exit beat ends, until Max crosses the door line — the objective
        /// strip reads "GO TO THE EXIT" and a chevron trail leads him there (<see cref="RefreshExitTrail"/>).
        /// Ends on <see cref="HudSignals.FinaleGateCrossed"/> (<see cref="OnFinaleGateCrossedForTrail"/>),
        /// the same signal both <see cref="WorldJoinSequence"/>'s own corridor crossing and THIS class's
        /// own last-world crossing check (<see cref="Update"/>) raise, so one subscription covers both.</summary>
        private void BeginExitTrail(Vector3 doorPosition)
        {
            _trailActive = true;
            _trailDoorPosition = doorPosition;
            HudSignals.EmitObjective("GO TO THE EXIT", ExitTrailBorderColor);
            RefreshExitTrail();
        }

        /// <summary>Recomputes the chevron trail from Max's CURRENT position toward the door, stepping
        /// <see cref="ExitTrailStepDistance"/> m at a time along <see cref="EnemyNavigation.Waypoint"/> —
        /// the same "where would a robot walk" router every robot in the arena already asks, so the trail
        /// follows a real walkable route (a straight line when Max and the door are in the same room,
        /// same as <see cref="EnemyNavigation.Waypoint"/> itself resolves that case). Public — the test can
        /// drive this directly after moving Max, and <see cref="Update"/> calls it every live frame so the
        /// trail re-lays itself as he actually walks.</summary>
        public void RefreshExitTrail()
        {
            if (!_trailActive) return;

            Transform max = MaxTransform();
            Vector3 cursor = max != null ? max.position : Vector3.zero;

            int shown = 0;
            for (; shown < ExitTrailMaxChevrons; shown++)
            {
                Vector3 waypoint = EnemyNavigation.Waypoint(cursor, _trailDoorPosition);
                Vector3 delta = waypoint - cursor; delta.y = 0f;
                if (delta.magnitude <= ExitTrailStepDistance) break;

                Vector3 dir = delta.normalized;
                Vector3 next = cursor + dir * ExitTrailStepDistance;

                if (shown >= _trailChevrons.Count) _trailChevrons.Add(GroundChevron.Create($"MV-1125 Exit Chevron {shown}"));
                _trailChevrons[shown].Show(next, dir, ExitTrailChevronColor);

                cursor = next;
            }

            for (int i = shown; i < _trailChevrons.Count; i++) _trailChevrons[i].Hide();
            ExitTrailChevronCount = shown;
        }

        private void EndExitTrail()
        {
            if (!_trailActive) return;
            _trailActive = false;
            foreach (GroundChevron c in _trailChevrons) c.Hide();
            ExitTrailChevronCount = 0;
            HudSignals.EmitObjective(null, default);
        }

        private void OnFinaleGateCrossedForTrail() => EndExitTrail();

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
            UpdateCoreBeacon();
            if (_weaponBeatActive) TickWeaponBeat(Time.unscaledDeltaTime);
            // MV-1122: clamped, unlike the beats above -- TickCleanup's own safety-net thresholds span
            // real seconds of gameplay (10-45s), and an EditMode caller driving this gate through plain
            // Update() (InvokeUpdate, every sibling finale test's own idiom) can hand it an arbitrarily
            // large stray Time.unscaledDeltaTime between two synchronous statements (the editor's real
            // clock, not a simulated frame) -- unclamped, that one stray value mass-kills every counted
            // robot instantly instead of just fast-forwarding a short cosmetic beat the way the weapon/
            // exit beats above tolerate. A real 60fps frame (~0.016s) is always far under this ceiling,
            // so live gameplay is unaffected; a test wanting to drive real elapsed clean-up time still
            // does so explicitly and unclamped through the public TickCleanup(dt) itself.
            if (_cleanupActive && !IsOpen) TickCleanup(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            if (_exitBeatActive) TickExitBeat(Time.unscaledDeltaTime);
            if (_trailActive) RefreshExitTrail();

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
