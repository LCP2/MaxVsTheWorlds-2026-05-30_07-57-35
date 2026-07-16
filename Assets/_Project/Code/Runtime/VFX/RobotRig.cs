using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Backyard robots (YT-96) — and until now, two primitives.
    ///
    /// The swarm the Mower Hutch pours out has been Unity's default capsule (the rusher) and cube (the
    /// bruiser) since YT-36, tinted turquoise and violet by <see cref="CharacterSkin"/> so you could at
    /// least tell them apart from each other and from Max. But they are canonically garden ROBOTS, and
    /// every other actor in the yard got a real body weeks ago — the boss is a mower (YT-90), Max is a
    /// kid in a hoodie (YT-95). The things you spend the whole fight reading were the two shapes with
    /// the least meaning in them.
    ///
    /// They are machines now, built the same way the boss's rig is (YT-90) and to be read from the same
    /// place — thirty metres up at ~72°, the only angle this game has. The two silhouettes are
    /// deliberate OPPOSITES, because a rusher and a bruiser want opposite responses (kite one, spend
    /// three seconds of held spray on the other) and the player has to know which is which from a
    /// twenty-pixel shape while being chased by both:
    ///
    ///   THE RUSHER — a light skitter-bot. A round pod on two thin splayed legs, leaning forward, with
    ///     a single bright forward eye and a pair of garden-shear arms reaching ahead of it. Tall for
    ///     its width, leggy, all forward momentum: it reads as QUICK.
    ///   THE BRUISER — a heavy roller-bot. A wide low chassis on tank treads, a broad two-eyed visor, a
    ///     lawn-roller drum slung across its front and thick crusher arms. Low, wide and planted: it
    ///     reads as SLOW and HEAVY, a fridge on treads. Its threat is that it will not go away.
    ///
    /// Both keep the cool "them" colour identity (YT-87): the body wears exactly the colour
    /// <see cref="CharacterSkin"/> already gives its kind — turquoise for the rusher, violet for the
    /// bruiser — because that colour is decided in ONE place and a second copy here would drift the
    /// first time anyone tuned it. The ground rings (YT-85) are untouched: <see cref="GroundAnchorVfx"/>
    /// keys off the CharacterController, which this rig never changes.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// THE EYE IS THE TELL, AND NOTHING ELSE MAY PAINT IT
    ///
    /// <see cref="RobotEnemy"/> has a wind-up state — it telegraphs before it lunges, which is the
    /// dodge window — and <see cref="RobotEnemy.TelegraphProgress"/> is a clean read of how far through
    /// it is. On a small body a colour tell does not carry far, so the eye does the work: it idles GOLD
    /// and heats to the ORANGE every other telegraph in the game uses (<see cref="CharacterSkin"/>'s
    /// warn colour) as the lunge lands. A hit whites it out, same word the rest of the cast uses.
    ///
    /// This is the same trap the boss's rig had to be built around. Two scene-wide sweeps re-material
    /// anything they recognise, and either one getting hold of a part means a SECOND writer on its
    /// property block, decided by script order — which is how the boss's tell shipped dead:
    ///
    ///   * <see cref="RuntimeSurfaceDirector"/> already skips anything under an <see cref="IDamageable"/>,
    ///     and the whole model hangs under the robot, so it is safe from that one by construction.
    ///   * <see cref="CharacterSkinDirector"/> CLAIMS every renderer under an <see cref="IDamageable"/> —
    ///     so every part built here carries <see cref="SelfDrivenTint"/>, which is the marker that tells
    ///     it "gameplay drives this block, keep off." The robot's own root gets it too, so the director
    ///     stops re-skinning the greybox this rig just hid.
    ///
    /// Which means nothing else hands these renderers a material, and a primitive's default material is
    /// not in the build's shader set — it ships MAGENTA (YT-58). Every part is given a real material
    /// explicitly, from instances this rig owns and destroys.
    ///
    /// Reads gameplay, writes none of it: <see cref="RobotEnemy.TelegraphProgress"/> and
    /// <see cref="RobotEnemy.Kind"/> are getters, and the hit flash comes off the same
    /// <see cref="HudSignals.DamageDealt"/> signal the HUD already raises. Delete this file and the game
    /// plays identically — the robots just go back to being a capsule and a cube.
    ///
    /// One rig PER robot, attached and pooled with it by <see cref="RobotRigDirector"/>. A robot is
    /// pooled by kind (a dead bruiser never comes back as a rusher — YT-66), so the body a rig builds is
    /// the body that GameObject wears for its whole life: the model is built once and reused, never
    /// rebuilt on respawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotRig : MonoBehaviour
    {
        // ---------------------------------------------------------------- the palette

        /// <summary>The eye at rest. Gold — the "gold-ring/eye" <see cref="RobotEnemy"/> has always
        /// meant to idle at, and warm without being the warn colour, so a robot that is merely awake
        /// does not read as one that is about to hit you.</summary>
        private static readonly Color EyeIdle = new Color(0.90f, 0.72f, 0.22f);

        /// <summary>Winding up. The same orange every telegraph in the game uses
        /// (<see cref="CharacterSkin"/>'s warn colour), so the boss, the robots and the ground rings all
        /// speak one word for "incoming".</summary>
        private static readonly Color EyeWarn = new Color(1f, 0.35f, 0.12f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ---------------------------------------------------------------- tuning

        [Tooltip("How fast the eye reaches the colour the current state calls for. Fast: a telegraph " +
                 "that eases in is a telegraph that arrives after the lunge does.")]
        [SerializeField] private float eyeResponse = 12f;

        [Tooltip("How fast a hit flash falls away. Must be fast — the blaster lands a tick every 0.1s, " +
                 "so a slow flash never clears and the body sits washed white (CharacterSkin.flashDecay).")]
        [SerializeField] private float flashDecay = 16f;

        [Tooltip("How close a damage event has to be to flash THIS robot. Tight, so a hit on one robot " +
                 "in a pack does not flash the whole pack.")]
        [SerializeField] private float flashRadius = 1.0f;

        // ---------------------------------------------------------------- state

        private RobotEnemy _enemy;
        private Transform _model;
        private MeshRenderer[] _eyes;
        private Material _bodyMat;
        private Material _accentMat;
        private MaterialPropertyBlock _eyeMpb;

        private bool _built;
        private Color _tellColor = EyeIdle;
        private float _flash;

        /// <summary>The colour the eye is currently burning — the tell, in one value. What a test looks
        /// at to prove the wind-up actually reaches the screen.</summary>
        public Color TellColor => _tellColor;

        /// <summary>True once the model has been built (once per pooled GameObject).</summary>
        public bool Built => _built;

        // ---------------------------------------------------------------- lifecycle

        private void Awake() => EnsureBuilt();

        private void OnEnable()
        {
            EnsureBuilt();
            HudSignals.DamageDealt += OnDamage;

            // A pooled robot comes back fresh: no leftover flash, eye back at idle.
            _flash = 0f;
            _tellColor = EyeIdle;
            ApplyEyes(_tellColor);
        }

        private void OnDisable() => HudSignals.DamageDealt -= OnDamage;

        // ---------------------------------------------------------------- build

        private void EnsureBuilt()
        {
            if (_built) return;
            _enemy = GetComponent<RobotEnemy>();
            if (_enemy == null) return;   // nothing to dress

            // CharacterSkinDirector claims every renderer under a damageable. Marking the ROOT keeps it
            // from re-skinning the greybox we are about to hide; every PART carries the same marker (see
            // Part). RuntimeSurfaceDirector already skips everything under an IDamageable.
            if (GetComponent<SelfDrivenTint>() == null) gameObject.AddComponent<SelfDrivenTint>();

            _eyeMpb = new MaterialPropertyBlock();
            BuildMaterials();

            // The greybox goes — the capsule or cube EnemySpawner built as a stand-in. Its COLLIDERS
            // stay: the CharacterController is what the water hits and what Max walks into, and only the
            // visual is this rig's to change (docs/CODE_DRIVEN_SCENES.md).
            var greybox = GetComponent<MeshRenderer>();
            if (greybox != null) greybox.enabled = false;

            BuildModel();

            _built = true;
            ApplyEyes(_tellColor);
        }

        /// <summary>
        /// Two materials, both OURS.
        ///
        /// Instances of <see cref="MaterialLibrary.Character()"/> — never that material itself, which is
        /// worn by every robot in the yard and by Max and the boss; heating this one robot's chassis on
        /// its wind-up would heat the entire cast. Instances rather than a MaterialPropertyBlock per
        /// renderer for the same reason the boss's rig does it: a block BREAKS SRP batching and a shared
        /// material instance KEEPS it, and the whole model heats with one property write.
        ///
        /// The body wears the kind's own colour, straight from <see cref="CharacterSkin"/> so "them" is
        /// one colour everywhere. The accent is a darker step of it — treads, legs, joints, arms — which
        /// gives the silhouette some internal shape without ever leaving the kind's hue.
        /// </summary>
        private void BuildMaterials()
        {
            CharacterRole role = _enemy.Kind == EnemyKind.Bruiser ? CharacterRole.Bruiser : CharacterRole.Robot;
            Color body = CharacterSkin.BaseColorFor(role);

            // A darker step of the SAME hue (alpha kept at 1 — scaling a Color scales its alpha too, and
            // an accent at 0.55 alpha would go see-through on a transparent shader variant).
            Color accent = new Color(body.r * 0.55f, body.g * 0.55f, body.b * 0.55f, 1f);

            _bodyMat = NewCharacterMaterial($"Robot_{role}_Body", body);
            _accentMat = NewCharacterMaterial($"Robot_{role}_Accent", accent);
        }

        private static Material NewCharacterMaterial(string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58): a
            // plain lit material still draws a correctly coloured robot, just without the outline.
            var template = MaterialLibrary.Character();
            var m = template != null
                ? new Material(template)
                : new Material(MaterialLibrary.SurfaceShader);

            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, Color.black);
            return m;
        }

        /// <summary>
        /// The model, in metres, with the robot's FEET at y = 0 and +Z where it is running.
        ///
        /// The robot's own transform is SCALED by its body scale (a rusher is 0.8, a bruiser 1.15 —
        /// EnemyArchetype), and its origin sits at the collider's centre, half a body off the ground. So
        /// the model hangs under a metre-space container that cancels that scale
        /// (<see cref="ParentScale"/>), and a "feet" pivot drops it to the floor — after which every part
        /// below is authored in plain world metres with y = 0 on the lawn.
        /// </summary>
        private void BuildModel()
        {
            _model = ParentScale.MakeMetreSpace(new GameObject("RobotModel").transform, transform);

            float spawnHeight = EnemyArchetype.Of(_enemy.Kind).SpawnHeight;
            var feet = new GameObject("Feet").transform;
            feet.SetParent(_model, worldPositionStays: false);
            feet.localPosition = new Vector3(0f, -spawnHeight, 0f);

            if (_enemy.Kind == EnemyKind.Bruiser) BuildBruiser(feet);
            else BuildRusher(feet);
        }

        /// <summary>
        /// The rusher: a round pod on two thin splayed legs, leaning forward, a single bright eye out in
        /// front and a pair of shear-arms reaching ahead. Narrow and leggy — it reads as quick, and it
        /// reads as NOT the bruiser at a glance.
        /// </summary>
        private void BuildRusher(Transform feet)
        {
            // Legs — thin, splayed outward, planted feet. The stance is the whole "it runs" read.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part("Leg", PrimitiveType.Cylinder, feet,
                     new Vector3(side * 0.17f, 0.30f, -0.02f), new Vector3(0.11f, 0.30f, 0.13f),
                     _accentMat, Quaternion.Euler(-8f, 0f, side * 13f));
                Part("Foot", PrimitiveType.Cube, feet,
                     new Vector3(side * 0.24f, 0.04f, 0.05f), new Vector3(0.16f, 0.08f, 0.30f), _accentMat);
            }

            // The pod. A rounded body — everything else on a rusher is thin, so the one round mass is
            // what makes it a machine with a middle rather than a bundle of sticks.
            Part("Pod", PrimitiveType.Sphere, feet,
                 new Vector3(0f, 0.72f, 0f), new Vector3(0.52f, 0.56f, 0.52f), _bodyMat);

            // A hip collar where the legs meet the pod, so the join is not a gap.
            Part("Hips", PrimitiveType.Cube, feet,
                 new Vector3(0f, 0.48f, 0f), new Vector3(0.34f, 0.20f, 0.32f), _accentMat);

            // Head, leaning forward — the lean is the momentum.
            Part("Head", PrimitiveType.Sphere, feet,
                 new Vector3(0f, 1.00f, 0.10f), new Vector3(0.36f, 0.32f, 0.38f), _bodyMat,
                 Quaternion.Euler(18f, 0f, 0f));

            // THE EYE — single, forward, the tell. Big enough to read at gameplay zoom: on a body ~1.3 m
            // tall drawn at twenty-odd pixels, a 0.2 m lamp is a couple of pixels you can actually find.
            _eyes = new[] { Eye("Eye", feet, new Vector3(0f, 1.02f, 0.30f), 0.20f) };

            // A whip antenna, off to one side — an asymmetric fleck reads on a silhouette where symmetric
            // detail vanishes.
            Part("Antenna", PrimitiveType.Cylinder, feet,
                 new Vector3(0.11f, 1.28f, -0.02f), new Vector3(0.03f, 0.20f, 0.03f), _accentMat,
                 Quaternion.Euler(0f, 0f, -12f));

            // Shear-arms, reaching ahead. The garden tool that says what this thing is FOR.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part("Claw", PrimitiveType.Cube, feet,
                     new Vector3(side * 0.27f, 0.74f, 0.18f), new Vector3(0.10f, 0.12f, 0.30f), _accentMat,
                     Quaternion.Euler(0f, side * -12f, 0f));
            }
        }

        /// <summary>
        /// The bruiser: a wide low chassis on tank treads, a broad two-eyed visor, a lawn-roller drum
        /// across the front and thick crusher arms. Low, wide, planted — a fridge on treads. Its garden
        /// motif is the roller (the boss's reel's blunt cousin), and its threat is that it does not stop.
        /// </summary>
        private void BuildBruiser(Transform feet)
        {
            // Tread skirts — long, low, dark, down the sides. Treads not legs: a heavy thing rolls.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part("Tread", PrimitiveType.Cube, feet,
                     new Vector3(side * 0.50f, 0.22f, 0f), new Vector3(0.24f, 0.34f, 0.98f), _accentMat);
            }

            // The chassis. Wide and low — the fridge.
            Part("Chassis", PrimitiveType.Cube, feet,
                 new Vector3(0f, 0.55f, 0f), new Vector3(0.96f, 0.58f, 0.82f), _bodyMat);

            // Heavy shoulder blocks — bulk up top, where the crusher arms hang.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part("Shoulder", PrimitiveType.Cube, feet,
                     new Vector3(side * 0.34f, 0.98f, -0.06f), new Vector3(0.36f, 0.34f, 0.52f), _bodyMat);
            }

            // A broad low visor for a head — a face you read two eyes off, not a dome.
            Part("Visor", PrimitiveType.Cube, feet,
                 new Vector3(0f, 1.00f, 0.20f), new Vector3(0.52f, 0.30f, 0.38f), _bodyMat,
                 Quaternion.Euler(12f, 0f, 0f));

            // TWO eyes, wide-set — the heavy stare, and the read that separates it from the rusher's
            // single cyclops lamp even before you clock that one is a box and the other a pod.
            _eyes = new MeshRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                _eyes[i] = Eye("Eye", feet, new Vector3(side * 0.17f, 1.02f, 0.40f), 0.16f);
            }

            // The lawn-roller drum, slung across the front — the blunt garden weapon. Axle across the
            // machine, like the boss's reel, so it reads as a thing that flattens what is in front of it.
            Part("Roller", PrimitiveType.Cylinder, feet,
                 new Vector3(0f, 0.34f, 0.52f), new Vector3(0.24f, 0.46f, 0.24f), _accentMat,
                 Quaternion.Euler(0f, 0f, 90f));

            // Crusher arms — thick, forward, heavy.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part("Arm", PrimitiveType.Cube, feet,
                     new Vector3(side * 0.42f, 0.78f, 0.30f), new Vector3(0.20f, 0.22f, 0.42f), _accentMat);
            }
        }

        /// <summary>One part of a robot. Given a real material always, marked <see cref="SelfDrivenTint"/>
        /// so no director claims it, and stripped of the collider a primitive is born with — gameplay's
        /// CharacterController is the only hitbox this robot has.</summary>
        private static Transform Part(string name, PrimitiveType shape, Transform parent, Vector3 at,
                                      Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Strip(go);
            go.AddComponent<SelfDrivenTint>();

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>An eye. Additive and unlit, like the boss's lamps and Max's goggles — an eye is a
        /// LIGHT, not a painted ball, so it stays bright while the body is in its own shadow. Shared VFX
        /// material + a property block, so a robot's eyes cost no material of their own.</summary>
        private MeshRenderer Eye(string name, Transform parent, Vector3 at, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.AddComponent<SelfDrivenTint>();

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * size;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // ---------------------------------------------------------------- running it

        /// <summary>
        /// LateUpdate, so the tell is read AFTER the enemy's state machine has ticked in Update — a
        /// wind-up that started this frame is already on the eye this frame, not next.
        /// </summary>
        private void LateUpdate()
        {
            if (!_built || _enemy == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;   // paused on the result screen — hold the pose

            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - flashDecay * dt);

            float windup = _enemy.TelegraphProgress;
            Color target = TellColorFor(windup, _flash);
            _tellColor = Color.Lerp(_tellColor, target, 1f - Mathf.Exp(-eyeResponse * dt));
            ApplyEyes(_tellColor);

            // The chassis heats WITH the eye, but only in emission and only a little: the body has to
            // stay its own turquoise/violet or it stops reading as its kind. Same trick the boss uses.
            Color heat = EyeWarn * (windup * 0.30f) + Color.white * (_flash * 0.6f);
            if (_bodyMat != null && _bodyMat.HasProperty(EmissionId)) _bodyMat.SetColor(EmissionId, heat);
            if (_accentMat != null && _accentMat.HasProperty(EmissionId)) _accentMat.SetColor(EmissionId, heat);
        }

        /// <summary>
        /// The eye colour for a given wind-up and flash, 0..1 each. Pure, so a test can prove the tell
        /// warms without having to drive a whole enemy into its lunge: gold at rest, the game's warn
        /// orange at full wind-up, white under a hit.
        /// </summary>
        public static Color TellColorFor(float windup, float flash)
        {
            Color c = Color.Lerp(EyeIdle, EyeWarn, Mathf.Clamp01(windup));
            c = Color.Lerp(c, Color.white, Mathf.Clamp01(flash));
            return c;
        }

        private void ApplyEyes(Color c)
        {
            if (_eyes == null) return;
            c.a = 1f;
            for (int i = 0; i < _eyes.Length; i++)
            {
                var r = _eyes[i];
                if (r == null) continue;
                r.GetPropertyBlock(_eyeMpb);
                _eyeMpb.SetColor(BaseColorId, c);
                r.SetPropertyBlock(_eyeMpb);
            }
        }

        /// <summary>A hit near this robot flashes it — same white "that landed" the whole cast uses. The
        /// signal carries a position, not a victim, so a tight radius keeps one robot's hit from flashing
        /// the pack around it.</summary>
        private void OnDamage(Vector3 pos, float amount, bool crit)
        {
            if ((pos - transform.position).sqrMagnitude <= flashRadius * flashRadius) _flash = 1f;
        }

        private void OnDestroy()
        {
            // Instances, and ours: nothing else points at them.
            if (_bodyMat != null) Destroy(_bodyMat);
            if (_accentMat != null) Destroy(_accentMat);
        }
    }
}
