using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1018: Anchorhead's generated body — World 3's a30 final boss, a salvage/dredging robot built
    /// around a ship's anchor, dormant in a flooded cargo hold. Replaces the reused mower/Brood-Hulk
    /// body (<see cref="BigBermudaRig"/>) for this one boss only, selected by
    /// <see cref="MaxWorlds.Arena.MapRuntime.BuildBoss"/> off the entity's own authored id
    /// ("anchorhead"), not by world index.
    ///
    /// Built from <see cref="CharacterMeshes"/>'s lathe/prism/beam vocabulary only — no
    /// <c>GameObject.CreatePrimitive</c> on the character itself, the same "not Unity primitives" rule
    /// <see cref="SludgequeenRig"/>'s own header already states for its boss: a hulking humanoid torso
    /// and shoulders, chains hanging off the arms, and a ship's anchor forming the head — the shank and
    /// stock cross the crown, the two flukes sweep down into a face, two amber eyes set into them. Rust-
    /// brown and charcoal body; amber eyes; the one hazard-orange note is the floor ring that rings out
    /// the anchor-slam telegraph (<see cref="AnchorheadBoss.TelegraphProgress01"/>) — nowhere else.
    ///
    /// Unparented and FOLLOWS the boss each LateUpdate (yaw only), same reasoning as
    /// <see cref="BigBermudaRig.Follow"/>: <see cref="CharacterSkinDirector"/> only claims renderers
    /// under an <see cref="MaxWorlds.Core.IDamageable"/>, and a rig kept outside that hierarchy is what
    /// dodges it (MV-527) rather than having this director's own flat-colour sweep fight the tell
    /// writes below over the same MaterialPropertyBlock.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("rig")]
    public sealed class AnchorheadRig : MonoBehaviour
    {
        /// <summary>Give <paramref name="boss"/>/<paramref name="anchor"/> their own rig, bound to them
        /// alone — same per-instance reasoning as <see cref="BigBermudaRig.CreateFor"/> (MV-573).</summary>
        public static AnchorheadRig CreateFor(BigBermudaBoss boss, AnchorheadBoss anchor)
        {
            if (boss == null || anchor == null) return null;
            var rig = new GameObject("AnchorheadRig").AddComponent<AnchorheadRig>();
            rig.Bind(boss, anchor);
            return rig;
        }

        // ---------------------------------------------------------------- palette

        private static readonly Color RustBrown = new Color(0.34f, 0.20f, 0.11f);
        private static readonly Color Charcoal = new Color(0.11f, 0.11f, 0.12f);
        private static readonly Color EyeAmber = new Color(1f, 0.66f, 0.12f);
        private static readonly Color EyeRage = new Color(0.95f, 0.10f, 0.05f);
        private static readonly Color HazardOrange = new Color(1f, 0.42f, 0.05f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ---------------------------------------------------------------- dimensions
        //
        // Authored for the boss's own real footprint (world3_config.json's a30 boss, 4.5 x 4.5) — this
        // boss is only ever built once, by id, so there is no cross-world reuse to normalise against the
        // way BigBermudaRig's LegacyAuthoredBodyWidth has to.
        private const float AuthoredBodyWidth = 4.5f;

        private const float HipY = 0.85f, ShoulderY = 2.55f;
        private const float TorsoBottomR = 0.72f, TorsoTopR = 1.05f;
        private const float ShoulderOffsetX = 1.25f;
        private const float HeadBaseY = 2.75f, HeadTopY = 3.55f;
        private const float FlukeOffsetX = 0.62f, FlukeY = 2.35f;
        private const float LegSpreadX = 0.62f;

        // ---------------------------------------------------------------- state

        private BigBermudaBoss _boss;
        private AnchorheadBoss _anchor;

        private Material _rustMat;
        private Material _charcoalMat;

        private readonly MeshRenderer[] _eyes = new MeshRenderer[2];
        private MeshRenderer _telegraphRing;
        private MaterialPropertyBlock _portMpb;

        private Color _eyeColor = EyeAmber;
        private float _flash;
        private float _lastHealth = 1f;

        private bool _dying;
        private float _dieTimer;

        /// <summary>The boss this rig is bound to — for a test to prove distinct rigs bind to distinct
        /// bosses (MV-573's own multi-boss guarantee), and specifically that a30 carries THIS rig and
        /// not a reused <see cref="BigBermudaRig"/>.</summary>
        public BigBermudaBoss Boss => _boss;

        private void OnEnable()
        {
            HudSignals.BossHealthChanged += OnHealth;
            HudSignals.BossKilled += OnKilled;
        }

        private void OnDisable()
        {
            HudSignals.BossHealthChanged -= OnHealth;
            HudSignals.BossKilled -= OnKilled;
        }

        private void Bind(BigBermudaBoss boss, AnchorheadBoss anchor)
        {
            _boss = boss;
            _anchor = anchor;

            gameObject.AddComponent<KeepsOwnMaterial>();
            _portMpb = new MaterialPropertyBlock();
            BuildMaterials();

            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            // Scale the whole rig to the boss it binds (MV-613's own reasoning) — a no-op fraction
            // today (both are 4.5), but correct if a30's own authored size ever changes.
            transform.localScale = Vector3.one * (_boss.transform.localScale.x / AuthoredBodyWidth);

            Build();
            Follow();

            _boss.FitColliderTo(RenderedBoundsRelativeTo(_boss.transform));
        }

        private Bounds RenderedBoundsRelativeTo(Transform reference)
        {
            var renderers = GetComponentsInChildren<MeshRenderer>();
            Bounds world = renderers.Length > 0
                ? renderers[0].bounds
                : new Bounds(transform.position, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

            Vector3 c = world.center, e = world.extents;
            var local = new Bounds(reference.InverseTransformPoint(c), Vector3.zero);
            for (int xi = -1; xi <= 1; xi += 2)
                for (int yi = -1; yi <= 1; yi += 2)
                    for (int zi = -1; zi <= 1; zi += 2)
                        local.Encapsulate(reference.InverseTransformPoint(c + Vector3.Scale(e, new Vector3(xi, yi, zi))));
            return local;
        }

        private void BuildMaterials()
        {
            var character = MaterialLibrary.Character();
            _rustMat = NewCharacterMaterial(character, "Anchorhead_Rust", RustBrown);
            _charcoalMat = NewCharacterMaterial(character, "Anchorhead_Charcoal", Charcoal);
        }

        private static Material NewCharacterMaterial(Material template, string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58) — see
            // BigBermudaRig.NewCharacterMaterial's own doc for the full MV-651 reasoning this mirrors.
            if (template == null && MaterialLibrary.SurfaceShader == null) return null;
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);

            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty(EmissionId))
            {
                m.SetColor(EmissionId, Color.black);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            return m;
        }

        private void Build()
        {
            BuildLegs();
            BuildTorso();
            BuildArmsAndChains();
            BuildAnchorHead();
            BuildTelegraphRing();
        }

        /// <summary>A pair of squat, heavy legs — mostly hidden under the torso from the 72° camera, but
        /// still what plants the silhouette's footprint.</summary>
        private void BuildLegs()
        {
            foreach (float side in new[] { -1f, 1f })
            {
                // Beam is centred on its own local origin (ground to hip spans -height/2..+height/2) --
                // placing that centre at half the hip height puts its base on the ground and its top at
                // the hip, same "centre at the midpoint" convention BigBermudaRig's own LegBone uses.
                Vector3 at = new Vector3(side * LegSpreadX, HipY * 0.5f, 0f);
                CharacterPart.Add(transform, CharacterMeshes.Beam(HipY, 0.30f, 0.24f, 8),
                    _charcoalMat, at, Quaternion.identity, Vector3.one, "Leg");
            }
        }

        /// <summary>The torso — a tapered prism from hip to shoulder, widening upward into the
        /// shoulders (a hulking mass, per the brief), plus two shoulder pauldrons proud of its flanks.</summary>
        private void BuildTorso()
        {
            float height = ShoulderY - HipY;
            CharacterPart.Add(transform, CharacterMeshes.Prism(8, TorsoBottomR, TorsoTopR, height, 0.14f),
                _charcoalMat, new Vector3(0f, HipY + height * 0.5f, 0f), Quaternion.identity, Vector3.one, "Torso");

            foreach (float side in new[] { -1f, 1f })
            {
                CharacterPart.Add(transform, CharacterMeshes.Prism(8, 0.55f, 0.48f, 0.6f, 0.16f),
                    _rustMat, new Vector3(side * ShoulderOffsetX, ShoulderY - 0.1f, 0f),
                    Quaternion.Euler(0f, 0f, side * 90f), Vector3.one, "Shoulder");
            }
        }

        /// <summary>Two tapered arms hanging from the shoulders, each ending in a blunt fist with a
        /// short chain of rings dangling further below it — "chains hanging from the arms" (the brief's
        /// own words), built the same washer shape <see cref="CharacterMeshes.Ring"/> already gives
        /// <see cref="FactoryBodies"/>'s valve-wheel rim.</summary>
        private void BuildArmsAndChains()
        {
            const float armLength = 1.15f;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 shoulder = new Vector3(side * ShoulderOffsetX, ShoulderY - 0.2f, 0f);
                Vector3 fist = shoulder + new Vector3(side * 0.12f, -armLength, 0f);

                CharacterPart.Add(transform, CharacterMeshes.Beam(armLength, 0.24f, 0.16f, 8),
                    _charcoalMat, Vector3.Lerp(shoulder, fist, 0.5f),
                    Quaternion.FromToRotation(Vector3.up, (fist - shoulder).normalized), Vector3.one, "Arm");

                CharacterPart.Add(transform, CharacterMeshes.Prism(6, 0.20f, 0.18f, 0.26f, 0.18f),
                    _rustMat, fist, Quaternion.identity, Vector3.one, "Fist");

                for (int i = 0; i < 3; i++)
                {
                    Vector3 at = fist + Vector3.down * (0.16f + i * 0.14f);
                    CharacterPart.Add(transform, CharacterMeshes.Ring(0.05f, 0.09f, 0.035f),
                        _rustMat, at, Quaternion.Euler(90f, 0f, 0f), Vector3.one, "ChainLink");
                }
            }
        }

        /// <summary>
        /// The head — a ship's anchor. A vertical SHANK rising off the torso, a horizontal STOCK
        /// crossing it near the top (the classic anchor silhouette), a RING at the crown, and two
        /// FLUKES sweeping down and out from the shank's base into the boss's face — the two amber eyes
        /// sit in the flukes, full glowing spheres (not lenses on a vertical face, which BigBermudaRig's
        /// own doc notes vanish edge-on under this game's fixed top-down camera).
        /// </summary>
        private void BuildAnchorHead()
        {
            float shankHeight = HeadTopY - HeadBaseY;
            CharacterPart.Add(transform, CharacterMeshes.Beam(shankHeight, 0.20f, 0.15f, 8),
                _rustMat, new Vector3(0f, HeadBaseY + shankHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "AnchorShank");

            // The stock -- a short crossbar through the shank, near the top.
            float stockY = HeadBaseY + shankHeight * 0.72f;
            CharacterPart.Add(transform, CharacterMeshes.Beam(0.7f, 0.09f, 0.07f, 6),
                _charcoalMat, new Vector3(0f, stockY, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one, "AnchorStock");

            // The ring at the crown -- oriented face-forward (the same "lying flat, rotate X by 90 to
            // face it" idiom SludgequeenRig.PortRing uses) so it actually reads as a ring, not a disc,
            // from the 72 degree camera.
            CharacterPart.Add(transform, CharacterMeshes.Ring(0.14f, 0.24f, 0.07f),
                _charcoalMat, new Vector3(0f, HeadTopY + 0.08f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one, "AnchorRing");

            // The two flukes -- swept down and out from the shank's base, the anchor's own "face".
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 from = new Vector3(0f, HeadBaseY, 0f);
                Vector3 to = new Vector3(side * FlukeOffsetX, FlukeY, 0.28f);
                CharacterPart.Add(transform, CharacterMeshes.Beam(Vector3.Distance(from, to), 0.16f, 0.09f, 8),
                    _rustMat, Vector3.Lerp(from, to, 0.5f),
                    Quaternion.FromToRotation(Vector3.up, (to - from).normalized), Vector3.one, "Fluke");

                int i = side < 0f ? 0 : 1;
                _eyes[i] = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(12),
                    to + Vector3.up * 0.05f, Quaternion.identity, Vector3.one * 0.22f);
                _eyes[i].gameObject.name = "Eye";
            }

            ApplyEyes(_eyeColor);
        }

        /// <summary>The anchor-slam telegraph — a flat hazard-orange ring on the floor, the one place
        /// this boss ever shows hazard orange, driven purely by
        /// <see cref="AnchorheadBoss.TelegraphProgress01"/> below.</summary>
        private void BuildTelegraphRing()
        {
            _telegraphRing = CharacterPart.AddLens(transform, CharacterMeshes.Ring(1.5f, 1.85f, 0.04f),
                new Vector3(0f, 0.03f, 0f), Quaternion.identity, Vector3.one);
            _telegraphRing.gameObject.name = "SlamTelegraphRing";
            ApplyLensColor(_telegraphRing, Color.black);
        }

        private void ApplyLensColor(MeshRenderer r, Color c)
        {
            if (r == null) return;
            c.a = 1f;
            r.GetPropertyBlock(_portMpb);
            _portMpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_portMpb);
        }

        private void ApplyEyes(Color c)
        {
            for (int i = 0; i < _eyes.Length; i++) ApplyLensColor(_eyes[i], c);
        }

        // ---------------------------------------------------------------- running it

        private void LateUpdate()
        {
            if (_boss == null) return;
            Follow();

            if (_dying) { TickDeath(); return; }

            TickTells(Time.deltaTime);
        }

        private void Follow()
        {
            Vector3 p = _boss.transform.position;
            var at = new Vector3(p.x, 0f, p.z);
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }

        private void TickTells(float dt)
        {
            _flash = Mathf.Max(0f, _flash - dt * 7f);

            Color target = _anchor != null && _anchor.Phase == AnchorheadBoss.AnchorPhase.Awake ? EyeRage : EyeAmber;
            if (_flash > 0f) target = Color.Lerp(target, Color.white, _flash * 0.7f);
            _eyeColor = Color.Lerp(_eyeColor, target, 1f - Mathf.Exp(-9f * dt));
            ApplyEyes(_eyeColor);

            float ring = _anchor != null ? _anchor.TelegraphProgress01 : 0f;
            ApplyLensColor(_telegraphRing, HazardOrange * ring);
        }

        private void OnHealth(float normalized)
        {
            if (normalized < _lastHealth) _flash = 1f;
            _lastHealth = normalized;
        }

        /// <summary>Starts THIS rig's own death the instant its own boss actually dies — guarded the
        /// same MV-625/MV-721 way <see cref="BigBermudaRig.OnKilled"/> is: <see cref="HudSignals.BossKilled"/>
        /// is scene-wide (every rig built so far hears it), so a check against this rig's own boss is
        /// what picks "was it mine" out of the broadcast.</summary>
        private void OnKilled(Vector3 diedAt)
        {
            if (_boss == null || !_boss.IsDead) return;
            if (_dying) return;
            _dying = true;
            _dieTimer = 0f;
        }

        private void TickDeath()
        {
            const float duration = 0.55f;
            _dieTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_dieTimer / duration);
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);

            _eyeColor = Color.Lerp(_eyeColor, Color.black, k);
            ApplyEyes(_eyeColor);
            ApplyLensColor(_telegraphRing, Color.black);

            transform.rotation *= Quaternion.Euler(t * 11f, 0f, t * 7f);
            transform.position += Vector3.down * (t * t * 0.8f);

            if (t >= 1f) gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (var m in new[] { _rustMat, _charcoalMat })
                if (m != null) DestroyImmediate(m);
        }
    }
}
