using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// Assembly Cradle — the Mothership's robot-building machine (MV-1223, behaviour only; the look —
    /// gantry frame, visible build stages, red corner turrets — is MV-1227). Follows the
    /// Replicator/MowerHutch shape rather than inventing a new one: <see cref="DestructibleHealth"/> for
    /// HP, and an attached <see cref="EnemySpawner"/> immediately <see cref="EnemySpawner.Stop"/>'d —
    /// this cradle drives its own flat 4.0 s build cadence (never the spawner's own Invasion-ramped one)
    /// and emits on demand via <see cref="EnemySpawner.SpawnExact"/>, the same "never ticks its own
    /// cadence" idiom <see cref="Replicator"/> uses for its doubled twins.
    ///
    /// Builds one robot every <see cref="BuildSeconds"/> while Max stands in its area
    /// (<see cref="SetMaxInArea"/>), capped at <see cref="MaxAliveSpawned"/> alive at once — the build
    /// timer is held at zero while the cap is full (Lee's "then stops") and only starts counting again
    /// once a slot frees, so a replacement always lands a FULL cycle after the kill that freed it, never
    /// sooner. A kill mid-cycle never completes: <see cref="OnDestroyed"/> reads whether a build was in
    /// progress off that same timer, with no separate "pending robot" flag that could fall out of sync
    /// with it.
    /// </summary>
    [RequireComponent(typeof(EnemySpawner))]
    [MaxWorlds.Core.PerfSection("assembly_cradle")]
    public sealed class AssemblyCradle : MonoBehaviour, IDamageable, IFactoryBody, IHealthReadout
    {
        public enum BuildStage { None, Frame, Plates, Done }

        // Lee's first-pass numbers (17 Oct, Cradle tab) — tuning comes after a play, not this ticket.
        public const float BuildSeconds = 4.0f;
        public const float FrameAtSeconds = 1.5f;
        public const float PlatesAtSeconds = 3.0f;
        public const int MaxAliveSpawned = 6;

        /// <summary>A robot destroyed mid-build drops this many Parts (PickupKind.PowerCell) at the bay
        /// instead of ever completing.</summary>
        public const int ScrapParts = 3;

        /// <summary>A cradle destroyed idle (no build in progress) pays out this many Power Cells
        /// (PickupKind.PowerCellSecondary) and this many Parts (PickupKind.PowerCell) — PickupKind's own
        /// names are the pre-rename C# ones; see PickupWallet's doc for which is which to the player.</summary>
        public const int PayoutCells = 15;
        public const int PayoutParts = 10;

        /// <summary>Same tier HP as every other machine (MowerHutch.factoryHealth / Replicator.ReplicatorHealth).</summary>
        private const float CradleHealth = 474.75f;

        private const float OutPadHalfWidth = 1.0f; // half of the ticket's 2x2 m out pad
        private const float DropScatterRadius = 0.6f;

        // --- MV-1227: the look — health bar sizes follow MowerHutch/Replicator's own world-bar
        // convention, scaled for the ticket's taller 3x3x3 m frame. ---
        private const float BarHeightAboveCentre = 2.6f;
        private const float BarWorldWidth = 1.8f;

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
        private float _buildTimer;
        private bool _maxInArea;

        // --- MV-1246: the two turret housings fire, reusing World 1's own ShedFitting behaviour
        // (MV-547/1073's already-tuned range/cadence/HP table) rather than inventing a new weapon. ---
        private ShedFitting[] _turretFittings;
        private ShedFittingKind _turretKind = ShedFittingKind.None;
        private Transform _maxTransform;

        // --- MV-1244: a field-wide registry of every built cradle, so a Welder
        // (RobotEnemy.TickWelderRepair) can find "the nearest damaged cradle in its own area" without a
        // map-wide scene query — same reasoning as RobotEnemy's own _active registry. ---
        private static readonly List<AssemblyCradle> _active = new List<AssemblyCradle>(8);

        /// <summary>Test-only reset (MV-465 idiom, same as RobotEnemy.ResetRegistry/PickupWallet.Reset) —
        /// an EditMode suite shares one physics scene for the whole cc-verify run with no per-test reset,
        /// so a cradle left behind by an earlier test would otherwise leak into a later test's "nearest"
        /// search.</summary>
        public static void ResetRegistry() => _active.Clear();

        /// <summary>MV-820-style stamp: which 1-based area this cradle was placed into — set once by
        /// whoever built the world (<see cref="WorldMapLoader"/>), not re-derived live from position. 0
        /// until stamped, same default every unplaced <see cref="RobotEnemy.AreaIndex"/> starts at.</summary>
        public int AreaIndex { get; private set; }

        public void SetAreaIndex(int area) => AreaIndex = area;

        /// <summary>MV-1248: this cradle's own stable id (the <c>"{area.id}_cradleN"</c> fallback
        /// <see cref="MaxWorlds.Arena.WorldRunner"/> already computes to look itself up in the built
        /// scene, now also stamped onto the component) — same reasoning as <see cref="Replicator.Id"/>/
        /// <see cref="MowerHutch.Id"/>: a checkpoint persists and restores "which cradles are destroyed"
        /// by id, never by a C# reference that dies with the scene. Empty until <see cref="SetId"/> is
        /// called.</summary>
        public string Id { get; private set; } = string.Empty;

        /// <summary>Public so an EditMode test can drive identity directly, same reasoning as every other
        /// post-build configure call in this file.</summary>
        public void SetId(string id) => Id = id ?? string.Empty;

        /// <summary>The nearest cradle below full health, in <paramref name="areaIndex"/>, within
        /// <paramref name="maxRange"/> of <paramref name="from"/> — what a Welder's repair tick asks for
        /// every frame. Null if nothing in range qualifies.</summary>
        public static AssemblyCradle NearestDamagedInArea(Vector3 from, int areaIndex, float maxRange)
        {
            AssemblyCradle best = null;
            float bestDistSqr = maxRange * maxRange;
            for (int i = 0; i < _active.Count; i++)
            {
                AssemblyCradle c = _active[i];
                if (c == null || !c.IsAlive) continue;
                if (c.AreaIndex != areaIndex) continue;
                if (c.Normalized >= 1f) continue; // not damaged — nothing to repair
                float distSqr = (c.transform.position - from).sqrMagnitude;
                if (distSqr <= bestDistSqr) { bestDistSqr = distSqr; best = c; }
            }
            return best;
        }

        // --- MV-1227: the gantry's own moving/measurable parts, built once in Build(). ---
        private Transform[] _arms;
        private Transform _bayRoot;
        private Transform _frameStage;
        private Transform _platesStage;
        private Transform _doneStage;
        private EnemyKind _previewKind;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Max-side damage (Light Missile, Orbitals, sentinels, ...) can hit it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        // --- MV-1227: IHealthReadout — the always-on world health bar the ticket asks for. ---
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;
        public string ReadoutName => "ASSEMBLY CRADLE";

        /// <summary>The two front-top turret housings (MV-1227 AC1 measures their own resolved
        /// renderer width) — the barrel is a separate child, deliberately excluded so it can never
        /// skew the bound.</summary>
        public Transform[] TurretHousings { get; private set; }

        /// <summary>The bay's current frame-stage body root — null until the first build cycle starts.
        /// Exposed for MV-1227's own EditMode test to read each stage's resolved renderer-enabled set.</summary>
        public Transform FrameStage => _frameStage;
        public Transform PlatesStage => _platesStage;
        public Transform DoneStage => _doneStage;

        /// <summary>0..1 through the current build cycle — for MV-1227's visual stages.</summary>
        public float BuildProgress => Mathf.Clamp01(_buildTimer / BuildSeconds);

        /// <summary>The current build's visible stage — frame at 1.5 s, plates at 3.0 s, done (eye lit)
        /// at 4.0 s — for MV-1227.</summary>
        public BuildStage Stage
        {
            get
            {
                if (_buildTimer < FrameAtSeconds) return BuildStage.None;
                if (_buildTimer < PlatesAtSeconds) return BuildStage.Frame;
                if (_buildTimer < BuildSeconds) return BuildStage.Plates;
                return BuildStage.Done;
            }
        }

        /// <summary>Construct the health model and stop this cradle's own EnemySpawner from ticking its
        /// own cadence — see the class doc. Public (same reasoning as Replicator.Build) so an EditMode
        /// test can invoke it directly; Awake never runs as a side effect of AddComponent outside Play
        /// mode.</summary>
        public void Build()
        {
            _health = new DestructibleHealth(CradleHealth);
            _health.Destroyed += OnDestroyed;
            _spawner = GetComponent<EnemySpawner>();
            _spawner.Stop();

            BuildVisual();
            WorldHealthBar.Attach(gameObject, this, BarHeightAboveCentre, BarWorldWidth,
                alwaysShow: true, showNumber: false);

            if (!_active.Contains(this)) _active.Add(this); // MV-1244: see the registry's own doc comment

            // MV-1247: self-register with the HUD counter's own census (as Replicator/MowerHutch do) so
            // "CRADLES destroyed/47" reads correctly from the first frame instead of HudModel's own
            // one-factory placeholder default never being overwritten.
            FactoryCensus.RegisterCradle(this);
            HudSignals.EmitFactoryRegistered();
        }

        private void OnDestroy() => _active.Remove(this);

        /// <summary>MV-1244: a Welder's own repair tick — restores HP toward the ceiling, never past it,
        /// and never revives a cradle already destroyed. Delegates straight to <see cref="DestructibleHealth.Heal"/>,
        /// the same one-shot-destroyed semantics every other healer in this codebase (a Sentinel's own
        /// passive regen, MV-398) already relies on.</summary>
        public void Heal(float amount) => _health?.Heal(amount);

        /// <summary>MV-1227: the gantry frame, arms, turrets and out-pad outline — the primitive cube
        /// this component sits on stays as the collider (the Water Blaster's hitbox), but its own
        /// renderer is switched off, the same "renderer off, collider stays" treatment
        /// <see cref="Replicator"/>'s own generated-mesh body uses.</summary>
        private void BuildVisual()
        {
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                rend.enabled = false;
                MapStaticBatchRoot.Active?.MarkPermanentlyHidden(rend);
            }

            Transform bodyRoot = ParentScale.MakeMetreSpace(new GameObject("Body").transform, transform);
            FactoryBodies.CradleGantry gantry = FactoryBodies.BuildAssemblyCradle(bodyRoot, transform.lossyScale);
            _arms = gantry.Arms;
            _bayRoot = gantry.BayRoot;
            TurretHousings = gantry.TurretHousings;
            BuildTurretFittings();
        }

        /// <summary>MV-1246: one <see cref="ShedFitting"/> per housing, sitting exactly at the housing's
        /// own position (never the barrel's far tip — the ticket's own 0.3 m "near a turret housing"
        /// tolerance is sized to the housing itself, not a half-metre barrel's length further out) so
        /// every shot's <see cref="DamageInfo.Point"/> reads as coming from that housing. Starts inert
        /// (<see cref="ShedFittingKind.None"/>) until <see cref="SetTurretKind"/> authors a real tier —
        /// built with no visual (<see cref="ShedFitting.Bind"/>'s own doc) since the look is MV-1227's,
        /// and <c>enabled = false</c> so this fitting's own <c>Update()</c> can never double-tick its
        /// cooldown alongside the explicit <see cref="TickTurrets"/> call below.</summary>
        private void BuildTurretFittings()
        {
            _turretFittings = new ShedFitting[TurretHousings.Length];
            for (int i = 0; i < TurretHousings.Length; i++)
            {
                var fittingGo = new GameObject(TurretHousings[i].name + "Fitting");
                fittingGo.transform.SetParent(TurretHousings[i], worldPositionStays: false);
                fittingGo.transform.localPosition = Vector3.zero;

                var fitting = fittingGo.AddComponent<ShedFitting>();
                fitting.enabled = false;
                fitting.Bind(null, ShedFittingKind.None, buildVisual: false);
                _turretFittings[i] = fitting;
            }
        }

        /// <summary>MV-1246: the tier both turret housings fire at — the SAME per-area mechanism
        /// (<see cref="ShedFittingKind"/>) World 1 sheds already resolve from <c>WorldArea.shedFittings</c>,
        /// applied here by <see cref="MaxWorlds.Arena.MapRuntime"/> from the cradle's own authored area.
        /// Public so an EditMode test can drive it directly, same reasoning as <see cref="SetMaxInArea"/>.</summary>
        public void SetTurretKind(ShedFittingKind kind)
        {
            _turretKind = kind;
            if (_turretFittings == null) return;
            for (int i = 0; i < _turretFittings.Length; i++)
                _turretFittings[i]?.Bind(null, kind, buildVisual: false);
        }

        private void Awake() => Build();

        /// <summary>Whether Max is standing in this cradle's own area — the sole gate on production.
        /// Public so an EditMode test can drive it directly, same reasoning as every other post-build
        /// configure call in this file's sibling factories.</summary>
        public void SetMaxInArea(bool present) => _maxInArea = present;

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The per-frame build step, pulled out with an explicit <paramref name="dt"/> — same
        /// "Tick(dt), not a bare Update()" shape as EnemySpawner.Tick/Replicator.TickConsumption — so an
        /// EditMode test can drive many simulated seconds with no live Unity frame loop.</summary>
        public void Tick(float dt)
        {
            if (!IsAlive || !_maxInArea) return;
            TickTurrets(dt); // MV-1246: independent of the build cadence below — gated only by the same IsAlive/_maxInArea this method already checked, so a destroyed cradle fires no further shots and an empty area never fires at all
            if (_spawner.LiveCount >= MaxAliveSpawned) return; // at the cap: held at zero, not merely paused

            if (_buildTimer <= 0f)
            {
                // A fresh cycle starting — decide (and cache) which kind this build will produce. Read
                // exactly once per cycle, here at the start rather than at completion (MV-1223's
                // original shape): TryNextKind mutates the cadence's own emitted tally (MV-1227 doc on
                // AssemblyCradle), so calling it a second time to "peek" a preview kind would double-count
                // every cycle. Moving the one call earlier keeps it at exactly once while letting the
                // bay's build-stage visuals know which kind to preview for the whole cycle.
                if (!_spawner.Cadence.TryNextKind(out EnemyKind kind)) return; // this area authors nothing
                _previewKind = kind;
                RebuildPreview(kind);
            }

            _buildTimer += dt;
            ApplyBuildStageVisual();
            if (_buildTimer < BuildSeconds) return;

            _buildTimer = 0f;
            List<RobotEnemy> spawned = _spawner.SpawnExact(_previewKind, 1, ignorePerFactoryCap: true);
            PlaceOnOutPad(spawned);
        }

        /// <summary>MV-1246: drives both turret fittings at whatever <see cref="ResolveTurretTarget"/>
        /// hands back — Max by default, or the nearest in-range Sentinel, the same proximity-only swap
        /// <see cref="MaxWorlds.Bosses.BigBermudaBoss.RetargetIfNeeded"/> already applies. A no-op while
        /// unauthored (<see cref="ShedFittingKind.None"/>): the housings stay drawn but silent, exactly
        /// today's bug, until a real tier is set.</summary>
        private void TickTurrets(float dt)
        {
            if (_turretFittings == null || _turretKind == ShedFittingKind.None) return;

            if (_maxTransform == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _maxTransform = p.transform;
            }

            Transform target = ResolveTurretTarget();
            for (int i = 0; i < _turretFittings.Length; i++)
            {
                ShedFitting fitting = _turretFittings[i];
                if (fitting == null) continue;
                fitting.SetTarget(target);
                fitting.Tick(dt);
            }
        }

        /// <summary>Max, unless a Sentinel is both nearer than Max and within
        /// <see cref="SentinelTargeting.AggroRadius"/> — the exact rule
        /// <see cref="SentinelTargeting.ShouldEngageSentinel"/> already encodes for every other
        /// Max-or-Sentinel swap in this codebase.</summary>
        private Transform ResolveTurretTarget()
        {
            if (_maxTransform == null) return null;

            Sentinel nearest = SentinelTargeting.Nearest(transform.position);
            if (nearest == null) return _maxTransform;

            float distToMax = Vector3.Distance(transform.position, _maxTransform.position);
            float distToSentinel = Vector3.Distance(transform.position, nearest.transform.position);
            bool engageSentinel = SentinelTargeting.ShouldEngageSentinel(distToMax, distToSentinel, SentinelTargeting.AggroRadius);
            return engageSentinel ? nearest.transform : _maxTransform;
        }

        /// <summary>MV-1227: rebuilds the bay's three build-stage bodies for <paramref name="kind"/> —
        /// a no-op if <see cref="BuildVisual"/> never ran (a test fixture that calls <see cref="Tick"/>
        /// without ever calling <see cref="Build"/>, which can't happen outside a hand-built fixture
        /// since <see cref="Build"/> always sets <see cref="_bayRoot"/> first).</summary>
        private void RebuildPreview(EnemyKind kind)
        {
            if (_bayRoot == null) return;
            FactoryBodies.CradlePreview preview = FactoryBodies.BuildCradlePreview(_bayRoot, kind);
            _frameStage = preview.Frame;
            _platesStage = preview.Plates;
            _doneStage = preview.Done;
        }

        /// <summary>MV-1227 AC1: shows exactly one of the bay's three stage bodies, driven by
        /// <see cref="Stage"/> — toggling each renderer's own <c>enabled</c> flag (never
        /// <c>SetActive</c>, so the AC's "resolved renderer enabled flags" read holds literally). None
        /// shown while <see cref="Stage"/> is <see cref="BuildStage.None"/> (the lead-in before the
        /// frame stage begins): the bay reads empty, never a stale leftover from the previous cycle.</summary>
        private void ApplyBuildStageVisual()
        {
            BuildStage stage = Stage;
            SetStageRenderersEnabled(_frameStage, stage == BuildStage.Frame);
            SetStageRenderersEnabled(_platesStage, stage == BuildStage.Plates);
            SetStageRenderersEnabled(_doneStage, stage == BuildStage.Done);
        }

        private static void SetStageRenderersEnabled(Transform stageRoot, bool show)
        {
            if (stageRoot == null) return;
            Renderer[] renderers = stageRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = show;
        }

        /// <summary>The ticket's own "arms move while building" — a simple hinge sway, live only while
        /// a build is actually progressing. Cosmetic: never runs in an EditMode test (Unity's Update
        /// loop never ticks outside Play mode), so it cannot affect <see cref="Tick"/>'s own resolved
        /// state.</summary>
        private void LateUpdate()
        {
            if (_arms == null) return;
            bool building = IsAlive && _buildTimer > 0f && _buildTimer < BuildSeconds;
            for (int i = 0; i < _arms.Length; i++)
            {
                if (_arms[i] == null) continue;
                float sway = building ? Mathf.Sin(Time.time * 2.5f + i * Mathf.PI) * 12f : 0f;
                _arms[i].localRotation = Quaternion.Euler(sway, 0f, 0f);
            }
        }

        /// <summary>The finished robot steps onto the 2x2 m out pad, awake — placed through the exact
        /// same walkable-surface formula every arrival in the game uses (FactoryMouth's exit/muster
        /// points, snapped onto the loaded map's walkable surface), so it can never land inside cover or
        /// outside the area.</summary>
        private void PlaceOnOutPad(List<RobotEnemy> spawned)
        {
            if (spawned.Count == 0) return; // a population cap ate the spawn — nothing to place
            RobotEnemy e = spawned[0];

            Vector3 dir = transform.forward;
            Vector3 exit = FactoryMouth.ExitPoint(transform.position, dir, OutPadHalfWidth, transform.position.y);
            Vector3 raw = FactoryMouth.MusterPoint(exit, dir, 0);
            MapData map = EnemyNavigation.Map;
            Vector3 point = map != null
                ? map.SnapToWalkableSurface(transform.position, raw, OutPadHalfWidth * 0.5f)
                : raw;

            e.transform.position = point;
            if (dir.sqrMagnitude > 0.0001f) e.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // robots can't wreck their own cradle
            _health.TakeDamage(info.Amount);
        }

        /// <summary>MV-1248: everything a death does to this cradle EXCEPT the pickup drop/HUD signal/
        /// unlock-schedule bank (<see cref="OnDestroyed"/>'s own remainder) — pulled out so
        /// <see cref="ApplyCheckpointDestroyed"/> can silently re-apply a checkpoint's already-recorded
        /// destruction (a resume rebuilding the level from scratch) without re-granting loot or a second
        /// family reveal the player already banked on the run that actually earned it. Same split
        /// <see cref="Replicator.ApplyDestructionEffects"/> uses.</summary>
        private void ApplyDestructionEffects() =>
            // MV-1247: report to the census synchronously with the kill — same reasoning as
            // Replicator.ApplyDestructionEffects (a caller reacting to this same death needs this cradle
            // already counted, not one WorldRunner poll tick later). Idempotent.
            FactoryCensus.ReportCradleDestroyed(this);

        /// <summary>Scrap (a build was in progress — <see cref="_buildTimer"/> &gt; 0, read before this
        /// cycle ever reaches <see cref="BuildSeconds"/> and resets it): that robot is never spawned, and
        /// <see cref="ScrapParts"/> Parts drop at the bay instead. Idle (no build running, including the
        /// cap-paused case — both hold the timer at zero): the ordinary <see cref="PayoutCells"/>/
        /// <see cref="PayoutParts"/> destroy payout.</summary>
        private void OnDestroyed()
        {
            ApplyDestructionEffects();
            HudSignals.EmitFactoryDestroyed(transform.position);

            PickupDirector director = PickupDirector.EnsureInstalled();
            if (_buildTimer > 0f)
            {
                for (int i = 0; i < ScrapParts; i++)
                    director.PlacePickupAt(PickupKind.PowerCell, transform.position + ScatterOffset(i, ScrapParts));
            }
            else
            {
                for (int i = 0; i < PayoutCells; i++)
                    director.PlacePickupAt(PickupKind.PowerCellSecondary, transform.position + ScatterOffset(i, PayoutCells));
                for (int i = 0; i < PayoutParts; i++)
                    director.PlacePickupAt(PickupKind.PowerCell, transform.position + ScatterOffset(i, PayoutParts));
            }
            _buildTimer = 0f;

            // MV-1248: the unlock schedule — a LIVE kill only, never replayed on a checkpoint restore
            // (ApplyCheckpointDestroyed calls ApplyDestructionEffects alone), same "pay nothing again"
            // rule as the pickup drops just above.
            ApplyUnlockSchedule(FactoryCensus.CradlesDestroyed, director);
        }

        /// <summary>MV-1248: 2nd/4th cradle destroyed in World 5 banks SECONDARY's Orbitals family / the
        /// Sentinel family for THE RIG's next open (<see cref="MaxWorlds.Weapons.PendingMorphingModule.SetSecondaryFamily"/>/
        /// <see cref="MaxWorlds.Weapons.PendingMorphingModule.SetSentinelFamily"/>) — the same
        /// banked-and-revealed-at-RIG-open ceremony MV-1090's Rack Module already uses, never granted on
        /// the spot. Every even count from 6 on drops a bonus Supercell instead — there is no third
        /// family left to bank.</summary>
        private void ApplyUnlockSchedule(int cradlesDestroyed, PickupDirector director)
        {
            switch (cradlesDestroyed)
            {
                case 2:
                    PendingMorphingModule.SetSecondaryFamily();
                    break;
                case 4:
                    PendingMorphingModule.SetSentinelFamily();
                    break;
                default:
                    if (cradlesDestroyed >= 6 && cradlesDestroyed % 2 == 0)
                        director.PlacePickupAt(PickupKind.Supercell, transform.position);
                    break;
            }
        }

        /// <summary>MV-1248: a mid-run checkpoint's own re-application of this cradle's already-recorded
        /// destruction (<see cref="FactoryCensus.ApplyCheckpointDestroyedCradleIds"/>) — the exact
        /// "silent restore" split <see cref="Replicator.ApplyCheckpointDestroyed"/>/
        /// <see cref="MowerHutch.ApplyCheckpointDestroyed"/> use via
        /// <see cref="DestructibleHealth.SilentlyDestroy"/> instead of <see cref="TakeDamage"/>: a resume
        /// is restoring history, not scoring a new kill, so no pickup drop, HUD signal, or unlock-schedule
        /// bank fires a second time for a kill the player already banked on the run that earned it.</summary>
        public void ApplyCheckpointDestroyed()
        {
            if (_health == null || !_health.IsAlive) return;
            _health.SilentlyDestroy();
            ApplyDestructionEffects();
        }

        private static Vector3 ScatterOffset(int index, int total)
        {
            float angle = Mathf.Deg2Rad * (360f / Mathf.Max(1, total)) * index;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * DropScatterRadius;
        }
    }
}
