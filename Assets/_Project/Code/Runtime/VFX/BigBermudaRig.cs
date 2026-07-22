using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Backyard boss — the BROOD-HULK, an otherworldly chitin carrier (YT-150).
    ///
    /// The game's story is now a ROBOT INVASION: the bosses are alien invaders seeded by the comet in
    /// the intro, not earthly machines. Lee's round-2 pick (2026-07-22) re-skins the boss off the
    /// "Brood-Hulk" concept — a squat, wide insect carapace on splayed legs, an ocular core burning at
    /// the front, a glowing brood-seam down the spine. It reads as HATCHED, not built. This SUPERSEDES
    /// the possessed-boiler skin (YT-114) and every earthly skin before it (mower YT-90, cube YT-27), but
    /// it is a RE-SKIN, not a rewrite: the fight-reading language, the distance-driven walk, the wake, the
    /// hit-flash and the on-screen death are all unchanged — only the body and its materials change.
    ///
    /// Built to be read from thirty metres up at 72°, the only angle this game has. From there a boss is a
    /// silhouette and a colour — and HEIGHT FORESHORTENS: a tall figure shows you its top and hides its
    /// mass, so it reads small. The Brood-Hulk keeps the squat/wide footprint the on-camera review landed
    /// on (YT-114): a big footprint is what fills screen at this angle, where height would collapse into
    /// the top of the frame. From above it is:
    ///
    ///   * A WIDE, LOW CARAPACE — a dark chitin shell over a broad thorax, split down the spine into two
    ///     wing-case halves. Wider than it is tall on purpose.
    ///   * A GLOWING OCULAR CORE at the front (where it faces you) plus two dimmer flank glands. This is
    ///     the face, and it is where the fight is read from (below). Cold alien body, hot amber core — the
    ///     contrast is the whole figure-ground read.
    ///   * TWO SIDE HATCHES — the wing-case shell-halves hinge up and out to disgorge the swarm. See
    ///     THE SPAWN TELL below: their open state is the spawn telegraph (YT-157).
    ///   * FOUR SPLAYED CHITIN LEGS, planted wide on the lawn. They pump when it walks and coil into a
    ///     charge — a walker, not a floater, which is a thing you read from the legs first.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// THE TELL, AND WHY IT IS ONE WRITER
    ///
    /// The wind-up tell died for the whole life of this fight once (YT-90): the boss wrote an orange warn
    /// colour into its MaterialPropertyBlock, and <see cref="CharacterSkin"/> overwrote that same block
    /// every LateUpdate with the flat body colour — two writers on one block, script order picks the
    /// winner, the player sees nothing. The fix is ONE writer. This rig owns every renderer on the body
    /// and no director can reach them:
    ///
    ///   * <see cref="RuntimeSurfaceDirector"/> skips anything under a <see cref="KeepsOwnMaterial"/>,
    ///     which this object carries.
    ///   * <see cref="CharacterSkinDirector"/> only claims renderers under an <see cref="IDamageable"/>,
    ///     and the body is NOT parented to the boss (see <see cref="Follow"/>), so it claims none.
    ///
    /// And because nothing else hands these renderers a material, every part is given a real one here: a
    /// primitive's default material has no URP subshader and ships MAGENTA (YT-58/YT-38).
    ///
    /// ---------------------------------------------------------------------------------------------
    /// READING THE FIGHT OFF THE BODY
    ///
    /// Three states, three colours, in the language the player has already been taught — unchanged from
    /// the boiler, because the tell colours are the game's own and every telegraph in it uses the same:
    ///
    ///   ASLEEP      core dark. It stands beyond the gate from the first frame; a dead thing that opens
    ///               its eye when the last factory falls is worth more than one that fades in.
    ///   AWAKE       AMBER — the brood-glow. It pops hard on the cold chitin, and the telegraph survives
    ///               because the warn cools the gold OUT toward red.
    ///   WINDING UP  hot ORANGE, the whole shell glows, the spine flares and it coils on its legs — the
    ///               charge wind-up. This is <see cref="WarnColor"/>, the same orange every telegraph uses.
    ///   ENRAGED     RED, and it holds red between attacks, so phase 2 is legible at a glance.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// THE SPAWN TELL — the L/R brood-hatches (YT-150 + YT-157)
    ///
    /// The Brood-Hulk's signature attack (YT-157) flings robots out of its SIDE HATCHES. So the two
    /// wing-case shell-halves are functional: they hinge up and out, the brood-cavity floods hot, and the
    /// swarm spills from the flanks. The open shell IS the spawn telegraph — a read distinct from the
    /// charge wind-up (which is the eye cooling + the coil), so the two never blur into one another.
    ///
    /// YT-157 has landed: the gameplay boss now flings real robots on a brood volley, and the hatches open
    /// on THAT — <see cref="BigBermudaBoss.SpawnWindup01"/> is 0 shut … 1 flung. The rig reads it and shows
    /// it; it never writes back. The eased hinge, the brood-cavity flood and the mote burst are all still
    /// owned here — the gameplay says only WHEN, the art says what it looks like. This is the same
    /// read-gameplay-write-nothing seam the factory door uses (<see cref="FactoryDoorway"/>).
    ///
    /// Reads the fight, writes nothing to it: <see cref="BigBermudaBoss.Action"/>,
    /// <see cref="BigBermudaBoss.Enraged"/> and <see cref="BigBermudaBoss.Engaged"/> are getters, and the
    /// intro/defeat beats come off the same <see cref="HudSignals"/> YT-55's spectacle uses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BigBermudaRig : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BigBermudaRig>() != null) return;
            if (FindFirstObjectByType<BigBermudaBoss>() == null) return;   // no boss, nothing to build

            new GameObject("BigBermudaRig").AddComponent<BigBermudaRig>();
        }

        // ---------------------------------------------------------------- the palette
        //
        // Straight off the round-2 concept: a COLD alien body (void chitin, aubergine plate, an
        // iridescent seam) against a HOT amber core. The cold-body/hot-core contrast is the figure-ground
        // read — the invader is dark and wrong, and the one thing that glows is the thing you fight by.

        private static readonly Color ChitinDark = new Color(0.10f, 0.078f, 0.157f);  // void chitin — shell + legs
        private static readonly Color ChitinPlate = new Color(0.165f, 0.129f, 0.251f);// aubergine plate — ridges, feet, mandibles
        private static readonly Color Iridescent = new Color(0.29f, 0.44f, 0.42f);    // seam/edge sheen — the one cold non-black
        private static readonly Color XenoTeal = new Color(0.33f, 0.88f, 0.88f);      // the brood-glow rim + the disgorged motes

        /// <summary>Awake and idle: AMBER — the brood-glow. It pops hard on the cold chitin, and the
        /// telegraph still reads because the warn/rage cool the gold OUT toward red.</summary>
        private static readonly Color EyeIdle = new Color(1f, 0.66f, 0.12f);

        /// <summary>Winding up. The gold cools to a hot orange — the green channel drops out, a clear
        /// shift from the idle even though both are warm.</summary>
        private static readonly Color EyeWarn = new Color(1f, 0.30f, 0.04f);

        /// <summary>Phase 2. Full red, held between attacks, so "it got worse" reads at a glance.</summary>
        private static readonly Color EyeRage = new Color(0.95f, 0.10f, 0.05f);

        /// <summary>Committed to a charge, enraged. A white-hot flare — nowhere hotter to go.</summary>
        private static readonly Color EyeRageWarn = new Color(1f, 0.55f, 0.30f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ---------------------------------------------------------------- tuning

        [Header("Pressure (the coil + the glow)")]
        [Tooltip("How fast it builds toward a charge, and bleeds off after. Up fast, down slow — the slow " +
                 "bleed is what makes the recover read as 'it is spent' rather than 'it switched off'.")]
        [SerializeField] private float pressureRise = 5.5f;
        [SerializeField] private float pressureFall = 1.6f;

        [Header("The eyes")]
        [Tooltip("How fast the core reaches the colour the phase calls for. Fast: a telegraph that eases " +
                 "in is a telegraph that arrives after the charge does.")]
        [SerializeField] private float eyeResponse = 9f;

        [Tooltip("How much of the tell colour bleeds into the shell as it heats. Kept modest so the chitin " +
                 "stays dark until it actually commits.")]
        [Range(0f, 1f)]
        [SerializeField] private float chassisHeat = 0.5f;

        [Header("Gait")]
        [Tooltip("How far the legs swing, in metres, per metre the boss travels. The walk is driven by " +
                 "distance moved, not a clock, so it never has to know the boss's speed or be re-tuned.")]
        [SerializeField] private float strideScale = 0.9f;

        [Tooltip("How deep it coils as it winds up — the crouch before the charge. Metres.")]
        [SerializeField] private float crouchDepth = 0.35f;

        [Header("Brood hatches (the spawn tell)")]
        [Tooltip("How far each wing-case hinges open, in degrees. Big enough that 'the shell is open' " +
                 "reads from the 72° camera.")]
        [SerializeField] private float hatchMaxDeg = 64f;

        [Tooltip("How fast the hatches hinge to the state the spawn tell calls for.")]
        [SerializeField] private float hatchResponse = 7f;

        // ---------------------------------------------------------------- state

        private BigBermudaBoss _boss;
        private Transform _chassis;       // everything above the legs — bobs and crouches as one
        private ParticleSystem _motes;    // the disgorged swarm, spat from the open hatches

        private Material _chitinMat;
        private Material _plateMat;
        private Material _iridMat;

        private readonly MeshRenderer[] _eyes = new MeshRenderer[3];   // ocular core + two flank glands
        private MeshRenderer _spine;      // the brood-seam down the back — takes the tell + pressure
        private MeshRenderer _brood;      // the cavity revealed when the hatches open
        private MaterialPropertyBlock _portMpb;

        private Transform _hatchL, _hatchR;   // the two wing-case hinge pivots

        private const int LegCount = 4;
        private readonly Transform[] _legLift = new Transform[LegCount];   // the thing that swings per leg
        private readonly float[] _legPhase = new float[LegCount];

        private Color _eyeColor = Color.black;   // starts DARK: asleep beyond the gate
        private float _pressure;                 // 0..1 — spikes on commit
        private float _flash;                    // 1 the frame it takes a hit, decays
        private float _lastHealth = 1f;
        private float _wakeTimer;                // >0 while the core stutters on
        private float _gaitPhase;                // advanced by distance travelled
        private float _crouch;                   // current crouch offset, eased
        private Vector3 _lastPos;
        private bool _awake;
        private bool _dying;
        private float _dieTimer;

        private float _hatchOpen;                // 0 shut … 1 fully hinged, eased
        private bool _venting;                   // true while the hatches are meant to be open (YT-157)

        /// <summary>Awake and running. False while dormant, and false forever once it is dead.</summary>
        public bool Running => _awake && !_dying;

        /// <summary>The colour the core is burning — the tell in one value, for a test to read.</summary>
        public Color EyeColor => _eyeColor;

        /// <summary>The coil, 0 idle … 1 about to charge. Spikes through a wind-up and charge; the numeric
        /// a test can watch commit.</summary>
        public float Pressure => _pressure;

        /// <summary>The hatches, 0 shut … 1 fully open. The spawn tell, in one value a test can read.</summary>
        public float HatchOpen => _hatchOpen;

        // ---------------------------------------------------------------- build

        private void OnEnable()
        {
            HudSignals.BossEngaged += OnEngaged;
            HudSignals.BossHealthChanged += OnHealth;
            HudSignals.BossDefeated += OnDefeated;
        }

        private void OnDisable()
        {
            HudSignals.BossEngaged -= OnEngaged;
            HudSignals.BossHealthChanged -= OnHealth;
            HudSignals.BossDefeated -= OnDefeated;
        }

        private void Awake()
        {
            _boss = FindFirstObjectByType<BigBermudaBoss>();
            if (_boss == null) return;

            // Both scene-sweeping directors honour this, and it covers everything parented below us.
            gameObject.AddComponent<KeepsOwnMaterial>();

            _portMpb = new MaterialPropertyBlock();
            BuildMaterials();

            // The greybox cube goes. Its COLLIDERS stay — the CharacterController and box are what the
            // Water Blaster hits and what Max walks into. Only the visual changes (ModelSwap,
            // docs/CODE_DRIVEN_SCENES.md), which is the whole reason this is an art-stream ticket.
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            Build();

            // Stand it on the boss BEFORE the first frame, or the body spends frame one at the world origin
            // and frame two thirty metres away — and the gait, which reads its speed off distance
            // travelled, spins up as though it crossed the yard in a sixtieth of a second.
            Follow();
            _lastPos = transform.position;

            ApplyEyes(Color.black);   // asleep
        }

        /// <summary>
        /// Three body materials, all OURS and all instances.
        ///
        /// Instances rather than the shared <see cref="MaterialLibrary.Character()"/>, because that one
        /// material is worn by Max and every robot in the yard: heating the shell by tinting it would set
        /// the whole cast on fire. Owning instances is also what lets the shell glow with one property
        /// write per material instead of a MaterialPropertyBlock on thirty renderers — a block breaks SRP
        /// batching; a shared instance keeps it.
        /// </summary>
        private void BuildMaterials()
        {
            var character = MaterialLibrary.Character();
            _chitinMat = NewCharacterMaterial(character, "Brood_Chitin", ChitinDark);
            _plateMat = NewCharacterMaterial(character, "Brood_Plate", ChitinPlate);
            _iridMat = NewCharacterMaterial(character, "Brood_Irid", Iridescent);
        }

        private static Material NewCharacterMaterial(Material template, string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58): a plain
            // lit material still draws a correctly coloured body, just without the outline.
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);

            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty(EmissionId))
            {
                // Turn emission ON so the brood-heat write in TickTells actually lights: a cloned material
                // that had the keyword off would take the colour and render nothing.
                m.SetColor(EmissionId, Color.black);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            return m;
        }

        // ---- dimensions, so the stack of parts lines up and the tests can reason about it ----
        //
        // SQUAT AND WIDE, carried over from the boiler's on-camera review (YT-114): a tall figure
        // foreshortens on a 72° camera — you see the top but not the mass — so the whole body is low and
        // broad, a sumo squat with a big planted footprint. Width is what reads as big at this angle.
        private const float ThoraxY = 1.2f, ThoraxH = 1.0f, ThoraxR = 1.72f;   // wide, low body drum
        private const float HingeY = 1.72f;                                    // the spine ridge the shells hinge on
        private const float ShellHalfX = 0.85f;                                // each wing-case centre, off the spine
        private const float HeadY = 1.2f, HeadZ = 1.35f;                       // the front segment carrying the core
        private const float LegSpreadX = 2.05f, LegSpreadZ = 1.75f, HipY = 1.0f;   // planted wide

        private void Build()
        {
            var chassisGo = new GameObject("Chassis");
            chassisGo.transform.SetParent(transform, worldPositionStays: false);
            _chassis = chassisGo.transform;

            BuildLegs();       // parented to the ROOT — they stand on the ground, the chassis rides them
            BuildBody();       // thorax + head, parented to the chassis
            BuildHatches();    // the two wing-case shells + the brood cavity
            BuildFace();       // ocular core + flank glands + spine seam

            _motes = BuildMotes(new Vector3(0f, HingeY + 0.1f, 0f));
        }

        /// <summary>The body under the shell: a wide low thorax and a front head segment. Broad and heavy —
        /// a wide low mass reads as big at the 72° angle where a tall one just foreshortens away.</summary>
        private void BuildBody()
        {
            // The thorax — a wide low drum, the heaviest part of the silhouette.
            Part("Thorax", PrimitiveType.Cylinder, new Vector3(0f, ThoraxY, 0f),
                 new Vector3(ThoraxR * 2f, ThoraxH * 0.5f, ThoraxR * 2f), null, _chitinMat);
            // An underbelly plate, a touch proud, so the thorax reads as segmented rather than a can.
            Part("Underplate", PrimitiveType.Cylinder, new Vector3(0f, ThoraxY - 0.42f, 0f),
                 new Vector3((ThoraxR - 0.12f) * 2f, 0.18f, (ThoraxR - 0.12f) * 2f), null, _plateMat);

            // The head segment — a low chitin wedge jutting forward, where the ocular core sits. Kept low
            // so it does not add height; the ocular bulges up-and-forward off it to catch the top-down cam.
            Part("Head", PrimitiveType.Sphere, new Vector3(0f, HeadY, HeadZ),
                 new Vector3(2.0f, 1.3f, 1.5f), null, _chitinMat);

            // Two mandibles jutting from the head — pure menace in the silhouette, and the asymmetry that
            // reads as "alien organism" rather than "moulded toy".
            Mandible("MandibleL", new Vector3(-0.55f, 0.9f, HeadZ + 0.5f), new Vector3(-0.5f, -0.15f, 1f));
            Mandible("MandibleR", new Vector3(0.55f, 0.86f, HeadZ + 0.5f), new Vector3(0.5f, -0.18f, 1f));
        }

        private void Mandible(string name, Vector3 at, Vector3 dir)
        {
            dir = dir.normalized;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
            Part(name, PrimitiveType.Cylinder, at + dir * 0.45f,
                 new Vector3(0.22f, 0.45f, 0.22f), rot, _plateMat);
        }

        /// <summary>
        /// The two wing-case HATCHES and the brood cavity beneath them.
        ///
        /// Each hatch is a flattened chitin shell parented to a HINGE PIVOT sitting on the spine ridge. The
        /// pivot rotates about the forward (Z) axis, so the shell's OUTER edge lifts up-and-out while its
        /// inner (spine) edge stays put — a beetle's wing-cases opening. Left hinges one way, right mirrors
        /// it. Closed, the two halves meet at the spine into one dome; open, the brood cavity floods and
        /// the swarm spills from the flanks. That open state is the spawn telegraph (YT-157).
        /// </summary>
        private void BuildHatches()
        {
            _hatchL = BuildHatch("HatchL", -1f);
            _hatchR = BuildHatch("HatchR", 1f);

            // The brood cavity — an additive glow inside the shell, revealed as the hatches lift. Off until
            // it vents; its brightness rides the hatch-open amount (TickHatches).
            _brood = Port("BroodCore", new Vector3(0f, HingeY - 0.15f, 0f), 1.3f, flatten: 0.7f);
            _brood.transform.localScale = new Vector3(1.5f, 0.8f, 2.1f);   // a long cavity down the back
        }

        private Transform BuildHatch(string name, float side)
        {
            // The hinge pivot on the spine ridge — rotate THIS to open the shell.
            var pivotGo = new GameObject(name);
            pivotGo.transform.SetParent(_chassis, worldPositionStays: false);
            pivotGo.transform.localPosition = new Vector3(0f, HingeY, 0f);
            var pivot = pivotGo.transform;

            // The shell half — a flattened ellipsoid, offset off the spine so its mass sits to one side.
            var shell = Part($"{name}Shell", PrimitiveType.Sphere,
                             new Vector3(side * ShellHalfX, 0f, 0f),
                             new Vector3(1.95f, 0.9f, 2.6f), null, _chitinMat);
            shell.SetParent(pivot, worldPositionStays: false);

            // Two segment ridges across the shell, in the plate colour — the chitin's plating, and what
            // makes the closed shell read as a carapace rather than a helmet. They ride the hatch open.
            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? -0.7f : 0.7f;
                var ridge = Part($"{name}Ridge{i}", PrimitiveType.Cube,
                                 new Vector3(side * ShellHalfX, 0.42f, z),
                                 new Vector3(1.7f, 0.1f, 0.28f), null, _plateMat);
                ridge.SetParent(pivot, worldPositionStays: false);
            }

            // A glowing rim along the inner (spine) edge — the iridescent seam that lights the crack open.
            var rim = Part($"{name}Rim", PrimitiveType.Cube,
                           new Vector3(side * 0.08f, 0.2f, 0f),
                           new Vector3(0.12f, 0.14f, 2.5f), null, _iridMat);
            rim.SetParent(pivot, worldPositionStays: false);

            return pivot;
        }

        /// <summary>
        /// The face. One big glowing OCULAR CORE bulging up-and-forward off the head, plus two dimmer flank
        /// glands, plus the brood-SEAM down the spine. The core has to be visible from the 72° camera,
        /// which looks down at the TOP: a lens on a vertical face is seen edge-on and vanishes, so the core
        /// is a full glowing sphere that reads from any angle. The seam and glands carry the tell too — a
        /// cold alien body with a hot line down its back.
        /// </summary>
        private void BuildFace()
        {
            // ONE big ocular core, high on the front where the camera can see it. A single unblinking eye
            // reads as a threat; two matched eyes read as a face, and a face reads as cute.
            _eyes[0] = Port("OcularCore", new Vector3(0f, 1.62f, HeadZ + 0.62f), 1.05f, flatten: 1f);

            // Two dimmer glands low on the front flanks — they pulse with the phase but sit where they
            // cannot pair up into a face.
            float flankZ = Mathf.Sqrt(Mathf.Max(0.01f, ThoraxR * ThoraxR - 1.0f * 1.0f));
            PortRing("GlandLRim", new Vector3(-1.0f, 1.02f, flankZ - 0.04f), 0.46f);
            _eyes[1] = Port("GlandL", new Vector3(-1.0f, 1.02f, flankZ + 0.06f), 0.3f);

            PortRing("GlandRRim", new Vector3(1.0f, 0.94f, flankZ - 0.04f), 0.46f);
            _eyes[2] = Port("GlandR", new Vector3(1.0f, 0.94f, flankZ + 0.06f), 0.3f);

            // The brood-seam down the spine, on the ridge between the two shells — the hot line the cold
            // body is split by. Takes the tell plus the pressure heat (TickTells).
            _spine = Port("SpineSeam", new Vector3(0f, HingeY + 0.16f, 0f), 1f, flatten: 1f);
            _spine.transform.localScale = new Vector3(0.16f, 0.12f, 2.4f);
        }

        /// <summary>A brass-ish porthole rim — a short fat cylinder lying face-out, so the gland sits in a
        /// ring rather than floating on the hull.</summary>
        private void PortRing(string name, Vector3 at, float size)
        {
            Part(name, PrimitiveType.Cylinder, at, new Vector3(size, 0.08f, size),
                 Quaternion.Euler(90f, 0f, 0f), _iridMat);
        }

        private MeshRenderer Port(string name, Vector3 at, float size, float flatten = 0.55f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.transform.SetParent(_chassis, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(size, size, size * flatten);

            var r = go.GetComponent<MeshRenderer>();
            // Additive + unlit: a port is a LIGHT. Shared VFX material + a property block per port, so each
            // can burn a different colour without minting a material each (the Hutch's vents, YT-78, do
            // exactly this).
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>
        /// Four legs, splayed out under the thorax and planted on the lawn. A walker walks where it likes;
        /// legs — not bogies — are how a player reads "it moves around on its own". Each leg is a thigh
        /// angled out to a knee and a shin angled in to a foot — a squat, heavy, insectoid stance that
        /// reads from directly above, where you see the feet around the hull.
        /// </summary>
        private void BuildLegs()
        {
            for (int i = 0; i < LegCount; i++)
            {
                float sx = (i == 0 || i == 3) ? -1f : 1f;
                float sz = (i < 2) ? 1f : -1f;
                var hip = new Vector3(sx * 0.9f, HipY, sz * 0.9f);
                var foot = new Vector3(sx * LegSpreadX, 0f, sz * LegSpreadZ);

                var liftGo = new GameObject($"Leg{i}");
                liftGo.transform.SetParent(transform, worldPositionStays: false);
                liftGo.transform.localPosition = hip;
                _legLift[i] = liftGo.transform;
                // Diagonal legs step together, opposite diagonals alternate — a stable four-beat walk.
                _legPhase[i] = (i == 0 || i == 2) ? 0f : Mathf.PI;

                // Thigh: hip out and up toward the knee, which flares OUT past the foot — a bent, braced
                // knee reads as heavy and planted, an insect stance rather than stilts.
                Vector3 knee = Vector3.Lerp(Vector3.zero, foot - hip, 0.55f)
                               + new Vector3(sx * 0.35f, 0.25f, 0f);
                LegBone("Thigh", Vector3.zero, knee, 0.26f, _legLift[i]);
                LegBone("Shin", knee, foot - hip, 0.22f, _legLift[i]);
                // Foot: a big planted claw-pad.
                var footPart = Part("Foot", PrimitiveType.Cube, foot - hip + Vector3.up * 0.12f,
                                    new Vector3(0.66f, 0.22f, 0.86f), null, _plateMat);
                footPart.SetParent(_legLift[i], worldPositionStays: false);
            }
        }

        /// <summary>One leg bone from <paramref name="a"/> to <paramref name="b"/> in the lift's space,
        /// aimed by construction rather than by a Euler angle I would be guessing the sign of.</summary>
        private void LegBone(string name, Vector3 a, Vector3 b, float thick, Transform parent)
        {
            Vector3 along = b - a;
            var bone = Part(name, PrimitiveType.Cylinder, (a + b) * 0.5f,
                            new Vector3(thick, along.magnitude * 0.5f, thick),
                            Quaternion.FromToRotation(Vector3.up, along.normalized), _chitinMat);
            bone.SetParent(parent, worldPositionStays: false);
        }

        /// <summary>One part of the body, given a real material always — nothing here may keep a
        /// primitive's default material, which has no URP subshader and ships MAGENTA.</summary>
        private Transform Part(string name, PrimitiveType shape, Vector3 at, Vector3 scale,
                               Quaternion? rot = null, Material mat = null)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Strip(go);

            go.transform.SetParent(_chassis != null ? _chassis : transform, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat ?? _chitinMat;
            return go.transform;
        }

        /// <summary>Nothing on this body can be shot or walked into. The boss's own CharacterController and
        /// box are the hitbox; an extra collider here would silently eat water aimed at the boss, and Max
        /// would bump into a leg gameplay does not believe is there.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        /// <summary>The disgorged swarm — a short burst of xeno-teal motes spat out of the hatches on a
        /// vent. Cosmetic only (YT-157 spawns the real robots); this is the visible "it is emptying" beat.</summary>
        private ParticleSystem BuildMotes(Vector3 at)
        {
            var go = new GameObject("BroodMotes");
            go.transform.SetParent(_chassis, worldPositionStays: false);
            go.transform.localPosition = at;

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // motes are left BEHIND as it moves
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startColor = XenoTeal;
            main.gravityModifier = 0.2f;
            main.maxParticles = 40;   // ambience budget: AmbiencePlayTests holds effects under 200

            var emission = ps.emission;
            emission.rateOverTime = 0f;   // bursts only, on a vent

            // Flung sideways out of the flanks, not up — a swarm spilling from the sides reads at 72°.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(XenoTeal, 0f), new GradientColorKey(XenoTeal, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            // A ParticleSystem from AddComponent has NO material and draws nothing (YT-47).
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- running it

        /// <summary>
        /// LateUpdate, not Update: the boss moves its CharacterController in Update, and a body that
        /// followed it in Update would sit one frame behind its own hitbox — at a 12 m/s charge that is a
        /// fifth of a metre of daylight between the shell and the damage.
        /// </summary>
        private void LateUpdate()
        {
            if (_boss == null) return;

            Follow();

            if (_dying) { TickDeath(); return; }

            float dt = Time.deltaTime;

            _flash = Mathf.Max(0f, _flash - dt * 7f);
            if (_wakeTimer > 0f) _wakeTimer = Mathf.Max(0f, _wakeTimer - dt);

            TickPressure(dt);
            TickTells(dt);
            TickHatches(dt);
            TickGait(dt);
        }

        /// <summary>
        /// The body sits on the LAWN, under the boss, facing where the boss faces.
        ///
        /// Its own y is thrown away and the ground is used instead, exactly as the shockwave does
        /// (BossSpectacle.Shockwave). The boss is a scaled cube balanced on a capsule; wherever gravity
        /// settles that, it is not where a walker's feet go, and a boss hovering over its own shadow is
        /// worse than a boss that is a cube.
        /// </summary>
        private void Follow()
        {
            Vector3 p = _boss.transform.position;
            var at = new Vector3(p.x, 0f, p.z);
            // Yaw only. The boss only ever turns on the spot; taking its full rotation would let any pitch
            // the controller picks up tip the body into the ground.
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }

        /// <summary>Winding up or charging: the two states where being near the front of this thing is
        /// fatal, and the two it has to shout about.</summary>
        private bool Committed =>
            _awake && (_boss.Action == BossAction.ChargeWindup || _boss.Action == BossAction.Charge);

        /// <summary>Pressure builds toward 1 while committed and bleeds off after — up fast, down slow. It
        /// drives the glow and the crouch, so both move as one system.</summary>
        private void TickPressure(float dt)
        {
            float target = Committed ? 1f : 0f;
            float rate = target > _pressure ? pressureRise : pressureFall;
            _pressure = Mathf.Lerp(_pressure, target, 1f - Mathf.Exp(-rate * dt));
        }

        private void TickTells(float dt)
        {
            Color target = TargetEyeColor();

            // A hit whites the core out for a moment — same word the robots use (CharacterSkin.Flash); on a
            // boss with thousands of HP, "did that land?" is asked a lot.
            if (_flash > 0f) target = Color.Lerp(target, Color.white, _flash * 0.7f);

            _eyeColor = Color.Lerp(_eyeColor, target, 1f - Mathf.Exp(-eyeResponse * dt));
            ApplyEyes(_eyeColor);

            // The brood-seam down the spine burns the tell, and brighter as pressure builds — the hot line
            // the cold body is split by.
            if (_spine != null)
            {
                Color seam = _eyeColor * (0.55f + _pressure * 0.8f);
                seam.a = 1f;
                _spine.GetPropertyBlock(_portMpb);
                _portMpb.SetColor(BaseColorId, seam);
                _spine.SetPropertyBlock(_portMpb);
            }

            // The shell glows with the tell as pressure builds — the chitin takes the heat as emission, so
            // the body reads as heating without losing its cold colour until it actually commits.
            float heat = Mathf.Max(_pressure, _flash) * chassisHeat;
            Color glow = _eyeColor * heat;
            if (_chitinMat != null && _chitinMat.HasProperty(EmissionId)) _chitinMat.SetColor(EmissionId, glow);
            if (_plateMat != null && _plateMat.HasProperty(EmissionId)) _plateMat.SetColor(EmissionId, glow * 0.6f);
        }

        private Color TargetEyeColor()
        {
            if (!_awake) return Color.black;

            // Waking. The core stutters — a thing dead in a garden a long time does not come on cleanly.
            // Lands on top of YT-55's dust and shockwave at the same moment.
            if (_wakeTimer > 0f)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 34f, 0f);
                return EyeIdle * (flicker > 0.45f ? 1f : 0.05f);
            }

            bool rage = _boss.Enraged;
            if (Committed) return rage ? EyeRageWarn : EyeWarn;
            return rage ? EyeRage : EyeIdle;
        }

        /// <summary>The three ports never quite agree — the big ocular core burns full, the flank glands
        /// dimmer and a touch cooler.</summary>
        private void ApplyEyes(Color c)
        {
            for (int i = 0; i < _eyes.Length; i++)
            {
                var r = _eyes[i];
                if (r == null) continue;

                Color eye = i == 0 ? c : c * 0.8f;
                eye.a = 1f;

                r.GetPropertyBlock(_portMpb);
                _portMpb.SetColor(BaseColorId, eye);
                r.SetPropertyBlock(_portMpb);
            }
        }

        /// <summary>
        /// The SPAWN TELL — the two wing-case hatches, and the brood cavity they reveal.
        ///
        /// The hatches open on a vent, hinge the shell up-and-out, and flood the cavity hot while a burst
        /// of motes spills from the flanks. They stay SHUT while it is committing to a charge, so the spawn
        /// read and the charge read never blur into one another. Asleep or dying, they are shut.
        ///
        /// YT-157 (the gameplay that flings the real robots) is not built yet — there is no Spawn action to
        /// read — so <see cref="_venting"/> is driven here by a stand-in art cadence. ONE LINE to swap when
        /// YT-157 lands: set `_venting` from the real spawn-windup signal and drop the cadence block.
        /// </summary>
        private void TickHatches(float dt)
        {
            // The hatches now open on the REAL spawn attack (YT-157 landed). BigBermudaBoss.SpawnWindup01
            // is 0 shut … 1 flung: it ramps up as the volley telegraphs, holds while the swarm spills, and
            // is already held shut while the boss commits to a charge — so the spawn read and the charge
            // read never blur into one another, exactly the rule the stand-in cadence used to enforce here.
            // The swarm spills as the shell first cracks, so the mote burst rides the rising edge.
            bool vent = _awake && !_dying && _boss.SpawnWindup01 > 0f;
            if (vent && !_venting && _motes != null) _motes.Emit(14);
            _venting = vent;

            float want = (_venting && !_dying) ? 1f : 0f;
            _hatchOpen = Mathf.Lerp(_hatchOpen, want, 1f - Mathf.Exp(-hatchResponse * dt));

            // Hinge each shell about the forward axis — left one way, right mirrored — so the OUTER edge
            // lifts while the spine edge stays put.
            float deg = _hatchOpen * hatchMaxDeg;
            if (_hatchL != null) _hatchL.localRotation = Quaternion.Euler(0f, 0f, deg);
            if (_hatchR != null) _hatchR.localRotation = Quaternion.Euler(0f, 0f, -deg);

            // The cavity floods as the shell opens — dark when shut, a hot amber wash (with a xeno-teal
            // rim) as it gapes.
            if (_brood != null)
            {
                Color cavity = Color.Lerp(EyeWarn, XenoTeal, 0.25f) * _hatchOpen;
                cavity.a = 1f;
                _brood.GetPropertyBlock(_portMpb);
                _portMpb.SetColor(BaseColorId, cavity);
                _brood.SetPropertyBlock(_portMpb);
            }
        }

        /// <summary>
        /// The walk, and the coil before a charge.
        ///
        /// The gait is driven by DISTANCE the boss actually moved, not a clock — so the legs swing in step
        /// with real motion and nothing here has to know its speed or be re-tuned when gameplay changes it.
        /// Legs on opposite diagonals alternate, a stable four-beat walk. When it is standing still the
        /// legs settle; when it winds up the whole chassis coils over planted feet — the crouch that says a
        /// charge is coming, on top of the eye-heat.
        /// </summary>
        private void TickGait(float dt)
        {
            Vector3 pos = transform.position;
            float travelled = Vector3.Distance(pos, _lastPos);
            _lastPos = pos;

            _gaitPhase += travelled * strideScale * 6f;

            for (int i = 0; i < LegCount; i++)
            {
                if (_legLift[i] == null) continue;
                float swing = Mathf.Sin(_gaitPhase + _legPhase[i]);
                float moving = Mathf.Clamp01(travelled / (0.02f + Time.deltaTime * 3f));
                float pitch = swing * 16f * moving;
                _legLift[i].localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // Coil on wind-up: ease the chassis down over the feet, and lean into the charge.
            float wantCrouch = _boss.Action == BossAction.ChargeWindup ? crouchDepth : 0f;
            _crouch = Mathf.Lerp(_crouch, wantCrouch, 1f - Mathf.Exp(-8f * dt));
            if (_chassis != null)
            {
                _chassis.localPosition = new Vector3(0f, -_crouch, 0f);
                float lean = _boss.Action == BossAction.Charge ? 6f : 0f;
                _chassis.localRotation = Quaternion.Lerp(
                    _chassis.localRotation, Quaternion.Euler(lean, 0f, 0f), 1f - Mathf.Exp(-6f * dt));
            }
        }

        // ---------------------------------------------------------------- the fight's beats

        /// <summary>The last factory is dead and it has woken. YT-55 throws dust and a shockwave on this
        /// same signal; this is the invader underneath it coming on.</summary>
        private void OnEngaged(string name, int phases)
        {
            _awake = true;
            _wakeTimer = 0.9f;   // inside the boss's own 1.6 s intro — lit and running before it moves
            _lastHealth = 1f;
        }

        /// <summary>Health only ever falls, so a fall is a hit. The signal carries a position, not a
        /// victim, so this is cleaner than guessing from proximity.</summary>
        private void OnHealth(float normalized)
        {
            if (normalized < _lastHealth) _flash = 1f;
            _lastHealth = normalized;
        }

        private void OnDefeated()
        {
            if (_dying) return;
            _dying = true;
            _dieTimer = 0f;
            _venting = false;

            if (_motes != null)
            {
                var emission = _motes.emission;
                emission.enabled = false;
            }
        }

        /// <summary>
        /// It comes apart, and it takes its time about it.
        ///
        /// UNSCALED, because the run ends on the frame the boss dies: RunTracker hears BossDefeated and
        /// ResultScreen sets timeScale = 0 the same frame, so anything on scaled time is frozen before it
        /// moves. YT-55's defeat sequence runs unscaled for exactly this reason and this runs alongside it.
        ///
        /// The boss deactivates its own GameObject the instant it dies — but this body is not parented to
        /// it, so it outlives it by half a second and actually DIES on screen: the core goes out, the
        /// shells sag apart, and the whole hulk sinks into the lawn while YT-55's flashes go off on top.
        /// Then it is gone, before the result card settles.
        /// </summary>
        private void TickDeath()
        {
            const float duration = 0.55f;

            _dieTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_dieTimer / duration);
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);

            // The core dies first, and fast. Whatever was in there has left.
            _eyeColor = Color.Lerp(_eyeColor, Color.black, k);
            ApplyEyes(_eyeColor);
            if (_spine != null)
            {
                _spine.GetPropertyBlock(_portMpb);
                _portMpb.SetColor(BaseColorId, _eyeColor);
                _spine.SetPropertyBlock(_portMpb);
            }

            // The shells sag half-open as it dies — the carrier splitting one last time. Absolute, driven
            // entirely by t (Follow() re-anchored us this frame, so each pose is applied clean; accumulate
            // instead and the fall would depend on the frame rate).
            _hatchOpen = Mathf.Lerp(_hatchOpen, 0.4f, k);
            float deg = _hatchOpen * hatchMaxDeg;
            if (_hatchL != null) _hatchL.localRotation = Quaternion.Euler(0f, 0f, deg);
            if (_hatchR != null) _hatchR.localRotation = Quaternion.Euler(0f, 0f, -deg);
            if (_brood != null)
            {
                _brood.GetPropertyBlock(_portMpb);
                _portMpb.SetColor(BaseColorId, Color.Lerp(EyeWarn, Color.black, t) * _hatchOpen);
                _brood.SetPropertyBlock(_portMpb);
            }

            // And it topples — over into the grass it spent the whole fight cutting.
            transform.rotation *= Quaternion.Euler(t * 11f, 0f, t * 7f);
            transform.position += Vector3.down * (t * t * 0.8f);

            if (_chitinMat != null && _chitinMat.HasProperty(EmissionId)) _chitinMat.SetColor(EmissionId, Color.black);
            if (_plateMat != null && _plateMat.HasProperty(EmissionId)) _plateMat.SetColor(EmissionId, Color.black);

            if (t >= 1f) gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            // Instances, and ours: nothing else points at them, so nothing else has to be told.
            foreach (var m in new[] { _chitinMat, _plateMat, _iridMat })
                if (m != null) Destroy(m);
        }
    }
}
