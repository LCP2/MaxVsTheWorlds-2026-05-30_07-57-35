using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Helmsman's body (MV-1242) — follows <see cref="SludgequeenRig"/>'s own pattern (the ticket's
    /// own instruction) and the game's robot design rules: near-black machinery, a hard rust/teal split
    /// down the middle, one huge off-centre gold lens with a thick bezel, and a thin gold "Curator" ring
    /// above it. Built from <see cref="CharacterMeshes"/>'s prism/beam/ring/bevelled-box vocabulary via
    /// <see cref="CharacterPart"/> — no <c>GameObject.CreatePrimitive</c> on the character itself.
    ///
    /// Two phases, read straight off <see cref="HelmsmanBoss.IsWalking"/> every tick, never written back:
    /// Phase 1 seats the body on a wide throne with its own rear cables and two side pod-hatches; Phase 2
    /// hides the throne (cables included) and shows four articulated legs instead, lifting the SAME body
    /// so its top sits at the ticket's own 4 m. The lens brightens on <see cref="HelmsmanBoss.LensTelling"/>
    /// — the rig's cue for the 0.8 s sweep tell, same "read a bool, never own the state" shape every other
    /// boss rig uses for its own phase/health-driven look.
    ///
    /// <see cref="AuthoredBodyWidth"/> (4.5) is deliberately set to the Mothership's own shipped a30 boss
    /// size (<c>mothership_config.json</c>'s "boss":{"size":{"w":4.5}}), NOT to this file's own 3 m body —
    /// the same <c>boss.transform.localScale.x / AuthoredBodyWidth</c> rig-scale formula every other boss
    /// rig uses resolves to 1 at that authored size, so the real Helmsman needs no config edit to come out
    /// at exactly this file's own 3 m <see cref="BodyWidth"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("rig")]
    public sealed class HelmsmanRig : MonoBehaviour
    {
        public static HelmsmanRig CreateFor(HelmsmanBoss boss)
        {
            if (boss == null) return null;
            var rig = new GameObject("HelmsmanRig").AddComponent<HelmsmanRig>();
            rig.Bind(boss);
            return rig;
        }

        // ---------------------------------------------------------------- dimensions

        private const float AuthoredBodyWidth = 4.5f;

        private const float BodyWidth = 3.0f, BodyHeight = 2.3f, BodyDepth = 1.7f;
        private const float ThroneWidth = 4.4f, ThroneHeight = 1.3f, ThroneDepth = 2.6f;
        private const float ThroneBodyCenterY = ThroneHeight + BodyHeight * 0.5f;

        private const float Phase2TopHeight = 4.0f;

        private static readonly Vector3 EyeAnchor = new Vector3(BodyWidth * 0.18f, BodyHeight * 0.12f, BodyDepth * 0.5f);
        private const float EyeBezelRadius = 0.5f, EyeLensRadius = 0.34f;
        private const float RingInnerRadius = 0.58f, RingOuterRadius = 0.66f;

        private const int CableCount = 4;
        private static readonly Vector3 HatchSize = new Vector3(0.4f, 1.0f, 1.2f);

        private const int LegCount = 4;
        private const float LegHipRadius = 1.1f, LegFootRadius = 1.9f;
        private const float LegUpperR0 = 0.16f, LegUpperR1 = 0.12f;
        private const float LegLowerR0 = 0.12f, LegLowerR1 = 0.07f;
        private const float LegKneeBallDiameter = 0.28f;
        private const int LegSegmentSides = 8;

        // ---------------------------------------------------------------- palette (same rust/teal/near-
        // black/gold vocabulary SludgequeenRig/BigBermudaRig each re-declare locally — there is no shared
        // robot-palette class in this codebase to point at instead).

        private static readonly Color RustColor = new Color(0.66f, 0.34f, 0.15f);
        private static readonly Color TealColor = new Color(0.25f, 0.46f, 0.48f);
        private static readonly Color NearBlackColor = new Color(0.085f, 0.09f, 0.11f);
        private static readonly Color SteelGreyColor = new Color(0.42f, 0.44f, 0.48f);
        private static readonly Color BronzeColor = new Color(0.32f, 0.22f, 0.12f);
        private static readonly Color GoldColor = new Color32(0xFF, 0xB0, 0x20, 0xFF);
        private static readonly Color GoldBrightColor = new Color32(0xFF, 0xE0, 0x60, 0xFF);

        private static Material s_rust, s_teal, s_nearBlack, s_steelGrey, s_bronze, s_gold;

        private static void EnsureMaterials()
        {
            if (s_rust != null) return;
            s_rust = NewMaterial("Helmsman_Rust", RustColor);
            s_teal = NewMaterial("Helmsman_Teal", TealColor);
            s_nearBlack = NewMaterial("Helmsman_NearBlack", NearBlackColor);
            s_steelGrey = NewMaterial("Helmsman_SteelGrey", SteelGreyColor);
            s_bronze = NewMaterial("Helmsman_Bronze", BronzeColor);
            s_gold = NewMaterial("Helmsman_Gold", GoldColor);
        }

        private static Material NewMaterial(string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58).
            var template = MaterialLibrary.Character();
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);
            if (m == null) return null;
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ---------------------------------------------------------------- state

        private HelmsmanBoss _boss;
        private MaterialPropertyBlock _mpb;
        private float _rigScale;
        private bool _wasWalking;

        private Transform _bodyRoot;
        private Transform _throneRoot;
        private Transform _legsRoot;

        private MeshRenderer _eyeLens;
        private readonly MeshRenderer[] _cableRenderers = new MeshRenderer[CableCount];

        /// <summary>The upper body only (housing + eye) — never the throne/legs — what MV-1242's own
        /// resolved-body-width test measures.</summary>
        public Transform BodyRoot => _bodyRoot;

        /// <summary>The throne's rear cable renderers — enabled in Phase 1 (seated), disabled the instant
        /// Phase 2 tears them free.</summary>
        public MeshRenderer[] CableRenderers => _cableRenderers;

        public HelmsmanBoss Boss => _boss;

        private void OnEnable() => HudSignals.BossDefeated += OnDefeated;
        private void OnDisable() => HudSignals.BossDefeated -= OnDefeated;

        private void Bind(HelmsmanBoss boss)
        {
            _boss = boss;
            gameObject.AddComponent<KeepsOwnMaterial>();
            _mpb = new MaterialPropertyBlock();

            // The greybox cube goes; its collider stays (refitted below once the real body exists) —
            // same split every other boss rig's own Bind uses.
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            _rigScale = _boss.transform.localScale.x / AuthoredBodyWidth;
            transform.localScale = Vector3.one * _rigScale;

            Build();
            ApplyPhase(force: true);
            Follow();

            _boss.FitColliderTo(RenderedBoundsRelativeTo(_boss.transform, _bodyRoot));
        }

        private void Build()
        {
            EnsureMaterials();
            BuildBody();
            BuildThrone();
            BuildLegs();
        }

        /// <summary>The upper body: a hard rust/teal split (two half-width boxes meeting at x=0 — together
        /// they span exactly <see cref="BodyWidth"/>, the literal assertion MV-1242's own test makes) plus
        /// the one off-centre eye. Present and unchanged across both phases — only what carries it
        /// (<see cref="BuildThrone"/>/<see cref="BuildLegs"/>) differs.</summary>
        private void BuildBody()
        {
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(transform, worldPositionStays: false);
            _bodyRoot = bodyGo.transform;

            Vector3 halfSize = new Vector3(BodyWidth * 0.5f, BodyHeight, BodyDepth);
            Mesh halfMesh = CharacterMeshes.Bevelled(halfSize, CharacterMeshes.DefaultBevel(halfSize));

            CharacterPart.Add(_bodyRoot, halfMesh, s_rust, new Vector3(-BodyWidth * 0.25f, 0f, 0f),
                Quaternion.identity, Vector3.one, "HousingRust");
            CharacterPart.Add(_bodyRoot, halfMesh, s_teal, new Vector3(BodyWidth * 0.25f, 0f, 0f),
                Quaternion.identity, Vector3.one, "HousingTeal");

            BuildEye();
        }

        /// <summary>One huge off-centre lens: a near-black bezel, a gold lens proud of it along the body's
        /// own front-and-out direction, and a thin gold ring floating above — the Curator tell. Same
        /// AABB-clearance offset <see cref="SludgequeenRig.BuildEye"/> uses so the bezel's own bounding box
        /// never reaches into the lens's.</summary>
        private void BuildEye()
        {
            Vector3 outDir = new Vector3(0.15f, 0.1f, 1f).normalized;
            const float clearance = 0.04f;

            CharacterPart.Add(_bodyRoot, CharacterMeshes.Sphere(14), s_nearBlack, EyeAnchor,
                Quaternion.identity, Vector3.one * (EyeBezelRadius * 2f), "EyeBezel");

            Vector3 lensPos = OffsetClearingAabb(EyeAnchor, outDir, EyeBezelRadius + EyeLensRadius + clearance);
            _eyeLens = CharacterPart.AddLens(_bodyRoot, CharacterMeshes.Sphere(14), lensPos,
                Quaternion.identity, Vector3.one * (EyeLensRadius * 2f));
            _eyeLens.gameObject.name = "EyeLens";
            ApplyLensColor(_eyeLens, GoldColor);

            Vector3 ringPos = EyeAnchor + Vector3.up * (EyeBezelRadius + 0.35f);
            CharacterPart.Add(_bodyRoot, CharacterMeshes.Ring(RingInnerRadius, RingOuterRadius, 0.05f),
                s_gold, ringPos, Quaternion.identity, Vector3.one, "CuratorRing");
        }

        /// <summary>Offsets <paramref name="from"/> along <paramref name="dir"/> far enough that the
        /// result's axis-aligned bounds are guaranteed clear of a sphere of <paramref name="clearance"/>
        /// radius centred at <paramref name="from"/> — same reasoning as
        /// <see cref="SludgequeenRig"/>'s own copy of this helper.</summary>
        private static Vector3 OffsetClearingAabb(Vector3 from, Vector3 dir, float clearance)
        {
            float dominantAxis = Mathf.Max(Mathf.Abs(dir.x), Mathf.Max(Mathf.Abs(dir.y), Mathf.Abs(dir.z)));
            float distance = clearance / Mathf.Max(dominantAxis, 1e-4f);
            return from + dir * distance;
        }

        /// <summary>Phase 1 only: the throne base, its rear cables into the body's back, and the two side
        /// pod hatches (pods only ever fire while seated — see <see cref="HelmsmanTuning.PodsPerVolley"/>'s
        /// own doc). All hidden together the instant Phase 2 tears free.</summary>
        private void BuildThrone()
        {
            var throneGo = new GameObject("Throne");
            throneGo.transform.SetParent(transform, worldPositionStays: false);
            _throneRoot = throneGo.transform;

            Vector3 throneSize = new Vector3(ThroneWidth, ThroneHeight, ThroneDepth);
            CharacterPart.Add(_throneRoot, CharacterMeshes.Bevelled(throneSize, CharacterMeshes.DefaultBevel(throneSize)),
                s_bronze, new Vector3(0f, ThroneHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "ThroneBase");

            BuildCables();
            BuildHatch(-1f, "HatchLeft");
            BuildHatch(1f, "HatchRight");
        }

        private void BuildCables()
        {
            for (int i = 0; i < CableCount; i++)
            {
                float t = (i + 0.5f) / CableCount;
                float x = Mathf.Lerp(-ThroneWidth * 0.3f, ThroneWidth * 0.3f, t);

                Vector3 from = new Vector3(x, ThroneHeight * 0.92f, -ThroneDepth * 0.5f);
                Vector3 to = new Vector3(x * 0.4f, ThroneBodyCenterY, -BodyDepth * 0.5f);
                Vector3 d = to - from;

                Transform cable = CharacterPart.Add(_throneRoot, CharacterMeshes.Beam(d.magnitude, 0.07f, 0.05f, 6),
                    s_nearBlack, (from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), Vector3.one,
                    $"Cable{i}");
                _cableRenderers[i] = cable.GetComponent<MeshRenderer>();
            }
        }

        private void BuildHatch(float side, string name)
        {
            Vector3 at = new Vector3(side * (BodyWidth * 0.5f + HatchSize.x * 0.5f), ThroneBodyCenterY, 0f);
            CharacterPart.Add(_throneRoot, CharacterMeshes.Bevelled(HatchSize, CharacterMeshes.DefaultBevel(HatchSize)),
                s_steelGrey, at, Quaternion.identity, Vector3.one, name);
        }

        /// <summary>Phase 2 only: four articulated legs (hip/knee/foot, same vocabulary
        /// <see cref="SludgequeenRig"/>'s own six use), carrying the SAME body up to the ticket's own
        /// 4 m top height. A fixed stance — this ticket is the look, not a walk cycle.</summary>
        private void BuildLegs()
        {
            var legsGo = new GameObject("Legs");
            legsGo.transform.SetParent(transform, worldPositionStays: false);
            _legsRoot = legsGo.transform;

            float hipY = Phase2TopHeight - BodyHeight; // the body's own underside once legs lift it
            float kneeY = hipY * 0.55f;

            for (int i = 0; i < LegCount; i++)
            {
                float angleDeg = 45f + i * (360f / LegCount);
                Vector3 dir = AngleDir(angleDeg);

                Vector3 hipPos = dir * LegHipRadius + Vector3.up * hipY;
                Vector3 kneePos = dir * ((LegHipRadius + LegFootRadius) * 0.5f) + Vector3.up * kneeY;
                Vector3 footPos = dir * LegFootRadius; // y = 0 -- planted on the ground

                var hipGo = new GameObject($"Leg{i}Hip");
                hipGo.transform.SetParent(_legsRoot, worldPositionStays: false);
                hipGo.transform.localPosition = hipPos;

                Vector3 kneeLocal = kneePos - hipPos;
                CharacterPart.Add(hipGo.transform, CharacterMeshes.Beam(kneeLocal.magnitude, LegUpperR0, LegUpperR1, LegSegmentSides),
                    s_nearBlack, kneeLocal * 0.5f, Quaternion.FromToRotation(Vector3.up, kneeLocal.normalized), Vector3.one, "UpperLeg");

                var kneeGo = new GameObject($"Leg{i}Knee");
                kneeGo.transform.SetParent(hipGo.transform, worldPositionStays: false);
                kneeGo.transform.localPosition = kneeLocal;

                CharacterPart.Add(kneeGo.transform, CharacterMeshes.Sphere(10), s_rust, Vector3.zero,
                    Quaternion.identity, Vector3.one * LegKneeBallDiameter, "KneeBall");

                Vector3 footLocal = footPos - kneePos;
                CharacterPart.Add(kneeGo.transform, CharacterMeshes.Beam(footLocal.magnitude, LegLowerR0, LegLowerR1, LegSegmentSides),
                    s_nearBlack, footLocal * 0.5f, Quaternion.FromToRotation(Vector3.up, footLocal.normalized), Vector3.one, "LowerLeg");

                var footGo = new GameObject($"Leg{i}Foot");
                footGo.transform.SetParent(kneeGo.transform, worldPositionStays: false);
                footGo.transform.localPosition = footLocal;

                CharacterPart.Add(footGo.transform, CharacterMeshes.Prism(8, 0.22f, 0.18f, 0.12f, 0.3f),
                    s_nearBlack, Vector3.zero, Quaternion.identity, Vector3.one, "FootPad");
            }
        }

        private static Vector3 AngleDir(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>Same axis-aligned-in-another-frame rebuild as <see cref="SludgequeenRig.RenderedBoundsRelativeTo"/>,
        /// scoped to <paramref name="scopeRoot"/>'s own renderers only — the body, not the throne/legs.</summary>
        private Bounds RenderedBoundsRelativeTo(Transform reference, Transform scopeRoot)
        {
            var renderers = scopeRoot.GetComponentsInChildren<MeshRenderer>();
            Bounds world = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

            Vector3 c = world.center, e = world.extents;
            var local = new Bounds(reference.InverseTransformPoint(c), Vector3.zero);
            for (int xi = -1; xi <= 1; xi += 2)
                for (int yi = -1; yi <= 1; yi += 2)
                    for (int zi = -1; zi <= 1; zi += 2)
                        local.Encapsulate(reference.InverseTransformPoint(c + Vector3.Scale(e, new Vector3(xi, yi, zi))));
            return local;
        }

        private void LateUpdate()
        {
            if (_boss == null) return;
            Tick();
        }

        /// <summary>Everything this rig does once a frame, pulled out of <see cref="LateUpdate"/> so a
        /// test can drive it directly — same reasoning as every other rig's own <c>Tick</c>.</summary>
        private void Tick()
        {
            Follow();
            ApplyPhase(force: false);
            UpdateLensGlow();
        }

        private void Follow()
        {
            Vector3 p = _boss.transform.position;
            var at = new Vector3(p.x, 0f, p.z);
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }

        /// <summary>Reads <see cref="HelmsmanBoss.IsWalking"/> and never writes it back: throne (cables
        /// included) vs. legs, and the body's own resting height, both follow from this one bool.</summary>
        private void ApplyPhase(bool force)
        {
            bool walking = _boss != null && _boss.IsWalking;
            if (!force && walking == _wasWalking) return;
            _wasWalking = walking;

            _throneRoot.gameObject.SetActive(!walking);
            _legsRoot.gameObject.SetActive(walking);

            for (int i = 0; i < _cableRenderers.Length; i++)
                if (_cableRenderers[i] != null) _cableRenderers[i].enabled = !walking;

            _bodyRoot.localPosition = new Vector3(0f,
                walking ? Phase2TopHeight - BodyHeight * 0.5f : ThroneBodyCenterY, 0f);
        }

        /// <summary>The ticket's own "glows brighter during the 0.8 s sweep tell" — a direct read of
        /// <see cref="HelmsmanBoss.LensTelling"/>, never owned here.</summary>
        private void UpdateLensGlow()
        {
            if (_eyeLens == null) return;
            ApplyLensColor(_eyeLens, _boss.LensTelling ? GoldBrightColor : GoldColor);
        }

        private void ApplyLensColor(MeshRenderer r, Color c)
        {
            if (r == null) return;
            c.a = 1f;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        private void OnDefeated()
        {
            // Same MV-625 guard every other boss rig's own OnDefeated uses: HudSignals.BossDefeated
            // carries no identity, and every rig built so far hears it.
            if (_boss == null || !_boss.IsDead) return;
            gameObject.SetActive(false);
        }
    }
}
