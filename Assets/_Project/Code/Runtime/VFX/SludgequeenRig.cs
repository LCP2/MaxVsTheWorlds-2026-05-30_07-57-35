using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1124: Sludgequeen's body, replacing MV-699's plain olive-and-rust drum with a form that
    /// reads from the fixed top-down camera and follows the game's robot design rules — a top-heavy
    /// pump housing on six spindly legs, one big off-centre eye, a hard rust/teal colour split, and a
    /// glowing sludge crown ringed by five vents. The old violet "Curator eye", the valve-wheel crown
    /// and the under-housing drips are gone (MV-699's own header explained why they were never meant
    /// to be final — they ignored the design rules entirely).
    ///
    /// Built from <see cref="CharacterMeshes"/>'s prism/beam/ring vocabulary plus
    /// <see cref="CharacterMeshes.Bevelled"/> for the flat-faced hatch/door/chute parts — no
    /// <c>GameObject.CreatePrimitive</c> on the character itself (<see cref="BuildFlood"/>'s
    /// environment overlay is the one exception, same split <see cref="BigBermudaRig"/> draws between
    /// a character's own body and the ground it stands on).
    ///
    /// The pump housing's rust/teal split is built as two copies of the same cached taper mesh per
    /// section — one centred, one offset along the split plane's normal — rather than a true clipped
    /// mesh: nothing in this toolkit cuts a mesh along an arbitrary plane, and a bulge of one colour
    /// proud of the other reads as a hard split at gameplay distance without needing one. See
    /// <see cref="BuildHousingSection"/>.
    ///
    /// The eye sits well above the housing's own roofline (<see cref="EyeAnchor"/> is higher than the
    /// ticket's own authored y) for the same reason <see cref="BigBermudaRig"/>'s ocular core "bulges
    /// up-and-forward" rather than sitting flush: a lens embedded in the body's own silhouette is
    /// exactly what MV-699 shipped, and it is invisible from the fixed camera. The five vents are
    /// phased around the crown specifically to leave the eye's own bearing clear (see
    /// <see cref="BuildVents"/>) rather than evenly from a fixed start angle, since nothing in the
    /// ticket pins their start angle but AC1b pins the eye's visibility.
    ///
    /// Unparented and FOLLOWS the boss each LateUpdate (yaw only), same reasoning as
    /// <see cref="BigBermudaRig.Follow"/>. The flood plane, its own death-drain and the boss's collider
    /// fit are all MV-699's unchanged — this ticket replaces the body only.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("rig")]
    public sealed class SludgequeenRig : MonoBehaviour
    {
        /// <summary>Give <paramref name="boss"/> its own rig, bound to it alone — same per-instance
        /// reasoning as <see cref="BigBermudaRig.CreateFor"/> (MV-573): a multi-boss arena must not have
        /// only its first boss grow a body.</summary>
        public static SludgequeenRig CreateFor(SludgequeenBoss boss)
        {
            if (boss == null) return null;
            var rig = new GameObject("SludgequeenRig").AddComponent<SludgequeenRig>();
            rig.Bind(boss);
            return rig;
        }

        // ---------------------------------------------------------------- dimensions (authored for a
        // body width of 6; the whole rig is scaled by authored width / 6 in Bind, as every other boss
        // rig does)

        private const float AuthoredBodyWidth = 6f;
        private const int HousingSides = 12;

        // the pump housing's three stacked taper sections (radius0, radius1, y0, height)
        private const float LowerR0 = 1.2f, LowerR1 = 1.75f, LowerY0 = 1.25f, LowerH = 0.45f;
        private const float MainR0 = 1.75f, MainR1 = 1.95f, MainY0 = 1.70f, MainH = 1.5f;
        private const float ShoulderR0 = 1.95f, ShoulderR1 = 1.35f, ShoulderY0 = 3.2f, ShoulderH = 0.55f;

        /// <summary>The colour-split plane's normal and offset (the ticket's own numbers) — see
        /// <see cref="BuildHousingSection"/> for how this is turned into geometry.</summary>
        private static readonly Vector3 SplitNormal = new Vector3(1f, 0f, 0.35f).normalized;
        private const float SplitOffset = 0.25f;

        private const float WaistBandRadius = 2.0f, WaistBandHeight = 0.16f, WaistBandY = 2.35f;

        private const float CrownRadius = 1.05f, CrownHeight = 0.5f, CrownY0 = 3.7f;
        private const float SludgeSurfaceRadius = 0.9f;
        private const float GoldRingOuter = 1.07f, GoldRingY = 4.12f;

        private const int VentCount = 5;
        private const float VentR0 = 0.2f, VentR1 = 0.16f, VentHeight = 1.5f;
        private const float VentCircleRadius = 1.25f, VentY0 = 3.55f, VentCapHeight = 0.16f;

        /// <summary>Raised from the ticket's authored y (3.25) to clear the housing's own roofline
        /// (3.75) along the camera ray — see the class doc. X/Z keep the ticket's own off-centre
        /// anchor.</summary>
        private static readonly Vector3 EyeAnchor = new Vector3(-0.75f, 3.9f, 1.45f);
        private const float EyeBezelRadius = 0.78f, EyeLensRadius = 0.52f, EyeHighlightRadius = 0.2f;

        private static readonly Vector3 AlarmAnchor = new Vector3(0.55f, 3.95f, 1.3f);
        private const float AlarmDiameter = 0.34f;

        private const float HatchHousingW = 0.5f, HatchHousingH = 1.1f, HatchHousingD = 1.5f;
        private const float DoorHingeDeg = 18f, ChuteSlopeDeg = 36f;

        private const int LegSegmentSides = 8;

        /// <summary>Six leg roots, each doubling as the leg's own exposed "hip" transform (MV-1124
        /// AC1a).</summary>
        public const int LegCount = 6;
        private const float HipRadius = 1.55f, HipY = 1.9f;
        private const float KneeRadius = 3.0f, KneeY = 3.0f;
        private const float FootRadius = 3.85f, FootY = 0.12f;
        private const float UpperLegR0 = 0.18f, UpperLegR1 = 0.14f;
        private const float LowerLegR0 = 0.14f, LowerLegR1 = 0.08f;
        private const float KneeBallDiameter = 0.34f;

        // ---------------------------------------------------------------- palette

        private static readonly Color RustColor = new Color(0.66f, 0.34f, 0.15f);
        private static readonly Color TealColor = new Color(0.25f, 0.46f, 0.48f);
        private static readonly Color DarkColor = new Color(0.14f, 0.15f, 0.18f);
        private static readonly Color NearBlackColor = new Color(0.085f, 0.09f, 0.11f);
        private static readonly Color SteelGreyColor = new Color(0.42f, 0.44f, 0.48f);
        private static readonly Color GoldColor = new Color32(0xFF, 0xB0, 0x20, 0xFF);

        private static readonly Color SludgeGlow = new Color(0.52f, 0.72f, 0.25f);
        private static readonly Color AlarmDark = new Color(0.22f, 0.04f, 0.03f);
        private static readonly Color AlarmBright = new Color32(0xFF, 0x2A, 0x1A, 0xFF);

        private static readonly Color FloodColor = new Color(0.30f, 0.42f, 0.16f);

        /// <summary>Same number <c>MapRuntime.SludgeScrollSpeed</c> already authors for ground sludge —
        /// one flow speed for "sludge" everywhere in the game, not a bespoke one for this boss alone.</summary>
        private static readonly Vector2 FloodScrollSpeed = new Vector2(0f, 0.12f);

        private const float FloodThickness = 0.06f;
        private const float FoamHeight = 0.16f;
        private const float FoamDepth = 0.4f;

        // ---------------------------------------------------------------- materials (static, shared —
        // nothing here needs a per-instance emissive tween)

        private static Material s_rust, s_teal, s_dark, s_nearBlack, s_steelGrey, s_gold;

        private static void EnsureMaterials()
        {
            if (s_rust != null) return;
            s_rust = NewMaterial("Sludgequeen_Rust", RustColor);
            s_teal = NewMaterial("Sludgequeen_Teal", TealColor);
            s_dark = NewMaterial("Sludgequeen_Dark", DarkColor);
            s_nearBlack = NewMaterial("Sludgequeen_NearBlack", NearBlackColor);
            s_steelGrey = NewMaterial("Sludgequeen_SteelGrey", SteelGreyColor);
            s_gold = NewMaterial("Sludgequeen_Gold", GoldColor);
        }

        private static Material NewMaterial(string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58): a
            // plain lit material still draws the right colour, just without the outline.
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

        private SludgequeenBoss _boss;
        private MaterialPropertyBlock _mpb;

        /// <summary>Only the housing + hatch boxes live here — <see cref="SludgequeenBoss.FitColliderTo"/>
        /// is fitted against this subset, not the legs (the ticket's own "fitted to the housing and
        /// hatches, not to the legs").</summary>
        private Transform _colliderRoot;

        private readonly Transform[] _legHips = new Transform[LegCount];
        private readonly Transform[] _legKnees = new Transform[LegCount];
        private readonly Transform[] _legFeet = new Transform[LegCount];

        private readonly Transform[] _chuteFeet = new Transform[2];
        private readonly Transform[] _nozzleMouths = new Transform[2];

        private MeshRenderer _eyeLens;
        private MeshRenderer _sludgeSurface;
        private MeshRenderer _alarmLens;
        private readonly MeshRenderer[] _hatchStrips = new MeshRenderer[2];
        private readonly MeshRenderer[] _cannonMouths = new MeshRenderer[2];
        private bool _alarmCritical;

        // The flood plane covers the ARENA (world X/Z), not the boss, so it is never parented under
        // this rig's own following transform — see RefreshFlood.
        private Transform _floodPlane;
        private Transform _foamEdge;

        private bool _dying;
        private float _dieTimer;

        /// <summary>The flood's own scrolling ground plane — a resolved value a test can read, same
        /// "public getter off private state" shape <see cref="BigBermudaRig.EyeColor"/> already uses.</summary>
        public SludgeFlow Flood { get; private set; }

        /// <summary>The eye lens renderer — what MV-1124 AC1b's camera-ray test must hit first.</summary>
        public MeshRenderer EyeLens => _eyeLens;

        /// <summary>The glowing sludge disc atop the crown — AC1b's second camera-ray target.</summary>
        public MeshRenderer SludgeSurface => _sludgeSurface;

        /// <summary>The health-reactive alarm lamp — AC1f.</summary>
        public MeshRenderer AlarmLamp => _alarmLens;

        /// <summary>Leg <paramref name="index"/>'s hip pivot — also the leg's own root transform.</summary>
        public Transform LegHip(int index) => _legHips[index];
        public Transform LegKnee(int index) => _legKnees[index];
        public Transform LegFoot(int index) => _legFeet[index];

        /// <summary>The nozzle mouth / chute foot transforms the fight ticket (MV-1127) needs —
        /// <paramref name="side"/> 0 = left (-X), 1 = right (+X).</summary>
        public Transform NozzleMouth(int side) => _nozzleMouths[side];
        public Transform ChuteFoot(int side) => _chuteFeet[side];

        private void OnEnable()
        {
            HudSignals.BossDefeated += OnDefeated;
            HudSignals.BossHealthChanged += OnHealth;
        }

        private void OnDisable()
        {
            HudSignals.BossDefeated -= OnDefeated;
            HudSignals.BossHealthChanged -= OnHealth;
        }

        private void Bind(SludgequeenBoss boss)
        {
            _boss = boss;
            gameObject.AddComponent<KeepsOwnMaterial>();
            _mpb = new MaterialPropertyBlock();

            // The greybox cube goes; its collider stays (the CharacterController is what Max and the
            // Water Blaster actually hit) — same split BigBermudaRig.Bind uses.
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            transform.localScale = Vector3.one * (_boss.transform.localScale.x / AuthoredBodyWidth);

            Build();
            BuildFlood();
            Follow();
            RefreshFlood();

            _boss.FitColliderTo(RenderedBoundsRelativeTo(_boss.transform, _colliderRoot));
        }

        private void Build()
        {
            EnsureMaterials();

            var colliderGo = new GameObject("Housing");
            colliderGo.transform.SetParent(transform, worldPositionStays: false);
            _colliderRoot = colliderGo.transform;

            BuildHousingSection(LowerR0, LowerR1, LowerY0, LowerH);
            BuildHousingSection(MainR0, MainR1, MainY0, MainH);
            BuildHousingSection(ShoulderR0, ShoulderR1, ShoulderY0, ShoulderH);

            CharacterPart.Add(_colliderRoot, CharacterMeshes.Prism(HousingSides, WaistBandRadius, WaistBandRadius, WaistBandHeight),
                s_dark, new Vector3(0f, WaistBandY, 0f), Quaternion.identity, Vector3.one, "WaistBand");

            BuildHatch(-1f, 0);
            BuildHatch(1f, 1);

            BuildCrown();
            BuildVents();
            BuildEye();
            BuildAlarmLamp();
            BuildCannon(-1f, 0);
            BuildCannon(1f, 1);
            BuildLegs();
        }

        /// <summary>One taper section of the pump housing, in rust AND teal: the same cached mesh
        /// (<see cref="CharacterMeshes"/> keys purely on shape, so this costs nothing extra) added
        /// twice — once centred, once offset by <see cref="SplitOffset"/> along <see cref="SplitNormal"/>
        /// — so the teal copy bulges proud of the rust one on the split's far side and reads as a hard
        /// colour break at gameplay distance. See the class doc for why this toolkit builds a "split"
        /// this way rather than clipping a single mesh.</summary>
        private void BuildHousingSection(float r0, float r1, float y0, float height)
        {
            float centerY = y0 + height * 0.5f;
            Mesh mesh = CharacterMeshes.Prism(HousingSides, r0, r1, height);

            CharacterPart.Add(_colliderRoot, mesh, s_rust, new Vector3(0f, centerY, 0f),
                Quaternion.identity, Vector3.one, "HousingRust");
            CharacterPart.Add(_colliderRoot, mesh, s_teal, SplitNormal * SplitOffset + new Vector3(0f, centerY, 0f),
                Quaternion.identity, Vector3.one, "HousingTeal");
        }

        /// <summary>One side intake hatch: a near-black housing (collider-scoped), a glowing strip
        /// inside it, a door panel hinged open (half rust, half teal) and a chute sloping to the floor,
        /// its far end exposed for the fight ticket (MV-1127).</summary>
        private void BuildHatch(float side, int index)
        {
            Vector3 at = new Vector3(side * 1.95f, 1.6f, -0.2f);
            Vector3 boxSize = new Vector3(HatchHousingW, HatchHousingH, HatchHousingD);

            CharacterPart.Add(_colliderRoot, CharacterMeshes.Bevelled(boxSize, CharacterMeshes.DefaultBevel(boxSize)),
                s_nearBlack, at, Quaternion.identity, Vector3.one, "HatchHousing");

            Vector3 stripSize = new Vector3(HatchHousingW * 0.7f, HatchHousingH * 0.7f, 0.05f);
            var strip = CharacterPart.AddLens(_colliderRoot, CharacterMeshes.Bevelled(stripSize, CharacterMeshes.DefaultBevel(stripSize)),
                at, Quaternion.identity, Vector3.one);
            strip.gameObject.name = "HatchStrip";
            ApplyLensColor(strip, SludgeGlow);
            _hatchStrips[index] = strip;

            var doorPivotGo = new GameObject($"Hatch{index}DoorHinge");
            doorPivotGo.transform.SetParent(transform, worldPositionStays: false);
            doorPivotGo.transform.localPosition = at + new Vector3(0f, HatchHousingH * 0.5f, 0f);
            doorPivotGo.transform.localRotation = Quaternion.Euler(-DoorHingeDeg, 0f, 0f);

            Vector3 doorHalfSize = new Vector3(0.65f, 0.1f, 1.5f);
            Mesh doorMesh = CharacterMeshes.Bevelled(doorHalfSize, CharacterMeshes.DefaultBevel(doorHalfSize));
            CharacterPart.Add(doorPivotGo.transform, doorMesh, s_rust, new Vector3(-0.325f, 0f, 0f),
                Quaternion.identity, Vector3.one, "DoorRust");
            CharacterPart.Add(doorPivotGo.transform, doorMesh, s_teal, new Vector3(0.325f, 0f, 0f),
                Quaternion.identity, Vector3.one, "DoorTeal");

            var chutePivotGo = new GameObject($"Hatch{index}ChuteHinge");
            chutePivotGo.transform.SetParent(transform, worldPositionStays: false);
            chutePivotGo.transform.localPosition = at + new Vector3(0f, -HatchHousingH * 0.5f, 0f);
            chutePivotGo.transform.localRotation = Quaternion.Euler(-ChuteSlopeDeg, 0f, 0f);

            const float chuteLength = 1.3f;
            Vector3 chuteSize = new Vector3(1.9f, 0.1f, chuteLength);
            CharacterPart.Add(chutePivotGo.transform, CharacterMeshes.Bevelled(chuteSize, CharacterMeshes.DefaultBevel(chuteSize)),
                s_steelGrey, new Vector3(0f, 0f, chuteLength * 0.5f), Quaternion.identity, Vector3.one, "Chute");

            var footGo = new GameObject($"Hatch{index}ChuteFoot");
            footGo.transform.SetParent(chutePivotGo.transform, worldPositionStays: false);
            footGo.transform.localPosition = new Vector3(0f, 0f, chuteLength);
            _chuteFeet[index] = footGo.transform;
        }

        /// <summary>The crown tank, its glowing sludge surface (the topmost point on the model, so the
        /// camera ray to its centre never has to clear anything above it) and the gold accent ring —
        /// "the only gold on her" (the ticket's own words).</summary>
        private void BuildCrown()
        {
            float centerY = CrownY0 + CrownHeight * 0.5f;
            CharacterPart.Add(transform, CharacterMeshes.Prism(16, CrownRadius, CrownRadius, CrownHeight),
                s_nearBlack, new Vector3(0f, centerY, 0f), Quaternion.identity, Vector3.one, "CrownTank");

            float surfaceY = CrownY0 + CrownHeight + 0.01f;
            float surfaceDiameter = SludgeSurfaceRadius * 2f;
            _sludgeSurface = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(16),
                new Vector3(0f, surfaceY, 0f), Quaternion.identity,
                new Vector3(surfaceDiameter, surfaceDiameter * 0.12f, surfaceDiameter));
            _sludgeSurface.gameObject.name = "SludgeSurface";
            ApplyLensColor(_sludgeSurface, SludgeGlow);

            CharacterPart.Add(transform, CharacterMeshes.Ring(GoldRingOuter - 0.07f, GoldRingOuter, 0.05f),
                s_gold, new Vector3(0f, GoldRingY, 0f), Quaternion.identity, Vector3.one, "GoldRing");
        }

        /// <summary>Five vents standing on a ring around the crown. Phased off the eye's own bearing
        /// (rather than a fixed start angle — nothing in the ticket pins one) so none of them sits
        /// between the camera and the eye; see the class doc.</summary>
        private void BuildVents()
        {
            float eyeBearingDeg = Mathf.Atan2(EyeAnchor.x, EyeAnchor.z) * Mathf.Rad2Deg;
            float startDeg = eyeBearingDeg + (360f / VentCount) * 0.5f;

            for (int i = 0; i < VentCount; i++)
            {
                float angleDeg = startDeg + i * (360f / VentCount);
                Vector3 dir = AngleDir(angleDeg);
                Vector3 basePos = dir * VentCircleRadius + Vector3.up * VentY0;

                CharacterPart.Add(transform, CharacterMeshes.Prism(8, VentR0, VentR1, VentHeight),
                    s_nearBlack, basePos + Vector3.up * (VentHeight * 0.5f), Quaternion.identity, Vector3.one, "Vent");

                CharacterPart.Add(transform, CharacterMeshes.Prism(8, VentR1 * 1.05f, VentR1 * 0.75f, VentCapHeight, 0.3f),
                    s_rust, basePos + Vector3.up * (VentHeight + VentCapHeight * 0.5f), Quaternion.identity, Vector3.one, "VentCap");
            }
        }

        /// <summary>The one big off-centre eye: a near-black bezel, a gold-amber lens proud of it, and a
        /// small highlight proud of the lens — three nested spheres along the same outward-and-up
        /// direction, which (being full spheres) read correctly from any angle without needing the
        /// ticket's own 48° tilt applied as a literal rotation. Each is offset from the last by MORE
        /// than the sum of their radii (not just enough for the spheres themselves to clear) so their
        /// axis-aligned bounds don't overlap either — AC1b's camera-ray test resolves visibility against
        /// bounds, and a bezel whose bounding BOX still reaches the lens's centre would wrongly read as
        /// occluding its own lens.</summary>
        private void BuildEye()
        {
            Vector3 outDir = EyeAnchor.normalized;
            const float clearanceMargin = 0.05f;

            CharacterPart.Add(transform, CharacterMeshes.Sphere(14), s_nearBlack, EyeAnchor,
                Quaternion.identity, Vector3.one * (EyeBezelRadius * 2f), "EyeBezel");

            Vector3 lensPos = OffsetClearingAabb(EyeAnchor, outDir, EyeBezelRadius + EyeLensRadius + clearanceMargin);
            _eyeLens = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(14), lensPos,
                Quaternion.identity, Vector3.one * (EyeLensRadius * 2f));
            _eyeLens.gameObject.name = "EyeLens";
            ApplyLensColor(_eyeLens, GoldColor);

            Vector3 highlightPos = OffsetClearingAabb(lensPos, outDir, EyeLensRadius + EyeHighlightRadius + clearanceMargin);
            var highlight = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(10), highlightPos,
                Quaternion.identity, Vector3.one * (EyeHighlightRadius * 2f));
            highlight.gameObject.name = "EyeHighlight";
            ApplyLensColor(highlight, Color.white);
        }

        /// <summary>Offsets <paramref name="from"/> along <paramref name="dir"/> by enough that the
        /// result's axis-aligned bounds are guaranteed clear of a sphere of <paramref name="clearance"/>
        /// radius centred at <paramref name="from"/> — not just far enough for the SPHERES to separate,
        /// but far enough along <paramref name="dir"/>'s single largest axis that the two AABBs stop
        /// overlapping on that axis regardless of how diagonal <paramref name="dir"/> is. A plain
        /// <c>from + dir * clearance</c> under-shoots whenever <paramref name="dir"/> isn't axis-aligned,
        /// since its largest component is then smaller than its own magnitude.</summary>
        private static Vector3 OffsetClearingAabb(Vector3 from, Vector3 dir, float clearance)
        {
            float dominantAxis = Mathf.Max(Mathf.Abs(dir.x), Mathf.Max(Mathf.Abs(dir.y), Mathf.Abs(dir.z)));
            float distance = clearance / Mathf.Max(dominantAxis, 1e-4f);
            return from + dir * distance;
        }

        private void BuildAlarmLamp()
        {
            _alarmLens = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(10), AlarmAnchor,
                Quaternion.identity, Vector3.one * AlarmDiameter);
            _alarmLens.gameObject.name = "AlarmLamp";
            ApplyLensColor(_alarmLens, AlarmDark);
        }

        /// <summary>One cannon: a three-segment hose approximating the ticket's curved path through its
        /// four authored points, ending in a tilted nozzle with a glowing mouth exposed for the fight
        /// ticket (MV-1127).</summary>
        private void BuildCannon(float side, int index)
        {
            Vector3 p0 = new Vector3(side * 1.7f, 2.6f, 0.3f);
            Vector3 p1 = new Vector3(side * 2.5f, 3.5f, 0.9f);
            Vector3 p2 = new Vector3(side * 2.3f, 3.9f, 1.9f);
            Vector3 p3 = new Vector3(side * 1.9f, 3.6f, 2.6f);

            HoseSegment(p0, p1);
            HoseSegment(p1, p2);
            HoseSegment(p2, p3);

            // Tilted 40 degrees up and forward from vertical, the ticket's own number.
            Vector3 nozzleDir = new Vector3(0f, Mathf.Sin(40f * Mathf.Deg2Rad), Mathf.Cos(40f * Mathf.Deg2Rad));
            const float nozzleLength = 0.7f;

            CharacterPart.Add(transform, CharacterMeshes.Beam(nozzleLength, 0.26f, 0.36f, LegSegmentSides), s_rust,
                p3 + nozzleDir * (nozzleLength * 0.5f), Quaternion.FromToRotation(Vector3.up, nozzleDir), Vector3.one, "Nozzle");

            Vector3 mouthPos = p3 + nozzleDir * nozzleLength;
            var mouth = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(10), mouthPos, Quaternion.identity, Vector3.one * 0.3f);
            mouth.gameObject.name = "NozzleMouth";
            ApplyLensColor(mouth, SludgeGlow);
            _cannonMouths[index] = mouth;
            _nozzleMouths[index] = mouth.transform;
        }

        private void HoseSegment(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            CharacterPart.Add(transform, CharacterMeshes.Beam(d.magnitude, 0.2f, 0.2f, LegSegmentSides), s_dark,
                (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), Vector3.one, "Hose");
        }

        /// <summary>Six legs, evenly spaced 60 degrees apart starting 30 degrees off the front (the
        /// ticket's own numbers). Each is a separate hierarchy under its own hip pivot — hip, knee and
        /// foot are all exposed transforms (AC1a) — standing still in this ticket's pose; the walk
        /// (<see cref="LegGaitDriver"/>) is the next ticket's, not this one's.</summary>
        private void BuildLegs()
        {
            for (int i = 0; i < LegCount; i++)
            {
                float angleDeg = 30f + i * (360f / LegCount);
                Vector3 dir = AngleDir(angleDeg);

                Vector3 hipPos = dir * HipRadius + Vector3.up * HipY;
                Vector3 kneePos = dir * KneeRadius + Vector3.up * KneeY;
                Vector3 footPos = dir * FootRadius + Vector3.up * FootY;

                var hipGo = new GameObject($"Leg{i}Hip");
                hipGo.transform.SetParent(transform, worldPositionStays: false);
                hipGo.transform.localPosition = hipPos;
                _legHips[i] = hipGo.transform;

                Vector3 kneeLocal = kneePos - hipPos;
                CharacterPart.Add(hipGo.transform, CharacterMeshes.Beam(kneeLocal.magnitude, UpperLegR0, UpperLegR1, LegSegmentSides),
                    s_nearBlack, kneeLocal * 0.5f, Quaternion.FromToRotation(Vector3.up, kneeLocal.normalized), Vector3.one, "UpperLeg");

                var kneeGo = new GameObject($"Leg{i}Knee");
                kneeGo.transform.SetParent(hipGo.transform, worldPositionStays: false);
                kneeGo.transform.localPosition = kneeLocal;
                _legKnees[i] = kneeGo.transform;

                CharacterPart.Add(kneeGo.transform, CharacterMeshes.Sphere(10), s_rust, Vector3.zero,
                    Quaternion.identity, Vector3.one * KneeBallDiameter, "KneeBall");

                Vector3 footLocal = footPos - kneePos;
                CharacterPart.Add(kneeGo.transform, CharacterMeshes.Beam(footLocal.magnitude, LowerLegR0, LowerLegR1, LegSegmentSides),
                    s_nearBlack, footLocal * 0.5f, Quaternion.FromToRotation(Vector3.up, footLocal.normalized), Vector3.one, "LowerLeg");

                var footGo = new GameObject($"Leg{i}Foot");
                footGo.transform.SetParent(kneeGo.transform, worldPositionStays: false);
                footGo.transform.localPosition = footLocal;
                _legFeet[i] = footGo.transform;

                CharacterPart.Add(footGo.transform, CharacterMeshes.Prism(8, 0.22f, 0.18f, 0.1f, 0.3f),
                    s_nearBlack, Vector3.zero, Quaternion.identity, Vector3.one, "FootPad");
            }
        }

        /// <summary>0 degrees = local +Z ("the front"), increasing toward +X — the shared angle
        /// convention <see cref="BuildVents"/>/<see cref="BuildLegs"/> both place their rings with.</summary>
        private static Vector3 AngleDir(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>The flood's own rising sludge plane and its foam leading edge — an ENVIRONMENT
        /// overlay covering <see cref="SludgequeenBoss.FloodRect"/>'s world extent, built the same
        /// primitive-cube-plus-<see cref="SludgeFlow"/> way <c>MapRuntime.BuildSludge</c> already
        /// builds ground sludge. Deliberately unparented: FloodRect is a world-space rect, and this
        /// rig's own transform follows the boss (see Follow) — parenting the flood under it would
        /// drag the arena-sized plane around by the boss's own position. Unchanged from MV-699.
        /// </summary>
        private void BuildFlood()
        {
            var floodGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floodGo.name = "SludgequeenFlood";
            Strip(floodGo);
            floodGo.transform.SetParent(null);
            _floodPlane = floodGo.transform;

            Material floodMat = MaterialLibrary.Tinted(SurfaceKind.Prop, FloodColor);
            var floodRenderer = floodGo.GetComponent<MeshRenderer>();
            if (floodMat != null) floodRenderer.sharedMaterial = floodMat;
            floodRenderer.shadowCastingMode = ShadowCastingMode.Off;
            floodRenderer.receiveShadows = false;

            Flood = floodGo.AddComponent<SludgeFlow>();
            Flood.Configure(floodMat, FloodScrollSpeed);

            var foamGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foamGo.name = "SludgequeenFloodFoam";
            Strip(foamGo);
            foamGo.transform.SetParent(null);
            _foamEdge = foamGo.transform;

            var foamRenderer = foamGo.GetComponent<MeshRenderer>();
            foamRenderer.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            foamRenderer.shadowCastingMode = ShadowCastingMode.Off;
            foamRenderer.receiveShadows = false;
            ApplyLensColor(foamRenderer, Color.white * 0.8f);
        }

        /// <summary>Resizes the flood plane and its foam edge to <see cref="SludgequeenBoss.FloodRect"/>'s
        /// current extent, and shows them only while the fight is actually on
        /// (<see cref="SludgequeenBoss.Engaged"/>). Unchanged from MV-699.</summary>
        private void RefreshFlood()
        {
            if (_boss == null || _floodPlane == null) return;

            Rect r = _boss.FloodRect;
            float w = Mathf.Max(0.01f, r.width);
            float d = Mathf.Max(0.01f, r.height);

            _floodPlane.position = new Vector3(r.center.x, FloodThickness * 0.5f, r.center.y);
            _floodPlane.localScale = new Vector3(w, FloodThickness, d);

            _foamEdge.position = new Vector3(r.center.x, FloodThickness + FoamHeight * 0.5f, r.yMax);
            _foamEdge.localScale = new Vector3(w, FoamHeight, FoamDepth);

            bool visible = _boss.Engaged && !_dying;
            if (_floodPlane.gameObject.activeSelf != visible) _floodPlane.gameObject.SetActive(visible);
            if (_foamEdge.gameObject.activeSelf != visible) _foamEdge.gameObject.SetActive(visible);
        }

        /// <summary>DestroyImmediate, not Destroy: this rig is built inside EditMode tests too, not
        /// only at runtime, and Destroy() logs an error there — same reasoning as
        /// <see cref="BigBermudaRig.Strip"/>.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
        }

        /// <summary>Same axis-aligned-in-another-frame rebuild as <see cref="BigBermudaRig.RenderedBoundsRelativeTo"/>
        /// (MV-613), scoped to <paramref name="scopeRoot"/>'s own renderers only — what
        /// <see cref="SludgequeenBoss.FitColliderTo"/> needs, fitted to the housing and hatches, not the
        /// legs (MV-1124).</summary>
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
            Follow();

            if (_dying) { TickDeath(); return; }

            RefreshFlood();
        }

        private void Follow()
        {
            Vector3 p = _boss.transform.position;
            var at = new Vector3(p.x, 0f, p.z);
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }

        /// <summary>The alarm lamp's health tell (AC1f): dark red above half health, bright red at or
        /// below it — a direct snap on the real <see cref="HudSignals.BossHealthChanged"/> broadcast
        /// (the same scene-wide-combined signal <see cref="BigBermudaRig.OnHealth"/> already reads for
        /// its own flash), not an eased tween — there is nothing here to ease between two discrete
        /// states.</summary>
        private void OnHealth(float normalized)
        {
            bool critical = normalized <= SludgequeenTuning.PhaseTwoThreshold;
            if (critical == _alarmCritical) return;
            _alarmCritical = critical;
            ApplyLensColor(_alarmLens, critical ? AlarmBright : AlarmDark);
        }

        /// <summary>The drain-away on death (MV-696's own "the 3 s drain-out itself is a rig-only visual,
        /// left to the art pass"). UNSCALED, same reason as <see cref="BigBermudaRig.TickDeath"/>: the run
        /// ends the frame the boss dies and ResultScreen sets timeScale = 0 that same frame.
        /// </summary>
        private void TickDeath()
        {
            const float duration = 3f;   // the ticket's own "3 s drain-out"

            _dieTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_dieTimer / duration);

            float shrink = 1f - t;
            SetFloodShrink(shrink, shrink > 0.01f);

            ApplyLensColor(_eyeLens, Color.Lerp(GoldColor, Color.black, t));
            ApplyLensColor(_sludgeSurface, Color.Lerp(SludgeGlow, Color.black, t));
            ApplyLensColor(_alarmLens, Color.Lerp(_alarmCritical ? AlarmBright : AlarmDark, Color.black, t));
            for (int i = 0; i < 2; i++)
            {
                ApplyLensColor(_hatchStrips[i], Color.Lerp(SludgeGlow, Color.black, t));
                ApplyLensColor(_cannonMouths[i], Color.Lerp(SludgeGlow, Color.black, t));
            }

            transform.position += Vector3.down * (t * t * 1.2f);   // sinks into the drained floor

            if (t >= 1f) gameObject.SetActive(false);
        }

        private void SetFloodShrink(float shrink, bool visible)
        {
            if (_floodPlane == null) return;
            Rect r = _boss != null ? _boss.FloodRect : new Rect();
            float w = Mathf.Max(0.01f, r.width) * Mathf.Max(0f, shrink);
            float d = Mathf.Max(0.01f, r.height) * Mathf.Max(0f, shrink);
            _floodPlane.localScale = new Vector3(w, FloodThickness, d);
            _floodPlane.gameObject.SetActive(visible);
            if (_foamEdge != null) _foamEdge.gameObject.SetActive(false);
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
            // Same MV-625 guard BigBermudaRig.OnDefeated uses: HudSignals.BossDefeated carries no
            // identity, and every rig built so far hears it — without this check, one boss dying would
            // drain and hide every OTHER still-alive boss's rig in a multi-boss scene.
            if (_boss == null || !_boss.IsDead) return;
            if (_dying) return;
            _dying = true;
            _dieTimer = 0f;
        }

        private void OnDestroy()
        {
            // The flood plane/foam are unparented (see BuildFlood), so destroying this rig does not
            // take them with it automatically — clean them up explicitly, same DestroyImmediate
            // reasoning as Strip (this runs inside EditMode tests too).
            if (_floodPlane != null) DestroyImmediate(_floodPlane.gameObject);
            if (_foamEdge != null) DestroyImmediate(_foamEdge.gameObject);
        }
    }
}
