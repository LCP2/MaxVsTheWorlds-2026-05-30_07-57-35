using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.VFX;

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
        }

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

        /// <summary>Scrap (a build was in progress — <see cref="_buildTimer"/> &gt; 0, read before this
        /// cycle ever reaches <see cref="BuildSeconds"/> and resets it): that robot is never spawned, and
        /// <see cref="ScrapParts"/> Parts drop at the bay instead. Idle (no build running, including the
        /// cap-paused case — both hold the timer at zero): the ordinary <see cref="PayoutCells"/>/
        /// <see cref="PayoutParts"/> destroy payout.</summary>
        private void OnDestroyed()
        {
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
        }

        private static Vector3 ScatterOffset(int index, int total)
        {
            float angle = Mathf.Deg2Rad * (360f / Mathf.Max(1, total)) * index;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * DropScatterRadius;
        }
    }
}
