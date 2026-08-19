using UnityEngine;
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
    /// Reads gameplay, writes none of it: <see cref="PlayerController.MoveInput"/>,
    /// <see cref="PlayerController.IsAiming"/> and <see cref="WaterBlaster.IsFiring"/> are all getters.
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

        /// <summary>The goggle lenses, and the one warm glint on him. Amber, because amber is what
        /// workshop safety glass is — and because the only other lit eyes in the yard are the robots'
        /// cold turquoise and the boss's acid green. Nothing that glows on Max may be mistakable for
        /// something that is trying to kill him.</summary>
        private static readonly Color LensGlass = new Color(1f, 0.72f, 0.24f);

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

        private Transform _body;       // lean pivot, at the ground
        private Transform _torso;      // bob + counter-rotation, at the waist
        private Transform _hairPivot;
        private Transform _charmPivot;
        private Transform _gun;
        private Transform _armL, _armR;
        private Transform _handL, _handR;
        private readonly Transform[] _hips = new Transform[2];

        /// <summary>The gadget glow (MV-451) — the two emissive parts <see cref="MaxBody.Build"/>
        /// returns. The only cool light in the whole cast; see <see cref="Water"/>.</summary>
        private MeshRenderer[] _gadgetGlow;

        private Material _skinMat, _hairMat, _jacketMat, _hoodMat, _fabricMat, _darkMat,
                         _bootMat, _soleMat, _metalMat, _eyeMat, _goggleMat;
        private MaterialPropertyBlock _lensMpb;

        private float _stride;
        private float _aim;            // 0 = at the hip, 1 = presented
        private Vector3 _lastPos;
        private Vector3 _velocity;
        private Vector3 _laggedVelocity;

        /// <summary>How far the gadget is presented: 0 at the hip, 1 up and aiming. What a test looks
        /// at to prove the weapon actually comes up when the aim stick does.</summary>
        public float AimPose => _aim;

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
            _skinMat = CharacterMaterial("Max_Skin", Skin);
            _hairMat = CharacterMaterial("Max_Hair", Hair);
            _jacketMat = CharacterMaterial("Max_Jacket", Hoodie);
            _hoodMat = CharacterMaterial("Max_Hood", HoodieShade);
            _fabricMat = CharacterMaterial("Max_Fabric", Trousers);
            _darkMat = CharacterMaterial("Max_Dark", Rubber);
            _bootMat = CharacterMaterial("Max_Boot", Rubber);
            _soleMat = CharacterMaterial("Max_Sole", Bone);
            _metalMat = CharacterMaterial("Max_Metal", Steel);
            _eyeMat = CharacterMaterial("Max_Eye", Rubber);
            _goggleMat = CharacterMaterial("Max_Goggle", LensGlass);
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

        // ---------------------------------------------------------------- the kid

        /// <summary>
        /// MV-451: the body is generated geometry now (<see cref="MaxBody"/>), one fused static mesh
        /// in place of the thirty-five hand-placed primitives this method used to assemble part by
        /// part. <see cref="_body"/> (lean) and <see cref="_torso"/> (bob) stay — the whole mesh hangs
        /// under a "Feet" pivot at the torso's own hip offset, so <c>MaxBody</c>'s "feet at y = 0"
        /// coordinates land on the ground exactly the way <see cref="RobotBodies"/> does it for the
        /// robots. Everything the old per-part hierarchy gave the run/aim/secondary-motion code to grab
        /// — the hip pivots, the gun, the arms, the hair and charm pivots — has nothing left to hang off
        /// it, so those fields stay null and the (already null-guarded) code that drove them quietly
        /// stops moving anything visible, exactly as deleting this file was always documented to do.
        /// </summary>
        private void Build()
        {
            _body = Pivot("Body", transform, Vector3.zero);           // leans, at the ground
            _torso = Pivot("Torso", _body, new Vector3(0f, HipY, 0f)); // bobs, at the waist

            var feet = Pivot("Feet", _torso, new Vector3(0f, -HipY, 0f));
            var palette = new MaxPalette(_skinMat, _hairMat, _jacketMat, _hoodMat, _fabricMat,
                                         _darkMat, _bootMat, _soleMat, _metalMat, _eyeMat, _goggleMat);
            _gadgetGlow = MaxBody.Build(feet, palette);

            // The gadget glow is the only COOL light in the whole cast, against every robot's warm eye
            // (see the class doc). Coloured once here, the same way the old goggle lenses were.
            if (_lensMpb == null) _lensMpb = new MaterialPropertyBlock();
            for (int i = 0; i < _gadgetGlow.Length; i++)
            {
                var r = _gadgetGlow[i];
                if (r == null) continue;
                r.GetPropertyBlock(_lensMpb);
                _lensMpb.SetColor(BaseColorId, Water);
                r.SetPropertyBlock(_lensMpb);
            }
        }

        private static Transform Pivot(string name, Transform parent, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            return go.transform;
        }

        // ---------------------------------------------------------------- running him

        /// <summary>
        /// LateUpdate, not Update: <see cref="PlayerController"/> moves the CharacterController in
        /// Update, and a Max who followed it in Update would render one frame behind his own hitbox —
        /// visible daylight between the kid and the thing the robots are actually hitting.
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

            _body.localRotation = Quaternion.Slerp(
                _body.localRotation,
                Quaternion.Euler(moveLocal.z * leanAngle, 0f, -moveLocal.x * leanAngle),
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

            // MV-451: the gadget is fused into the generated body now, so there is no separate _gun
            // transform to move — this still computes _aim/pose for AimPose/BarrelHeight (WaterBlaster,
            // MaxRigTests) and for the recoil shudder above, it just has nothing left to apply to.
            if (_gun == null) return;
            _gun.localPosition = pos;
            _gun.localRotation = rot;
        }

        /// <summary>
        /// The hair and the charms lag behind him, then catch up.
        ///
        /// A smoothed velocity trails the real one; the DIFFERENCE between them is how hard he just
        /// changed direction, and that is what the hair reacts to. Take off at a sprint and it blows
        /// back; stop dead and it swings forward past him. It costs one Vector3 and it is most of what
        /// separates a character from a statue being slid around a lawn.
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
            Kill(_skinMat); Kill(_hairMat); Kill(_jacketMat); Kill(_hoodMat); Kill(_fabricMat);
            Kill(_darkMat); Kill(_bootMat); Kill(_soleMat); Kill(_metalMat); Kill(_eyeMat); Kill(_goggleMat);
        }

        private static void Kill(Material m)
        {
            if (m != null) Destroy(m);
        }
    }
}
