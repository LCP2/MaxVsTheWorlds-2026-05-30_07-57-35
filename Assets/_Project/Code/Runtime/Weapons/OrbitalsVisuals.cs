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

        /// <summary>The ticket's own pinned number: an orb's glow diameter at POWER's own max level (5).</summary>
        public const float MaxVisualDiameter = 0.6f;

        private const float CoreDiameterFraction = 0.4f;
        private const float TrailSeconds = 0.2f;
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
        public static float DiameterForPowerLevel(int powerLevel, int maxLevel) =>
            Mathf.Lerp(MinVisualDiameter, MaxVisualDiameter,
                WeaponCatalog.VisualStrengthFraction(Mathf.Max(1, powerLevel), Mathf.Max(1, maxLevel)));

        private void Update() => Tick();

        /// <summary>The real per-frame refresh — public so an EditMode test can drive it deterministically
        /// without a live PlayerLoop, same seam <see cref="Orbitals.Tick"/> itself carries. Resolves
        /// <see cref="_orbitals"/> lazily rather than in <c>Awake</c> — <c>Awake</c> never runs as a side
        /// effect of <c>AddComponent</c> outside Play mode, the same reason <c>Orbitals</c> itself and
        /// every sibling weapon component here carry no Awake-set state of their own.</summary>
        public void Tick()
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
            float diameter = DiameterForPowerLevel(powerLevel, WeaponCatalog.MaxLevel(OrbitalsTrackKind.Power));
            float brightnessT = WeaponCatalog.VisualStrengthFraction(Mathf.Max(1, powerLevel),
                Mathf.Max(1, WeaponCatalog.MaxLevel(OrbitalsTrackKind.Power)));

            // MV-1336 change item 3 ("any other World 5 weapon/robot VFX: cap emissive/HDR colour so
            // nothing exceeds the Ship looks' bloom threshold [1.2] by more than 20%" -> per-channel ceiling
            // 1.44): the old 1.6x/1.8x boosts pushed GlowColor's own 0.953 peak channel to 1.525 and the
            // core's white to 1.8, both past that ceiling. 1.5x/1.44x keep the orbs "clearly visible as gold
            // balls" (ticket) while landing at/under 1.44 (0.953*1.5=1.430).
            Material glowMat = VfxMaterials.AdditiveTinted(GlowColor * Mathf.Lerp(1f, 1.5f, brightnessT));
            Material coreMat = VfxMaterials.AdditiveTinted(Color.white * Mathf.Lerp(1.1f, 1.44f, brightnessT));

            for (int i = 0; i < Orbitals.MaxOrbs; i++)
            {
                bool active = i < count;
                _orbRoots[i].gameObject.SetActive(active);
                if (!active) continue;

                _orbRoots[i].position = _orbitals.OrbPosition(i);

                _glows[i].localScale = Vector3.one * diameter;
                SetMaterial(_glows[i], glowMat);

                _cores[i].localScale = Vector3.one * (diameter * CoreDiameterFraction);
                SetMaterial(_cores[i], coreMat);

                // MV-1336 change item 2 ("trail width multiplier halved"): less overlap between the
                // MaxOrbs trails riding the same circular path is what stops them merging into one
                // solid blooming band.
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
                trail.time = TrailSeconds;
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

        private static void SetMaterial(Transform t, Material mat)
        {
            if (t == null || mat == null) return;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }
    }
}
