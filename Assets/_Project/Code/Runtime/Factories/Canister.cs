using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

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
        private const float CanisterHealth = 80f;           // MV-1417: doubled from 40
        private const float FuseDuration = 1.0f;           // ">= 60 frames" at 60fps
        private const float BlastRadius = 6f;               // MV-1421: doubled from 3 m (Lee, 23 Nov)
        private const float BlastFullRadius = 3f;           // full damage out to here...
        private const float BlastDamageInner = 120f;        // ...then a linear fall...
        private const float BlastDamageEdge = 60f;          // ...to this at BlastRadius
        private const float BlastQueryMargin = 1.5f;        // a big body's pivot can sit this far from its collider
        private const float ScorchLife = 5f;                // "a scorch decal that fades in 5 s" (AmbienceVfx.CanisterScorchLife)
        private const float HitFlashDuration = 0.08f;       // MV-1417: whole drum flashes white this long per hit
        private const float HitFlashBlend = 0.8f;           // ...at least 60% of the way to white
        private const float HealthBarWidth = 1.2f;          // MV-1417: was 0.8 m

        // --- Body proportions (ticket: "a drum 0.8 m across, 1.0 m tall") ---
        private const float DrumDiameter = WorldMapLoader.CanisterFootprint;
        private const float DrumHeight = WorldMapLoader.CanisterHeight;
        private const float BandHeight = 0.22f;
        private const float BandDiameterPad = 0.02f;   // sits a hair proud of the drum, never z-fights it
        private const float FlashesPerSecond = 6f;     // how fast the band blinks during the fuse

        // MV-1417: a red hazard drum that stands out in every world.
        private static readonly Color DrumColor = new Color32(0xB2, 0x1C, 0x18, 255);       // #B21C18
        private static readonly Color CapColor = new Color32(0xD6, 0x3A, 0x30, 255);        // lighter top cap
        private static readonly Color ShadeColor = new Color32(0x7E, 0x12, 0x10, 255);      // darker left shade
        private static readonly Color BandColor = new Color32(0xFF, 0xC4, 0x00, 255);       // #FFC400 yellow band
        private static readonly Color StripeColor = new Color(0.05f, 0.05f, 0.05f);         // black hazard stripes
        private static readonly Color FlashColor = Color.white;
        private static readonly Color ChevronColor = Color.white;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private DestructibleHealth _health;
        private Collider _collider;
        private Renderer _bodyRenderer;
        private Renderer _bandRenderer;
        private Renderer _chevronRenderer;
        private MaterialPropertyBlock _bandMpb;

        // MV-1417: the drum's red parts (body, cap, shade, band) that blend toward white on a hit, and
        // every cosmetic renderer built under Body (hidden together on destruction).
        private readonly List<Renderer> _tintParts = new List<Renderer>(4);
        private readonly List<Color> _tintBase = new List<Color>(4);
        private readonly List<Renderer> _allParts = new List<Renderer>(16);
        private float _flashLeft;
        private CanisterFx _fx;

        private bool _fusing;
        private float _fuseElapsed;
        private bool _exploded;

        // MV-1421: the blast front - it travels with the ring of fire (CanisterFx.RingSpeed) and each target
        // is damaged once, the moment the front reaches it.
        private bool _blasting;
        private float _blastFront;
        private Vector3 _blastPoint;
        private readonly HashSet<IDamageable> _blastHit = new HashSet<IDamageable>();

        /// <summary>MV-1417: the live explosion fireball (null before the blast and once it is gone).</summary>
        public GameObject Fireball => _fx?.Fireball;

        /// <summary>MV-1417: the fireball's resolved radius in metres (0 when there is none).</summary>
        public float FireballRadius => _fx?.FireballRadius ?? 0f;

        /// <summary>MV-1417: the root every transient effect hangs under (null when none is live).</summary>
        public GameObject FxRoot => _fx?.Root;

        /// <summary>MV-1417: the drum body's resolved colour as last pushed through its MaterialPropertyBlock.</summary>
        public Color DrumResolvedColor
        {
            get
            {
                if (_bodyRenderer == null) return Color.clear;
                var mpb = new MaterialPropertyBlock();
                _bodyRenderer.GetPropertyBlock(mpb);
                return mpb.GetColor(BaseColorId);
            }
        }

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
        public static void ResetRegistry()
        {
            foreach (Canister c in Registered) c?._fx?.Dispose(); // MV-1417: a level teardown must not strand live effects
            Registered.Clear();
        }

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
            WorldHealthBar.Attach(gameObject, this, DrumHeight * 0.5f + 0.6f, HealthBarWidth, showNumber: false);

            _fx = new CanisterFx(); // world-rooted on purpose: a scaled map parent would distort every piece

            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDestroy()
        {
            Registered.Remove(this);
            _fx?.Dispose();
        }

        private void BuildBody()
        {
            // The root primitive IS the drum (MapRuntime.BuildCanister spawns a Cylinder) — unlike
            // Pylon/Replicator, which hide a cosmetic-mismatched cube root, this one just gets tinted.
            _collider = GetComponent<Collider>();
            _bodyRenderer = GetComponent<Renderer>();
            _bandMpb = new MaterialPropertyBlock();
            if (_bodyRenderer != null)
            {
                Material drum = MaterialLibrary.Tinted(SurfaceKind.Metal, DrumColor);
                if (drum != null) _bodyRenderer.sharedMaterial = drum;
                RegisterTint(_bodyRenderer, DrumColor);
            }

            Transform bodyRoot = ParentScale.MakeMetreSpace(new GameObject("Body").transform, transform);
            float r = DrumDiameter * 0.5f;

            // Lighter top cap — a thin disc a hair above the drum's top face.
            Renderer cap = Part(bodyRoot, PrimitiveType.Cylinder, "Cap", new Vector3(0f, DrumHeight * 0.5f + 0.004f, 0f),
                new Vector3(DrumDiameter * 0.97f, 0.01f, DrumDiameter * 0.97f), Quaternion.identity, CapColor);
            RegisterTint(cap, CapColor);

            // Slightly darker left shade — a narrow strip hugging the drum's left flank.
            Renderer shade = Part(bodyRoot, PrimitiveType.Cube, "Shade",
                new Vector3(-Mathf.Sin(65f * Mathf.Deg2Rad) * r * 1.01f, 0f, Mathf.Cos(65f * Mathf.Deg2Rad) * r * 1.01f),
                new Vector3(0.1f, DrumHeight * 0.94f, 0.02f), Quaternion.Euler(0f, -65f, 0f), ShadeColor);
            RegisterTint(shade, ShadeColor);

            // Yellow band, the cylinder mesh is 2 units tall so half its height goes into the Y scale.
            _bandRenderer = Part(bodyRoot, PrimitiveType.Cylinder, "Band", Vector3.zero,
                new Vector3(DrumDiameter + BandDiameterPad, BandHeight * 0.5f, DrumDiameter + BandDiameterPad),
                Quaternion.identity, BandColor);
            RegisterTint(_bandRenderer, BandColor);

            // Black diagonal hazard stripes across the front arc of the band.
            float stripeR = r + BandDiameterPad * 0.5f;
            for (int i = 0; i < 7; i++)
            {
                float a = (i - 3) * 17f * Mathf.Deg2Rad;
                Part(bodyRoot, PrimitiveType.Cube, "Stripe", new Vector3(Mathf.Sin(a) * stripeR, 0f, Mathf.Cos(a) * stripeR),
                    new Vector3(0.04f, BandHeight * 1.28f, 0.012f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 40f), StripeColor);
            }

            // White hazard icon on the front: an up-arrow (diamond head + stem) above the band.
            _chevronRenderer = Part(bodyRoot, PrimitiveType.Cube, "Chevron",
                new Vector3(0f, 0.2f, r + 0.005f), new Vector3(0.16f, 0.16f, 0.02f), Quaternion.Euler(0f, 0f, 45f), ChevronColor);
            Part(bodyRoot, PrimitiveType.Cube, "ChevronStem", new Vector3(0f, 0.09f, r + 0.005f),
                new Vector3(0.07f, 0.16f, 0.02f), Quaternion.identity, ChevronColor);
        }

        /// <summary>One cosmetic primitive under the body root, on a tinted Metal surface; remembered so it
        /// is hidden (and marked hidden) when the drum is destroyed.</summary>
        private Renderer Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale,
            Quaternion localRot, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col); // cosmetic only — the drum's own collider is the real one
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            var rend = go.GetComponent<MeshRenderer>();
            Material m = MaterialLibrary.Tinted(SurfaceKind.Metal, color);
            if (m != null) rend.sharedMaterial = m;
            SetPartColor(rend, color);
            _allParts.Add(rend);
            return rend;
        }

        private void RegisterTint(Renderer r, Color baseColor)
        {
            _tintParts.Add(r);
            _tintBase.Add(baseColor);
            SetPartColor(r, baseColor);
        }

        private void SetPartColor(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_bandMpb);
            _bandMpb.SetColor(BaseColorId, c);
            _bandMpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_bandMpb);
        }

        /// <summary>Blend every red part toward white by <paramref name="amount"/> (0 = its own colour).
        /// The band is left to the fuse's blink while fusing.</summary>
        private void ApplyDrumTint(float amount)
        {
            for (int i = 0; i < _tintParts.Count; i++)
            {
                if (_fusing && _tintParts[i] == _bandRenderer) continue;
                SetPartColor(_tintParts[i], Color.Lerp(_tintBase[i], FlashColor, amount));
            }
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // always true for Team.Neutral — kept for symmetry with every other TakeDamage in this codebase
            HudSignals.EmitDamage(transform.position + Vector3.up * (DrumHeight * 0.5f + 0.3f), info.Amount);

            if (info.Amount > 0f)
            {
                // MV-1417: every hit flashes the whole drum and sprays sparks from the hit point.
                _flashLeft = HitFlashDuration;
                ApplyDrumTint(HitFlashBlend);
                Vector3 hit = info.Point;
                if ((hit - transform.position).sqrMagnitude > 4f) hit = transform.position;
                _fx?.HitSparks(hit);
            }

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
            _fx?.BeginFuse(FuseTop, transform.position);
        }

        private Vector3 FuseTop => transform.position + Vector3.up * (DrumHeight * 0.5f);

        /// <summary>Advances the fuse (and its band flash) toward <see cref="Explode"/> — dt-parameterised
        /// like <see cref="Pylon.TickCable"/>, called from this object's own <c>Update</c> in Play mode
        /// and directly by an EditMode test.</summary>
        public void Tick(float dt)
        {
            _fx?.Tick(dt);
            if (_blasting) AdvanceBlast(dt);

            if (_flashLeft > 0f)
            {
                _flashLeft -= dt;
                if (_flashLeft <= 0f) ApplyDrumTint(0f);
            }

            if (!_fusing || _exploded) return;

            _fuseElapsed += dt;
            _fx?.FuseTick(dt, FuseTop);

            bool flashOn = Mathf.FloorToInt(_fuseElapsed * FlashesPerSecond) % 2 == 0;
            SetBandColor(flashOn ? FlashColor : BandColor);

            if (_fuseElapsed >= FuseDuration)
            {
                Explode();
                AdvanceBlast(_fuseElapsed - FuseDuration); // any overshoot of this step already travels the front
            }
        }

        private void Explode()
        {
            _fusing = false;
            _exploded = true;

            Vector3 point = transform.position;
            _blasting = true;
            _blastFront = 0f;
            _blastPoint = point;
            _blastHit.Clear();
            _blastHit.Add(this);
            _fx?.Explode(point, BlastRadius);
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
            // MV-1416: hidden AND marked, or MapStaticBatchRoot.ApplyAreaGate re-enables them within 0.25 s.
            HideAndMark(_bodyRenderer);
            foreach (Renderer part in _allParts) HideAndMark(part);
        }

        private static void HideAndMark(Renderer r)
        {
            if (r == null) return;
            r.enabled = false;
            MapStaticBatchRoot.Active?.MarkPermanentlyHidden(r);
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

        // MV-1421: was 32 with mask ~0 - floor, walls and cover could fill every slot before Max or a robot
        // was reached, and OverlapSphereNonAlloc drops the rest silently.
        private static readonly Collider[] s_hits = new Collider[256];

        /// <summary>The damage a target <paramref name="distance"/> metres from the blast takes: 120 out to
        /// 3 m, then a linear fall to 60 at 6 m (MV-1421, Lee 23 Nov); 0 beyond the radius.</summary>
        public static float BlastDamageAt(float distance)
        {
            if (distance > BlastRadius) return 0f;
            if (distance <= BlastFullRadius) return BlastDamageInner;
            return Mathf.Lerp(BlastDamageInner, BlastDamageEdge, (distance - BlastFullRadius) / (BlastRadius - BlastFullRadius));
        }

        /// <summary>Advances the blast front by <paramref name="dt"/> and damages every not-yet-hit damageable
        /// it has reached - Max, Sentinels, robots (incl. captured allies), other canisters (so chains form),
        /// relays/sheds/cradles, destructible cover, a boss. <c>docs/DECISIONS.md</c> §4, "Hazards: world-run
        /// vs player-triggered" (Lee, 27 Oct 2026): set off by the PLAYER shooting the canister, so it is
        /// licensed to damage robots. <see cref="Team.Neutral"/> satisfies <see cref="DamageRules.Applies"/>
        /// against every target team. An active Force Field absorbs it through
        /// <c>PlayerHealth.AbsorbForceFieldDamage</c> like any other hit - correct, not a bug (MV-1421).
        /// The damageable is resolved like <see cref="RicochetDisc.TryResolveDamageable"/> (MV-1203) and then
        /// up the parent chain, and de-duplicated by damageable instance, not collider GameObject.</summary>
        private void AdvanceBlast(float dt)
        {
            if (!_blasting) return;
            _blastFront = Mathf.Min(BlastRadius, _blastFront + CanisterFx.RingSpeed * Mathf.Max(0f, dt));

            MapData map = EnemyNavigation.Map;

            // Physics.autoSyncTransforms is off project-wide (see GateSolidityTests) - force a sync so a
            // target that moved this very frame is still found (same reasoning as ForceFieldBubble).
            Physics.SyncTransforms();

            int count = Physics.OverlapSphereNonAlloc(_blastPoint, BlastRadius + BlastQueryMargin, s_hits, ~0,
                QueryTriggerInteraction.Ignore);
            if (count >= s_hits.Length)
                Debug.LogWarning($"Canister blast overlap buffer full ({s_hits.Length}); some colliders were not examined.");

            for (int i = 0; i < count; i++)
            {
                Collider hit = s_hits[i];
                if (hit == null) continue;
                if (!RicochetDisc.TryResolveDamageable(hit, out IDamageable d))
                    d = hit.GetComponentInParent<IDamageable>();
                if (d == null || !d.IsAlive || _blastHit.Contains(d)) continue;

                Transform tr = d is Component c ? c.transform : hit.transform;
                Vector3 offset = tr.position - _blastPoint;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > _blastFront) continue; // the ring of fire has not reached it yet
                if (!CombatLevel.SameLevel(map, _blastPoint, tr.position)) continue;

                _blastHit.Add(d);
                d.TakeDamage(new DamageInfo(BlastDamageAt(distance), _blastPoint, Vector3.up, Team.Neutral,
                    source: DamageSource.Environment));
            }

            if (_blastFront >= BlastRadius)
            {
                _blasting = false;
                _blastHit.Clear();
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
