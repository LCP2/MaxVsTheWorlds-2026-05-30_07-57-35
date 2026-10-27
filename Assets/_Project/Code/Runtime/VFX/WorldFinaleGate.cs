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
    /// latches <see cref="_finalBossDefeated"/>.
    ///
    /// MV-1183: Lee's device playtests (12-14 Oct) found the finale "totally broken" — MV-1122's
    /// clean-up phase (a visible count, red rings, off-screen arrows, a 45s timer), MV-1125's watched
    /// exit-open beat and ground trail, and World 1 a30's two-boss orb drop (the FIRST boss dying
    /// dropped the orb while the second sat Dormant, never registered with <c>BossCensus</c>, so
    /// <c>BossCensus.AnyLivingIn</c> alone already read the area "empty") have all been retired/fixed.
    /// The rule now (option A, "heavily simplify"): every boss in the final area must die
    /// (<see cref="IsFinalBossAreaDefeat"/> checks the AUTHORED total, <c>WorldArea.Bosses().Length</c>,
    /// not just the registered-living set) before the orb appears; collecting it
    /// (<see cref="OnWeaponCoreCollected"/>) destroys every robot still alive in the final area and
    /// opens the exit, all in the same frame, with no further clean-up/exit-beat gate and no camera or
    /// input take-over steering Max toward the door — he walks there himself.
    ///
    /// MV-1309: the City's own final area (a30) now authors a real boss (GRIDLOCK) too, so the
    /// bossless finale path MV-1179/MV-1195/MV-1198 built for it (<c>ResolveBosslessFinaleIfReady</c>,
    /// <c>TryApplyCityToReefHandover</c>, and the <c>EnemyKilled</c>/<c>FactoryDestroyed</c> hooks that
    /// fed them) is removed outright — every world's final area is boss-role now, so this class needs
    /// only the one finale path every other world already used.
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

        /// <summary>MV-1129: true once this finale has moved past its weapon moment — the Core
        /// collected and the exit opened. MV-1183: collecting the Core now destroys the final area's
        /// remaining robots and opens the door in the same call (see <see cref="OnWeaponCoreCollected"/>),
        /// so there is no longer a gap between "the Core is granted" and "the door is open" for this to
        /// cover — it is simply <see cref="IsOpen"/>. What <see cref="MaxWorlds.Save.SaveSlotData.CheckpointFinaleWeaponGranted"/>
        /// persists.</summary>
        public bool WeaponMomentResolved => IsOpen;

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
        /// <see cref="OnBossDefeated"/>, which no longer opens the exit directly. A boss can die well
        /// before Max ever walks back to collect the Core it dropped.</summary>
        private bool _finalBossDefeated;

        /// <summary>MV-1129: guards <see cref="ResumeFromCheckpoint"/> to exactly once per gate.</summary>
        private bool _resumedFromCheckpoint;

        /// <summary>MV-1195/MV-1245: whether the interim hand-over/ending banner is currently showing —
        /// see <see cref="OnHelmKeyCollected"/>.</summary>
        private bool _interimBannerActive;
        private float _interimBannerTime;

        /// <summary>MV-1195: how long the interim hand-over banner holds before fading — same figure
        /// as Beat A's own <see cref="WeaponBeatDuration"/> below, for the same read-the-banner time,
        /// without borrowing that cinematic's own camera/ring/flash machinery (there is no physical
        /// Core object for a bossless finale to fly, so Beat A's flight animation has nothing to
        /// animate here).</summary>
        private const float InterimBannerSeconds = 2.5f;

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

        private FinaleBanner _banner;
        private readonly List<RobotEnemy> _frozenRobots = new List<RobotEnemy>(16);
        private PlayerController _frozenPlayer;

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
        }

        private void OnDisable()
        {
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.WeaponCoreCollected -= OnWeaponCoreCollected;
            HudSignals.WeaponCoreDropped -= OnWeaponCoreDropped;

            // MV-1079: a gate torn down mid-beat (scene reload, test teardown) must not leak its own
            // scratch VFX/UI or leave gameplay stuck suspended.
            if (_weaponBeatActive) RestoreGameplayForBeat();
            HideWeaponBeatVisuals();
            HideCoreBeacon();
            if (_banner != null) { _banner.DestroySelf(); _banner = null; }
        }

        /// <summary>MV-956: <see cref="HudSignals.BossDefeated"/> fires for the last REGISTERED living
        /// boss of ANY area emptying (a12, a20, a30 alike) — only latch for THIS gate's own final area.
        /// MV-1183: see <see cref="IsFinalBossAreaDefeat"/> for why that alone isn't enough.</summary>
        private void OnBossDefeated()
        {
            if (_finalBossDefeated) return;
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
        /// otherwise, and the ordinary live BossDefeated -&gt; Core -&gt; door chain runs exactly as
        /// before this ticket. Idempotent — a second call does nothing once the first already ran.
        ///
        /// MV-1183: <paramref name="weaponGranted"/> and <paramref name="exitOpen"/> are now equivalent
        /// (collecting the Core opens the door in the very same call, see
        /// <see cref="OnWeaponCoreCollected"/>) — either one means the orb was already taken before the
        /// checkpoint was captured, so this restores the SAME shared step live collection uses: the
        /// final area's remaining robots destroyed and the door open, re-applied here since a cold-boot
        /// RESUME rebuilds those robots fresh from the level's own authored placements.</summary>
        /// <param name="weaponGranted">The checkpoint's own <c>CheckpointFinaleWeaponGranted</c> — true
        /// once the Core had already been collected by the time the checkpoint was captured.</param>
        /// <param name="exitOpen">The checkpoint's own <c>CheckpointFinaleExitOpen</c>.</param>
        public void ResumeFromCheckpoint(bool weaponGranted, bool exitOpen)
        {
            if (_resumedFromCheckpoint) return;
            if (_cfg?.dials == null) return;

            int finalAreaIndex = _cfg.dials.areaCount;
            if (finalAreaIndex <= 0) return;

            // MV-1179: the City's a1 has no boss, so BossCensus.IsAreaDefeated(finalAreaIndex) can
            // never read true for it -- the guard below would otherwise strand every bossless RESUME.
            // Its own door-state flags (already equivalent to IsOpen, see WeaponMomentResolved's own
            // doc) are the only signal this shape has: nothing to resume if neither was ever set, and
            // DestroyRestAndOpenDoor re-applies the shared step (destroy what's left, re-open the door)
            // when either was.
            WorldArea finalArea = _cfg.AreaByIndex(finalAreaIndex);
            if (finalArea != null && finalArea.IsExitRole && !finalArea.IsBossRole)
            {
                if (!weaponGranted && !exitOpen) return;
                _resumedFromCheckpoint = true;
                DestroyRestAndOpenDoor(finalAreaIndex);
                return;
            }

            if (!BossCensus.IsAreaDefeated(finalAreaIndex)) return;

            _resumedFromCheckpoint = true;
            _finalBossDefeated = true;

            if (weaponGranted || exitOpen) { DestroyRestAndOpenDoor(finalAreaIndex); return; }

            PickupDirector.EnsureInstalled().SpawnWeaponCore(ResolveFinalBossPosition(finalAreaIndex));
        }

        /// <summary>Where <see cref="ResumeFromCheckpoint"/> drops a resume-spawned Weapon Core: the
        /// final area's own boss, still standing Dormant in the rebuilt scene — <c>BossCensus.IsAreaDefeated</c>
        /// is seeded before any boss's own <c>Wake()</c> ever runs, so it silently self-destructs without
        /// ever registering (see <c>BigBermudaBoss.Wake</c>'s own doc comment) rather than vanishing the
        /// instant the scene builds, leaving its GameObject and position right there to read. Falls back
        /// to the exit gate's own position (or Max's, with no gate built at all) for a geometry-
        /// independent fixture with no boss built.</summary>
        private Vector3 ResolveFinalBossPosition(int areaIndex)
        {
            foreach (BigBermudaBoss boss in FindObjectsByType<BigBermudaBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (boss != null && IsInArea(boss.transform.position, areaIndex))
                    return boss.transform.position + Vector3.up * 1.4f;
            // MV-1127: World 2's boss is a SludgequeenBoss, not a BigBermudaBoss.
            foreach (SludgequeenBoss boss in FindObjectsByType<SludgequeenBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (boss != null && IsInArea(boss.transform.position, areaIndex))
                    return boss.transform.position + Vector3.up * 1.4f;
            if (_exitGate != null) return _exitGate.transform.position;
            Transform max = MaxTransform();
            return max != null ? max.position : Vector3.zero;
        }

        /// <summary>MV-1078/MV-1183: the orb is what matters, never the boss dying by itself — never
        /// before the final boss has actually died (a Weapon Core only ever drops off that death, but
        /// this guard keeps the two concerns independent), and never twice for the same finale. MV-1183,
        /// Lee's "heavily simplify" ruling: taking it resolves the WHOLE finale in this one call — the
        /// final area's remaining robots are destroyed and the door opens (<see cref="DestroyRestAndOpenDoor"/>),
        /// same frame, no further gate. <see cref="BeginWeaponBeat"/> (Beat A, unchanged) only plays the
        /// player-visible "orb becomes the weapon" flourish on top of that; it no longer gates anything
        /// downstream of it.</summary>
        private void OnWeaponCoreCollected()
        {
            if (!_finalBossDefeated || _weaponBeatActive || IsOpen) return;
            HideCoreBeacon();

            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            WorldConfig cfg = areaDirector != null ? areaDirector.ActiveWorldConfig : null;
            int finalAreaIndex = cfg?.dials != null ? cfg.dials.areaCount : 0;

            DestroyRestAndOpenDoor(finalAreaIndex);
            BeginWeaponBeat();
        }

        /// <summary>MV-1245: World 5's own finale orb -- the Helm Key is the Mothership's equivalent of
        /// the Weapon Core (see <see cref="PickupKind.HelmKey"/>'s own doc comment). Collecting it runs
        /// the SAME shared step <see cref="OnWeaponCoreCollected"/> uses (<see cref="DestroyRestAndOpenDoor"/>)
        /// but never <see cref="BeginWeaponBeat"/> -- the Mothership is the last world, so there is no
        /// next weapon to morph into (the ticket's own "no new weapon" rule) -- and shows "THE SHIP IS
        /// YOURS" instead of "NEW WEAPON", reusing the same interim-banner hold (<see cref="_interimBannerActive"/>)
        /// as its own one-off hand-over banner.
        /// Called directly from <see cref="PickupDirector"/>'s own HelmKey case (there is no generic
        /// <c>HudSignals.WeaponCoreCollected</c>-style signal for this -- that event also drives the
        /// "Weapon Core fanfare" SFX cue and <c>RunTracker</c>'s own Victory/Result-screen seal, neither
        /// of which applies to the game's true ending; see <see cref="ObservationDeckSequence"/> for what
        /// happens once Max actually walks through the now-open viewport instead).</summary>
        public void OnHelmKeyCollected()
        {
            if (!_finalBossDefeated || IsOpen) return;

            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            WorldConfig cfg = areaDirector != null ? areaDirector.ActiveWorldConfig : null;
            int finalAreaIndex = cfg?.dials != null ? cfg.dials.areaCount : 0;

            DestroyRestAndOpenDoor(finalAreaIndex);

            if (_banner == null) _banner = FinaleBanner.Create();
            _banner.Show("THE SHIP IS YOURS", string.Empty, string.Empty, 1f);
            _interimBannerActive = true;
            _interimBannerTime = 0f;

            if (_map?.exitDoorway != null)
                MaxWorlds.Intro.ObservationDeckSequence.Build(_map.exitDoorway.Value);
        }

        /// <summary>MV-1183's shared end-of-finale step: every robot still alive in
        /// <paramref name="areaIndex"/> — awake, Dormant, replicated or stuck mid-air alike — is
        /// destroyed with the ordinary death effect and no drops (same <see cref="RobotEnemy.KillWithoutDrops"/>
        /// MV-1122's own safety nets already used), that area's sheds/Replicators stop producing, and
        /// the exit door opens and stays open. Filtered on <see cref="RobotEnemy.AreaIndex"/> alone —
        /// the stamp every spawn path already sets — not map footprint, so this needs no built scene to
        /// run (a geometry-independent test's whole point). Called from the live orb pickup
        /// (<see cref="OnWeaponCoreCollected"/>) and from <see cref="ResumeFromCheckpoint"/>.</summary>
        private void DestroyRestAndOpenDoor(int areaIndex)
        {
            if (areaIndex > 0)
            {
                var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
                areaDirector?.DiscardQueuedForArea(areaIndex);
                StopFinalAreaProduction(areaIndex);

                foreach (RobotEnemy r in FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (r != null && r.IsAlive && r.AreaIndex == areaIndex) r.KillWithoutDrops();
            }

            Open();
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

        /// <summary>MV-1079, Beat A entry point: the player-visible "orb becomes the weapon" beat (2.5 s)
        /// — gameplay suspends, the Core flies into the gadget, the morph applies at its own authored
        /// instant mid-beat (<see cref="TickWeaponBeat"/>). MV-1183: purely cosmetic now — the final
        /// area's robots and the door are already resolved by the time this is called (see
        /// <see cref="OnWeaponCoreCollected"/>/<see cref="DestroyRestAndOpenDoor"/>), so there is nothing
        /// further to hand off to once it ends. Skipped entirely — nothing to animate or apply — when
        /// this world has no next weapon (the last world).</summary>
        private void BeginWeaponBeat()
        {
            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            int playedWorld = areaDirector != null ? areaDirector.ActiveWorldIndex : 0;

            if (playedWorld + 1 >= WorldLibrary.Count) return;

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
                // MV-1079: the weapon change lands at THIS instant, not the moment the Core was
                // collected — the beat itself is what applies it, inside the flash below.
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
                WeaponCatalog.PrimaryKind.Ricochet => ("RICOCHET", "Discs that bounce off walls and robots"),
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

        /// <summary>MV-1183: a dormant companion boss (not yet woken/registered when its sibling died)
        /// must still count — <see cref="BossCensus.AnyLivingIn"/> (what the <see cref="HudSignals.BossDefeated"/>
        /// signal that lands us here is already gated on) only ever sees a REGISTERED boss, so it read
        /// World 1 a30's two Big Bermudas as "empty" the instant the first died while the second sat
        /// Dormant, never approached. Re-check against the area's AUTHORED boss total
        /// (<see cref="WorldArea.Bosses"/>) when one exists; a synthetic fixture with no authored boss
        /// data (every pre-MV-1183 test here) falls back to the old "no more boss we know about is
        /// still alive" answer, unchanged. Kept local rather than shared with <c>BossVictoryPayoff</c>'s
        /// own identical check (two lines, and the two classes must never depend on one another).
        /// Deliberately independent of this component's own <see cref="Awake"/>-resolved geometry: a
        /// test driving pure boss-death/seal logic has no reason to build a real scene.</summary>
        private static bool IsFinalBossAreaDefeat()
        {
            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            WorldConfig cfg = areaDirector != null ? areaDirector.ActiveWorldConfig : null;
            if (cfg?.dials == null) return false;

            int areaIndex = BossCensus.LastDefeatedAreaIndex;
            WorldArea area = cfg.AreaByIndex(areaIndex);
            if (area == null || !area.IsBossRole || areaIndex != cfg.dials.areaCount) return false;

            int authoredTotal = area.Bosses().Length;
            return authoredTotal <= 0 || BossCensus.DeadCountInArea(areaIndex) >= authoredTotal;
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

            if (_interimBannerActive)
            {
                _interimBannerTime += Time.unscaledDeltaTime;
                if (_interimBannerTime >= InterimBannerSeconds)
                {
                    _interimBannerActive = false;
                    _banner?.Hide();
                }
            }

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
