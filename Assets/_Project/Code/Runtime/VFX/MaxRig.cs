using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Feel;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

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

        /// <summary>MV-669 approved geometry: the goggle lenses moved off amber to a pale blue-white.
        /// Amber competed with the hoodie for the eye; the pale lens is what makes the raised lens cups
        /// read as two bright discs from the 60-degree camera, which is the whole point of the new
        /// goggle geometry. Public: <see cref="MaxPortraitStage"/>'s separate primitive bust reads this
        /// too (MV-669 rev.3, R6), rather than carrying its own copy of the number that could drift.</summary>
        public static readonly Color LensGlass = new Color(0.75f, 0.89f, 1f);

        /// <summary>The utility belt band — MV-669's one new silhouette-breaker, giving the torso a
        /// middle where the old body had none.</summary>
        private static readonly Color Belt = new Color(0.29f, 0.25f, 0.22f);

        /// <summary>The pouches riding on the belt. A shade darker than the band itself.</summary>
        private static readonly Color Pouch = new Color(0.23f, 0.20f, 0.17f);

        /// <summary>MV-702: the LPPE's own cyan-white lens — <see cref="LensGlass"/>, to the digit, so
        /// the gadget's glow reads as the same "pale blue-white" family the goggles already established,
        /// distinct from the RCDA's more saturated water-cyan tank glow (<see cref="Water"/>).</summary>
        private static readonly Color LppeLens = LensGlass;

        /// <summary>The Shoulder Rack's tubes, dim while reloading. <see cref="RackTubeReady"/> is the
        /// colour they lerp to as they finish reloading — the same cyan-white family as the LPPE's own
        /// lens, since the rack is welded to the LPPE's own primary once World 2's morph fires.</summary>
        private static readonly Color RackTubeCharging = LensGlass * 0.25f;
        private static readonly Color RackTubeReady = LensGlass;

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

        /// <summary>MV-717: how far a relaxed arm hangs down from the shoulder while not aiming —
        /// roughly to hip height. See <see cref="PoseArms"/> for why this has to be nonzero.</summary>
        private const float ArmHangDrop = 0.45f;

        /// <summary>
        /// MV-669 (Lee: "10% bigger"), then reverted: applied uniformly at <see cref="_body"/> — the
        /// rig's model root, which sits at ground level (<c>Pivot("Body", transform, Vector3.zero)</c>)
        /// — so a non-1 value here would grow the whole kid together. Lee tried the +10% build on
        /// device and rejected it (revision 2, 2026-09-06): Max ships at 1, his original size. The
        /// re-emitted body block was independently re-tuned (shorter/thinner legs, upper body dropped
        /// 0.076 m) to land within 2% of the pre-MV-669 ~1.95 m crown at this scale, so no compensating
        /// value was needed — see the fix comment for the measured before/after heights.
        ///
        /// This must never touch <see cref="EnemyArchetype.PlayerHeight"/>/<see
        /// cref="EnemyArchetype.PlayerRadius"/> or Max's <c>CharacterController</c> — those drive every
        /// robot's body-separation clamp, the spawn-height maths and the YT-74 "nothing out-sizes Max"
        /// rule. <see cref="MaxRig"/> is a scene-root object that only FOLLOWS Max (see
        /// <see cref="Follow"/>) and never touches his collider, so scaling this transform cannot reach
        /// the CharacterController even by accident.
        ///
        /// Feet stay on the ground for free: <c>Torso</c> sits at local (0, HipY, 0) under <see
        /// cref="_body"/> and <c>Feet</c> sits at local (0, -HipY, 0) under <c>Torso</c>, so Feet's
        /// position IN BODY SPACE is exactly (0, 0, 0) — the ground pivot itself. Scaling a transform
        /// never moves points that already sit at its own origin, so the feet don't float or sink at
        /// any scale factor.
        /// </summary>
        public const float VisualScale = 1f;

        /// <summary>Where the gadget sits when he is just running: down at the hip, across the body,
        /// held two-handed. This is the pose you see 90% of the time.
        ///
        /// MV-730: the Y was 0.155 (implying an absolute hip height of 0.895) but the gadget mesh
        /// <see cref="MaxBody"/> actually builds sits at world y ≈ 0.75 — a stale constant nobody had
        /// re-measured since the gadget was ported pre-MV-669. <see cref="BarrelHeight"/> is the number
        /// <see cref="WaterVfx"/>'s jet is judged against, so a 14 cm gap between that formula and where
        /// the gun is actually drawn read as the muzzle floating below the visible barrel. 0.01 matches
        /// the real built mesh instead.</summary>
        private static readonly Vector3 GunHipPos = new Vector3(0.03f, 0.01f, 0.30f);
        private static readonly Vector3 GunHipRot = new Vector3(17f, -13f, 0f);

        /// <summary>
        /// And where it goes when he aims. Up, level, and pointed at whatever he is about to soak.
        ///
        /// The height is not a taste decision. <see cref="WaterBlaster"/> casts its damage from
        /// <c>transform.position</c> — Max's capsule centre, 1.0 m off the ground — and
        /// <see cref="WaterVfx"/> emits the stream from that same origin. In TORSO space (which starts
        /// at <see cref="HipY"/>) that is y = 0.26. This pose puts the barrel's axis at 0.30, so the
        /// water leaves the gadget at the height the gadget is actually held at, and the jet reads as
        /// coming out of the nozzle rather than out of his chest.
        ///
        /// MV-730 (Lee: "all the action happens below the waist"): raised from 0.285 — a small nudge on
        /// top of the <see cref="GunHipPos"/> recalibration above, which is what actually does the work.
        /// Together they put the built gadget's own muzzle glow at world y ≈ 1.06 at full aim (was 0.90),
        /// comfortably clear of <see cref="HipY"/> (0.74).
        /// </summary>
        private static readonly Vector3 GunAimPos = new Vector3(0.09f, 0.30f, 0.32f);
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
        [SerializeField] private float strideRate = 1.85f;   // MV-678: was 2.15 (Lee: "slightly reduce")

        [Tooltip("How far the legs swing, in degrees, at full stick.")]
        [SerializeField] private float legSwing = 26f;   // MV-678: was 32 (Lee: "slightly reduce the length")

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

        [Tooltip("MV-717: how long the anticipation dip lasts before the raise, in seconds. A prop " +
                 "that snaps straight to its target reads as teleporting onto a socket; a dip first " +
                 "reads as hefting it.")]
        [SerializeField] private float anticipationDuration = 0.06f;

        [Tooltip("How far the gadget dips down during the anticipation beat, in metres.")]
        [SerializeField] private float anticipationDip = 0.02f;

        [Header("Weapon recoil (MV-770)")]
        [Tooltip("How far the gun/rack kicks back on an LPPE pulse or a Rack rocket launch, in metres " +
                 "(spec: \"kicks back 0.08m and returns over 0.10s\").")]
        [SerializeField] private float weaponRecoilDistance = 0.08f;
        [SerializeField] private float weaponRecoilKickSeconds = 0.02f;
        [SerializeField] private float weaponRecoilReturnSeconds = 0.10f;

        [Tooltip("How far the whole body leans, in degrees, on the LPPE's own Shock (4th) hit landing " +
                 "(spec: \"on the SHOCK pulse the whole body leans\" — no magnitude given, picked to " +
                 "read against the existing run-lean's own leanAngle without swamping it).")]
        [SerializeField] private float shockLeanAngle = 6f;
        [SerializeField] private float shockLeanKickSeconds = 0.03f;
        [SerializeField] private float shockLeanReturnSeconds = 0.12f;

        [Header("Arms (MV-717)")]
        [Tooltip("How far the arms swing opposite the legs while not aiming, in metres at the hand. " +
                 "Smaller than the legs' own swing — this is a kid's arm, not a sprinter's.")]
        [SerializeField] private float armSwingAmplitude = 0.10f;

        [Tooltip("MV-730: how far the SHOULDER itself travels through the same swing, in metres. " +
                 "Without this the shoulder end of PoseArm's sleeve never moves while running (only the " +
                 "hand end does), which reads as the top of the arm being welded to the torso.")]
        [SerializeField] private float shoulderSwingAmplitude = 0.035f;

        [Header("Idle (MV-717)")]
        [Tooltip("How fast he breathes while standing still and not aiming, in Hz. He must never be " +
                 "perfectly frozen.")]
        [SerializeField] private float idleBobRate = 0.4f;

        [Tooltip("How far he bobs while breathing, in metres.")]
        [SerializeField] private float idleBobAmount = 0.005f;

        [Tooltip("How fast his head catches up after his body turns, in Hz-like terms — higher is " +
                 "snappier. Picked so the catch-up reads as roughly 0.12 s.")]
        [SerializeField] private float headCatchUp = 8f;

        [Tooltip("How far he rolls toward the planted foot on each footfall, in degrees. Kept small — " +
                 "this is a weight shift, not a stagger. MV-730 (Lee: \"it's now a bit of a waddle\"): " +
                 "was 2.5 — this IS the lateral (roll) component TickRun adds on top of the legs' own " +
                 "sagittal swing, and Lee's own diagnosis (\"a waddle is nearly always too much " +
                 "side-to-side\") points straight at it. Halved rather than zeroed: some roll is what " +
                 "keeps a stride reading as weight actually shifting underneath him rather than his legs " +
                 "just windmilling in place.")]
        [SerializeField] private float weightShiftAngle = 1.2f;

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
        private Transform _head;
        private readonly Transform[] _hips = new Transform[2];

        /// <summary>MV-717: decays from 1 to 0 over <see cref="anticipationDuration"/> once the aim
        /// stick is pushed — see <see cref="TickGadget"/>.</summary>
        private float _anticipation;
        private bool _wasAiming;

        /// <summary>MV-717: the breathing idle's own phase. Advances every tick regardless of whether
        /// he is actually idle, so he is never caught mid-reset — only the AMPLITUDE fades him out
        /// while he is moving or aiming (see <see cref="TickRun"/>).</summary>
        private float _idlePhase;

        /// <summary>MV-717: the head-lag's own smoothed copy of <see cref="Transform.eulerAngles"/>'s
        /// Y — see <see cref="TickHeadLag"/>.</summary>
        private float _laggedFacingYaw;

        /// <summary>The gadget glow (MV-451) — the two emissive parts <see cref="MaxBody.Build"/>
        /// returns. The only cool light in the whole cast; see <see cref="Water"/>.</summary>
        private MeshRenderer[] _gadgetGlow;

        /// <summary>MV-702: the two gadget submeshes toggled on <see cref="WeaponSystemState.ActivePrimary"/>,
        /// the LPPE's own lens (tinted separately from the RCDA's), and the Shoulder Rack's mount + its
        /// three tube-tip lenses (migrated here from <c>ShoulderRack</c>'s own placeholder — see that
        /// class's history).</summary>
        private GameObject _rcdaGadget, _lppeGadget, _rackMount;
        private MeshRenderer[] _lppeGlow, _rackTubeGlow;
        private ShoulderRack _shoulderRack;
        private Vector3 _rackMountBasePos;

        /// <summary>MV-770: the two weapon-arm recoil kicks (one per LPPE pulse, one per Rack rocket)
        /// and the Shock body lean — each a fresh two-step AnimSequence started by its own
        /// <see cref="HudSignals"/> event, ticked and applied in <see cref="TickGadget"/>/
        /// <see cref="TickShoulderRackMount"/>/<see cref="TickRun"/> respectively.</summary>
        private AnimSequence _gunRecoilSeq, _rackRecoilSeq, _shockLeanSeq;

        private Material _skinMat, _hairMat, _jacketMat, _hoodMat, _fabricMat, _darkMat,
                         _bootMat, _soleMat, _metalMat, _eyeMat, _goggleMat, _beltMat, _pouchMat;
        private MaterialPropertyBlock _lensMpb;

        private float _stride;

        /// <summary>MV-678: +1 while the stride is running forwards, -1 backwards. Held across frames
        /// where <c>moveLocal.z</c> sits inside <see cref="StrideDeadband"/> — a pure strafe puts it
        /// near zero, and flipping on the raw sign there makes the direction flicker every frame.
        /// Starts forwards, matching the direction the old, always-forwards stride used to run in.</summary>
        private float _strideDir = 1f;

        /// <summary>How far <c>moveLocal.z</c> has to clear, either side of zero, before the stride
        /// direction is allowed to flip. Picked by looking at the same twin-stick input that already
        /// drives <see cref="TickRun"/>'s lean: a stick pushed hard enough to register as a deliberate
        /// strafe (not noise) sits well under this, and a stick pushed hard enough to register as
        /// travel forwards or back clears it easily.</summary>
        private const float StrideDeadband = 0.15f;

        private float _aim;            // 0 = at the hip, 1 = presented
        private Vector3 _lastPos;
        private Vector3 _velocity;
        private Vector3 _laggedVelocity;

        /// <summary>How far the gadget is presented: 0 at the hip, 1 up and aiming. What a test looks
        /// at to prove the weapon actually comes up when the aim stick does.</summary>
        public float AimPose => _aim;

        /// <summary>Stride phase, in radians. Advances only while he is moving.</summary>
        public float Stride => _stride;

        /// <summary>MV-730: the shoulder point <see cref="PoseArm"/> stretches the left sleeve from,
        /// in torso space — the same claim <see cref="AimPose"/> makes for the gadget's own pose, here
        /// so a test can prove the shoulder travels through the stride rather than sitting welded to
        /// the torso while only the hand end of the sleeve moves.</summary>
        public Vector3 ShoulderL { get; private set; }

        /// <summary>The mirror of <see cref="ShoulderL"/>, for the right sleeve.</summary>
        public Vector3 ShoulderR { get; private set; }

        // ---------------------------------------------------------------- build

        private void Awake()
        {
            _max = FindFirstObjectByType<PlayerController>();
            if (_max == null) return;

            _blaster = _max.GetComponent<WaterBlaster>();
            _shoulderRack = _max.GetComponent<ShoulderRack>();

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

            // MV-702: the LPPE gadget only shows once WeaponSystemState.ActivePrimary flips (MV-689's
            // World 2 morph) — both submeshes are already built (see Build()/MaxBody.Build), so this is
            // a visibility flip, not a rebuild. Subscribed once, for the rig's whole lifetime; matched
            // by the unsubscribe in OnDestroy.
            ApplyPrimaryVisual();
            WeaponSystemState.Changed += ApplyPrimaryVisual;

            // MV-770: one recoil kick per shot/rocket, one body lean per Shock hit — matched by the
            // unsubscribe in OnDestroy, same pairing as WeaponSystemState.Changed just above.
            HudSignals.LppePulseFired += OnLppePulseFired;
            HudSignals.RocketMuzzle += OnRocketMuzzle;
            HudSignals.ShockPulseLanded += OnShockPulseLanded;

            // Stand him on Max BEFORE the first frame, or he spends frame one at the world origin and
            // frame two three metres away — and the hair, which reads its whip off how far he actually
            // moved, snaps like he was fired out of a cannon.
            Follow();
            _lastPos = transform.position;
        }

        /// <summary>
        /// Thirteen materials, all OURS (MV-669 added Belt and Pouch to the original eleven).
        ///
        /// Instances of <see cref="MaterialLibrary.Character()"/> — never that material itself, which
        /// is worn by every robot in the yard and by the boss, and tinting it to give Max brown hair
        /// would give the entire cast brown hair.
        ///
        /// Instances rather than one material and thirteen MaterialPropertyBlocks, for the same reason
        /// the boss's rig does it: a property block is what BREAKS SRP batching, and a shared material
        /// instance is what keeps it. Thirteen materials on one shader batch; thirteen blocks do not.
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

            // MV-669's hero detail: the goggles are LIT, not painted — the one small, warm, specific
            // thing that catches the eye at gameplay zoom (this class's own doc has asked for this
            // since YT-95: "they are lit rather than painted... the same trick the boss's lamps use").
            // A flat _EmissionColor on the shared character shader (StylizedCharacter.shader adds it
            // unconditionally, no keyword needed — the same channel CharacterSkin drives per-frame for
            // the hit flash) is enough; no new geometry, so MaxBody.cs's generated mesh is untouched.
            _goggleMat = CharacterMaterial("Max_Goggle", LensGlass, emission: LensGlass * 0.6f);
            _beltMat = CharacterMaterial("Max_Belt", Belt);
            _pouchMat = CharacterMaterial("Max_Pouch", Pouch);
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
        private Material CharacterMaterial(string name, Color color, bool outline = true, Color? emission = null)
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
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, emission ?? Color.black);
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
        /// robots. <see cref="_hips"/> came back at MV-474: <c>MaxBody.Build</c> hands back two hip
        /// pivots with the boot geometry hanging off them, so <c>TickRun</c>'s stride rotation — which
        /// kept writing <c>_hips[i].localRotation</c> the whole time, into a null-guarded no-op — has
        /// something to turn again. MV-717 brings back the rest of what MV-451 dropped: the gun, both
        /// arms, both hand grips and the head all get wired up below, the same "hand back a rig point,
        /// keep everything else static" pattern the hips already proved out. The hair and charm pivots
        /// (<see cref="_hairPivot"/>/<see cref="_charmPivot"/>) are still gone — out of MV-717's scope,
        /// since the body doc names no static geometry to hang them off.
        /// </summary>
        private void Build()
        {
            _body = Pivot("Body", transform, Vector3.zero);           // leans, at the ground
            _body.localScale = Vector3.one * VisualScale;              // MV-669 rev.2: reverted to 1x
            _torso = Pivot("Torso", _body, new Vector3(0f, HipY, 0f)); // bobs, at the waist

            var feet = Pivot("Feet", _torso, new Vector3(0f, -HipY, 0f));
            var palette = new MaxPalette(_skinMat, _hairMat, _jacketMat, _hoodMat, _fabricMat,
                                         _darkMat, _bootMat, _soleMat, _metalMat, _eyeMat, _goggleMat,
                                         _beltMat, _pouchMat);
            var body = MaxBody.Build(feet, palette, HipY);
            _gadgetGlow = body.GadgetGlow;
            _hips[0] = body.Hips[0];
            _hips[1] = body.Hips[1];

            _rcdaGadget = body.RcdaGadget;
            _lppeGadget = body.LppeGadget;
            _lppeGlow = body.LppeGlow;
            _rackMount = body.RackMount;
            _rackTubeGlow = body.RackTubeGlow;
            // MV-770: the rack's resting local pose, so a per-rocket recoil kick has a fixed point to
            // offset from and return to rather than drifting off whatever TickShoulderRackMount last set.
            if (_rackMount != null) _rackMountBasePos = _rackMount.transform.localPosition;

            // MV-717: wire up the moving parts MV-451's fused mesh dropped (see the class doc's "HE
            // CARRIES THE GADGET" section). All of this happens here, in Build, still inside Awake and
            // before torso or feet have ever been rotated by a tick — the worldPositionStays reparents
            // below only resolve to the right numbers while that holds.
            //
            // The gun: MaxBody handed back "Gun" sitting at feet's own origin, wrapping the gadget at
            // its current (correct, already-shipped) appearance. A pivot placed at GunHipPos/GunHipRot
            // — both already TORSO-space constants — absorbs it with worldPositionStays, so Unity
            // solves "what local offset keeps this rendering exactly where it already is" once, and
            // the result becomes this pivot's own local pose. Hoisting that pivot onto _torso the same
            // way then resolves it to GunHipPos/GunHipRot exactly, because _torso is still at identity
            // rotation at this point in Awake — so TickGadget's very first frame (_aim = 0) sets
            // _gun's transform right back to the pose it is already sitting at. No visual jump.
            var gunRestPivot = Pivot("GunRest", feet, GunHipPos + new Vector3(0f, HipY, 0f));
            gunRestPivot.localRotation = Quaternion.Euler(GunHipRot);
            body.Gun.SetParent(gunRestPivot, worldPositionStays: true);
            gunRestPivot.SetParent(_torso, worldPositionStays: true);
            _gun = gunRestPivot;

            // The hands: fixed grip points MaxBody placed on the gun itself, so they move with it for
            // free — PoseArms only ever reads their world position.
            _handL = body.HandL;
            _handR = body.HandR;

            // The arms: brand-new dynamic sleeves, nothing to preserve visually — straight under the
            // torso, which is the space PoseArm's own math already assumes.
            _armL = body.ArmL;
            _armL.SetParent(_torso, worldPositionStays: false);
            _armR = body.ArmR;
            _armR.SetParent(_torso, worldPositionStays: false);

            // The head: same "preserve what's already correct" hoist as the gun, minus the extra rest
            // pose — nothing ever moved this before, so there is nothing to re-seat it against.
            _head = body.Head;
            _head.SetParent(_torso, worldPositionStays: true);

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

            // MV-702: the LPPE's own lens, tinted separately (see LppeLens's doc for why).
            for (int i = 0; i < _lppeGlow.Length; i++)
            {
                var r = _lppeGlow[i];
                if (r == null) continue;
                r.GetPropertyBlock(_lensMpb);
                _lensMpb.SetColor(BaseColorId, LppeLens);
                r.SetPropertyBlock(_lensMpb);
            }
        }

        /// <summary>MV-702: shows the gadget submesh matching <see cref="WeaponSystemState.ActivePrimary"/>
        /// and hides the other — both are already built (see <see cref="Build"/>), so this is a
        /// visibility flip, not a rebuild. Runs once at build time and again every time
        /// <see cref="WeaponSystemState.Changed"/> fires (MV-689's World 2 morph is the only thing that
        /// ever actually changes it mid-run).</summary>
        private void ApplyPrimaryVisual() => ApplyPrimaryVisual(_rcdaGadget, _lppeGadget, WeaponSystemState.ActivePrimary);

        /// <summary>The toggle rule itself, pulled out as a pure static so an EditMode test can prove
        /// the resolved <c>GameObject.activeSelf</c> state directly off <see cref="MaxBody.Build"/>'s
        /// own output — <see cref="Awake"/> requires a live <see cref="PlayerController"/> in the scene,
        /// which <see cref="MaxBody.Build"/> does not.</summary>
        public static void ApplyPrimaryVisual(GameObject rcdaGadget, GameObject lppeGadget,
            WeaponCatalog.PrimaryKind primary)
        {
            bool lppe = primary == WeaponCatalog.PrimaryKind.Lppe;
            if (rcdaGadget != null) rcdaGadget.SetActive(!lppe);
            if (lppeGadget != null) lppeGadget.SetActive(lppe);
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
            TickShoulderRackMount();
            TickSecondary(dt);
            TickHeadLag(dt);

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

            // Max's own yaw is our yaw, so his move input — which is already in world XZ — has to come
            // back into local space to know whether he is running forwards, backwards, or sideways.
            // Read up here, before the stride update below, because the stride needs the same signed
            // value the lean uses at the bottom of this method.
            Vector3 moveLocal = transform.InverseTransformDirection(
                new Vector3(_max.MoveInput.x, 0f, _max.MoveInput.y));

            // MV-678: moveLocal.z is signed — positive forwards, negative back — so the legs can now
            // run backwards when he does, instead of always playing a forwards cycle while he
            // backpedals. A raw sign flip would flicker every frame during a pure strafe, where
            // moveLocal.z hovers around zero: only cross StrideDeadband to change direction, and hold
            // the last direction while inside it.
            if (moveLocal.z > StrideDeadband) _strideDir = 1f;
            else if (moveLocal.z < -StrideDeadband) _strideDir = -1f;

            // The stride only advances while he is actually moving, so he stops mid-step instead of
            // marching on the spot.
            _stride += strideRate * speed01 * dt * Mathf.PI * 2f * _strideDir;
            if (_stride > Mathf.PI * 2f) _stride -= Mathf.PI * 2f;
            else if (_stride < -Mathf.PI * 2f) _stride += Mathf.PI * 2f;

            float swing = Mathf.Sin(_stride) * legSwing * speed01;
            if (_hips[0] != null) _hips[0].localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (_hips[1] != null) _hips[1].localRotation = Quaternion.Euler(-swing, 0f, 0f);

            // Two bobs per stride — one per foot landing. Abs(), not Sin(), or he floats up on one
            // step and sinks through the lawn on the other.
            float bounce = Mathf.Abs(Mathf.Sin(_stride)) * bob * speed01;

            // MV-717: the breathing idle. The phase always advances — he must never be perfectly
            // frozen — but the amplitude fades to nothing once he is actually moving or presenting the
            // gadget, so it never fights the stride bounce or the aim-in hold.
            _idlePhase += dt * idleBobRate * Mathf.PI * 2f;
            float idleBounce = Mathf.Sin(_idlePhase) * idleBobAmount * (1f - speed01) * (1f - _aim);

            // MV-717: a small roll toward the planted foot, on the same stride phase as the legs — the
            // weight actually shifting underneath him, not just the legs swinging in place.
            float weightShift = -Mathf.Sin(_stride) * weightShiftAngle * speed01;

            // Shoulders counter-rotate against the hips. This is what stops a run cycle from reading as
            // a puppet on a stick — raised from 0.14 (MV-717, Lee: from a 60-degree camera the old
            // factor was a couple of degrees of yaw and invisible; yaw is the rotation a top-down
            // camera reads best). The gadget is parented to the torso, so it swings with him — which is
            // what a thing held in two hands does.
            _torso.localPosition = new Vector3(0f, HipY + bounce + idleBounce, 0f);
            _torso.localRotation = Quaternion.Euler(0f, -swing * 0.35f, weightShift);

            _body.localRotation = Quaternion.Slerp(
                _body.localRotation,
                Quaternion.Euler(moveLocal.z * leanAngle, 0f, -moveLocal.x * leanAngle),
                1f - Mathf.Exp(-14f * dt));

            // MV-770 spec part 2, item 2: "on the SHOCK pulse the whole body leans" — additive on top
            // of the movement lean above, not replacing it, so a Shock landing mid-backpedal still
            // reads as a punch through whatever he is already doing.
            if (_shockLeanSeq != null)
            {
                _shockLeanSeq.Tick(dt);
                float u = RecoilAmount(_shockLeanSeq);
                if (u > 0f) _body.localRotation *= Quaternion.Euler(-shockLeanAngle * u, 0f, 0f);
            }
        }

        /// <summary>MV-770: the eased 0..1 "how far into its kick-then-return" a two-step recoil/lean
        /// <see cref="AnimSequence"/> (step 0 = kick out, step 1 = return) is right now — 0 before it
        /// starts, back to 0 once <see cref="AnimSequence.IsComplete"/>, peaking at 1 exactly at the
        /// step-0/step-1 boundary. Shared by the gun kick, the rack kick and the Shock lean so all
        /// three "kick then return" the same way.</summary>
        private static float RecoilAmount(AnimSequence seq)
        {
            float kickU = seq.Progress(0);
            return kickU < 1f ? kickU : 1f - seq.Progress(1);
        }

        /// <summary>
        /// MV-717: the head yaws slightly behind the body when he turns, then catches up — the
        /// strongest single cue at this size that he is alive rather than a sprite being dragged.
        ///
        /// The rig root snaps to Max's yaw exactly (see <see cref="Follow"/>), so to make the head
        /// APPEAR to lag, its LOCAL yaw is set to the difference between that instant facing and a
        /// smoothed copy of it — the head's world yaw is then the smoothed value, which catches up to
        /// the real one as the smoothing converges.
        /// </summary>
        private void TickHeadLag(float dt)
        {
            float facingYaw = transform.eulerAngles.y;
            _laggedFacingYaw = Mathf.LerpAngle(_laggedFacingYaw, facingYaw, 1f - Mathf.Exp(-headCatchUp * dt));

            if (_head != null)
                _head.localRotation = Quaternion.Euler(0f, Mathf.DeltaAngle(facingYaw, _laggedFacingYaw), 0f);
        }

        /// <summary>
        /// Up to aim, down to run — the pose the GDD asks for by name, and the only thing on screen
        /// that says the gadget is live before the water does.
        /// </summary>
        private void TickGadget(float dt)
        {
            bool aiming = _max.IsAiming;
            float target = aiming ? 1f : 0f;

            // MV-717: a brief anticipation dip before the raise. Kicked only on the false-to-true edge
            // (not on release, and not re-kicked while already aiming) and decayed linearly over
            // anticipationDuration; Sin(t * PI) shapes it into a bump that starts and ends at zero so
            // it blends cleanly into the lerp below rather than snapping in and out.
            if (aiming && !_wasAiming) _anticipation = 1f;
            _wasAiming = aiming;
            _anticipation = Mathf.Max(0f, _anticipation - dt / anticipationDuration);

            _aim = Mathf.Lerp(_aim, target, 1f - Mathf.Exp(-presentSpeed * dt));

            GadgetPose(_aim, out Vector3 pos, out Quaternion rot);
            pos.y -= Mathf.Sin(_anticipation * Mathf.PI) * anticipationDip;

            // A kick while the water is actually flowing. Not while merely AIMING: the blaster stops
            // firing when the energy runs out (YT-80), and a gun that keeps bucking on an empty tank is
            // a gun that is lying to you about whether you still have ammo.
            if (_blaster != null && _blaster.IsFiring)
            {
                // Along the gadget's own axis, so the kick is always backwards down the barrel.
                float shudder = Mathf.Sin(Time.time * 47f) * 0.35f + 0.65f;
                pos -= rot * Vector3.forward * (recoil * shudder);
            }

            // MV-770 spec part 2, item 2: one discrete kick per LPPE pulse, on top of (not instead of)
            // the RCDA's own continuous shudder above — the two primaries are never live at once
            // (WeaponSystemState.ActivePrimary), so in practice only one of these is ever non-zero.
            if (_gunRecoilSeq != null)
            {
                _gunRecoilSeq.Tick(dt);
                pos -= rot * Vector3.forward * (weaponRecoilDistance * RecoilAmount(_gunRecoilSeq));
            }

            // MV-717: the gadget is a real rig point again (see Build) — the gun raises to aim, and the
            // arms follow it (see PoseArms).
            if (_gun == null) return;
            _gun.localPosition = pos;
            _gun.localRotation = rot;
        }

        /// <summary>MV-702: shows the Shoulder Rack mount once <see cref="ShoulderRack.IsBought"/> (AC2,
        /// carried over from MV-694's own placeholder), and glows its three tubes from dim to bright as
        /// <see cref="ShoulderRack.ReloadFraction01"/> climbs back to 1 — "tubes glow as they reload".</summary>
        private void TickShoulderRackMount()
        {
            bool bought = _shoulderRack != null && _shoulderRack.IsBought;
            if (_rackMount != null && _rackMount.activeSelf != bought) _rackMount.SetActive(bought);

            // MV-770 spec part 2, item 2: "on a rocket salvo, one kick per rocket" — ticked whether or
            // not the mount is currently shown, same "the rocket already left, the visual finishes"
            // shape as ProcessPendingLaunches keeps firing regardless of IsBought.
            if (_rackMount != null && _rackRecoilSeq != null)
            {
                _rackRecoilSeq.Tick(Time.deltaTime);
                float u = RecoilAmount(_rackRecoilSeq);
                _rackMount.transform.localPosition = _rackMountBasePos + Vector3.back * (weaponRecoilDistance * u);
            }

            if (!bought || _rackTubeGlow == null) return;

            Color glow = Color.Lerp(RackTubeCharging, RackTubeReady, _shoulderRack.ReloadFraction01);
            for (int i = 0; i < _rackTubeGlow.Length; i++)
            {
                var r = _rackTubeGlow[i];
                if (r == null) continue;
                r.GetPropertyBlock(_lensMpb);
                _lensMpb.SetColor(BaseColorId, glow);
                r.SetPropertyBlock(_lensMpb);
            }
        }

        // ---------------------------------------------------------------- weapon recoil (MV-770)

        private AnimSequence NewRecoilSequence() => new AnimSequence(new[]
        {
            new AnimStep(0f, weaponRecoilKickSeconds, AnimEase.OutQuad),
            new AnimStep(weaponRecoilKickSeconds, weaponRecoilReturnSeconds, AnimEase.OutQuad),
        });

        private void OnLppePulseFired(Vector3 worldPos, Vector3 forward) => _gunRecoilSeq = NewRecoilSequence();

        private void OnRocketMuzzle(Vector3 worldPos, Vector3 forward) => _rackRecoilSeq = NewRecoilSequence();

        private void OnShockPulseLanded(Vector3 worldPos) => _shockLeanSeq = new AnimSequence(new[]
        {
            new AnimStep(0f, shockLeanKickSeconds, AnimEase.OutQuad),
            new AnimStep(shockLeanKickSeconds, shockLeanReturnSeconds, AnimEase.OutQuad),
        });

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

        /// <summary>
        /// MV-717: while not aiming, the arms swing opposite the legs off the same stride phase — so
        /// they cannot drift out of sync with the walk — and blend out to the fixed hand-on-gun grip as
        /// <see cref="_aim"/> rises. The left arm swings opposite the LEFT leg (i.e. with the right, per
        /// a natural contralateral gait), and the right arm the mirror.
        ///
        /// MV-730 (Lee: "the top part of his arms are glued to his body when he walks"): the shoulder
        /// point below now carries its own (smaller) share of the same swing — see
        /// <see cref="shoulderSwingAmplitude"/>. Before this fix the shoulder passed to <see
        /// cref="PoseArm"/> only ever moved via <see cref="ShoulderAimOffset"/> (aim-only), so while
        /// running the near end of the sleeve sat at a completely static point and only the hand end
        /// travelled — a box pivoting from a point that never moves reads as welded to the torso.
        /// </summary>
        private void PoseArms()
        {
            Vector3 aimOffset = Vector3.Lerp(ShoulderRestOffset, ShoulderAimOffset, _aim);

            float speed01 = Mathf.Clamp01(_max.MoveInput.magnitude);
            float swingL = -Mathf.Sin(_stride) * armSwingAmplitude * speed01;
            float swingR = Mathf.Sin(_stride) * armSwingAmplitude * speed01;

            // The shoulder travels through a fraction of the same swing, fading out as he aims (the aim
            // pose fixes the shoulder via ShoulderAimOffset instead) — see this method's own doc.
            float shoulderSwingL = -Mathf.Sin(_stride) * shoulderSwingAmplitude * speed01 * (1f - _aim);
            float shoulderSwingR = Mathf.Sin(_stride) * shoulderSwingAmplitude * speed01 * (1f - _aim);

            Vector3 shoulderL = new Vector3(-ShoulderX - aimOffset.x, ShoulderY + aimOffset.y, aimOffset.z + shoulderSwingL);
            Vector3 shoulderR = new Vector3(ShoulderX + aimOffset.x, ShoulderY + aimOffset.y, aimOffset.z + shoulderSwingR);
            ShoulderL = shoulderL;
            ShoulderR = shoulderR;

            // A relaxed arm hangs DOWN from the shoulder, not level with it — ArmHangDrop is that
            // vertical reach, roughly to hip height. Without it the swing target sits at shoulder
            // height and only ever moves in Z, so FromToRotation only ever sees two possible
            // directions (the swing's sign) instead of a continuously varying angle — the arm would
            // twitch between two poses instead of swinging through them.
            Vector3 freeL = shoulderL + new Vector3(0f, -ArmHangDrop, swingL);
            Vector3 freeR = shoulderR + new Vector3(0f, -ArmHangDrop, swingR);

            // Both hands settle on the gun's fixed grip points as _aim rises (Part 1's HandL/HandR,
            // parented to the gadget so they cannot come off it); at _aim = 0 the arm swing above is
            // what actually reaches, since there is nothing to grip while he is just running.
            Vector3 targetL = _handL != null
                ? Vector3.Lerp(freeL, _torso.InverseTransformPoint(_handL.position), _aim)
                : freeL;
            Vector3 targetR = _handR != null
                ? Vector3.Lerp(freeR, _torso.InverseTransformPoint(_handR.position), _aim)
                : freeR;

            PoseArm(_armL, shoulderL, targetL);
            PoseArm(_armR, shoulderR, targetR);
        }

        /// <summary>
        /// One sleeve, stretched from a shoulder to wherever the hand is reaching this frame.
        ///
        /// There is no elbow and there is no IK. The sleeve is a box whose length is however far the
        /// target happens to be, which means the arm CANNOT come off the gadget once it settles there —
        /// and a hand floating next to its own gun is the single most obvious way a rig like this
        /// breaks. The cost is that his arms stretch by a few centimetres between the hip carry and the
        /// aim; at the size he is actually drawn, that is a fraction of a pixel.
        ///
        /// All of it in torso space: the shoulder and the target are both already given in that space
        /// (see <see cref="PoseArms"/>), so nothing here has to touch world coordinates or care that he
        /// is bobbing.
        /// </summary>
        private void PoseArm(Transform arm, Vector3 shoulder, Vector3 targetLocal)
        {
            if (arm == null) return;

            Vector3 along = targetLocal - shoulder;

            float len = along.magnitude;
            if (len < 0.01f) return;

            arm.localPosition = (shoulder + targetLocal) * 0.5f;
            arm.localRotation = Quaternion.FromToRotation(Vector3.down, along / len);
            arm.localScale = new Vector3(SleeveWidth, len, SleeveWidth);
        }

        private void OnDestroy()
        {
            WeaponSystemState.Changed -= ApplyPrimaryVisual;
            HudSignals.LppePulseFired -= OnLppePulseFired;
            HudSignals.RocketMuzzle -= OnRocketMuzzle;
            HudSignals.ShockPulseLanded -= OnShockPulseLanded;

            // Instances, and ours: nothing else points at them, so nothing else has to be told.
            Kill(_skinMat); Kill(_hairMat); Kill(_jacketMat); Kill(_hoodMat); Kill(_fabricMat);
            Kill(_darkMat); Kill(_bootMat); Kill(_soleMat); Kill(_metalMat); Kill(_eyeMat); Kill(_goggleMat);
            Kill(_beltMat); Kill(_pouchMat);
        }

        private static void Kill(Material m)
        {
            if (m != null) Destroy(m);
        }
    }
}
