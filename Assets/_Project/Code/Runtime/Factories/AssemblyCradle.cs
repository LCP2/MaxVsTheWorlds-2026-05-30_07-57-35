using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

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
    public sealed class AssemblyCradle : MonoBehaviour, IDamageable, IFactoryBody
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

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
        private float _buildTimer;
        private bool _maxInArea;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Max-side damage (Light Missile, Orbitals, sentinels, ...) can hit it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

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

            _buildTimer += dt;
            if (_buildTimer < BuildSeconds) return;

            _buildTimer = 0f;
            if (!_spawner.Cadence.TryNextKind(out EnemyKind kind)) return; // this area authors nothing

            List<RobotEnemy> spawned = _spawner.SpawnExact(kind, 1, ignorePerFactoryCap: true);
            PlaceOnOutPad(spawned);
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
