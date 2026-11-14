using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 5's Orbitals LOOK (MV-1241): gold orbs (a white-hot core inside a soft gold glow,
    /// #F3D27A, both growing and brightening with POWER), a short trailing arc behind each orb along
    /// its path, and a faint dashed gold ring at the REACH radius shown only while at least one robot
    /// sits inside it. MV-1236 built the gameplay orbit/hit math and THE RIG board only, and explicitly
    /// left "the look" — this file — to a later ticket; it reads <see cref="Orbitals.OrbPosition"/>/
    /// <see cref="Orbitals.OrbCount"/>/<see cref="Orbitals.Reach"/> every frame rather than duplicating
    /// any of that math.
    ///
    /// The ticket's third bullet, "Hit: a white flash on the robot," needs no new code here: World 5's
    /// robots already flash white on ANY landed hit — <see cref="RobotEnemy.TakeDamage"/> raises
    /// <c>HudSignals.EmitDamage</c> for every hit including this weapon's own (<see cref="Orbitals.HitAtPosition"/>'s
    /// <c>TakeDamage</c> call), and <c>RobotRig.OnDamage</c> already flashes every nearby robot white
    /// off that same signal — the same "that landed" cue every weapon in the game already gets for free.
    /// </summary>
    [RequireComponent(typeof(Orbitals))]
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class OrbitalsVisuals : MonoBehaviour
    {
        /// <summary>The ticket's own pinned number: an orb's glow diameter at POWER level 1.</summary>
        public const float MinVisualDiameter = 0.35f;

        /// <summary>MV-1373: an orb's glow diameter at POWER's own max level (5) - was 0.6 m, which with
        /// HDR bloom and up to 8 orbs filled the screen. POWER now shows as trail + pulse + red tinge.</summary>
        public const float MaxVisualDiameter = 0.42f;

        /// <summary>MV-1336's per-channel brightness ceiling (1.2 bloom threshold + 20%).</summary>
        public const float PeakChannelCeiling = 1.44f;

        // MV-1373: POWER 1..5 tables.
        private static readonly float[] DiameterTable = { 0.35f, 0.36f, 0.38f, 0.40f, 0.42f };
        private static readonly float[] TrailSecondsTable = { 0.20f, 0.30f, 0.40f, 0.50f, 0.60f };
        private static readonly float[] PulseHzTable = { 0f, 0.8f, 1.2f, 1.8f, 2.5f };
        private static readonly float[] PulseAmpTable = { 0f, 0.06f, 0.10f, 0.14f, 0.18f };
        private static readonly float[] RedTingeTable = { 0f, 0f, 0.2f, 0.4f, 0.6f };
        private static readonly Color TingeColor = new Color(1f, 42f / 255f, 42f / 255f); // #FF2A2A

        /// <summary>Per-orb phase step (radians) so the ring does not throb in unison.</summary>
        private const float PhaseStepPerOrb = 2.399963f; // golden angle

        private const float CoreDiameterFraction = 0.4f;
        private const float RingHeight = 0.05f;
        private const float RingWidth = 0.035f;
        private const int RingSegments = 48;

        /// <summary>Roughly this many glow dots around the ring's own full circumference — the same
        /// "a string of dots reads as dashed" idiom <c>LightMissileAimPreviewVfx</c> uses for its dashed
        /// launch lines, re-derived every <see cref="Tick"/> since (unlike that line's fixed length) the
        /// ring's circumference changes with REACH's own level.</summary>
        private const float RingDashesPerRevolution = 20f;

        // #F3D27A — the ticket's own pinned gold glow colour.
        private static readonly Color GlowColor = new Color(0xF3 / 255f, 0xD2 / 255f, 0x7A / 255f);

        /// <summary>MV-1336 (Lee, iPhone, World 5 Hangar: "spinning orbitals' trails merge into a solid
        /// cream ring that blooms"): reach-ring alpha cut 0.55 -> 0.3 — change item 2's own number.</summary>
        private static readonly Color RingColor = new Color(0xF3 / 255f, 0xD2 / 255f, 0x7A / 255f, 0.3f);

        private Orbitals _orbitals;
        private Transform[] _orbRoots;
        private Transform[] _glows;
        private Transform[] _cores;
        private TrailRenderer[] _trails;
        private LineRenderer _reachRing;
        private Material _ringMaterial;
        private bool _built;
        private EasedValue _diameterEase;
        private float _age;
        private float _phaseOffset;
        private MaterialPropertyBlock _mpb;

        private static int Idx(int powerLevel) => Mathf.Clamp(powerLevel, 1, 5) - 1;

        /// <summary>MV-1373: trail lifetime in seconds at a POWER level. Pure.</summary>
        public static float TrailSecondsForPowerLevel(int powerLevel) => TrailSecondsTable[Idx(powerLevel)];
        /// <summary>MV-1373: pulse rate (Hz) at a POWER level; 0 = none. Pure.</summary>
        public static float PulseHzForPowerLevel(int powerLevel) => PulseHzTable[Idx(powerLevel)];
        /// <summary>MV-1373: pulse amplitude (fraction, +/- scale and brightness). Pure.</summary>
        public static float PulseAmplitudeForPowerLevel(int powerLevel) => PulseAmpTable[Idx(powerLevel)];
        /// <summary>MV-1373: how far the glow is lerped toward #FF2A2A. Pure.</summary>
        public static float RedTingeForPowerLevel(int powerLevel) => RedTingeTable[Idx(powerLevel)];

        /// <summary>MV-1373 test seam: pins every orb's pulse phase (orb 0 at exactly this phase) and
        /// age zero, so a test can sample a deterministic point on the sine wave.</summary>
        public void SetPulsePhaseForTests(float phaseRadians)
        {
            _phaseOffset = phaseRadians;
            _age = 0f;
        }

        /// <summary>MV-1373 (Tier 2): orb index's resolved trail lifetime, read off the live TrailRenderer.</summary>
        public float ResolvedTrailTime(int index) => _trails != null && _trails[index] != null
            ? _trails[index].time
            : 0f;

        /// <summary>Orb <paramref name="index"/>'s own resolved world-space bounds (MV-465 Tier 2) —
        /// read off the glow's live <see cref="Renderer"/> after <see cref="Tick"/> has scaled it, not
        /// the float that produced the scale. The core sits concentric and strictly smaller, so it
        /// never grows this.</summary>
        public Bounds ResolvedOrbBounds(int index) => _glows != null && _glows[index] != null
            ? _glows[index].GetComponent<Renderer>().bounds
            : new Bounds(transform.position, Vector3.zero);

        /// <summary>MV-1336 (MV-465 Tier 2): the resolved per-vertex colour <see cref="Build"/> set on
        /// orb <paramref name="index"/>'s own trail — read off the live <see cref="TrailRenderer"/>, not
        /// the private <see cref="GlowColor"/> field that produced it.</summary>
        public Color ResolvedTrailColor(int index) => _trails != null && _trails[index] != null
            ? _trails[index].startColor
            : Color.clear;

        /// <summary>MV-1336 (MV-465 Tier 2): the resolved per-vertex colour <see cref="Build"/> set on
        /// the reach ring — read off the live <see cref="LineRenderer"/>, not the private
        /// <see cref="RingColor"/> field that produced it.</summary>
        public Color ResolvedReachRingColor => _reachRing != null ? _reachRing.startColor : Color.clear;

        /// <summary>POWER's own diameter progression — a straight lerp from <see cref="MinVisualDiameter"/>
        /// at level 1 to <see cref="MaxVisualDiameter"/> at <paramref name="maxLevel"/>. Pure, so a test
        /// can assert it without a live component.</summary>
        public static float DiameterForPowerLevel(int powerLevel) => DiameterTable[Idx(powerLevel)];

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame refresh — public so an EditMode test can drive it deterministically
        /// without a live PlayerLoop, same seam <see cref="Orbitals.Tick"/> itself carries. Resolves
        /// <see cref="_orbitals"/> lazily rather than in <c>Awake</c> — <c>Awake</c> never runs as a side
        /// effect of <c>AddComponent</c> outside Play mode, the same reason <c>Orbitals</c> itself and
        /// every sibling weapon component here carry no Awake-set state of their own.</summary>
        public void Tick() => Tick(0f);

        /// <summary>MV-1355: <paramref name="dt"/> drives the 0.6 s ease-out of the orb diameter after a
        /// POWER upgrade (the first call adopts the real diameter outright, so a fresh component never
        /// eases in from zero).</summary>
        public void Tick(float dt)
        {
            if (_orbitals == null) _orbitals = GetComponent<Orbitals>();

            // MV-1248: same SECONDARY-unlock gate as Orbitals.Tick — the visuals must not show orbs
            // circling before the family is actually revealed.
            if (WeaponSystemState.SecondaryKind != SecondaryKind.Orbitals || !RigState.IsCategoryUnlocked("SECONDARY"))
            {
                if (_built) HideAll();
                return;
            }

            if (!_built) { Build(); _built = true; }

            int count = Mathf.Clamp(_orbitals.OrbCount, 0, Orbitals.MaxOrbs);
            int powerLevel = WeaponSystemState.OrbitalsTrackLevel(OrbitalsTrackKind.Power);
            _diameterEase.Step(DiameterForPowerLevel(powerLevel), dt, Orbitals.GrowthEaseSeconds);
            _age += dt;
            float diameter = _diameterEase.Value;
            float brightnessT = WeaponCatalog.VisualStrengthFraction(Mathf.Max(1, powerLevel),
                Mathf.Max(1, WeaponCatalog.MaxLevel(OrbitalsTrackKind.Power)));

            // MV-1336 change item 3 ("any other World 5 weapon/robot VFX: cap emissive/HDR colour so
            // nothing exceeds the Ship looks' bloom threshold [1.2] by more than 20%" -> per-channel ceiling
            // 1.44): the old 1.6x/1.8x boosts pushed GlowColor's own 0.953 peak channel to 1.525 and the
            // core's white to 1.8, both past that ceiling. 1.5x/1.44x keep the orbs "clearly visible as gold
            // balls" (ticket) while landing at/under 1.44 (0.953*1.5=1.430).
            // MV-1373: the pulse adds up to +amp brightness, so the product is clamped to the same ceiling
            // (ClampPeak). Colour goes through a property block on ONE shared white material, so the
            // pulse never mints materials.
            float amp = PulseAmplitudeForPowerLevel(powerLevel);
            float hz = PulseHzForPowerLevel(powerLevel);
            Color tinted = Color.Lerp(GlowColor, TingeColor, RedTingeForPowerLevel(powerLevel));
            Color glowBase = tinted * Mathf.Lerp(1f, 1.5f, brightnessT);
            Color coreBase = Color.white * Mathf.Lerp(1.1f, 1.44f, brightnessT);
            Material shared = VfxMaterials.AdditiveTinted(Color.white);
            float trailSeconds = TrailSecondsForPowerLevel(powerLevel);
            Color trailStart = tinted; trailStart.a = 1f;
            Color trailEnd = tinted; trailEnd.a = 0f;

            for (int i = 0; i < Orbitals.MaxOrbs; i++)
            {
                bool active = i < count;
                _orbRoots[i].gameObject.SetActive(active);
                if (!active) continue;

                _orbRoots[i].position = _orbitals.OrbPosition(i);

                // Each orb pulses on its own phase so the ring does not throb in unison.
                float sn = hz > 0f ? Mathf.Sin(Mathf.PI * 2f * hz * _age + _phaseOffset + i * PhaseStepPerOrb) : 0f;
                float k = 1f + amp * sn;

                _glows[i].localScale = Vector3.one * (diameter * k);
                SetMaterial(_glows[i], shared);
                Paint(_glows[i], ClampPeak(glowBase, k));

                _cores[i].localScale = Vector3.one * (diameter * CoreDiameterFraction * k);
                SetMaterial(_cores[i], shared);
                Paint(_cores[i], ClampPeak(coreBase / (1f + amp), k));

                // MV-1336 change item 2 ("trail width multiplier halved"): width stays 0.25 x diameter.
                _trails[i].time = trailSeconds;
                _trails[i].startColor = trailStart;
                _trails[i].endColor = trailEnd;
                _trails[i].widthMultiplier = diameter * 0.25f;
                _trails[i].textureScale = new Vector2(3f / Mathf.Max(diameter, 0.01f), 1f);
            }

            RefreshReachRing(count > 0 && AnyRobotWithinReach(_orbitals.Reach), _orbitals.Reach);
        }

        /// <summary>The ticket's own ring-visibility rule: "only while at least one robot is within it" —
        /// a live, awake Team.Enemy robot within <paramref name="reach"/> metres of Max's own XZ position,
        /// same alive/awake gate <see cref="LightMissileBall.IsEligibleRobot"/> applies to a targeting
        /// query.</summary>
        private bool AnyRobotWithinReach(float reach)
        {
            Vector3 origin = transform.position;
            float reachSq = reach * reach;
            var active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy robot = active[i];
                if (robot == null || !robot.IsAlive || robot.IsDormant) continue;
                Vector3 to = robot.transform.position - origin;
                to.y = 0f;
                if (to.sqrMagnitude <= reachSq) return true;
            }

            return false;
        }

        private void RefreshReachRing(bool visible, float radius)
        {
            _reachRing.gameObject.SetActive(visible);
            if (!visible) return;

            float circumference = 2f * Mathf.PI * Mathf.Max(0.01f, radius);
            _ringMaterial.mainTextureScale = new Vector2(RingDashesPerRevolution / circumference, 1f);

            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * (360f / RingSegments) * Mathf.Deg2Rad;
                _reachRing.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, RingHeight, Mathf.Sin(a) * radius));
            }
        }

        private void HideAll()
        {
            for (int i = 0; i < Orbitals.MaxOrbs; i++) _orbRoots[i].gameObject.SetActive(false);
            _reachRing.gameObject.SetActive(false);
        }

        private void Build()
        {
            _orbRoots = new Transform[Orbitals.MaxOrbs];
            _glows = new Transform[Orbitals.MaxOrbs];
            _cores = new Transform[Orbitals.MaxOrbs];
            _trails = new TrailRenderer[Orbitals.MaxOrbs];

            for (int i = 0; i < Orbitals.MaxOrbs; i++)
            {
                var root = new GameObject("Orb" + i);
                root.AddComponent<KeepsOwnMaterial>();
                root.transform.SetParent(transform, worldPositionStays: true);

                _glows[i] = BuildPrimitive(root.transform, "Glow", PrimitiveType.Sphere);
                _cores[i] = BuildPrimitive(root.transform, "Core", PrimitiveType.Sphere);

                var trail = root.AddComponent<TrailRenderer>();
                trail.time = TrailSecondsForPowerLevel(1);
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                trail.minVertexDistance = 0.02f;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.textureMode = LineTextureMode.Tile;
                trail.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
                Color trailColor = GlowColor;
                trail.startColor = trailColor;
                trailColor.a = 0f;
                trail.endColor = trailColor;

                _trails[i] = trail;
                _orbRoots[i] = root.transform;
                root.SetActive(false);
            }

            var ringGo = new GameObject("ReachRing");
            ringGo.AddComponent<KeepsOwnMaterial>();
            ringGo.transform.SetParent(transform, worldPositionStays: false);
            ringGo.transform.localPosition = Vector3.zero;

            _reachRing = ringGo.AddComponent<LineRenderer>();
            _reachRing.useWorldSpace = false;
            _reachRing.loop = true;
            _reachRing.positionCount = RingSegments;
            _reachRing.widthMultiplier = RingWidth;
            _reachRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _reachRing.receiveShadows = false;
            // "Dashed" per the ticket — a string of fading glow dots, same Tile idiom
            // LightMissileAimPreviewVfx's own dashed launch lines use, re-scaled per REACH level in
            // RefreshReachRing since (unlike that line's fixed length) this ring's circumference
            // changes with REACH's own track level.
            _reachRing.textureMode = LineTextureMode.Tile;
            _ringMaterial = new Material(VfxMaterials.Additive(VfxMaterials.Glow()));
            _reachRing.sharedMaterial = _ringMaterial;
            _reachRing.startColor = RingColor;
            _reachRing.endColor = RingColor;
            ringGo.SetActive(false);
        }

        private static Transform BuildPrimitive(Transform parent, string name, PrimitiveType shape)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Color ClampPeak(Color c, float mult)
        {
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * mult;
            if (peak > PeakChannelCeiling) mult *= PeakChannelCeiling / peak;
            return new Color(c.r * mult, c.g * mult, c.b * mult, 1f);
        }

        private void Paint(Transform t, Color c)
        {
            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return;
            _mpb ??= new MaterialPropertyBlock();
            mr.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            mr.SetPropertyBlock(_mpb);
        }

        private static void SetMaterial(Transform t, Material mat)
        {
            if (t == null || mat == null) return;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }
    }
}
