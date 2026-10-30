using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A challenge pad (MV-1318) — a player-triggered 3-wave gauntlet. Max stepping inside the ring while
    /// READY starts it; three waves (4, 6, 8 robots) beam in on a 6-9 m ring around the pad, drawn from
    /// this pad's own area composition (<see cref="SetComposition"/>, resolved by <see cref="MaxWorlds.Arena.WorldMapLoader"/>);
    /// clearing all three WINS — 1 Supercell, or 2 if Max took no hit the whole way through. Dying, or
    /// straying more than <see cref="FailDistance"/> from the pad while it runs, FAILS it: the remaining
    /// challenge robots are left as ordinary robots (nothing here despawns them — <see cref="EnemySpawner"/>
    /// already owns their whole lifecycle), no reward, and the pad goes dark for good. One attempt per
    /// pad (<see cref="State"/> never leaves <see cref="ChallengeState.Won"/>/<see cref="ChallengeState.Failed"/>
    /// once it gets there).
    ///
    /// Follows the Assembly Cradle/Replicator shape: an attached <see cref="EnemySpawner"/>, immediately
    /// <see cref="EnemySpawner.Stop"/>'d (this pad drives its own wave cadence, never the spawner's own
    /// Invasion-ramped one) and emitted into on demand via <see cref="EnemySpawner.SpawnExact"/> — which
    /// still rolls elites and resolves <see cref="MaxWorlds.Arena.WorldConfig.enemyOverrides"/> exactly
    /// like any other spawn, since it runs through the same <c>SpawnKind</c> the timer-driven path does.
    /// </summary>
    [RequireComponent(typeof(EnemySpawner))]
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class ChallengePad : MonoBehaviour
    {
        public enum ChallengeState { Ready, Running, Won, Failed }

        // --- Ticket's own numbers ---
        private const float PadRadius = 2.5f;              // "radius 2.5 m (~120 px)"
        private const float IconHeight = 2f;                // "floating 2 m above it"
        private const float IconDiameter = 0.8f;             // ">= 32 px" at 48 px/m (docs/DECISIONS.md)
        private const float FailDistance = 15f;              // "more than 15 m from the pad"
        private const float WaveClearDelaySeconds = 1.5f;    // "starts 1.5 s after the previous is cleared"
        private const float BeamIntervalSeconds = 0.2f;      // "0.2 s apart"
        private const float RingMinRadius = 6f;              // "6-9 m from the pad"
        private const float RingMaxRadius = 9f;
        private const float RingEdgeMargin = 0.5f;           // same margin AssemblyCradle.PlaceOnOutPad uses
        private static readonly int[] WaveSizes = { 4, 6, 8 };

        private const float BeamWidth = 0.6f;
        private const float BeamHeight = 3f;
        private const float BeamLifeSeconds = 0.4f;

        private const float WonGoldHoldSeconds = 1.5f;       // "WON (ring turns gold, then dark)"
        private const float NoHitBannerSeconds = 2.5f;       // "NO HIT — DOUBLE" hold before the strip clears
        private const float PulseHz = 1.4f;
        private const float OuterRingRadiusFraction = 1f;
        private const float InnerRingRadiusFraction = 0.65f; // the image's double-ring look

        private static readonly Color ReadyColor = new Color(0.86f, 0.14f, 0.14f);
        private static readonly Color RunningColor = new Color(0.95f, 0.35f, 0.22f);
        private static readonly Color WonColor = new Color(0.95f, 0.78f, 0.22f);
        private static readonly Color UsedColor = new Color(0.22f, 0.22f, 0.24f);
        private static readonly Color BeamColor = new Color(1f, 0.5f, 0.35f);

        private EnemySpawner _spawner;
        private GroundRing _ringOuter;
        private GroundRing _ringInner;
        private CameraFacingFlare _icon;

        private WorldComposition _composition;
        private EnemyMix.AreaCadence _cadence;

        private ChallengeState _state = ChallengeState.Ready;
        private int _waveIndex;
        private readonly Queue<EnemyKind> _beamQueue = new Queue<EnemyKind>();
        private readonly List<RobotEnemy> _waveLive = new List<RobotEnemy>(8);
        private float _beamTimer;
        private bool _waveClearPending;
        private float _waveClearTimer;

        private bool _tookHit;
        private PlayerHealth _playerHealth;
        private Transform _player;

        private float _wonHoldElapsed;
        private bool _wonFaded;
        private float _noHitBannerElapsed;
        private bool _noHitBannerActive;

        /// <summary>This pad's stable id (MV-1318) — same "a checkpoint can persist/restore by id"
        /// reasoning as <see cref="Canister.Id"/>/<see cref="Pylon.Id"/>.</summary>
        public string Id { get; private set; } = string.Empty;

        public void SetId(string id) => Id = id ?? string.Empty;

        /// <summary>This pad's own area composition (or the world's dial-derived fallback), resolved by
        /// <see cref="MaxWorlds.Arena.WorldMapLoader"/> and handed here via <see cref="MaxWorlds.Arena.MapEntity.challengeComposition"/>
        /// — called before <see cref="Build"/> needs it (<see cref="MaxWorlds.Arena.MapRuntime.BuildChallenge"/>
        /// calls this immediately after <c>AddComponent</c>, same ordering <see cref="MaxWorlds.Arena.MapRuntime.BuildCradle"/>
        /// already uses for <see cref="EnemySpawner.ConfigureAreaComposition"/>).</summary>
        public void SetComposition(WorldComposition composition) => _composition = composition;

        /// <summary>Post-build readout, never an authored constant — same "Tier 2" idiom as
        /// <see cref="Canister.IsFusing"/>.</summary>
        public ChallengeState State => _state;

        /// <summary>The current wave's robots this pad itself is still tracking as live — test-only
        /// introspection, same footing as <see cref="EnemySpawner.Cadence"/>. Deliberately this pad's
        /// own bookkeeping rather than <see cref="RobotEnemy.Active"/>/<see cref="RobotEnemy.ActiveCount"/>:
        /// <c>RobotEnemy.OnEnable</c> (which populates that static registry) does not reliably fire off a
        /// plain <c>SetActive(true)</c> toggle under <c>-batchmode -nographics</c>, even though the
        /// spawned robot is otherwise genuinely alive (<see cref="RobotEnemy.Apply"/> seeds its health
        /// directly, independent of <c>OnEnable</c>) — so a test must read this pad's own list, not that
        /// registry, to see what it actually put on the field.</summary>
        public IReadOnlyList<RobotEnemy> LiveWaveRobots => _waveLive;

        /// <summary>How many Supercells <see cref="Win"/> actually placed — 0 before a win, else 1 or 2
        /// depending on whether a hit was recorded. Test-only introspection of the same reliability as
        /// <see cref="LiveWaveRobots"/>: <c>Pickup.OnEnable</c> (which populates <c>Pickup.Active</c>)
        /// has the identical batchmode unreliability as <c>RobotEnemy.OnEnable</c>, so a test reads the
        /// resolved OUTCOME of this pad's own reward computation here rather than trying to count a
        /// registry that a freshly-placed drop may never actually register into under
        /// <c>-batchmode -nographics</c>.</summary>
        public int SupercellsAwarded { get; private set; }

        // --- Static registry — same "the map builds them, Build() registers them, a fresh level wipes
        // them" shape as Canister.Registered. ---
        private static readonly List<ChallengePad> Registered = new List<ChallengePad>(4);

        public static void ResetRegistry() => Registered.Clear();

        /// <summary>The stable ids of every registered pad already WON this run — what a mid-run
        /// checkpoint capture persists. Same shape as <see cref="Canister.DestroyedIds"/>.</summary>
        public static string[] WonIds() => IdsInState(ChallengeState.Won);

        /// <summary>The stable ids of every registered pad already FAILED this run. Same shape as
        /// <see cref="WonIds"/>.</summary>
        public static string[] FailedIds() => IdsInState(ChallengeState.Failed);

        private static string[] IdsInState(ChallengeState state)
        {
            var ids = new List<string>();
            foreach (ChallengePad p in Registered)
                if (p != null && p._state == state && !string.IsNullOrEmpty(p.Id)) ids.Add(p.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded WON set onto whichever currently-registered,
        /// still-READY pads carry a matching <see cref="Id"/> — called by <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/>
        /// on RESUME. Same shape as <see cref="Canister.ApplyCheckpointDestroyedIds"/>. A no-op for an
        /// empty/null set.</summary>
        public static void ApplyCheckpointWonIds(IReadOnlyList<string> wonIds) =>
            ApplyCheckpointState(wonIds, ChallengeState.Won);

        /// <summary>Same as <see cref="ApplyCheckpointWonIds"/>, for the FAILED set.</summary>
        public static void ApplyCheckpointFailedIds(IReadOnlyList<string> failedIds) =>
            ApplyCheckpointState(failedIds, ChallengeState.Failed);

        private static void ApplyCheckpointState(IReadOnlyList<string> ids, ChallengeState state)
        {
            if (ids == null || ids.Count == 0) return;

            foreach (ChallengePad p in Registered)
            {
                if (p == null || p._state != ChallengeState.Ready || string.IsNullOrEmpty(p.Id)) continue;
                for (int i = 0; i < ids.Count; i++)
                {
                    if (ids[i] != p.Id) continue;
                    p.ApplyCheckpointUsed(state);
                    break;
                }
            }
        }

        /// <summary>MV-1318: silently applies a checkpoint's already-recorded outcome — a resume is
        /// restoring history, not re-playing the win/loss, so this skips straight to the resolved visual
        /// state with no wave, no reward placement and no gold hold.</summary>
        private void ApplyCheckpointUsed(ChallengeState state)
        {
            _state = state;
            _wonFaded = true;
            UpdateVisual();
        }

        private void Awake() => Build();

        /// <summary>Construct the hidden spawner and the greybox visuals. Exposed publicly, same reasoning
        /// as <see cref="Canister.Build"/> — <c>Awake</c> never runs as a side effect of <c>AddComponent</c>
        /// outside Play mode, so an EditMode test calls this directly.</summary>
        public void Build()
        {
            if (_spawner != null) return; // already built

            _spawner = GetComponent<EnemySpawner>();
            _spawner.Stop(); // this pad drives its own wave cadence, never the spawner's own timer (AssemblyCradle's own idiom)

            _ringOuter = GroundRing.Create($"{name} Ring Outer");
            _ringInner = GroundRing.Create($"{name} Ring Inner");
            _icon = CameraFacingFlare.Create($"{name} Icon");

            if (!Registered.Contains(this)) Registered.Add(this);
            UpdateVisual();
        }

        private void OnDestroy() => Registered.Remove(this);

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The per-frame step, pulled out of <see cref="Update"/> with an explicit <paramref name="dt"/>
        /// — same "Tick(dt), not a bare Update()" shape as <see cref="Canister.Tick"/>, so an EditMode test
        /// can drive many simulated seconds with no live Unity frame loop.</summary>
        public void Tick(float dt)
        {
            switch (_state)
            {
                case ChallengeState.Ready:
                    TickReady();
                    break;
                case ChallengeState.Running:
                    TickRunning(dt);
                    break;
                case ChallengeState.Won:
                    if (!_wonFaded || _noHitBannerActive) TickWonFade(dt);
                    break;
                // Failed: fully inert, dark forever — nothing to tick.
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private Transform ResolvePlayer()
        {
            if (_player != null) return _player;
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            _player = p != null ? p.transform : null;
            return _player;
        }

        private void TickReady()
        {
            Transform player = ResolvePlayer();
            if (player == null) return;
            float d = Vector3.Distance(Flat(player.position), Flat(transform.position));
            if (d <= PadRadius) StartChallenge(player);
        }

        private void StartChallenge(Transform player)
        {
            _state = ChallengeState.Running;
            _waveIndex = 0;
            _tookHit = false;
            _cadence = new EnemyMix.AreaCadence(_composition);

            _playerHealth = player.GetComponentInParent<PlayerHealth>();
            if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;
            // MV-1318: force-field-absorbed hits never reach HudSignals.PlayerHit at all — a FULLY
            // absorbed hit returns out of PlayerHealth.TakeDamage before EmitPlayerHit fires — so
            // subscribing here already excludes them with no extra plumbing (ticket: "Force-field-absorbed
            // hits do not count as hits").
            HudSignals.PlayerHit += OnPlayerHit;

            BeginWave();
            UpdateVisual();
        }

        private void BeginWave()
        {
            _beamQueue.Clear();
            _waveLive.Clear();
            _beamTimer = 0f;

            int count = WaveSizes[_waveIndex];
            for (int i = 0; i < count; i++)
                if (_cadence.TryNextKind(out EnemyKind kind)) _beamQueue.Enqueue(kind);

            HudSignals.EmitObjective($"WAVE {_waveIndex + 1}/{WaveSizes.Length}", ReadyColor);
        }

        private void TickRunning(float dt)
        {
            if (_playerHealth != null && !_playerHealth.IsAlive) { Fail(); return; }

            Transform player = ResolvePlayer();
            if (player != null &&
                Vector3.Distance(Flat(player.position), Flat(transform.position)) > FailDistance)
            {
                Fail();
                return;
            }

            TickBeamIn(dt);

            if (_beamQueue.Count == 0 && _waveLive.Count == 0 && !_waveClearPending)
            {
                if (_waveIndex + 1 >= WaveSizes.Length) { Win(); return; }
                _waveClearPending = true;
                _waveClearTimer = 0f;
            }

            if (_waveClearPending)
            {
                _waveClearTimer += dt;
                if (_waveClearTimer >= WaveClearDelaySeconds)
                {
                    _waveClearPending = false;
                    _waveIndex++;
                    BeginWave();
                }
            }

            PulseRunningVisual();
        }

        private void TickBeamIn(float dt)
        {
            if (_beamQueue.Count == 0) return;
            _beamTimer -= dt;
            if (_beamTimer > 0f) return;
            _beamTimer = BeamIntervalSeconds;
            SpawnWaveRobot(_beamQueue.Dequeue());
        }

        /// <summary>One robot of <paramref name="kind"/>, through the shared spawn pipeline (ticket: "same
        /// construction, budget and landing clamp as every other spawn") — <see cref="EnemySpawner.SpawnExact"/>
        /// builds/pools and rolls elites exactly like any other spawn, then this repositions it onto the
        /// pad's own 6-9 m ring, snapped onto walkable floor the same way <see cref="AssemblyCradle.PlaceOnOutPad"/>
        /// snaps its own out-pad spawn (the "landing clamp"). Empty <paramref name="kind"/> spawn list
        /// (a population-cap refusal) is a no-op, same as every other <c>SpawnExact</c> caller in this
        /// codebase accepts.</summary>
        private void SpawnWaveRobot(EnemyKind kind)
        {
            List<RobotEnemy> spawned = _spawner.SpawnExact(kind, 1, ignorePerFactoryCap: true);
            if (spawned.Count == 0) return;

            RobotEnemy e = spawned[0];
            Vector3 point = ResolveRingPoint();
            e.transform.position = point;
            Vector3 face = Flat(transform.position - point);
            if (face.sqrMagnitude > 0.0001f) e.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);

            e.Died += OnWaveRobotDied;
            _waveLive.Add(e);

            PlayBeam(point);
        }

        private Vector3 ResolveRingPoint()
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(RingMinRadius, RingMaxRadius);
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            Vector3 raw = transform.position + offset;

            MapData map = EnemyNavigation.Map;
            return map != null ? map.SnapToWalkableSurface(transform.position, raw, RingEdgeMargin) : raw;
        }

        private void PlayBeam(Vector3 point)
        {
            LightPillar pillar = LightPillar.Create($"{name} Beam");
            pillar.Show(point, BeamWidth, BeamHeight, BeamColor);
            pillar.gameObject.AddComponent<BeamLifetimeVfx>().Begin(pillar, BeamLifeSeconds);
        }

        private void OnWaveRobotDied(RobotEnemy e)
        {
            e.Died -= OnWaveRobotDied;
            _waveLive.Remove(e);
        }

        private void OnPlayerHit(Vector3 pos, Vector3 dir, bool isContact) => _tookHit = true;

        private void OnPlayerDied() => Fail();

        /// <summary>All three waves cleared (ticket Change 4) — 1 Supercell, 2 if <see cref="_tookHit"/>
        /// never fired across the whole attempt.</summary>
        private void Win()
        {
            _state = ChallengeState.Won;
            _wonHoldElapsed = 0f;
            _wonFaded = false;
            _noHitBannerElapsed = 0f;
            _noHitBannerActive = !_tookHit;
            StopListening();

            int count = _tookHit ? 1 : 2;
            SupercellsAwarded = count;
            PickupDirector director = PickupDirector.EnsureInstalled();
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = count == 1 ? Vector3.zero : new Vector3((i == 0 ? -0.6f : 0.6f), 0f, 0f);
                director.PlacePickupAt(PickupKind.Supercell, transform.position + offset);
            }

            HudSignals.EmitObjective(!_tookHit ? "NO HIT — DOUBLE" : null, WonColor);
            UpdateVisual();
        }

        /// <summary>Max died, or strayed more than <see cref="FailDistance"/> from the pad while it ran
        /// (ticket Change 5) — the remaining challenge robots are left exactly as they are: nothing here
        /// despawns or detaches them, they simply stop being tracked by this pad and carry on as ordinary
        /// robots on the field, same as the ticket's own "remaining challenge robots stay as ordinary
        /// robots" spells out.</summary>
        private void Fail()
        {
            _state = ChallengeState.Failed;
            StopListening();
            _beamQueue.Clear();
            _waveLive.Clear();
            HudSignals.EmitObjective(null, default);
            UpdateVisual();
        }

        private void StopListening()
        {
            if (_playerHealth != null)
            {
                _playerHealth.Died -= OnPlayerDied;
                _playerHealth = null;
            }
            HudSignals.PlayerHit -= OnPlayerHit;

            foreach (RobotEnemy r in _waveLive)
                if (r != null) r.Died -= OnWaveRobotDied;
        }

        // ---------------------------------------------------------------- Visual (ticket Change 2)

        private void UpdateVisual()
        {
            switch (_state)
            {
                case ChallengeState.Ready:
                    ShowRings(ReadyColor);
                    _icon.Show(IconCenter, IconDiameter, ReadyColor);
                    break;
                case ChallengeState.Running:
                    ShowRings(RunningColor);
                    _icon.Show(IconCenter, IconDiameter, RunningColor);
                    break;
                case ChallengeState.Won:
                    ShowRings(WonColor);
                    _icon.Hide();
                    break;
                case ChallengeState.Failed:
                    ShowRings(UsedColor);
                    _icon.Hide();
                    break;
            }
        }

        private Vector3 IconCenter => transform.position + Vector3.up * IconHeight;

        private void ShowRings(Color c)
        {
            _ringOuter.Show(transform.position, PadRadius * OuterRingRadiusFraction, c);
            _ringInner.Show(transform.position, PadRadius * InnerRingRadiusFraction, c);
        }

        /// <summary>RUNNING (ticket: "ring pulses") — a plain sine alpha oscillation. Cosmetic only;
        /// nothing in this codebase asserts a rendered pixel outside the conformance harness (MV-465
        /// Rule 2/Tier 3), so this never needs to be deterministic for a test.</summary>
        private void PulseRunningVisual()
        {
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * PulseHz * Mathf.PI * 2f);
            Color c = RunningColor;
            c.a *= pulse;
            ShowRings(c);
        }

        private void TickWonFade(float dt)
        {
            if (!_wonFaded)
            {
                _wonHoldElapsed += dt;
                if (_wonHoldElapsed >= WonGoldHoldSeconds)
                {
                    _wonFaded = true;
                    ShowRings(UsedColor);
                }
            }

            // The "NO HIT — DOUBLE" banner outlives the gold hold on its own, longer clock — ticked
            // independently so a short WonGoldHoldSeconds never truncates the banner the player is still
            // reading.
            if (_noHitBannerActive)
            {
                _noHitBannerElapsed += dt;
                if (_noHitBannerElapsed >= NoHitBannerSeconds)
                {
                    _noHitBannerActive = false;
                    HudSignals.EmitObjective(null, default);
                }
            }
        }

        /// <summary>Grows/fades a beam-in column of light then destroys its own GameObject — self-
        /// contained, same shape as <see cref="Canister"/>'s own <c>BlastRingLifetimeVfx</c>.</summary>
        [MaxWorlds.Core.PerfSection("factories")]
        private sealed class BeamLifetimeVfx : MonoBehaviour
        {
            private LightPillar _pillar;
            private float _life;
            private float _t;

            public void Begin(LightPillar pillar, float life)
            {
                _pillar = pillar;
                _life = life;
            }

            private void Update()
            {
                _t += Time.deltaTime;
                if (_t >= _life)
                {
                    if (_pillar != null) _pillar.Hide();
                    Destroy(gameObject);
                }
            }
        }
    }
}
