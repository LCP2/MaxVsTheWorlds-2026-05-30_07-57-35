using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// Destructible cover (MV-1298) — a <see cref="WorldCover"/> piece authored with <c>hp &gt; 0</c>
    /// (0 stays indestructible, every existing piece, unchanged) takes damage from shots of ANY team it
    /// blocks and breaks through three visible stages: intact with a cracked texture, &lt;= 50% hp with
    /// deeper cracks, 0 is a short rubble burst leaving a flat, non-blocking rubble decal.
    ///
    /// Deliberately NOT a <see cref="MowerHutch"/>/<see cref="Replicator"/> subclass — a cover piece
    /// drops no loot, triggers no difficulty/finale wiring, and is counted by no census — same "reuse
    /// what's actually shared, nothing more" reasoning <see cref="Pylon"/>'s own doc comment gives for
    /// not subclassing <see cref="MowerHutch"/>. It reuses <see cref="DestructibleHealth"/> for HP and
    /// <see cref="HudSignals.EmitFittingDestroyed"/> for the break spark/SFX — the same small "coming
    /// apart", not a building, burst a shed corner fitting and a pylon already use.
    /// </summary>
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class DestructibleCover : MonoBehaviour, IDamageable
    {
        private const float DamagedNormalizedThreshold = 0.5f;
        private const float RubbleBurstLife = 0.5f;     // ticket: "a short rubble burst (<= 0.5 s)"
        private const float RubbleDecalHeight = 0.04f;  // flat, flush with the floor

        private static readonly Color IntactColor = new Color(0.55f, 0.53f, 0.50f);   // cracked concrete
        private static readonly Color DamagedColor = new Color(0.40f, 0.37f, 0.34f);  // deeper cracks
        private static readonly Color RubbleColor = new Color(0.34f, 0.31f, 0.29f);

        private DestructibleHealth _health;
        private Collider _collider;
        private Renderer _bodyRenderer;
        private MapEntity _entity;
        private float _configuredHp;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Neutral; // ticket: "takes damage from shots of ANY team"

        public string Id { get; private set; } = string.Empty;

        /// <summary>Public so an EditMode test can drive identity directly, same convention as every
        /// other post-AddComponent configure call in this codebase.</summary>
        public void SetId(string id) => Id = id ?? string.Empty;

        /// <summary>True once this piece has broken — public so an EditMode test can read the resolved
        /// state directly, same "post-build readout, never an authored constant" idiom every other
        /// Tier 2 assertion in this codebase uses.</summary>
        public bool IsBroken { get; private set; }

        // --- Static registry — same "the map builds them, Build() registers them, a fresh level wipes
        // them" shape as Pylon.Registered/Canister.Registered. ---
        private static readonly List<DestructibleCover> Registered = new List<DestructibleCover>(8);

        /// <summary>Wipe the registry. Called by <see cref="MapRuntime.Build"/> when a level starts
        /// building.</summary>
        public static void ResetRegistry() => Registered.Clear();

        /// <summary>The stable ids of every registered destructible cover piece already broken this run
        /// (MV-1298) — what a mid-run checkpoint capture persists so a resume can re-apply the exact
        /// same broken set onto the fresh instances a level rebuild creates. Same shape as
        /// <see cref="Canister.DestroyedIds"/>.</summary>
        public static string[] DestroyedIds()
        {
            var ids = new List<string>();
            foreach (DestructibleCover c in Registered)
                if (c != null && !c.IsAlive && !string.IsNullOrEmpty(c.Id)) ids.Add(c.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded breaks (MV-1298) onto whichever
        /// currently-registered, still-intact pieces carry a matching <see cref="Id"/> — called once by
        /// <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME. Same shape as
        /// <see cref="Canister.ApplyCheckpointDestroyedIds"/>. A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointDestroyedIds(IReadOnlyList<string> destroyedIds)
        {
            if (destroyedIds == null || destroyedIds.Count == 0) return;

            foreach (DestructibleCover c in Registered)
            {
                if (c == null || !c.IsAlive || string.IsNullOrEmpty(c.Id)) continue;
                for (int i = 0; i < destroyedIds.Count; i++)
                {
                    if (destroyedIds[i] != c.Id) continue;
                    c.ApplyCheckpointDestroyed();
                    break;
                }
            }
        }

        /// <summary>Wires this piece to its authored hp and the <see cref="MapEntity"/>
        /// <see cref="ZoneRouteGrid"/> rasterises from — called by <see cref="MapRuntime"/> right after
        /// AddComponent, same "post-build configure call" convention as <see cref="Pylon.Configure"/>.
        /// Order-independent of <see cref="Build"/> (either may run first, depending on whether
        /// AddComponent's own Awake fires, same reasoning <see cref="Pylon.SetFeedTarget"/>'s own doc
        /// comment gives) — <see cref="Build"/> re-derives <see cref="_health"/> from these fields
        /// whichever order they run in.</summary>
        public void Configure(MapEntity entity, float hp)
        {
            _entity = entity;
            _configuredHp = hp;
            if (_health != null) RebuildHealth();
        }

        private void Awake() => Build();

        /// <summary>Construct the health model. Exposed publicly, same reasoning as
        /// <see cref="Pylon.Build"/>/<see cref="Canister.Build"/> — Awake never runs as a side effect of
        /// AddComponent outside Play mode, so an EditMode test calls this directly.</summary>
        public void Build()
        {
            if (_health != null) return; // already built — AddComponent's Awake may or may not have run first

            _collider = GetComponent<Collider>();
            _bodyRenderer = GetComponent<Renderer>();
            RebuildHealth();
            SetStageTint(damaged: false);

            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDestroy() => Registered.Remove(this);

        private void RebuildHealth()
        {
            _health = new DestructibleHealth(Mathf.Max(1f, _configuredHp));
            _health.Destroyed += Break;
            _health.Changed += OnHealthChanged;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // always true for Team.Neutral — kept for symmetry with every other TakeDamage in this codebase
            _health.TakeDamage(info.Amount);
        }

        private void OnHealthChanged(float current)
        {
            if (_health == null) return;
            SetStageTint(_health.Normalized <= DamagedNormalizedThreshold);
        }

        private void SetStageTint(bool damaged)
        {
            if (_bodyRenderer == null) return;
            Material mat = MaterialLibrary.Tinted(SurfaceKind.Stone, damaged ? DamagedColor : IntactColor);
            if (mat != null) _bodyRenderer.sharedMaterial = mat;
        }

        private void Break()
        {
            ApplyBreakEffects();
            HudSignals.EmitFittingDestroyed(transform.position);
        }

        /// <summary>Everything a break does EXCEPT the live spark/SFX signal — pulled out so
        /// <see cref="ApplyCheckpointDestroyed"/> can silently re-apply a checkpoint's already-recorded
        /// break (a resume rebuilding the level from scratch) without replaying it, same split
        /// <see cref="Pylon"/>/<see cref="Canister"/> keep.</summary>
        private void ApplyBreakEffects()
        {
            if (IsBroken) return;
            IsBroken = true;

            if (_collider != null) _collider.enabled = false; // ticket: "disable its collider"
            if (_bodyRenderer != null) _bodyRenderer.enabled = false;
            gameObject.layer = 0; // Default — ticket: "remove it from CoverLayer"

            // Ticket Change 3: mark the MapEntity broken so ZoneRouteGrid no longer rasterises it, then
            // call Forget() so robots path through the gap — the same call gates already make
            // (EnemyNavigation.cs's RegisterGate, Opened/Closed -> ZoneRouteGrid.Forget).
            if (_entity != null) _entity.broken = true;
            ZoneRouteGrid.Forget();

            BuildRubbleDecal();
        }

        /// <summary>MV-1298: silently applies a checkpoint's already-recorded break — called once, on
        /// RESUME, by <see cref="ApplyCheckpointDestroyedIds"/>. Skips the live-combat
        /// <see cref="Break"/> path's spark/SFX signal entirely (via <see cref="DestructibleHealth.SilentlyDestroy"/>
        /// instead of <see cref="TakeDamage"/>) — a resume is restoring history, not scoring a new
        /// break.</summary>
        public void ApplyCheckpointDestroyed()
        {
            if (_health == null || !_health.IsAlive) return;
            _health.SilentlyDestroy();
            ApplyBreakEffects();
        }

        private void BuildRubbleDecal()
        {
            Vector3 footprint = transform.localScale;

            var decal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            decal.name = "Rubble";
            var col = decal.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col); // ticket: "a flat rubble decal that does not block"
            decal.transform.SetParent(transform.parent, worldPositionStays: false);
            decal.transform.position = new Vector3(transform.position.x, RubbleDecalHeight * 0.5f, transform.position.z);
            decal.transform.rotation = Quaternion.identity;
            float span = Mathf.Max(footprint.x, footprint.z);
            decal.transform.localScale = new Vector3(span, RubbleDecalHeight, span);

            var rend = decal.GetComponent<MeshRenderer>();
            Material mat = MaterialLibrary.Tinted(SurfaceKind.Stone, RubbleColor);
            if (mat != null) rend.sharedMaterial = mat;

            decal.AddComponent<RubbleBurstVfx>().Begin(decal.transform, RubbleBurstLife);
        }

        /// <summary>Pops the rubble decal up from a fraction of its own resting footprint to full size
        /// over <see cref="RubbleBurstLife"/> — ticket: "breaks into a short rubble burst (&lt;= 0.5 s)".
        /// Self-contained, same "no owning component left alive to drive it" shape as
        /// <see cref="Canister"/>'s own <c>BlastRingLifetimeVfx</c> (this cover's own body is already
        /// hidden by the time this runs).</summary>
        [MaxWorlds.Core.PerfSection("factories")]
        private sealed class RubbleBurstVfx : MonoBehaviour
        {
            private const float StartFraction = 0.2f;

            private Vector3 _restScale;
            private float _life;
            private float _t;

            public void Begin(Transform decal, float life)
            {
                _restScale = decal.localScale;
                _life = life;
                decal.localScale = new Vector3(_restScale.x * StartFraction, _restScale.y, _restScale.z * StartFraction);
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float u = Mathf.Clamp01(_life > 0f ? _t / _life : 1f);
                float eased = 1f - (1f - u) * (1f - u);
                Vector3 start = new Vector3(_restScale.x * StartFraction, _restScale.y, _restScale.z * StartFraction);
                transform.localScale = Vector3.Lerp(start, _restScale, eased);
                if (u >= 1f) Destroy(this);
            }
        }
    }
}
