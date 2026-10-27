using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// An explosive canister (MV-1296) — a PLAYER-TRIGGERED hazard, not a world-run one: shoot it to
    /// 0 HP and, unlike a world-run hazard such as the plasma vent's own MV-924 constraint, its blast is
    /// explicitly licensed to damage or remove robots too — that is its whole point (<c>docs/DECISIONS.md</c>
    /// §4, "Hazards: world-run vs player-triggered", Lee 27 Oct 2026). A boss takes the blast's damage
    /// like anything else; nothing here "removes" it any more than any other lethal hit does — a boss's
    /// own multi-phase defeat flow is what decides what happens to it, same as a weapon hit would.
    ///
    /// Reaching 0 HP does NOT fire an immediate death the way every other <see cref="DestructibleHealth"/>
    /// user in this codebase does: the ticket's own "does not vanish — it sparks and its band flashes
    /// white for 1.0 s, then explodes" is a fuse, held here rather than in the shared health model, which
    /// stays a plain "hit zero, fire once" primitive. <see cref="Tick"/> drives that fuse and, once it
    /// elapses, the blast sweep — dt-parameterised and called from this object's own <c>Update</c> in
    /// Play mode, same "Update calls it with Time.deltaTime, a test calls it directly" idiom
    /// <see cref="Pylon.TickCable"/> already uses.
    /// </summary>
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class Canister : MonoBehaviour, IDamageable, IHealthReadout
    {
        // --- Ticket's own numbers ---
        private const float CanisterHealth = 40f;
        private const float FuseDuration = 1.0f;           // ">= 60 frames" at 60fps
        private const float BlastRadius = 3f;
        private const float BlastDamage = 60f;
        private const float BlastRingLifetime = 10f / 60f;  // "expanding to 3 m over ~10 frames"
        private const float ScorchLife = 5f;                // "a scorch decal that fades in 5 s" (AmbienceVfx.CanisterScorchLife)

        // --- Body proportions (ticket: "a drum 0.8 m across, 1.0 m tall") ---
        private const float DrumDiameter = WorldMapLoader.CanisterFootprint;
        private const float DrumHeight = WorldMapLoader.CanisterHeight;
        private const float BandHeight = 0.22f;
        private const float BandDiameterPad = 0.02f;   // sits a hair proud of the drum, never z-fights it
        private const float ChevronWidth = 0.34f;
        private const float ChevronHeight = 0.5f;
        private const float FlashesPerSecond = 6f;     // how fast the band blinks during the fuse

        private static readonly Color SteelColor = new Color(0.20f, 0.21f, 0.23f);          // "dark steel"
        private static readonly Color BandColor = new Color(0.95f, 0.45f, 0.08f);           // "bright orange hazard band"
        private static readonly Color FlashColor = Color.white;
        private static readonly Color ChevronColor = Color.white;
        private static readonly Color BlastRingColor = new Color(1f, 0.74f, 0.42f, 0.9f);   // "orange-white ring"

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private DestructibleHealth _health;
        private Collider _collider;
        private Renderer _bodyRenderer;
        private Renderer _bandRenderer;
        private Renderer _chevronRenderer;
        private MaterialPropertyBlock _bandMpb;

        private bool _fusing;
        private float _fuseElapsed;
        private bool _exploded;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Neutral; // ticket: "takes damage from any team"
        public float Normalized => _health?.Normalized ?? 0f;

        // --- IHealthReadout — the same world health bar every destructible in this codebase carries. ---
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;
        public string ReadoutName => "CANISTER";

        /// <summary>True once this canister has taken lethal damage and is counting down its 1.0 s fuse
        /// (ticket AC1) — public so an EditMode test can read the resolved state directly, same
        /// "post-build readout, never an authored constant" idiom every other Tier 2 assertion in this
        /// codebase uses.</summary>
        public bool IsFusing => _fusing;

        /// <summary>This canister's stable id (MV-1296) — same "a checkpoint can persist/restore by id"
        /// reasoning as <see cref="Pylon.Id"/>/<see cref="Replicator.Id"/>. Empty until <see cref="SetId"/>
        /// is called.</summary>
        public string Id { get; private set; } = string.Empty;

        /// <summary>Public so an EditMode test can drive identity directly, same convention as every other
        /// post-AddComponent configure call in this codebase.</summary>
        public void SetId(string id) => Id = id ?? string.Empty;

        // --- Static registry — same "the map builds them, Build() registers them, a fresh level wipes
        // them" shape as Pylon.Registered. ---
        private static readonly List<Canister> Registered = new List<Canister>(8);

        /// <summary>Wipe the registry. Called by <see cref="MapRuntime.Build"/> when a level starts
        /// building — see <see cref="Pylon.ResetRegistry"/>'s own doc comment for why this can't be
        /// named <c>Reset</c>.</summary>
        public static void ResetRegistry() => Registered.Clear();

        /// <summary>The stable ids of every registered canister already destroyed this run (MV-1296) —
        /// what a mid-run checkpoint capture persists so a resume can re-apply the exact same destroyed
        /// set onto the fresh instances a level rebuild creates. Same shape as <see cref="Pylon.DestroyedIds"/>.</summary>
        public static string[] DestroyedIds()
        {
            var ids = new List<string>();
            foreach (Canister c in Registered)
                if (c != null && !c.IsAlive && !string.IsNullOrEmpty(c.Id)) ids.Add(c.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded canister destruction (MV-1296) onto
        /// whichever currently-registered, still-alive canisters carry a matching <see cref="Id"/> —
        /// called once by <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME. Same shape
        /// as <see cref="Pylon.ApplyCheckpointDestroyedIds"/>. A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointDestroyedIds(IReadOnlyList<string> destroyedIds)
        {
            if (destroyedIds == null || destroyedIds.Count == 0) return;

            foreach (Canister c in Registered)
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

        private void Awake() => Build();

        /// <summary>Construct the health model and the greybox body. Exposed publicly, same reasoning as
        /// <see cref="Pylon.Build"/> — Awake never runs as a side effect of AddComponent outside Play
        /// mode, so an EditMode test calls this directly.</summary>
        public void Build()
        {
            if (_health != null) return; // already built — AddComponent's Awake may or may not have run first

            _health = new DestructibleHealth(CanisterHealth);
            _health.Destroyed += StartFuse;

            BuildBody();
            WorldHealthBar.Attach(gameObject, this, DrumHeight * 0.5f + 0.6f, 0.8f, showNumber: false);

            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDestroy() => Registered.Remove(this);

        private void BuildBody()
        {
            // The root primitive IS the drum (MapRuntime.BuildCanister spawns a Cylinder) — unlike
            // Pylon/Replicator, which hide a cosmetic-mismatched cube root, this one just gets tinted.
            _collider = GetComponent<Collider>();
            _bodyRenderer = GetComponent<Renderer>();
            if (_bodyRenderer != null)
            {
                Material steel = MaterialLibrary.Tinted(SurfaceKind.Metal, SteelColor);
                if (steel != null) _bodyRenderer.sharedMaterial = steel;
            }

            Transform bodyRoot = ParentScale.MakeMetreSpace(new GameObject("Body").transform, transform);

            var bandGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bandGo.name = "Band";
            var bandCol = bandGo.GetComponent<Collider>();
            if (bandCol != null) Object.DestroyImmediate(bandCol); // cosmetic only — the drum's own collider is the real one
            bandGo.transform.SetParent(bodyRoot, false);
            bandGo.transform.localPosition = Vector3.zero;
            // The cylinder mesh is 2 units tall, so half its height goes into the Y scale (same
            // compensation MapRuntime.BuildCanister's own root cylinder needs).
            bandGo.transform.localScale = new Vector3(DrumDiameter + BandDiameterPad, BandHeight * 0.5f, DrumDiameter + BandDiameterPad);
            _bandRenderer = bandGo.GetComponent<MeshRenderer>();
            Material band = MaterialLibrary.Tinted(SurfaceKind.Metal, BandColor);
            if (band != null) _bandRenderer.sharedMaterial = band;
            _bandMpb = new MaterialPropertyBlock();

            var chevronGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chevronGo.name = "Chevron";
            var chevronCol = chevronGo.GetComponent<Collider>();
            if (chevronCol != null) Object.DestroyImmediate(chevronCol);
            chevronGo.transform.SetParent(bodyRoot, false);
            chevronGo.transform.localPosition = new Vector3(0f, 0f, DrumDiameter * 0.5f);
            chevronGo.transform.localScale = new Vector3(ChevronWidth, ChevronHeight, 0.02f);
            _chevronRenderer = chevronGo.GetComponent<MeshRenderer>();
            Material chevron = MaterialLibrary.Tinted(SurfaceKind.Metal, ChevronColor);
            if (chevron != null) _chevronRenderer.sharedMaterial = chevron;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // always true for Team.Neutral — kept for symmetry with every other TakeDamage in this codebase
            HudSignals.EmitDamage(transform.position + Vector3.up * (DrumHeight * 0.5f + 0.3f), info.Amount);
            _health.TakeDamage(info.Amount);
        }

        /// <summary>Fired once, by <see cref="DestructibleHealth.Destroyed"/>, the instant this canister
        /// reaches 0 HP (from a direct hit OR a chained blast — <see cref="BlastSweep"/> calls
        /// <see cref="TakeDamage"/> on a neighbouring canister exactly like any other target). Ticket:
        /// "does not vanish ... sparks" — the body and collider are left exactly as they are, still solid,
        /// until <see cref="Explode"/> actually fires 1.0 s later.</summary>
        private void StartFuse()
        {
            _fusing = true;
            _fuseElapsed = 0f;
        }

        /// <summary>Advances the fuse (and its band flash) toward <see cref="Explode"/> — dt-parameterised
        /// like <see cref="Pylon.TickCable"/>, called from this object's own <c>Update</c> in Play mode
        /// and directly by an EditMode test.</summary>
        public void Tick(float dt)
        {
            if (!_fusing || _exploded) return;

            _fuseElapsed += dt;

            bool flashOn = Mathf.FloorToInt(_fuseElapsed * FlashesPerSecond) % 2 == 0;
            SetBandColor(flashOn ? FlashColor : BandColor);

            if (_fuseElapsed >= FuseDuration) Explode();
        }

        private void Explode()
        {
            _fusing = false;
            _exploded = true;

            Vector3 point = transform.position;
            BlastSweep(point);
            PlayBlastRing(point);
            HudSignals.EmitCanisterDetonated(point);

            ApplyDestructionEffects();
        }

        /// <summary>MV-1296: silently applies a checkpoint's already-recorded destruction — called once,
        /// on RESUME, by <see cref="ApplyCheckpointDestroyedIds"/>. Skips the live fuse/blast path
        /// entirely (via <see cref="DestructibleHealth.SilentlyDestroy"/> instead of <see cref="TakeDamage"/>)
        /// — a resume is restoring history, not re-chaining an explosion that already happened.</summary>
        public void ApplyCheckpointDestroyed()
        {
            if (_health == null || !_health.IsAlive) return;
            _health.SilentlyDestroy();
            _fusing = false;
            _exploded = true;
            ApplyDestructionEffects();
        }

        /// <summary>Ticket: "Pooled; a destroyed canister leaves only the scorch" — the drum, band and
        /// chevron are hidden and the collider dropped rather than the whole GameObject destroyed, so the
        /// registry (<see cref="DestroyedIds"/>) and this instance's own id survive for a checkpoint.</summary>
        private void ApplyDestructionEffects()
        {
            if (_collider != null) _collider.enabled = false;
            if (_bodyRenderer != null) _bodyRenderer.enabled = false;
            if (_bandRenderer != null) _bandRenderer.enabled = false;
            if (_chevronRenderer != null) _chevronRenderer.enabled = false;
        }

        private void SetBandColor(Color c)
        {
            if (_bandRenderer == null) return;
            _bandRenderer.GetPropertyBlock(_bandMpb);
            _bandMpb.SetColor(BaseColorId, c);
            _bandMpb.SetColor(ColorId, c);
            _bandRenderer.SetPropertyBlock(_bandMpb);
        }

        // ---------------------------------------------------------------- Blast (ticket Change 4)

        private static readonly Collider[] s_hits = new Collider[32];
        private static readonly HashSet<int> s_hitIds = new HashSet<int>();

        /// <summary>Every damageable within <see cref="BlastRadius"/> on the same combat level takes
        /// <see cref="BlastDamage"/> — Max, Sentinels, robots (incl. captured allies), other canisters
        /// (so chains form), relays/sheds/cradles, a boss. <c>docs/DECISIONS.md</c> §4, "Hazards:
        /// world-run vs player-triggered" (Lee, 27 Oct 2026): this hazard was set off by the PLAYER
        /// shooting the canister, so — unlike a world-run hazard such as the plasma vent's own MV-924
        /// constraint — it is explicitly licensed to damage robots too. <see cref="Team.Neutral"/> already
        /// satisfies <see cref="DamageRules.Applies"/> against every target team, so no further
        /// team-branching is needed to reach any of them.</summary>
        private void BlastSweep(Vector3 point)
        {
            MapData map = EnemyNavigation.Map;

            // Physics.autoSyncTransforms is off project-wide (see GateSolidityTests) — force a sync so
            // a target that moved this very frame (or a Rusher built straight into the blast, never
            // ticked by a physics step at all, the exact EditMode test shape) is still found, same
            // reasoning ForceFieldBubble's own build already gives for the same call.
            Physics.SyncTransforms();

            s_hitIds.Clear();
            int count = Physics.OverlapSphereNonAlloc(point, BlastRadius, s_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = s_hits[i];
                if (hit == null) continue;
                if (!s_hitIds.Add(hit.gameObject.GetInstanceID())) continue;
                if (!hit.TryGetComponent<IDamageable>(out var d) || !d.IsAlive) continue;
                if (!CombatLevel.SameLevel(map, point, hit.transform.position)) continue;

                d.TakeDamage(new DamageInfo(BlastDamage, point, Vector3.up, Team.Neutral,
                    source: DamageSource.Environment));
            }
        }

        private static void PlayBlastRing(Vector3 point)
        {
            GroundRing ring = GroundRing.Create("CanisterBlastRing");
            ring.gameObject.AddComponent<BlastRingLifetimeVfx>().Begin(ring, point, BlastRadius, BlastRingLifetime);
        }

        /// <summary>Grows the blast ring to <see cref="BlastRadius"/> over <see cref="BlastRingLifetime"/>,
        /// fading it out, then destroys its own GameObject — same self-contained shape as
        /// <c>MaxWorlds.Weapons.RocketImpactVfx.RingLifetime</c> (a detonation has no owning component
        /// left alive to drive it once this canister's body is hidden).</summary>
        [MaxWorlds.Core.PerfSection("factories")]
        private sealed class BlastRingLifetimeVfx : MonoBehaviour
        {
            private const float RingStartRadius = 0.05f;

            private GroundRing _ring;
            private Vector3 _origin;
            private float _maxRadius;
            private float _life;
            private float _t;

            public void Begin(GroundRing ring, Vector3 origin, float maxRadius, float life)
            {
                _ring = ring;
                _origin = origin;
                _maxRadius = maxRadius;
                _life = life;
                _ring.Show(_origin, RingStartRadius, BlastRingColor);
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float f = Mathf.Clamp01(_t / _life);
                _ring.Show(_origin, Mathf.Lerp(RingStartRadius, _maxRadius, f),
                    new Color(BlastRingColor.r, BlastRingColor.g, BlastRingColor.b, BlastRingColor.a * (1f - f)));
                if (f >= 1f)
                {
                    _ring.Hide();
                    Destroy(gameObject);
                }
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
