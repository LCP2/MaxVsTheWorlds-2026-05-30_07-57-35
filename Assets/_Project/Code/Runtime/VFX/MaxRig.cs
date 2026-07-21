using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Max (YT-95) — the 12-year-old tinkerer, and until now a capsule.
    ///
    /// The hero of the game has been Unity's default capsule primitive, tinted hot orange-red by
    /// <see cref="CharacterSkin"/>, with a cube stuck on the front so you could tell which way it was
    /// pointing. Every other actor in the yard has had a shape that means something for weeks — the
    /// rusher is a capsule because it is quick, the bruiser is a box because it is a fridge, the boss
    /// is a mower (YT-90). The one thing on screen the player is actually looking at was a blob.
    ///
    /// He is a kid in a red hoodie now, built to be read from thirty metres up at 72° — which, per the
    /// art bible, is the only angle anybody will ever see him from. That angle decides everything below:
    ///
    ///   * THE HOOD. Down at the shoulders, behind the neck. From almost overhead you cannot see a
    ///     face, a chest or a logo — you see the top of a head and the tops of two shoulders. A hood
    ///     lying across them is the one shape that says "kid in a hoodie" in plan view, and it is why
    ///     the hood is a piece of geometry and not a texture.
    ///   * THE BACKPACK, and the messy hair, and the wrench through the tool-belt. Three lumps that
    ///     break an otherwise symmetrical blob. A silhouette you can read is a silhouette with corners
    ///     on it; a capsule has none, which is exactly why the capsule failed.
    ///   * THE GOGGLES, pushed up on his forehead (GDD §9). They are the only thing on Max that is
    ///     bright and small, and the forehead is the one part of a face a top-down camera can see. They
    ///     are his eyes, as far as this game is concerned, so they are lit rather than painted — the
    ///     same trick the boss's lamps use.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// HE CARRIES THE GADGET, AND HE RAISES IT TO AIM
    ///
    /// From the GDD: "Holds the gadget two-handed at the hip when running; raises it to aim." That is
    /// a character note and it is also, for free, the clearest piece of gameplay feedback in the game:
    /// the Water Blaster only fires while the aim stick is pushed (<see cref="PlayerController.IsAiming"/>
    /// gates it), and until now NOTHING on screen told you the gadget was live except the water itself.
    /// Max presents the weapon. You can see the gun come up before a drop of water leaves it.
    ///
    /// Both hands are welded to the gun and the SLEEVES are rebuilt each frame to span shoulder-to-hand
    /// (<see cref="PoseArm"/>), so the arms cannot come off the weapon no matter what pose it is in.
    /// A stretchy sleeve on a 30-pixel character is invisible; an arm floating next to its own gun is
    /// not.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// NOTHING HERE MAY BE PAINTED BY ANYONE ELSE
    ///
    /// This is the same trap the boss's rig had to be built around, and it is worth stating plainly
    /// because it is not obvious and it is fatal:
    ///
    ///   * <see cref="CharacterSkinDirector"/> claims every MeshRenderer under an
    ///     <see cref="IDamageable"/> and repaints it flat orange in LateUpdate. Max IS an IDamageable
    ///     (<see cref="PlayerHealth"/>). So if this rig were parented to him — the obvious thing to do —
    ///     every part of it would be claimed and every colour below would be overwritten. His hair, his
    ///     jeans, his skin, the water in the tank: all flat hoodie-red, one frame later. The rig is
    ///     therefore a scene-root object that FOLLOWS Max (see <see cref="Follow"/>) and is under no
    ///     damageable at all. <see cref="KeepsOwnMaterial"/> does NOT save you here — the director does
    ///     not honour it.
    ///   * <see cref="RuntimeSurfaceDirector"/> DOES honour <see cref="KeepsOwnMaterial"/>, and without
    ///     it would classify these parts by shape and repaint the backpack as a paving stone.
    ///
    /// So every renderer is handed a real material explicitly, from materials this rig owns and
    /// destroys. A primitive's default material has no URP subshader and ships MAGENTA (YT-58).
    ///
    /// Reads gameplay, writes none of it: <see cref="PlayerController.MoveInput"/>, <see cref="PlayerController.IsAiming"/>,
    /// <see cref="PlayerController.IsDashing"/> and <see cref="WaterBlaster.IsFiring"/> are all getters.
    /// Delete this file and the game plays identically — Max just goes back to being a capsule.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaxRig : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MaxRig>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;   // no Max, nothing to build

            new GameObject("MaxRig").AddComponent<MaxRig>();
        }

        // ---------------------------------------------------------------- the palette
        //
        // Max is the only WARM thing that moves. The robots are turquoise and violet, the boss is
        // near-black, the yard is a held-back green and brown (YT-69, YT-77, YT-86). That temperature
        // split is the whole figure-ground plan, so nothing on this body is allowed to be cool except
        // the steel of the gadget and the water inside it — and those are 10 cm of him.

        /// <summary>The hoodie. Straight from <see cref="CharacterSkin"/>: Max's colour is decided in
        /// ONE place, and a second hot orange-red living here would drift away from it the first time
        /// anyone tuned either. It is also the colour of his ground ring (YT-85) and his damage
        /// numbers — "you" is one colour, everywhere.</summary>
        private static Color Hoodie => CharacterSkin.BaseColorFor(CharacterRole.Player);

        /// <summary>
        /// The hood and the sleeves. The same red, a step down in value.
        ///
        /// A step, and not a plunge. The first cut of this was 0.62 and it was wrong for a reason worth
        /// writing down: from overhead the hood is a big shape sitting right where the camera is looking,
        /// and at 0.62 it stopped reading as RED and started reading as a dark lump behind his head —
        /// which spends Max's contrast budget on making him look like he is carrying something. It is
        /// folded cloth, so it is darker than the stretched cloth over his chest; it is still his jumper,
        /// so it is still obviously his jumper.
        ///
        /// The sleeves take the same tone, and for a different reason: in the hip carry his left arm
        /// crosses his own chest, and an arm the exact colour of the chest behind it is not an arm, it
        /// is a stripe.
        /// </summary>
        private static Color HoodieShade => Hoodie * 0.80f;

        /// <summary>The kangaroo pocket, which really is a fold deep enough to go dark.</summary>
        private static Color HoodieDeep => Hoodie * 0.60f;

        /// <summary>Cargo trousers. Dark, and almost colourless on purpose.
        ///
        /// The legs are a third of him and they are the third nobody needs to read. Anything saturated
        /// down here competes with the hoodie for the eye, and anything olive or brown would put his
        /// legs in the same family as the lawn and the timber he is standing on. A dark cool neutral
        /// does neither: it recedes, and it makes the red above it louder by contrast.</summary>
        private static readonly Color Trousers = new Color(0.20f, 0.21f, 0.25f);

        private static readonly Color Skin = new Color(0.87f, 0.63f, 0.46f);

        /// <summary>Messy brown hair. The single biggest thing a 72° camera sees of him.</summary>
        private static readonly Color Hair = new Color(0.33f, 0.20f, 0.12f);

        /// <summary>The backpack, the tool-belt, the gloves. Worn canvas and leather — dark, so the
        /// pack reads as a lump on his back rather than as part of the hoodie, and low-saturation, so
        /// it never argues with the red.</summary>
        private static readonly Color CanvasTone = new Color(0.29f, 0.25f, 0.20f);

        private static readonly Color Rubber = new Color(0.13f, 0.13f, 0.15f);

        /// <summary>The soles of the high-tops. The only near-white on him, and it is at his feet —
        /// which is where the eye already goes, because that is where the ground ring is.</summary>
        private static readonly Color Bone = new Color(0.87f, 0.85f, 0.79f);

        /// <summary>The gadget. Cold, pale steel — the same family as the blades on the boss's reel and
        /// deliberately NOT the family of anything else on Max. A tool is not part of a person.</summary>
        private static readonly Color Steel = new Color(0.58f, 0.64f, 0.72f);

        /// <summary>The tank. This is <see cref="WaterVfx"/>'s own <c>waterColor</c>, to the digit —
        /// the ammunition you can see through the side of the gadget is the ammunition that comes out
        /// of it. Get this wrong and the tank is just a blue block.</summary>
        private static readonly Color Water = new Color(0.31f, 0.76f, 0.97f);

        /// <summary>Grip tape, holding a junk-built gadget together. Duct-tape yellow, gone grey.</summary>
        private static readonly Color Tape = new Color(0.62f, 0.55f, 0.33f);

        /// <summary>The goggle lenses, and the one warm glint on him. Amber, because amber is what
        /// workshop safety glass is — and because the only other lit eyes in the yard are the robots'
        /// cold turquoise and the boss's acid green. Nothing that glows on Max may be mistakable for
        /// something that is trying to kill him.</summary>
        private static readonly Color LensGlass = new Color(1f, 0.72f, 0.24f);

        /// <summary>Charms clipped to the pack (GDD §9: "backpack visibly stuffed with charms").</summary>
        private static readonly Color[] Charms =
        {
            new Color(1f, 0.78f, 0.25f),      // brass
            new Color(0.88f, 0.86f, 0.80f),   // bone
            new Color(0.75f, 0.18f, 0.20f),   // a red one, because of course he has a red one
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        // ---------------------------------------------------------------- the skeleton, in metres
        //
        // Ground at y = 0, +Z is where he is looking. He stands 1.83 m to the tips of his hair.
        //
        // His CharacterController is 2 m tall and 1 m wide (EnemyArchetype.PlayerHeight/PlayerRadius)
        // and that is a HITBOX, not a height — nothing about a top-down camera can see the top of a
        // capsule. What the yard's scale actually has to respect is the rule those constants exist to
        // enforce (YT-74): nothing in the swarm may out-size Max. The rusher stands 1.4 m and the
        // bruiser 1.15 m, so at 1.83 m he is comfortably the largest thing in the yard that is not the
        // boss — while still being a KID next to a 3 m mower, and while carrying the head-to-body ratio
        // (about 1:5.5) that reads as twelve years old rather than as a small adult. Chunky, not chibi.

        private const float HipY = 0.74f;         // the waist: legs below, everything else above
        private const float HipX = 0.155f;
        private const float ShoulderY = 0.505f;   // in TORSO space, so it bobs with him
        private const float ShoulderX = 0.30f;
        private const float SleeveWidth = 0.155f;

        /// <summary>Where the gadget sits when he is just running: down at the hip, across the body,
        /// held two-handed. This is the pose you see 90% of the time.</summary>
        private static readonly Vector3 GunHipPos = new Vector3(0.03f, 0.155f, 0.30f);
        private static readonly Vector3 GunHipRot = new Vector3(17f, -13f, 0f);

        /// <summary>
        /// And where it goes when he aims. Up, level, and pointed at whatever he is about to soak.
        ///
        /// The height is not a taste decision. <see cref="WaterBlaster"/> casts its damage from
        /// <c>transform.position</c> — Max's capsule centre, 1.0 m off the ground — and
        /// <see cref="WaterVfx"/> emits the stream from that same origin. In TORSO space (which starts
        /// at <see cref="HipY"/>) that is y = 0.26. This pose puts the barrel's axis at 0.285, so the
        /// water leaves the gadget at the height the gadget is actually held at, and the jet reads as
        /// coming out of the nozzle rather than out of his chest.
        /// </summary>
        private static readonly Vector3 GunAimPos = new Vector3(0.09f, 0.285f, 0.32f);
        private static readonly Vector3 GunAimRot = new Vector3(0f, 0f, 0f);

        /// <summary>Shoulders roll forward and in when he presents the weapon. Without this the left
        /// arm has to reach 70 cm across his own body to hold the fore-grip, and a sleeve that long on
        /// a 12-year-old reads as a broken rig.</summary>
        private static readonly Vector3 ShoulderRestOffset = Vector3.zero;
        private static readonly Vector3 ShoulderAimOffset = new Vector3(-0.05f, -0.01f, 0.10f);

        /// <summary>
        /// Where the gadget sits at a given presentation amount: 0 at the hip, 1 up and aiming. In
        /// TORSO space, which starts at <see cref="HipY"/>.
        ///
        /// Pure, and public, because the claim that actually matters here is a claim about THIS
        /// FUNCTION — "the water leaves the gadget at the height the gadget is held at" — and a test
        /// should be able to ask it without having to synthesise a gamepad to make Max aim.
        /// </summary>
        public static void GadgetPose(float aim, out Vector3 pos, out Quaternion rot)
        {
            float t = Mathf.Clamp01(aim);
            pos = Vector3.Lerp(GunHipPos, GunAimPos, t);
            rot = Quaternion.Slerp(Quaternion.Euler(GunHipRot), Quaternion.Euler(GunAimRot), t);
        }

        /// <summary>
        /// How high off the lawn the barrel is at a given presentation amount.
        ///
        /// At <paramref name="aim"/> = 1 this has to land on <see cref="WaterBlaster"/>'s firing
        /// origin — Max's capsule centre, <see cref="EnemyArchetype.PlayerHeight"/> / 2 — because that
        /// is where the damage is cast from and where <see cref="WaterVfx"/> puts the jet. Miss it and
        /// the water comes out of his chest while the gadget he is holding points somewhere else.
        /// </summary>
        public static float BarrelHeight(float aim) =>
            HipY + Vector3.Lerp(GunHipPos, GunAimPos, Mathf.Clamp01(aim)).y;

        // ---------------------------------------------------------------- tuning

        [Header("Run cycle")]
        [Tooltip("Strides per second at full stick. The legs swing at this rate and he bobs twice per " +
                 "stride, which is what a step is.")]
        [SerializeField] private float strideRate = 2.15f;

        [Tooltip("How far the legs swing, in degrees, at full stick.")]
        [SerializeField] private float legSwing = 32f;

        [Tooltip("How far he bobs, in metres, at full stick. Small. A bob you can measure is a bob " +
                 "that makes the whole character look like it is on a spring.")]
        [SerializeField] private float bob = 0.035f;

        [Tooltip("Degrees he leans INTO the direction he is travelling. Not the direction he is " +
                 "facing — he can strafe, and a kid running sideways leans sideways.")]
        [SerializeField] private float leanAngle = 9f;

        [Tooltip("Extra degrees of lean during the dash burst. He is throwing himself, not jogging.")]
        [SerializeField] private float dashLean = 17f;

        [Header("Secondary motion")]
        [Tooltip("How hard the hair and the charms whip when he changes direction. The GDD asks for " +
                 "'messy brown hair (secondary motion)' by name and this is it: they lag behind him, " +
                 "then catch up.")]
        [SerializeField] private float whip = 26f;

        [Tooltip("How fast the lag catches up. Low = floppier.")]
        [SerializeField] private float whipCatchUp = 7f;

        [Header("The line")]
        [Tooltip("Max's outline width, in the shader's screen-space units — and deliberately NOT the " +
                 "0.013 the rest of the cast wears (MaterialLibrary). The line is a constant number of " +
                 "PIXELS at any zoom; a robot is ONE primitive and wears it as a clean ring, while Max " +
                 "is thirty-five and wears thirty-five of them. Measured at the real camera: 0.013 " +
                 "buries him (a black smudge with an orange hem), 0.0045 still eats his arms and his " +
                 "gadget, 0.003 is an ink line on a kid you can find at a glance.")]
        [Range(0f, 0.02f)]
        [SerializeField] private float outlineWidth = 0.003f;

        [Header("The gadget")]
        [Tooltip("How fast the gadget comes up when he starts aiming. Fast — this is the tell that " +
                 "says the weapon is live, and a tell that eases in arrives after the shot.")]
        [SerializeField] private float presentSpeed = 13f;

        [Tooltip("How far the gadget kicks back while the water is actually flowing, in metres.")]
        [SerializeField] private float recoil = 0.022f;

        // ---------------------------------------------------------------- state

        private PlayerController _max;
        private WaterBlaster _blaster;

        private Transform _body;       // lean + dash pivot, at the ground
        private Transform _torso;      // bob + counter-rotation, at the waist
        private Transform _head;
        private Transform _hairPivot;
        private Transform _charmPivot;
        private Transform _gun;
        private Transform _nozzle;                 // the hose sprayer's tip — where the water leaves (YT-134)
        private Transform _armL, _armR;
        private Transform _handL, _handR;
        private readonly Transform[] _hips = new Transform[2];
        private readonly MeshRenderer[] _lenses = new MeshRenderer[2];

        // Outlined — the masses that make his silhouette.
        private Material _hoodieMat, _hoodieShadeMat, _trouserMat, _skinMat, _hairMat,
                         _canvasMat, _rubberMat, _steelMat, _waterMat;

        // Not outlined — the detail that a five-pixel line would eat. See CharacterMaterial.
        private Material _hoodieDeepMat, _hoodieTrimMat, _skinTrimMat, _hairTrimMat, _canvasTrimMat,
                         _rubberTrimMat, _steelTrimMat, _boneMat, _tapeMat, _hoseMat;
        private Material[] _charmMats;
        private MaterialPropertyBlock _lensMpb;

        private float _stride;
        private float _aim;            // 0 = at the hip, 1 = presented
        private Vector3 _lastPos;
        private Vector3 _velocity;
        private Vector3 _laggedVelocity;

        /// <summary>How far the gadget is presented: 0 at the hip, 1 up and aiming. What a test looks
        /// at to prove the weapon actually comes up when the aim stick does.</summary>
        public float AimPose => _aim;

        /// <summary>World position of the hose sprayer's tip — where the water leaves and where the
        /// visible tether should meet his hands (YT-134). The hose tether draws from a point off Max's
        /// capsule today; this lets it re-anchor to the real nozzle. Falls back to a metre up if the
        /// gadget has not built yet, so a caller never reads the world origin.</summary>
        public Vector3 MuzzleWorldPosition =>
            _nozzle != null ? _nozzle.position : transform.position + Vector3.up;

        /// <summary>Stride phase, in radians. Advances only while he is moving.</summary>
        public float Stride => _stride;

        // ---------------------------------------------------------------- build

        private void Awake()
        {
            _max = FindFirstObjectByType<PlayerController>();
            if (_max == null) return;

            _blaster = _max.GetComponent<WaterBlaster>();

            // RuntimeSurfaceDirector honours this, and it covers everything parented below us. Without
            // it, the director classifies each part BY SHAPE and repaints the backpack as a paving
            // stone — what it did to the factory's impeller (YT-78).
            //
            // CharacterSkinDirector does NOT honour it. Staying off Max's transform is what keeps that
            // one out; see the class comment.
            gameObject.AddComponent<KeepsOwnMaterial>();

            _lensMpb = new MaterialPropertyBlock();
            BuildMaterials();

            // The greybox goes — the capsule AND the cube nose that was bolted on so you could tell
            // which way the capsule was pointing (Stage34PlayerScaffold). Their COLLIDERS stay: the
            // CharacterController is what the robots hit and what Max walks the yard with, and only the
            // visual is this ticket's to change (docs/CODE_DRIVEN_SCENES.md).
            foreach (var r in _max.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                r.enabled = false;
            }

            Build();

            // Stand him on Max BEFORE the first frame, or he spends frame one at the world origin and
            // frame two three metres away — and the hair, which reads its whip off how far he actually
            // moved, snaps like he was fired out of a cannon.
            Follow();
            _lastPos = transform.position;
        }

        /// <summary>
        /// Eleven materials, all OURS.
        ///
        /// Instances of <see cref="MaterialLibrary.Character()"/> — never that material itself, which
        /// is worn by every robot in the yard and by the boss, and tinting it to give Max brown hair
        /// would give the entire cast brown hair.
        ///
        /// Instances rather than one material and eleven MaterialPropertyBlocks, for the same reason
        /// the boss's rig does it: a property block is what BREAKS SRP batching, and a shared material
        /// instance is what keeps it. Eleven materials on one shader batch; eleven blocks do not.
        /// </summary>
        private void BuildMaterials()
        {
            _hoodieMat = CharacterMaterial("Max_Hoodie", Hoodie);
            _hoodieShadeMat = CharacterMaterial("Max_HoodieShade", HoodieShade);
            _trouserMat = CharacterMaterial("Max_Trousers", Trousers);
            _skinMat = CharacterMaterial("Max_Skin", Skin);
            _hairMat = CharacterMaterial("Max_Hair", Hair);
            _canvasMat = CharacterMaterial("Max_Canvas", CanvasTone);
            _rubberMat = CharacterMaterial("Max_Rubber", Rubber);
            _steelMat = CharacterMaterial("Max_Steel", Steel);
            _waterMat = CharacterMaterial("Max_Water", Water);

            _hoodieDeepMat = DetailMaterial("Max_HoodieDeep", HoodieDeep);
            _hoodieTrimMat = DetailMaterial("Max_HoodieTrim", HoodieShade);
            _skinTrimMat = DetailMaterial("Max_SkinTrim", Skin);
            _hairTrimMat = DetailMaterial("Max_HairTrim", Hair);
            _canvasTrimMat = DetailMaterial("Max_CanvasTrim", CanvasTone);
            _rubberTrimMat = DetailMaterial("Max_RubberTrim", Rubber);
            _steelTrimMat = DetailMaterial("Max_SteelTrim", Steel);
            _boneMat = DetailMaterial("Max_Bone", Bone);
            _tapeMat = DetailMaterial("Max_Tape", Tape);
            // Garden-hose green — the same green the tether line is drawn in (HoseTether.HoseGreen), so
            // the coil on his hip and the line running to the tap read as one continuous hose (YT-134).
            _hoseMat = DetailMaterial("Max_Hose", new Color(0.24f, 0.55f, 0.30f));

            _charmMats = new Material[Charms.Length];
            for (int i = 0; i < Charms.Length; i++)
            {
                _charmMats[i] = DetailMaterial($"Max_Charm{i}", Charms[i]);
            }
        }

        /// <summary>
        /// AN OUTLINE IS A SILHOUETTE DEVICE, AND A 7 cm CHARM HAS NO SILHOUETTE.
        ///
        /// This is the measurement that nearly sank the whole ticket, and it is not obvious until you
        /// look at the shipped frame at the size it actually ships at.
        ///
        /// The character outline is a SCREEN-SPACE inverted hull — <c>positionCS.xy += dir *
        /// _OutlineWidth * positionCS.w</c> — so it holds a constant PIXEL width however far away the
        /// camera is. That is exactly right, and it is why the robots read at any zoom (YT-82). At the
        /// game's real camera it works out around five to eight pixels.
        ///
        /// A robot is ONE primitive, so it wears that as a clean ring. Max is thirty-five, and he wears
        /// THIRTY-FIVE of them. Two things follow, and neither is visible until you look at a real
        /// frame at the size it really ships at:
        ///
        ///   * Any part smaller than the line is SMALLER THAN ITS OWN OUTLINE. The hull swallows it and
        ///     it renders as a solid near-black lozenge. His charms, his hair tufts, his buckles, his
        ///     goggle strap — at gameplay zoom every one of them was pure outline.
        ///   * Every part that pokes into open air contributes its own six-pixel border, and the UNION
        ///     of all those borders — around the arms, the hood, the pack, the shoulders, the gadget —
        ///     is a black halo covering most of a character who is only forty-odd pixels tall.
        ///
        /// Together they turned the player character into a black smudge with an orange hem: measurably
        /// WORSE than the capsule he replaced, which was at least a solid slab of hot orange you could
        /// always find. That is a readability failure, and readability is the first tie-breaker in the
        /// Craft Bible — ahead of both game feel and the house style's love of a thick line.
        ///
        /// So the line is drawn where there is a silhouette to draw it on, and it is drawn THIN. The
        /// big masses are hulled at <see cref="outlineWidth"/>, which was picked by rendering him at
        /// the game's own camera and looking: 0.013 buries him, 0.0045 still eats his arms and his
        /// gadget, 0.003 is an ink line on a kid you can pick out of a fight at a glance. Everything
        /// too small to survive a line goes without one — up close it is a charm, and at gameplay zoom
        /// it is two honest pixels of brass instead of ten dishonest pixels of black.
        /// </summary>
        private Material CharacterMaterial(string name, Color color, bool outline = true)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58): a
            // plain lit material still draws a correctly coloured kid, just without the outline.
            var template = MaterialLibrary.Character();
            var m = template != null
                ? new Material(template)
                : new Material(MaterialLibrary.SurfaceShader);

            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, Color.black);
            if (m.HasProperty(OutlineWidthId)) m.SetFloat(OutlineWidthId, outline ? outlineWidth : 0f);
            return m;
        }

        /// <summary>A part too small to carry a line at all. See <see cref="CharacterMaterial"/>.</summary>
        private Material DetailMaterial(string name, Color color) =>
            CharacterMaterial(name, color, outline: false);

        // ---------------------------------------------------------------- the kid

        private void Build()
        {
            _body = Pivot("Body", transform, Vector3.zero);           // leans, at the ground
            _torso = Pivot("Torso", _body, new Vector3(0f, HipY, 0f)); // bobs, at the waist

            BuildLegs();
            BuildTorso();
            BuildPack();
            BuildHead();
            BuildGadget();
            BuildHoseCoil();
            BuildArms();
        }

        /// <summary>
        /// The legs, and the beat-up high-tops on the end of them.
        ///
        /// Each leg hangs off a pivot at the hip and swings around it, so a running cycle is one
        /// rotation per leg and nothing else. The shoe is parented to the SAME pivot, which is what
        /// keeps the foot on the end of the leg instead of sliding along the lawn beside it.
        /// </summary>
        private void BuildLegs()
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var hip = Pivot($"Hip{(i == 0 ? "L" : "R")}", _torso,
                                new Vector3(side * HipX, 0f, 0f));   // torso space: the waist is y = 0
                _hips[i] = hip;

                Part("Leg", hip, PrimitiveType.Cube,
                     new Vector3(0f, -0.31f, 0f), new Vector3(0.235f, 0.62f, 0.26f), _trouserMat);

                // The cargo pocket. It is 6 cm of geometry and it is the difference between "trousers"
                // and "cargo trousers" — and it is on the OUTSIDE of the thigh, which is the only part
                // of a leg a camera at 72° can see past the torso.
                Part("CargoPocket", hip, PrimitiveType.Cube,
                     new Vector3(side * 0.135f, -0.24f, 0.015f), new Vector3(0.055f, 0.19f, 0.215f),
                     _canvasTrimMat);

                // High-tops: a pale sole under a dark upper. Two boxes, and it reads as a trainer
                // because a trainer IS a pale sole under a dark upper. The shoe carries the outline —
                // it is a real corner of his silhouette — and the sole, which is 7 cm thick and would
                // be nothing but line, does not.
                Part("Sole", hip, PrimitiveType.Cube,
                     new Vector3(0f, -0.685f, 0.065f), new Vector3(0.265f, 0.075f, 0.40f), _boneMat);
                Part("Shoe", hip, PrimitiveType.Cube,
                     new Vector3(0f, -0.595f, 0.04f), new Vector3(0.245f, 0.15f, 0.325f), _rubberMat);
            }
        }

        private void BuildTorso()
        {
            // HE TAPERS. Two boxes, not one.
            //
            // The first cut was a single 60 cm slab from the belt to the neck and it read as a FRIDGE —
            // which is a problem twice over, because the bruiser is literally a fridge and the one thing
            // the player must never have to think about is which red box is him. Broad across the chest,
            // narrow at the waist: that shape is a person, and one box cannot make it.
            Part("Chest", _torso, PrimitiveType.Cube,
                 new Vector3(0f, 0.45f, 0f), new Vector3(0.60f, 0.34f, 0.38f), _hoodieMat);

            Part("Waist", _torso, PrimitiveType.Cube,
                 new Vector3(0f, 0.16f, 0f), new Vector3(0.47f, 0.32f, 0.33f), _hoodieMat);

            // Rounded shoulders, where the sleeves come out. They are spheres because everything else
            // up here is a box, and a body made entirely of boxes is a robot — the yard already has
            // twenty of those and they are the things trying to kill him.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part("Shoulder", _torso, PrimitiveType.Sphere,
                     new Vector3(side * 0.285f, 0.545f, 0f), new Vector3(0.24f, 0.23f, 0.30f),
                     _hoodieMat);
            }

            // The kangaroo pocket. Barely visible from overhead, and it is here for the frames where
            // the camera catches him side-on — but it costs one box.
            Part("Kangaroo", _torso, PrimitiveType.Cube,
                 new Vector3(0f, 0.185f, 0.185f), new Vector3(0.33f, 0.19f, 0.05f), _hoodieDeepMat);

            // THE HOOD — the load-bearing shape on the whole character, and it WRAPS.
            //
            // Seen from almost overhead every kid is a head and two shoulders, so the hood is what turns
            // those shoulders into a HOODIE. It is not a lump behind his neck: it is a collar that comes
            // up around the back and the sides of his head, so the plan view is a red horseshoe with a
            // face in the middle of it. That horseshoe is the single most recognisable thing about him
            // from the only angle this game has, and it is what puts the contrast budget back on the red
            // where it belongs.
            Part("Hood", _torso, PrimitiveType.Cube,
                 new Vector3(0f, 0.60f, -0.235f), new Vector3(0.44f, 0.28f, 0.26f), _hoodieShadeMat,
                 Quaternion.Euler(22f, 0f, 0f));

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                // No outline: a 14 cm collar is four pixels wide at the camera the game is played at,
                // and a five-pixel line around a four-pixel object is not an object, it is a line.
                // The hood and the shoulders it sits between carry the edge; this just fills it red.
                Part("Collar", _torso, PrimitiveType.Cube,
                     new Vector3(side * 0.19f, 0.60f, -0.10f), new Vector3(0.14f, 0.22f, 0.30f),
                     _hoodieTrimMat, Quaternion.Euler(0f, 0f, side * -13f));
            }

            Part("Neck", _torso, PrimitiveType.Cube,
                 new Vector3(0f, 0.615f, 0.015f), new Vector3(0.145f, 0.11f, 0.145f), _skinTrimMat);

            // The homemade tool-belt: bolts and gizmos (GDD §9). A belt, one heavy pouch, and a spanner
            // shoved through it at an angle.
            Part("Belt", _torso, PrimitiveType.Cube,
                 new Vector3(0f, 0.025f, 0f), new Vector3(0.585f, 0.10f, 0.40f), _canvasTrimMat);

            Part("Pouch", _torso, PrimitiveType.Cube,
                 new Vector3(0.315f, 0.005f, 0.03f), new Vector3(0.10f, 0.17f, 0.21f), _canvasTrimMat);

            // The spanner, shoved through the belt. It juts out past him on one side only — an
            // asymmetric lump is worth more to a 30-pixel silhouette than any amount of detail inside
            // it, and the boss's crooked lamp does the same job. Pale steel and no outline, so what
            // pokes out of him is a bright fleck rather than a black one.
            Part("Spanner", _torso, PrimitiveType.Cube,
                 new Vector3(-0.315f, 0.07f, -0.09f), new Vector3(0.05f, 0.27f, 0.05f), _steelTrimMat,
                 Quaternion.Euler(20f, 0f, -24f));
        }

        /// <summary>
        /// The backpack, and the charms hanging off it.
        ///
        /// It is deliberately SMALL. The first instinct is a big pack — it is the most visible thing on
        /// a character seen from behind and above — and that is exactly the trap: a big dark pack eats
        /// the red hoodie, which is the colour that says "this is you" in a crowded fight. It is big
        /// enough to break his outline and no bigger.
        /// </summary>
        private void BuildPack()
        {
            // Slung LOW, and smaller than it wants to be. Same argument as the head: from overhead a
            // big dark pack sits right where the camera is looking and eats the red that tells you
            // which of the things on screen is you. Down at his lower back it breaks his outline —
            // which is all it was ever for — without competing for the top of him.
            Part("Pack", _torso, PrimitiveType.Cube,
                 new Vector3(0f, 0.315f, -0.275f), new Vector3(0.37f, 0.40f, 0.18f), _canvasMat);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part("Strap", _torso, PrimitiveType.Cube,
                     new Vector3(side * 0.175f, 0.53f, -0.02f), new Vector3(0.07f, 0.055f, 0.42f),
                     _canvasTrimMat);
            }

            // "Backpack visibly stuffed with charms" (GDD §9) — and they swing, because a thing that
            // hangs off a running kid swings. They are the cheapest personality on the model.
            _charmPivot = Pivot("Charms", _torso, new Vector3(0f, 0.15f, -0.35f));

            Part("Charm", _charmPivot, PrimitiveType.Sphere,
                 new Vector3(-0.115f, -0.055f, 0f), Vector3.one * 0.08f, _charmMats[0]);
            Part("Charm", _charmPivot, PrimitiveType.Cube,
                 new Vector3(0.015f, -0.10f, 0.005f), Vector3.one * 0.072f, _charmMats[1],
                 Quaternion.Euler(0f, 22f, 16f));
            Part("Charm", _charmPivot, PrimitiveType.Sphere,
                 new Vector3(0.13f, -0.045f, -0.01f), Vector3.one * 0.068f, _charmMats[2]);
        }

        /// <summary>
        /// The head, the messy hair, and the goggles pushed up on his forehead.
        ///
        /// The hair cap is pushed UP and BACK off a sphere that is centred forward of it, which leaves a
        /// bare strip of forehead at the front — and that strip is the only real estate this camera
        /// angle gives you for a face. The goggles go there. Everything else about a face (eyes, mouth,
        /// the grease smear) is invisible from 72° and is therefore not modelled: the art bible says the
        /// fixed angle exists precisely to hide fine facial detail, so building it would be paying for
        /// something nobody can see.
        /// </summary>
        private void BuildHead()
        {
            _head = Pivot("Head", _torso, new Vector3(0f, 0.66f, 0f));

            // SMALLER THAN IT WANTS TO BE, and this is the one measurement on Max that was decided by
            // looking at the shipped frame rather than by taste.
            //
            // A stylised kid wants a big head. But the camera looks almost straight DOWN at him, so
            // whatever is on top of Max is most of Max — and what is on top of a head is HAIR, which is
            // brown, in a yard built out of brown timber and brown soil. The first cut had a 34 cm head
            // and it turned the player character into a dark brown disc with a red hem: the exact
            // figure-ground failure YT-86 exists to fix, arriving from the other direction. The old
            // capsule was a solid slab of hot orange and it was, whatever else was wrong with it,
            // findable.
            //
            // So the head gives ground to the hoodie. At 30 cm against a 60 cm chest and a hood that
            // wraps it, the plan view is a red mass with a brown dot in the middle of it — which is
            // both what a hoodie looks like from above and what a readable actor looks like from above.
            // Readability beats visual richness; the Craft Bible says so in that order.
            Part("Skull", _head, PrimitiveType.Sphere,
                 new Vector3(0f, 0.135f, 0.02f), new Vector3(0.30f, 0.30f, 0.29f), _skinMat);

            // The hair is a CAP on the top and back of his head, not a helmet around it — pushed back
            // far enough to leave a bare strip of forehead at the front, because that strip is the only
            // real estate this camera angle gives you for a face, and the goggles have to go on it.
            Part("Hair", _head, PrimitiveType.Sphere,
                 new Vector3(0f, 0.215f, -0.06f), new Vector3(0.325f, 0.21f, 0.32f), _hairMat);

            // THE GOGGLES, pushed up on his brow (GDD §9) — and pushed up is the whole point. The first
            // cut put the strap 4 cm above the centre of his face, which is not a brow, it is a
            // BLINDFOLD: it drew a hard black band straight across his eyes. It sits on the hairline
            // now, where a kid actually shoves them when he is not using them.
            Part("Strap", _head, PrimitiveType.Cube,
                 new Vector3(0f, 0.20f, 0.045f), new Vector3(0.30f, 0.06f, 0.26f), _rubberTrimMat);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                _lenses[i] = BuildLens($"Lens{(i == 0 ? "L" : "R")}",
                                       new Vector3(side * 0.078f, 0.205f, 0.145f));
            }

            // Messy. Two tufts, at two different angles, one bigger than the other — hair that matched
            // would read as a haircut, and this is a kid who has been under a lawnmower all morning.
            // They lag behind him when he turns (see TickSecondary).
            _hairPivot = Pivot("Tufts", _head, new Vector3(0f, 0.225f, -0.03f));

            Part("Tuft", _hairPivot, PrimitiveType.Cube,
                 new Vector3(-0.07f, 0.07f, 0.01f), new Vector3(0.085f, 0.125f, 0.085f), _hairTrimMat,
                 Quaternion.Euler(11f, 0f, -25f));
            Part("Tuft", _hairPivot, PrimitiveType.Cube,
                 new Vector3(0.062f, 0.08f, -0.055f), new Vector3(0.075f, 0.14f, 0.075f), _hairTrimMat,
                 Quaternion.Euler(-16f, 0f, 18f));
        }

        /// <summary>
        /// The Water Blaster. Junk-built, per every line the GDD writes about Max.
        ///
        /// A receiver, a barrel, a nozzle, and — the part that makes it read as a WATER gun rather than
        /// as a gun — a fat transparent-looking tank of water sitting on top of it, in the exact blue
        /// the stream comes out as. That is the whole silhouette of a Super Soaker and it is legible at
        /// eight pixels.
        ///
        /// The hands are children of this, not of the arms. Weld them to the gadget and they can never
        /// come off it; the sleeves are then stretched to reach them (<see cref="PoseArm"/>).
        /// </summary>
        private void BuildGadget()
        {
            _gun = Pivot("Gun", _torso, GunHipPos);
            _gun.localRotation = Quaternion.Euler(GunHipRot);

            Part("Receiver", _gun, PrimitiveType.Cube,
                 new Vector3(0f, 0f, 0.03f), new Vector3(0.115f, 0.135f, 0.35f), _steelMat);

            Part("Barrel", _gun, PrimitiveType.Cylinder,
                 new Vector3(0f, 0.01f, 0.31f), new Vector3(0.08f, 0.16f, 0.08f), _steelMat,
                 Quaternion.Euler(90f, 0f, 0f));

            _nozzle = Part("Nozzle", _gun, PrimitiveType.Cylinder,
                 new Vector3(0f, 0.01f, 0.485f), new Vector3(0.105f, 0.02f, 0.105f), _rubberTrimMat,
                 Quaternion.Euler(90f, 0f, 0f));

            // The tank. Lying along the top of the gun, full of the water that comes out the front.
            Part("Tank", _gun, PrimitiveType.Cylinder,
                 new Vector3(0f, 0.12f, -0.04f), new Vector3(0.125f, 0.135f, 0.125f), _waterMat,
                 Quaternion.Euler(90f, 0f, 0f));
            Part("Cap", _gun, PrimitiveType.Cylinder,
                 new Vector3(0f, 0.12f, 0.105f), new Vector3(0.075f, 0.02f, 0.075f), _steelTrimMat,
                 Quaternion.Euler(90f, 0f, 0f));

            Part("Grip", _gun, PrimitiveType.Cube,
                 new Vector3(0f, -0.135f, -0.06f), new Vector3(0.08f, 0.16f, 0.10f), _rubberTrimMat,
                 Quaternion.Euler(20f, 0f, 0f));

            // A wrap of tape where a shop-bought gun would have a foregrip. He built this out of a
            // pump-action bottle and whatever was on the bench.
            Part("Tape", _gun, PrimitiveType.Cube,
                 new Vector3(0f, 0.005f, 0.185f), new Vector3(0.13f, 0.10f, 0.05f), _tapeMat);

            _handR = Part("HandR", _gun, PrimitiveType.Cube,
                          new Vector3(0f, -0.055f, -0.05f), new Vector3(0.115f, 0.115f, 0.13f),
                          _rubberTrimMat);
            _handL = Part("HandL", _gun, PrimitiveType.Cube,
                          new Vector3(0f, -0.02f, 0.15f), new Vector3(0.115f, 0.115f, 0.14f),
                          _rubberTrimMat);

            // YT-134 — read it as a HOSE, not a self-contained bottle-gun. A green rubber hose feeds
            // into the BACK of the sprayer and drops away toward the hip coil (built on the body), so
            // the eye follows nozzle -> hose -> coil -> the tether line -> the tap. Two stub segments,
            // angled, so it curves rather than sticking straight out.
            Part("HoseInlet", _gun, PrimitiveType.Cylinder,
                 new Vector3(0f, -0.05f, -0.14f), new Vector3(0.06f, 0.12f, 0.06f), _hoseMat,
                 Quaternion.Euler(52f, 0f, 0f));
            Part("HoseDrop", _gun, PrimitiveType.Cylinder,
                 new Vector3(0f, -0.16f, -0.22f), new Vector3(0.06f, 0.12f, 0.06f), _hoseMat,
                 Quaternion.Euler(78f, 0f, 0f));
        }

        /// <summary>A coil of spare hose slung at Max's hip — where the tether line emanates, so the
        /// hose reads as carried ON him rather than sprouting from his belly (YT-134). Flattened rings
        /// stacked into a loop; parented to the torso so it bobs with him.</summary>
        private void BuildHoseCoil()
        {
            var coil = Pivot("HoseCoil", _torso, new Vector3(-0.24f, -0.02f, -0.12f));
            coil.localRotation = Quaternion.Euler(18f, 0f, 74f);   // hangs on his hip, slightly turned
            for (int i = 0; i < 3; i++)
            {
                Part($"Coil{i}", coil, PrimitiveType.Cylinder,
                     new Vector3(0f, 0.02f * (i - 1), 0f), new Vector3(0.2f, 0.03f, 0.2f), _hoseMat);
            }
        }

        /// <summary>Two sleeves. They have no pose of their own — they are stretched between the
        /// shoulder and whichever hand is where, every frame. See <see cref="PoseArm"/>.</summary>
        private void BuildArms()
        {
            _armL = Part("ArmL", _torso, PrimitiveType.Cube, Vector3.zero,
                         new Vector3(SleeveWidth, 0.4f, SleeveWidth), _hoodieShadeMat);
            _armR = Part("ArmR", _torso, PrimitiveType.Cube, Vector3.zero,
                         new Vector3(SleeveWidth, 0.4f, SleeveWidth), _hoodieShadeMat);
        }

        private static Transform Pivot(string name, Transform parent, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            return go.transform;
        }

        /// <summary>One piece of Max. Given a real material, always — nothing built here may keep a
        /// primitive's default material, which has no URP subshader and ships MAGENTA (YT-38).</summary>
        private static Transform Part(string name, Transform parent, PrimitiveType shape, Vector3 at,
                                      Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Strip(go);

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>A goggle lens. Additive and unlit, like the boss's lamps — glass CATCHES light, and
        /// a painted amber ball on a body that is mostly in its own shadow would just be a brown dot.
        /// Shared VFX material + a property block, so two lenses cost one material.</summary>
        private MeshRenderer BuildLens(string name, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);

            go.transform.SetParent(_head, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(0.115f, 0.10f, 0.075f);

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            r.GetPropertyBlock(_lensMpb);
            _lensMpb.SetColor(BaseColorId, LensGlass);
            r.SetPropertyBlock(_lensMpb);
            return r;
        }

        /// <summary>
        /// Nothing on Max can be shot or walked into.
        ///
        /// His CharacterController is the hitbox and it is gameplay's. An extra collider on the
        /// backpack would silently eat water that a player aimed past him, and the robots would bump
        /// into a spanner that gameplay does not believe is there.
        /// </summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // ---------------------------------------------------------------- running him

        /// <summary>
        /// LateUpdate, not Update: <see cref="PlayerController"/> moves the CharacterController in
        /// Update, and a Max who followed it in Update would render one frame behind his own hitbox —
        /// at an 18 m/s dash that is 30 cm of daylight between the kid and the thing the robots are
        /// actually hitting.
        /// </summary>
        private void LateUpdate()
        {
            if (_max == null) return;

            Follow();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;   // paused on the result screen — freeze him mid-stride, don't reset

            Vector3 pos = transform.position;
            _velocity = (pos - _lastPos) / dt;
            _lastPos = pos;

            TickRun(dt);
            TickGadget(dt);
            TickSecondary(dt);

            // The sleeves go LAST. They are stretched between the shoulders and the hands, and both of
            // those have just moved.
            PoseArms();
        }

        /// <summary>
        /// Max stands on the LAWN, under his own capsule, facing where he faces.
        ///
        /// His transform's y is his capsule's CENTRE (1 m up) and it drifts with the controller's skin
        /// width and gravity — so it is thrown away and the ground is used instead, exactly as the
        /// boss's rig does. Yaw only: <see cref="PlayerController"/> only ever turns him on the spot,
        /// and taking his full rotation would let any pitch the controller picks up tip the kid into
        /// the grass.
        /// </summary>
        private void Follow()
        {
            Vector3 p = _max.transform.position;
            transform.SetPositionAndRotation(
                new Vector3(p.x, 0f, p.z),
                Quaternion.Euler(0f, _max.transform.eulerAngles.y, 0f));
        }

        /// <summary>
        /// The run cycle: legs, bob, and a lean.
        ///
        /// The lean is into the direction he is TRAVELLING, not the direction he is facing, and those
        /// are routinely different — this is a twin-stick, so he spends most of a fight backpedalling
        /// away from a robot while hosing it down. A kid running backwards leans backwards. Getting
        /// this wrong is what makes a character look like it is being dragged rather than running.
        /// </summary>
        private void TickRun(float dt)
        {
            float speed01 = Mathf.Clamp01(_max.MoveInput.magnitude);

            // The stride only advances while he is actually moving, so he stops mid-step instead of
            // marching on the spot.
            _stride += strideRate * speed01 * dt * Mathf.PI * 2f;
            if (_stride > Mathf.PI * 2f) _stride -= Mathf.PI * 2f;

            float swing = Mathf.Sin(_stride) * legSwing * speed01;
            if (_hips[0] != null) _hips[0].localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (_hips[1] != null) _hips[1].localRotation = Quaternion.Euler(-swing, 0f, 0f);

            // Two bobs per stride — one per foot landing. Abs(), not Sin(), or he floats up on one
            // step and sinks through the lawn on the other.
            float bounce = Mathf.Abs(Mathf.Sin(_stride)) * bob * speed01;

            // Shoulders counter-rotate against the hips. Tiny, and it is what stops a run cycle from
            // reading as a puppet on a stick. The gadget is parented to the torso, so it swings with
            // him — which is what a thing held in two hands does.
            _torso.localPosition = new Vector3(0f, HipY + bounce, 0f);
            _torso.localRotation = Quaternion.Euler(0f, -swing * 0.14f, 0f);

            // Lean. Max's own yaw is our yaw, so his move input — which is already in world XZ — has to
            // come back into local space to know whether he is running forwards or sideways.
            Vector3 moveLocal = transform.InverseTransformDirection(
                new Vector3(_max.MoveInput.x, 0f, _max.MoveInput.y));

            float lean = leanAngle + (_max.IsDashing ? dashLean : 0f);
            _body.localRotation = Quaternion.Slerp(
                _body.localRotation,
                Quaternion.Euler(moveLocal.z * lean, 0f, -moveLocal.x * lean),
                1f - Mathf.Exp(-14f * dt));
        }

        /// <summary>
        /// Up to aim, down to run — the pose the GDD asks for by name, and the only thing on screen
        /// that says the gadget is live before the water does.
        /// </summary>
        private void TickGadget(float dt)
        {
            float target = _max.IsAiming ? 1f : 0f;
            _aim = Mathf.Lerp(_aim, target, 1f - Mathf.Exp(-presentSpeed * dt));

            GadgetPose(_aim, out Vector3 pos, out Quaternion rot);

            // A kick while the water is actually flowing. Not while merely AIMING: the blaster stops
            // firing when the energy runs out (YT-80), and a gun that keeps bucking on an empty tank is
            // a gun that is lying to you about whether you still have ammo.
            if (_blaster != null && _blaster.IsFiring)
            {
                // Along the gadget's own axis, so the kick is always backwards down the barrel.
                float shudder = Mathf.Sin(Time.time * 47f) * 0.35f + 0.65f;
                pos -= rot * Vector3.forward * (recoil * shudder);
            }

            _gun.localPosition = pos;
            _gun.localRotation = rot;
        }

        /// <summary>
        /// The hair and the charms lag behind him, then catch up.
        ///
        /// A smoothed velocity trails the real one; the DIFFERENCE between them is how hard he just
        /// changed direction, and that is what the hair reacts to. Start a dash and it blows back; stop
        /// dead and it swings forward past him. It costs one Vector3 and it is most of what separates a
        /// character from a statue being slid around a lawn.
        /// </summary>
        private void TickSecondary(float dt)
        {
            _laggedVelocity = Vector3.Lerp(_laggedVelocity, _velocity, 1f - Mathf.Exp(-whipCatchUp * dt));

            // In HIS space, so a hard left turn throws the hair to his right and not to the world's.
            Vector3 lag = transform.InverseTransformDirection(_laggedVelocity - _velocity);
            lag = Vector3.ClampMagnitude(lag, 6f);

            var tilt = Quaternion.Euler(lag.z * whip * 0.1f, 0f, -lag.x * whip * 0.1f);

            if (_hairPivot != null)
            {
                _hairPivot.localRotation = Quaternion.Slerp(_hairPivot.localRotation, tilt,
                                                            1f - Mathf.Exp(-18f * dt));
            }

            if (_charmPivot != null)
            {
                // The charms swing on the stride as well as on the turn — they are hanging off a bag
                // on a running kid, and a bag bounces.
                float swing = Mathf.Sin(_stride * 2f) * 7f * Mathf.Clamp01(_max.MoveInput.magnitude);
                _charmPivot.localRotation = Quaternion.Slerp(
                    _charmPivot.localRotation,
                    tilt * Quaternion.Euler(swing, 0f, 0f),
                    1f - Mathf.Exp(-13f * dt));
            }
        }

        private void PoseArms()
        {
            Vector3 aimOffset = Vector3.Lerp(ShoulderRestOffset, ShoulderAimOffset, _aim);

            PoseArm(_armL, new Vector3(-ShoulderX - aimOffset.x, ShoulderY + aimOffset.y, aimOffset.z), _handL);
            PoseArm(_armR, new Vector3(ShoulderX + aimOffset.x, ShoulderY + aimOffset.y, aimOffset.z), _handR);
        }

        /// <summary>
        /// One sleeve, stretched from a shoulder to a hand.
        ///
        /// There is no elbow and there is no IK. The sleeve is a box whose length is however far the
        /// hand happens to be, which means the arm CANNOT come off the gadget — and a hand floating
        /// next to its own gun is the single most obvious way a rig like this breaks. The cost is that
        /// his arms stretch by a few centimetres between the hip carry and the aim; at the size he is
        /// actually drawn, that is a fraction of a pixel.
        ///
        /// All of it in torso space: the shoulders and the gadget are both children of the torso, so
        /// nothing here has to touch world coordinates or care that he is bobbing.
        /// </summary>
        private void PoseArm(Transform arm, Vector3 shoulder, Transform hand)
        {
            if (arm == null || hand == null) return;

            Vector3 handLocal = _torso.InverseTransformPoint(hand.position);
            Vector3 along = handLocal - shoulder;

            float len = along.magnitude;
            if (len < 0.01f) return;

            arm.localPosition = (shoulder + handLocal) * 0.5f;
            arm.localRotation = Quaternion.FromToRotation(Vector3.down, along / len);
            arm.localScale = new Vector3(SleeveWidth, len, SleeveWidth);
        }

        private void OnDestroy()
        {
            // Instances, and ours: nothing else points at them, so nothing else has to be told.
            Kill(_hoodieMat); Kill(_hoodieShadeMat); Kill(_trouserMat); Kill(_skinMat);
            Kill(_hairMat); Kill(_canvasMat); Kill(_rubberMat); Kill(_steelMat); Kill(_waterMat);

            Kill(_hoodieDeepMat); Kill(_hoodieTrimMat); Kill(_skinTrimMat); Kill(_hairTrimMat);
            Kill(_canvasTrimMat); Kill(_rubberTrimMat); Kill(_steelTrimMat); Kill(_boneMat);
            Kill(_tapeMat); Kill(_hoseMat);

            if (_charmMats == null) return;
            for (int i = 0; i < _charmMats.Length; i++) Kill(_charmMats[i]);
        }

        private static void Kill(Material m)
        {
            if (m != null) Destroy(m);
        }
    }
}
