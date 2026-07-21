using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Backyard boss — a possessed BOILER-MACHINE (YT-114).
    ///
    /// It was a possessed mower before this (YT-90), which was itself a rescue of the tinted 3.5 m CUBE
    /// the fight ran on from YT-27. Lee's Level-1 direction (2026-07-21) re-pitches the silhouette off
    /// the boiler-locomotive concept: a squat green/red possessed boiler with glowing eye-ports and a
    /// coughing smokestack. The one change from the concept art is the load-bearing one — it is NOT on
    /// rails. The rail bogies are gone; it stands and WALKS on four heavy piston legs, because "a
    /// free-roaming arena boss that moves around on its own" is a thing you read from the legs first.
    ///
    /// Built to be read from thirty metres up at 72°, the only angle this game has. From there a boss is
    /// a silhouette and a colour — and, crucially, HEIGHT FORESHORTENS: a tall figure shows you its top
    /// and hides its mass, so it reads small. Lee's on-camera review (2026-07-21) cut the first, tall cut
    /// down by ~35% into a SQUAT, WIDE siege-engine. From above this one is:
    ///
    ///   * A WIDE, LOW BODY — a squat green boiler drum on a broad red belly, split by a brass waist
    ///     ring, on legs planted wide. It is wider than it is tall on purpose: a big FOOTPRINT is what
    ///     fills screen at this angle, where height would just collapse into the top of the frame.
    ///   * GLOWING EYE-PORTS — one big brass-rimmed port on the front (where it charges) and two smaller
    ///     mismatched ones on the drum, welded on crooked. They are the face, they are what makes it a
    ///     character and not a water heater, and they are where the fight is read from (below).
    ///   * A SMOKESTACK that coughs, and a steam GOVERNOR that spins up as it builds pressure. A boiler
    ///     winding up to blow is the most on-concept telegraph this fight could possibly have.
    ///   * FOUR LEGS, splayed out under the belly and planted on the lawn. They pump when it walks and
    ///     lean into a charge. This is the "not on rails" decision made visible.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// THE TELL, AND WHY IT IS ONE WRITER
    ///
    /// The wind-up tell died for the whole life of this fight (YT-90 diagnosed it): the boss wrote an
    /// orange warn colour into its MaterialPropertyBlock, and <see cref="CharacterSkin"/> overwrote that
    /// same block every LateUpdate with the flat body colour — two writers on one block, script order
    /// picks the winner, the player sees nothing. The fix is ONE writer. This rig owns every renderer on
    /// the machine and no director can reach them:
    ///
    ///   * <see cref="RuntimeSurfaceDirector"/> skips anything under a <see cref="KeepsOwnMaterial"/>,
    ///     which this object carries.
    ///   * <see cref="CharacterSkinDirector"/> only claims renderers under an <see cref="IDamageable"/>,
    ///     and the machine is NOT parented to the boss (see <see cref="Follow"/>), so it claims none.
    ///
    /// And because nothing else hands these renderers a material, every part is given a real one here: a
    /// primitive's default material has no URP subshader and ships MAGENTA (YT-58/YT-38).
    ///
    /// ---------------------------------------------------------------------------------------------
    /// READING THE FIGHT OFF THE MACHINE
    ///
    /// Three states, three colours, in the language the player has already been taught:
    ///
    ///   ASLEEP      ports dark. It stands beyond the gate from the first frame; a dead machine that
    ///               opens its eyes when the last factory falls is worth more than one that fades in.
    ///   AWAKE       AMBER — the furnace glow, off the concept's eye-ports. It pops on the green body
    ///               where a green eye vanished, and the telegraph survives because the warn cools the
    ///               gold OUT toward red (a green→orange idle-to-warn shift was never the point; a
    ///               gold→orange one reads just as clearly and looks like a furnace being stoked).
    ///   WINDING UP  hot ORANGE, the whole boiler glows, the governor screams up and the stack coughs —
    ///               a boiler over-pressuring. This is <see cref="WarnColor"/>, the same orange every
    ///               telegraph in the game uses. You get the wind-up window the fight always gave you
    ///               and, until YT-90, never showed you.
    ///   ENRAGED     RED, and it holds red between attacks, so phase 2 is legible at a glance.
    ///
    /// Reads the fight, writes nothing to it: <see cref="BigBermudaBoss.Action"/>,
    /// <see cref="BigBermudaBoss.Enraged"/> and <see cref="BigBermudaBoss.Engaged"/> are getters, and
    /// the intro/defeat beats come off the same <see cref="HudSignals"/> YT-55's spectacle uses.
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
        // Straight off the concept: green boiler, red belly, brass trim, a purple cap. This is a DEPARTURE
        // from the mower's near-black body — the boiler wants its own colours, not a rim-lit silhouette —
        // so the figure-ground work is done by the red/brass/purple against a desaturated lawn and by the
        // boss simply being three times Max's height, rather than by a black body under a rim light.

        private static readonly Color BoilerGreen = new Color(0.11f, 0.40f, 0.33f);  // deep teal drum — metal, not candy, and off the yellow-green lawn
        private static readonly Color BoilerRed = new Color(0.60f, 0.15f, 0.17f);    // crimson belly + legs
        private static readonly Color Brass = new Color(0.72f, 0.54f, 0.22f);        // waist ring, port rims, spouts
        private static readonly Color CapPurple = new Color(0.30f, 0.26f, 0.46f);    // the turret cap
        private static readonly Color Steel = new Color(0.24f, 0.25f, 0.30f);        // the stack, the governor

        /// <summary>Awake and idle: AMBER — the furnace glow, straight off the concept's eye-ports. The
        /// mower idled green because its body was near-black and green popped on it; this body is GREEN,
        /// so a green eye is green-on-green and vanishes (the first squat render proved it). Amber pops
        /// hard on the teal drum, it is the boiler's own fire, and the telegraph still reads because the
        /// warn/rage cool the gold OUT toward red.</summary>
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

        [Header("Pressure (the governor + the glow)")]
        [Tooltip("How fast boiler pressure builds when it commits to a charge, and bleeds off after. " +
                 "Up fast, down slow — a pressure vessel has lag, and the slow bleed is what makes the " +
                 "recover read as 'it is spent' rather than 'it switched off'.")]
        [SerializeField] private float pressureRise = 5.5f;
        [SerializeField] private float pressureFall = 1.6f;

        [Tooltip("Governor revolutions/sec at full pressure. Fast enough to blur — a governor at speed " +
                 "is the mechanical readout of 'about to blow'.")]
        [SerializeField] private float governorMaxSpin = 900f;

        [Header("The eyes")]
        [Tooltip("How fast the ports reach the colour the phase calls for. Fast: a telegraph that eases " +
                 "in is a telegraph that arrives after the charge does.")]
        [SerializeField] private float eyeResponse = 9f;

        [Tooltip("How much of the tell colour bleeds into the boiler as it heats. Kept modest so the " +
                 "green stays green until it actually commits.")]
        [Range(0f, 1f)]
        [SerializeField] private float chassisHeat = 0.5f;

        [Header("Gait")]
        [Tooltip("How far the legs swing, in metres, per metre the boss travels. The walk is driven by " +
                 "distance moved, not a clock, so it never has to know the boss's speed or be re-tuned.")]
        [SerializeField] private float strideScale = 0.9f;

        [Tooltip("How deep it crouches as it winds up — the coil before the charge. Metres.")]
        [SerializeField] private float crouchDepth = 0.35f;

        [Header("Exhaust")]
        [SerializeField] private float exhaustIdleRate = 5f;
        [SerializeField] private float exhaustChargeRate = 46f;
        [SerializeField] private Color exhaustColor = new Color(0.32f, 0.32f, 0.31f, 0.5f);

        // ---------------------------------------------------------------- state

        private BigBermudaBoss _boss;
        private Transform _chassis;      // everything above the legs — bobs and crouches as one
        private Transform _governor;     // the spinner on the cap
        private ParticleSystem _exhaust;

        private Material _greenMat;
        private Material _redMat;
        private Material _brassMat;
        private Material _capMat;
        private Material _steelMat;

        private readonly MeshRenderer[] _eyes = new MeshRenderer[3];   // big front + two mismatched
        private MaterialPropertyBlock _eyeMpb;

        private const int LegCount = 4;
        private readonly Transform[] _legLift = new Transform[LegCount];   // the thing that swings per leg
        private readonly float[] _legPhase = new float[LegCount];

        private Color _eyeColor = Color.black;   // starts DARK: asleep beyond the gate
        private float _pressure;                 // 0..1, boiler pressure — spikes on commit
        private float _governorSpin;
        private float _flash;                    // 1 the frame it takes a hit, decays
        private float _lastHealth = 1f;
        private float _wakeTimer;                // >0 while the ports stutter on
        private float _gaitPhase;                // advanced by distance travelled
        private float _crouch;                   // current crouch offset, eased
        private Vector3 _lastPos;
        private bool _awake;
        private bool _dying;
        private float _dieTimer;

        /// <summary>Awake and running. False while dormant, and false forever once it is dead.</summary>
        public bool Running => _awake && !_dying;

        /// <summary>The colour the ports are burning — the tell in one value, for a test to read.</summary>
        public Color EyeColor => _eyeColor;

        /// <summary>Boiler pressure, 0 idle … 1 about to blow. Spikes through a wind-up and charge; this
        /// is the boiler's version of the mower's reel-spin — the numeric a test can watch commit.</summary>
        public float Pressure => _pressure;

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

            _eyeMpb = new MaterialPropertyBlock();
            BuildMaterials();

            // The greybox cube goes. Its COLLIDERS stay — the CharacterController and box are what the
            // Water Blaster hits and what Max walks into. Only the visual changes (ModelSwap,
            // docs/CODE_DRIVEN_SCENES.md), which is the whole reason this is an art-stream ticket.
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            Build();

            // Stand it on the boss BEFORE the first frame, or the machine spends frame one at the world
            // origin and frame two thirty metres away — and the gait, which reads its speed off distance
            // travelled, spins up as though it crossed the yard in a sixtieth of a second.
            Follow();
            _lastPos = transform.position;

            ApplyEyes(Color.black);   // asleep
        }

        /// <summary>
        /// Five materials, all OURS and all instances.
        ///
        /// Instances rather than the shared <see cref="MaterialLibrary.Character()"/>, because that one
        /// material is worn by Max and every robot in the yard: heating the boiler by tinting it would
        /// set the whole cast on fire. Owning instances is also what lets the boiler glow with one
        /// property write per material instead of a MaterialPropertyBlock on thirty renderers — a block
        /// breaks SRP batching; a shared instance keeps it.
        /// </summary>
        private void BuildMaterials()
        {
            var character = MaterialLibrary.Character();
            _greenMat = NewCharacterMaterial(character, "Boiler_Green", BoilerGreen);
            _redMat = NewCharacterMaterial(character, "Boiler_Red", BoilerRed);
            _brassMat = NewCharacterMaterial(character, "Boiler_Brass", Brass);
            _capMat = NewCharacterMaterial(character, "Boiler_Cap", CapPurple);
            _steelMat = NewCharacterMaterial(character, "Boiler_Steel", Steel);
        }

        private static Material NewCharacterMaterial(Material template, string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58): a
            // plain lit material still draws a correctly coloured machine, just without the outline.
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);

            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty(EmissionId))
            {
                // Turn emission ON so the boiler-heat write in TickTells actually lights: a cloned
                // material that had the keyword off would take the colour and render nothing.
                m.SetColor(EmissionId, Color.black);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            return m;
        }

        /// <summary>
        /// The machine, in metres, ground at y = 0, +Z pointing at whatever it is about to run over.
        /// Nothing is parented to the boss: the boss transform is SCALED (3.5, 3, 3.5), so a 20 cm bolt
        /// hung off it would render as a 70 cm one (the bug that made the factory bar metres wide, YT-71)
        /// — and being outside the boss is what keeps CharacterSkinDirector off these renderers.
        ///
        /// A "chassis" child holds everything above the legs, so the whole body can crouch and bob as one
        /// while the legs stay planted on the lawn.
        /// </summary>
        private void Build()
        {
            var chassisGo = new GameObject("Chassis");
            chassisGo.transform.SetParent(transform, worldPositionStays: false);
            _chassis = chassisGo.transform;

            BuildLegs();     // parented to the ROOT — they stand on the ground, the chassis rides them
            BuildBoiler();   // parented to the chassis
            BuildFace();
            BuildStack();
            BuildSpouts();

            _exhaust = BuildExhaust(new Vector3(StackX, StackTopY + 0.1f, StackZ));
        }

        // ---- dimensions, so the stack of parts lines up and the tests can reason about it ----
        //
        // SQUAT AND WIDE, from Lee's on-camera review (2026-07-21). The first cut was ~5.4 m tall and
        // read SMALL: on a 72° camera a tall figure foreshortens — you see the top but not the mass. So
        // the whole machine is ~35% shorter (≈3.5 m) and much wider, a siege-engine / sumo squat with a
        // low centre of gravity and a broad planted footprint. Width is what reads as big at this angle;
        // the boiler mass now sits in the readable MIDDLE band of the screen, out of the foreshortened
        // top. Everything is measured off the actual game angle, not a straight-on hero render.
        private const float BellyY = 1.0f, BellyH = 1.3f, BellyR = 1.75f;   // wide, low, heavy chassis
        private const float DrumY = 2.15f, DrumH = 1.1f, DrumR = 1.55f;     // boiler mass, dropped to mid-band
        private const float WaistY = 1.62f;
        private const float CapY = 2.95f;
        private const float StackX = 0.42f, StackZ = -0.9f, StackBaseY = 2.5f, StackTopY = 3.5f;  // short + fat
        private const float LegSpreadX = 2.05f, LegSpreadZ = 1.75f, HipY = 1.0f;   // planted wide

        /// <summary>The boiler body: a wide red belly, a brass waist, a squat green drum with a shallow
        /// lid, a small steel pressure-cap and a stout funnel. Broad and heavy — a wide low mass reads as
        /// big at the 72° angle where a tall one just foreshortens away.</summary>
        private void BuildBoiler()
        {
            // The red belly — a wide low drum, the heaviest part of the silhouette.
            Part("Belly", PrimitiveType.Cylinder, new Vector3(0f, BellyY, 0f),
                 new Vector3(BellyR * 2f, BellyH * 0.5f, BellyR * 2f), null, _redMat);

            // The brass waist ring — proud of both drums, the trim line that splits red from green.
            Part("Waist", PrimitiveType.Cylinder, new Vector3(0f, WaistY, 0f),
                 new Vector3((BellyR + 0.08f) * 2f, 0.16f, (BellyR + 0.08f) * 2f), null, _brassMat);

            // The green drum, and a SHALLOW domed lid — flattened on purpose. A tall round shoulder
            // reads as a head and a head reads as cute; a shallow lid reads as the top of a tank.
            Part("Drum", PrimitiveType.Cylinder, new Vector3(0f, DrumY, 0f),
                 new Vector3(DrumR * 2f, DrumH * 0.5f, DrumR * 2f), null, _greenMat);
            // A shallow lid — flattened hard now that the machine is squat, so it does not add height.
            Part("Shoulder", PrimitiveType.Sphere, new Vector3(0f, DrumY + DrumH * 0.5f, 0f),
                 new Vector3(DrumR * 2f, DrumR * 0.62f, DrumR * 2f), null, _greenMat);

            // A heavy brass collar caps the drum — the trim line at the top, matching the waist, so the
            // top reads as the lid of a pressure vessel rather than the crown of a head.
            Part("Collar", PrimitiveType.Cylinder, new Vector3(0f, DrumY + DrumH * 0.5f + 0.05f, 0f),
                 new Vector3((DrumR + 0.06f) * 2f, 0.14f, (DrumR + 0.06f) * 2f), null, _brassMat);

            // A modest steel pressure-cap, set back off-centre — small and industrial, NOT a head, so
            // the top does not read as a face looking up. The TALL element on top is the stack now.
            Part("Cap", PrimitiveType.Cylinder, new Vector3(0f, CapY, -0.2f),
                 new Vector3(0.72f, 0.2f, 0.72f), null, _steelMat);
            Part("CapDome", PrimitiveType.Sphere, new Vector3(0f, CapY + 0.16f, -0.2f),
                 new Vector3(0.6f, 0.4f, 0.6f), null, _steelMat);
            // A short brass whistle-valve, not a tall antenna.
            Part("Whistle", PrimitiveType.Cylinder, new Vector3(-0.3f, CapY + 0.12f, 0.12f),
                 new Vector3(0.1f, 0.18f, 0.1f), null, _brassMat);

            // The governor: a steel hub with two out-flung balls on arms, spinning on the cap. A steam
            // governor flies OUT as it speeds up — a mechanical readout of pressure you can see from
            // across the yard. Built on its own pivot so it can spin without the rest of the cap.
            var govGo = new GameObject("Governor");
            govGo.transform.SetParent(_chassis, worldPositionStays: false);
            govGo.transform.localPosition = new Vector3(0f, CapY + 0.42f, -0.2f);
            _governor = govGo.transform;

            var hub = Part("GovHub", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.12f, 0.18f, 0.12f),
                           null, _steelMat);
            hub.SetParent(_governor, worldPositionStays: false);
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                var arm = Part($"GovArm{i}", PrimitiveType.Cube, new Vector3(s * 0.22f, 0.05f, 0f),
                               new Vector3(0.42f, 0.05f, 0.05f), Quaternion.Euler(0f, 0f, s * 18f), _steelMat);
                arm.SetParent(_governor, worldPositionStays: false);
                var ball = Part($"GovBall{i}", PrimitiveType.Sphere, new Vector3(s * 0.42f, 0.0f, 0f),
                                new Vector3(0.16f, 0.16f, 0.16f), null, _brassMat);
                ball.SetParent(_governor, worldPositionStays: false);
            }
        }

        /// <summary>
        /// The face. One big brass-rimmed port on the front where it charges, and two smaller ones
        /// welded on crooked at mismatched heights — salvaged, not designed. The ports are additive
        /// lights, not painted balls; they carry the tell.
        /// </summary>
        private void BuildFace()
        {
            // ONE big glaring eye, high on the front face where the game camera can actually see it (a
            // 72° top-down look foreshortens the lower body away). A cyclops reads as a threat; two
            // matched eyes read as a face, and a face reads as cute. A menacing brow/scowl is a great
            // art-polish lever, but at greybox a chunky brass box in front of the eye reads as a mail
            // slot — so the glare is left as a note for Lee, and the eye carries the front on its own.
            // The eye has to be visible from the 72° camera, which looks down at the TOP: a lens on the
            // vertical front face is seen edge-on and vanishes (the first squat cut proved it — the flat
            // green lid dominated and the face was gone). So the cyclops is a glowing DOME that bulges
            // up-and-forward off the front rim of the lid, where the top-down camera reads it as the one
            // bright focal point on the machine — a full sphere (flatten 1) so it reads from any angle.
            _eyes[0] = Port("EyeBig", new Vector3(0f, 2.66f, 1.12f), 1.05f, flatten: 1f);

            // Two smaller ports LOW on the wide red belly, off to the sides — furnace-lights, not eyes.
            // They still carry the tell (they pulse with the phase) but they sit where they cannot pair
            // up into a face. Placed on the belly's curved front, so z follows the radius.
            float bellyFrontZ = Mathf.Sqrt(Mathf.Max(0.01f, BellyR * BellyR - 1.0f * 1.0f));
            PortRing("EyeLRim", new Vector3(-1.0f, 1.02f, bellyFrontZ - 0.04f), 0.46f);
            _eyes[1] = Port("EyeL", new Vector3(-1.0f, 1.02f, bellyFrontZ + 0.06f), 0.3f);

            PortRing("EyeRRim", new Vector3(1.0f, 0.94f, bellyFrontZ - 0.04f), 0.46f);
            _eyes[2] = Port("EyeR", new Vector3(1.0f, 0.94f, bellyFrontZ + 0.06f), 0.3f);
        }

        /// <summary>A brass porthole rim — a short fat cylinder lying face-out, so the lens sits in a
        /// ring rather than floating on the hull.</summary>
        private void PortRing(string name, Vector3 at, float size)
        {
            Part(name, PrimitiveType.Cylinder, at, new Vector3(size, 0.08f, size),
                 Quaternion.Euler(90f, 0f, 0f), _brassMat);
        }

        private MeshRenderer Port(string name, Vector3 at, float size, float flatten = 0.55f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.transform.SetParent(_chassis, worldPositionStays: false);
            go.transform.localPosition = at;
            // flatten < 1 = a lens sunk into a ring; flatten 1 = a full dome that reads from above too.
            go.transform.localScale = new Vector3(size, size, size * flatten);

            var r = go.GetComponent<MeshRenderer>();
            // Additive + unlit: a port is a LIGHT. Shared VFX material + a property block per port, so
            // the three can burn different colours without minting a material each (the Hutch's vents,
            // YT-78, do exactly this).
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>The smokestack — a steel pipe with a brass band and a lip, off the back shoulder.
        /// A chimney on a machine says "boiler" faster than any amount of paint.</summary>
        private void BuildStack()
        {
            // Short and FAT now — a stout funnel, not a chimney on end. It reads as a boiler from the
            // side without adding the height the squat rework just cut.
            float midY = (StackBaseY + StackTopY) * 0.5f;
            Part("Stack", PrimitiveType.Cylinder, new Vector3(StackX, midY, StackZ),
                 new Vector3(0.62f, (StackTopY - StackBaseY) * 0.5f, 0.62f), null, _steelMat);
            Part("StackBand", PrimitiveType.Cylinder, new Vector3(StackX, StackBaseY + 0.35f, StackZ),
                 new Vector3(0.7f, 0.1f, 0.7f), null, _brassMat);
            // A flared lip in the purple cap-colour — the one splash of purple, at the top of the funnel.
            Part("StackLip", PrimitiveType.Cylinder, new Vector3(StackX, StackTopY, StackZ),
                 new Vector3(0.76f, 0.12f, 0.76f), null, _capMat);
        }

        /// <summary>Two cannon spouts off the sides — stubby barrels, one bigger, both bolted on crooked.
        /// The concept's maroon barrels: pure menace in the silhouette, and asymmetry that reads as
        /// "possessed junk" rather than "designed weapon".</summary>
        private void BuildSpouts()
        {
            // Jutting OUT to the sides, roughly level — from the 72° camera they read as two gun-arms
            // extending the silhouette wide and mean, which is exactly the width the squat rework wants.
            // Mounted on the drum's flank at mid-band height so they sit in the readable middle.
            Spout("SpoutL", new Vector3(-DrumR + 0.1f, 2.05f, 0.4f), new Vector3(-1f, -0.1f, 0.7f), 1.2f, 0.17f);
            Spout("SpoutR", new Vector3(DrumR - 0.05f, 1.85f, 0.35f), new Vector3(1f, -0.14f, 0.62f), 0.95f, 0.15f);
        }

        private void Spout(string name, Vector3 at, Vector3 dir, float length, float radius)
        {
            dir = dir.normalized;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);   // a cylinder's long axis is Y
            Part($"{name}Barrel", PrimitiveType.Cylinder, at + dir * length * 0.5f,
                 new Vector3(radius * 2f, length * 0.5f, radius * 2f), rot, _redMat);
            // A brass muzzle on the end.
            Part($"{name}Muzzle", PrimitiveType.Cylinder, at + dir * length,
                 new Vector3(radius * 2.4f, 0.1f, radius * 2.4f), rot, _brassMat);
        }

        /// <summary>
        /// Four legs, splayed out under the belly and planted on the lawn. THIS is the "not on rails"
        /// decision: a thing with legs walks where it likes; a thing on bogies is on a track. Each leg is
        /// a thigh angled out to a knee and a shin angled in to a foot — a squat, heavy, insectoid stance
        /// that reads as a walker from directly above, where you can see the feet around the hull.
        ///
        /// A "lift" pivot per leg carries the thigh+shin+foot, so the gait can swing the whole leg from
        /// the hip with one rotation rather than posing three parts.
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

                // Thigh: hip out and down toward the knee, which flares OUT past the foot — a bent,
                // braced knee reads as heavy and planted, a sumo stance rather than stilts.
                Vector3 knee = Vector3.Lerp(Vector3.zero, foot - hip, 0.55f)
                               + new Vector3(sx * 0.35f, 0.25f, 0f);
                LegBone("Thigh", Vector3.zero, knee, 0.28f, _legLift[i]);
                // Shin: knee down to the foot.
                LegBone("Shin", knee, foot - hip, 0.24f, _legLift[i]);
                // Foot: a big planted pad.
                var footPart = Part($"Foot", PrimitiveType.Cube, foot - hip + Vector3.up * 0.12f,
                                    new Vector3(0.7f, 0.24f, 0.9f), null, _redMat);
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
                            Quaternion.FromToRotation(Vector3.up, along.normalized), _redMat);
            bone.SetParent(parent, worldPositionStays: false);
        }

        /// <summary>One part of the machine, given a real material always — nothing here may keep a
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

            go.GetComponent<MeshRenderer>().sharedMaterial = mat ?? _greenMat;
            return go.transform;
        }

        /// <summary>Nothing on this machine can be shot or walked into. The boss's own CharacterController
        /// and box are the hitbox; an extra collider here would silently eat water aimed at the boss, and
        /// Max would bump into a leg gameplay does not believe is there.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private ParticleSystem BuildExhaust(Vector3 at)
        {
            var go = new GameObject("Exhaust");
            go.transform.SetParent(_chassis, worldPositionStays: false);
            go.transform.localPosition = at;

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // smoke is left BEHIND a charge
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startColor = exhaustColor;
            main.gravityModifier = -0.05f;
            main.maxParticles = 90;   // ambience budget: AmbiencePlayTests holds effects under 200

            var emission = ps.emission;
            emission.rateOverTime = 0f;   // asleep. Nothing is burning yet.

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.06f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // straight up out of the pipe

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.9f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.45f;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            // A ParticleSystem from AddComponent has NO material and draws nothing (YT-47).
            r.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- running it

        /// <summary>
        /// LateUpdate, not Update: the boss moves its CharacterController in Update, and a machine that
        /// followed it in Update would sit one frame behind its own hitbox — at a 12 m/s charge that is a
        /// fifth of a metre of daylight between the boiler and the damage.
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
            TickGait(dt);
        }

        /// <summary>
        /// The machine sits on the LAWN, under the boss, facing where the boss faces.
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
            // Yaw only. The boss only ever turns on the spot; taking its full rotation would let any
            // pitch the controller picks up tip the machine into the ground.
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }

        /// <summary>Winding up or charging: the two states where being near the front of this machine is
        /// fatal, and the two it has to shout about.</summary>
        private bool Committed =>
            _awake && (_boss.Action == BossAction.ChargeWindup || _boss.Action == BossAction.Charge);

        /// <summary>Pressure builds toward 1 while committed and bleeds off after — up fast, down slow.
        /// It drives the governor, the boiler glow and the crouch, so all three move as one system.</summary>
        private void TickPressure(float dt)
        {
            float target = Committed ? 1f : 0f;
            float rate = target > _pressure ? pressureRise : pressureFall;
            _pressure = Mathf.Lerp(_pressure, target, 1f - Mathf.Exp(-rate * dt));

            // The governor flies with pressure — spins up as it commits, coasts down after.
            _governorSpin = _pressure * governorMaxSpin;
            if (_governor != null) _governor.Rotate(Vector3.up, _governorSpin * dt, Space.Self);
        }

        private void TickTells(float dt)
        {
            Color target = TargetEyeColor();

            // A hit whites the ports out for a moment — same word the robots use (CharacterSkin.Flash);
            // on a boss with thousands of HP, "did that land?" is asked a lot.
            if (_flash > 0f) target = Color.Lerp(target, Color.white, _flash * 0.7f);

            _eyeColor = Color.Lerp(_eyeColor, target, 1f - Mathf.Exp(-eyeResponse * dt));
            ApplyEyes(_eyeColor);

            // The boiler glows with the tell as pressure builds — the green/red take the heat as emission,
            // so the body reads as heating without losing its own colour until it actually commits.
            float heat = Mathf.Max(_pressure, _flash) * chassisHeat;
            Color glow = _eyeColor * heat;
            if (_greenMat != null && _greenMat.HasProperty(EmissionId)) _greenMat.SetColor(EmissionId, glow);
            if (_redMat != null && _redMat.HasProperty(EmissionId)) _redMat.SetColor(EmissionId, glow);

            if (_exhaust != null)
            {
                var emission = _exhaust.emission;
                emission.rateOverTime = !_awake ? 0f
                    : Mathf.Lerp(exhaustIdleRate, exhaustChargeRate, _pressure);
            }
        }

        private Color TargetEyeColor()
        {
            if (!_awake) return Color.black;

            // Waking. The ports stutter — a machine dead in a garden a long time does not come on
            // cleanly. Lands on top of YT-55's dust and shockwave at the same moment.
            if (_wakeTimer > 0f)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 34f, 0f);
                return EyeIdle * (flicker > 0.45f ? 1f : 0.05f);
            }

            bool rage = _boss.Enraged;
            if (Committed) return rage ? EyeRageWarn : EyeWarn;
            return rage ? EyeRage : EyeIdle;
        }

        /// <summary>The three ports never quite agree — salvaged from different machines. The big front
        /// one burns full, the small ones dimmer and a touch cooler.</summary>
        private void ApplyEyes(Color c)
        {
            for (int i = 0; i < _eyes.Length; i++)
            {
                var r = _eyes[i];
                if (r == null) continue;

                Color eye = i == 0 ? c : c * 0.8f;
                eye.a = 1f;

                r.GetPropertyBlock(_eyeMpb);
                _eyeMpb.SetColor(BaseColorId, eye);
                r.SetPropertyBlock(_eyeMpb);
            }
        }

        /// <summary>
        /// The walk, and the coil before a charge.
        ///
        /// The gait is driven by DISTANCE the boss actually moved, not a clock — so the legs swing in
        /// step with real motion and nothing here has to know its speed or be re-tuned when gameplay
        /// changes it. Legs on opposite diagonals alternate, a stable four-beat walk. When it is standing
        /// still the legs settle; when it winds up the whole chassis crouches over planted feet — the
        /// coil that says a charge is coming, on top of the eye-heat and the governor.
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
                // Swing the leg from the hip: forward on the lift, a little up as it passes. Amplitude
                // eases toward zero as it stops moving, so a standing boss is not marching on the spot.
                float swing = Mathf.Sin(_gaitPhase + _legPhase[i]);
                float moving = Mathf.Clamp01(travelled / (0.02f + Time.deltaTime * 3f));
                float pitch = swing * 16f * moving;
                _legLift[i].localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // Crouch on wind-up: ease the chassis down over the feet, and lean into the charge.
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
        /// same signal; this is the machine underneath it coming on.</summary>
        private void OnEngaged(string name, int phases)
        {
            _awake = true;
            _wakeTimer = 0.9f;   // inside the boss's own 1.6 s intro — lit and running before it moves
            _lastHealth = 1f;

            if (_exhaust != null) _exhaust.Emit(26);   // it catches, hard
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

            if (_exhaust != null)
            {
                var emission = _exhaust.emission;
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
        /// The boss deactivates its own GameObject the instant it dies — but this machine is not parented
        /// to it, so it outlives it by half a second and actually DIES on screen: the ports go out, the
        /// governor seizes, and the whole boiler sags into the lawn while YT-55's flashes go off on top.
        /// Then it is gone, before the result card settles.
        /// </summary>
        private void TickDeath()
        {
            const float duration = 0.55f;

            _dieTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_dieTimer / duration);

            // The ports die first, and fast. Whatever was in there has left.
            _eyeColor = Color.Lerp(_eyeColor, Color.black, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            ApplyEyes(_eyeColor);

            // The governor seizes rather than coasting: something in it has caught.
            _governorSpin = Mathf.Lerp(_governorSpin, 0f, 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));
            if (_governor != null) _governor.Rotate(Vector3.up, _governorSpin * Time.unscaledDeltaTime, Space.Self);

            // And it topples — over into the grass it spent the whole fight cutting. ABSOLUTE offsets,
            // not accumulating ones: Follow() has already re-anchored the machine on the boss this frame,
            // so each is applied to a clean pose and driven entirely by t. Accumulate them instead and
            // the fall would depend on the frame rate.
            transform.rotation *= Quaternion.Euler(t * 11f, 0f, t * 7f);
            transform.position += Vector3.down * (t * t * 0.8f);

            if (_greenMat != null && _greenMat.HasProperty(EmissionId)) _greenMat.SetColor(EmissionId, Color.black);
            if (_redMat != null && _redMat.HasProperty(EmissionId)) _redMat.SetColor(EmissionId, Color.black);

            if (t >= 1f) gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            // Instances, and ours: nothing else points at them, so nothing else has to be told.
            foreach (var m in new[] { _greenMat, _redMat, _brassMat, _capMat, _steelMat })
                if (m != null) Destroy(m);
        }
    }
}
