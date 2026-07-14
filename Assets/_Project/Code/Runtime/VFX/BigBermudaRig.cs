using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Big Bermuda — the possessed industrial mower (YT-90).
    ///
    /// The fight has been running since YT-27 and the boss has been a 3.5 m CUBE the whole time. Not a
    /// greybox stand-in for a boss: a cube, tinted olive, charging at you. The end-cap of the Backyard,
    /// the story beat of the run, and the thing it most resembled was a crate.
    ///
    /// So it is a machine now — a mower, and one built to be read from thirty metres up at 72°, which
    /// is the only angle anybody will ever see it from:
    ///
    ///   * A CUTTING REEL across the front, blades out, spinning. This is the boss's whole threat and
    ///     it points at Max. Its outer edge sits at 2.35 m, which is where <c>chargeContactRadius</c>
    ///     (2.4 m) actually is — so the blades reach exactly as far as the damage does, and the hitbox
    ///     is something you can SEE rather than something you learn by dying to it.
    ///   * MISMATCHED EYES, welded on: two salvaged lamps at different sizes, at different heights, on
    ///     crooked brackets. They are the face, they are what makes it a character rather than a
    ///     vehicle, and they are where you read the fight from (below).
    ///   * A mower's HANDLE, arcing up and back over the grass catcher. Nothing else in the silhouette
    ///     says "lawnmower" as fast: a deck on wheels is a go-kart until the handle is on it.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// THE TELL THE FIGHT NEVER HAD
    ///
    /// <see cref="BigBermudaBoss.SetTell"/> has been writing an orange wind-up colour into the boss's
    /// MaterialPropertyBlock on every phase change since YT-27, and NONE of it has ever reached the
    /// screen. <see cref="CharacterSkin"/> claims the boss (it is a MeshRenderer under an IDamageable)
    /// and rewrites that same block every LateUpdate with the flat near-black body colour. Update
    /// writes; LateUpdate overwrites; the player sees nothing. The single most important read in the
    /// fight — "it is about to charge, MOVE" — has been dead code the entire time.
    ///
    /// That is the same bug that ate the Mower Hutch's vulnerable core (YT-38): two writers on one
    /// property block, and script order picks the winner. The fix is the same one — ONE writer. This
    /// rig owns every renderer on the machine, and no director can reach them:
    ///
    ///   * <see cref="RuntimeSurfaceDirector"/> skips anything under a <see cref="KeepsOwnMaterial"/>,
    ///     which this object carries.
    ///   * <see cref="CharacterSkinDirector"/> only claims renderers under an <see cref="IDamageable"/>,
    ///     and the machine is NOT parented to the boss (see <see cref="Follow"/>), so it claims none.
    ///
    /// Which means nothing else hands these renderers a material, and a primitive's default material is
    /// not in the build's shader set — it ships MAGENTA. Every part built here is given a real material
    /// explicitly. That is not belt-and-braces; it is the exact failure the Hutch's core shipped with.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// READING THE FIGHT OFF THE MACHINE
    ///
    /// Three states, three colours, and the language is one the player has already been taught:
    ///
    ///   ASLEEP      eyes dark. It is standing beyond the gate from the first frame of the run, and a
    ///               dead machine that opens its eyes when the factory dies is worth more than one that
    ///               fades in.
    ///   AWAKE       acid GREEN — the grass that possesses it, and the colour of its own clipping AoEs.
    ///               Deliberately not the warn colour: if the boss glowed orange while idling, orange
    ///               would stop meaning "incoming" within ten seconds.
    ///   WINDING UP  hot ORANGE, and the whole chassis heats with it. This is <see cref="WarnColor"/> —
    ///               the same orange every telegraph in the game already uses. The reel screams up to
    ///               speed and the stack coughs. You get 0.75 s, which is what the fight always gave
    ///               you and never showed you.
    ///   ENRAGED     RED, and it stays red between attacks, so phase 2 is legible at a glance and not
    ///               only in the half-second it is committing to something.
    ///
    /// Reads the fight, writes nothing to it: <see cref="BigBermudaBoss.Action"/>,
    /// <see cref="BigBermudaBoss.Enraged"/> and <see cref="BigBermudaBoss.Engaged"/> are getters, and
    /// the intro/defeat beats come off the same <see cref="HudSignals"/> that YT-55's spectacle uses.
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

        /// <summary>The machine's paint. Straight from <see cref="CharacterSkin"/> — the boss's body
        /// colour is decided in ONE place (YT-86 put it there) and a second near-black living here
        /// would drift away from it the first time anyone tuned either.</summary>
        private static Color BodyColor => CharacterSkin.BaseColorFor(CharacterRole.Boss);

        /// <summary>The reel. Cold, pale steel: the blades are the one part of this machine that is
        /// supposed to catch the light, because they are the part that kills you.</summary>
        private static readonly Color BladeColor = new Color(0.58f, 0.64f, 0.72f);

        /// <summary>Awake and idle: acid green. Bermuda grass, and the colour of the clippings it
        /// throws (BigBermudaBoss.grassColor). It is possessed BY the lawn.</summary>
        private static readonly Color EyeIdle = new Color(0.42f, 1f, 0.22f);

        /// <summary>Winding up. The same orange as every other telegraph in the game — the player has
        /// been taught this word by every robot in the yard, and the boss should not invent a new one.</summary>
        private static readonly Color EyeWarn = new Color(1f, 0.35f, 0.12f);

        /// <summary>Phase 2. It stays red BETWEEN attacks, so "it got worse" is readable at a glance.</summary>
        private static readonly Color EyeRage = new Color(1f, 0.12f, 0.06f);

        /// <summary>Committed to a charge, enraged. Past red — there is nowhere hotter to go.</summary>
        private static readonly Color EyeRageWarn = new Color(1f, 0.72f, 0.45f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ---------------------------------------------------------------- tuning

        [Header("The reel")]
        [Tooltip("Degrees/sec the cutting reel turns while it is just idling around. Slow — a mower " +
                 "ticking over. The spin-up to the charge is what carries the threat.")]
        [SerializeField] private float reelIdleSpin = 90f;

        [Tooltip("Degrees/sec at full commit. Fast enough to blur into a disc, which is what a blade " +
                 "at speed does and what makes it read as something you do not want to be in front of.")]
        [SerializeField] private float reelChargeSpin = 1500f;

        [Tooltip("How fast the reel reaches its target speed. It SCREAMS up during the wind-up and " +
                 "spins down slowly afterwards — a flywheel has mass.")]
        [SerializeField] private float reelSpinUp = 6f;

        [Header("The eyes")]
        [Tooltip("How fast the eyes reach the colour the current phase calls for. Fast: a telegraph " +
                 "that eases in is a telegraph that arrives after the charge does.")]
        [SerializeField] private float eyeResponse = 9f;

        [Tooltip("How much of the eye colour bleeds into the chassis as it heats. Low on purpose — the " +
                 "body must not stop being near-black, because the near-black is what the rim lights.")]
        [Range(0f, 1f)]
        [SerializeField] private float chassisHeat = 0.55f;

        [Header("Exhaust")]
        [SerializeField] private float exhaustIdleRate = 4f;
        [SerializeField] private float exhaustChargeRate = 40f;
        [SerializeField] private Color exhaustColor = new Color(0.30f, 0.30f, 0.29f, 0.5f);

        // ---------------------------------------------------------------- state

        private BigBermudaBoss _boss;
        private Transform _reel;
        private ParticleSystem _exhaust;
        private Material _bodyMat;
        private Material _bladeMat;

        private readonly MeshRenderer[] _eyes = new MeshRenderer[2];
        private readonly Transform[] _wheels = new Transform[4];
        private readonly float[] _wheelRadius = new float[4];
        private MaterialPropertyBlock _eyeMpb;

        private Color _eyeColor = Color.black;   // starts DARK: it is asleep beyond the gate
        private float _reelSpin;
        private float _flash;                    // 1 the frame it takes a hit, decays
        private float _lastHealth = 1f;
        private float _wakeTimer;                // >0 while the eyes are stuttering on
        private Vector3 _lastPos;
        private bool _awake;
        private bool _dying;
        private float _dieTimer;

        /// <summary>Awake and running. False while it is dormant, and false forever once it is dead.</summary>
        public bool Running => _awake && !_dying;

        /// <summary>The colour the eyes are currently burning. The tell, in one value — this is what a
        /// test can look at to prove the wind-up actually reaches the screen.</summary>
        public Color EyeColor => _eyeColor;

        /// <summary>Degrees/sec the cutting reel is turning. Spins up on the wind-up.</summary>
        public float ReelSpin => _reelSpin;

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
            // Without it, RuntimeSurfaceDirector classifies the machine BY SHAPE and repaints the deck
            // as a paving stone — exactly what it did to the factory's impeller (YT-78).
            gameObject.AddComponent<KeepsOwnMaterial>();

            _eyeMpb = new MaterialPropertyBlock();
            BuildMaterials();

            // The greybox goes. Its COLLIDERS stay — the CharacterController and the box are what the
            // Water Blaster has to hit, and what Max walks into. Only the visual changes, which is the
            // whole reason this is an art-stream ticket (docs/CODE_DRIVEN_SCENES.md, ModelSwap).
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            Build();

            // Stand it on the boss BEFORE the first frame. Otherwise the machine spends frame one at the
            // world origin and frame two thirty metres away, and the wheels — which read their speed off
            // how far it actually moved — spin up as though it had crossed the yard in a sixtieth of a
            // second.
            Follow();
            _lastPos = transform.position;

            ApplyEyes(Color.black);   // asleep
        }

        /// <summary>
        /// Two materials, both OURS.
        ///
        /// Instances, not the shared <see cref="MaterialLibrary.Character()"/> — that one material is
        /// worn by Max and every robot in the yard, and tinting it to heat the boss up would set the
        /// entire cast on fire. Owning an instance is also what lets the chassis heat with a single
        /// property write instead of a MaterialPropertyBlock on each of fifteen renderers (a block is
        /// what breaks SRP batching; a shared material instance is what keeps it).
        /// </summary>
        private void BuildMaterials()
        {
            var character = MaterialLibrary.Character();

            _bodyMat = NewCharacterMaterial(character, "BigBermuda_Body", BodyColor);
            _bladeMat = NewCharacterMaterial(character, "BigBermuda_Blades", BladeColor);
        }

        private static Material NewCharacterMaterial(Material template, string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58): a
            // plain lit material still draws a correctly coloured machine, just without the outline.
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
        /// The machine, in metres, with the ground at y = 0 and +Z pointing at whatever it is about to
        /// run over. Nothing here is parented to the boss, and that is deliberate twice over: the boss
        /// transform is SCALED (3.5, 3, 3.5), so a 20 cm bolt hung off it would render as a 70 cm one
        /// (the bug that made the factory's health bar metres wide, YT-71) — and being outside the boss
        /// is what keeps CharacterSkinDirector's hands off these renderers.
        /// </summary>
        private void Build()
        {
            // -- the deck. Low, wide, heavy: a machine that is mostly a blade housing on wheels.
            Part("Deck", PrimitiveType.Cube, new Vector3(0f, 0.62f, 0.05f), new Vector3(3.05f, 0.5f, 3.3f));
            Part("Skirt", PrimitiveType.Cube, new Vector3(0f, 0.3f, 0.05f), new Vector3(3.25f, 0.3f, 3.1f));

            // -- the engine it is possessed through.
            Part("Hood", PrimitiveType.Cube, new Vector3(0f, 1.32f, -0.55f), new Vector3(2f, 0.95f, 1.9f));

            // Bolted on crooked, on ONE side only. A silhouette with a mistake in it reads as a thing
            // that was built by something that does not care what it looks like.
            Part("Intake", PrimitiveType.Cube, new Vector3(-1.14f, 1.5f, -0.2f), new Vector3(0.5f, 0.5f, 0.85f),
                 Quaternion.Euler(0f, 0f, 9f));

            Part("Stack", PrimitiveType.Cylinder, new Vector3(0.78f, 1.95f, -0.72f), new Vector3(0.15f, 0.3f, 0.15f));

            // -- the grass catcher. Pulled IN, to z = -1.97 at its back face: Max is stopped by the
            //    boss's collider about 2.15 m from its centre, so anything inside that can never be
            //    walked into, and a hopper he could stand inside would be a hole in the machine.
            Part("Hopper", PrimitiveType.Cube, new Vector3(0f, 1.15f, -1.55f), new Vector3(2.3f, 1f, 0.85f),
                 Quaternion.Euler(-14f, 0f, 0f));

            // -- THE HANDLE. This is the single most load-bearing shape on the machine.
            //
            // Seen from 72° almost overhead, a mower is a wide deck with two rails and a crossbar
            // sticking out of the back of it. That plan-view "T" is the icon — without it a deck on
            // wheels is a go-kart, and the first render of this boss proved it: a dark rounded lump that
            // could have been anything.
            //
            // It is allowed to overhang the collider where the hopper is not: it is a thin bar 2.8 m up,
            // and Max walks underneath it rather than into it.
            var gripAt = new Vector3(0f, 2.75f, -3.3f);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -1.15f : 1.15f;
                var anchor = new Vector3(x, 1.5f, -1.55f);         // out of the top of the hopper
                var top = new Vector3(x, gripAt.y, gripAt.z);      // up to the grip

                // Aimed by construction rather than by a Euler angle whose sign I would be guessing at.
                Vector3 along = top - anchor;
                Part($"Handle{i}", PrimitiveType.Cube, (anchor + top) * 0.5f,
                     new Vector3(0.13f, along.magnitude, 0.13f),
                     Quaternion.FromToRotation(Vector3.up, along.normalized));
            }
            Part("Grip", PrimitiveType.Cube, gripAt, new Vector3(2.5f, 0.15f, 0.15f));

            // -- wheels. Big at the back, small at the front, because that is a mower and because a
            //    machine whose wheels do not match is a machine that was assembled out of other machines.
            Wheel(0, new Vector3(-1.62f, 0.52f, -1.45f), 0.52f);
            Wheel(1, new Vector3(1.62f, 0.52f, -1.45f), 0.52f);
            Wheel(2, new Vector3(-1.5f, 0.34f, 1.05f), 0.34f);
            Wheel(3, new Vector3(1.5f, 0.34f, 1.05f), 0.34f);

            // -- the business end. A cowl, and under it the reel.
            Part("Cowl", PrimitiveType.Cube, new Vector3(0f, 1.05f, 1.3f), new Vector3(3f, 0.6f, 0.7f),
                 Quaternion.Euler(18f, 0f, 0f));
            BuildReel();

            BuildEyes();
            _exhaust = BuildExhaust(new Vector3(0.78f, 2.3f, -0.72f));
        }

        /// <summary>
        /// The cutting reel: five blades on an axle that runs side to side across the front, so they
        /// come over the top and down towards you. A mower's blade spins FLAT, under the deck, where a
        /// camera thirty metres up at 72° would never see a frame of it — and the one thing this
        /// machine's threat cannot be is invisible. So it is a flail reel, which is a real thing on a
        /// real industrial mower, and it is pointed at Max.
        /// </summary>
        private void BuildReel()
        {
            // The axle sits low and forward: low enough that the blades sweep down to y = 0 and vanish
            // into the lawn at the bottom of the arc — which is where a mower's blade goes and is the
            // cheapest possible way to say "this thing cuts."
            var hub = new GameObject("Reel");
            hub.transform.SetParent(transform, worldPositionStays: false);
            hub.transform.localPosition = new Vector3(0f, ReelHeight, ReelForward);
            _reel = hub.transform;

            const int blades = 5;
            for (int i = 0; i < blades; i++)
            {
                // Around the X axis: the axle lies across the machine, so the blades come up over the
                // front and down at you. Rotated Y is the radial direction, so the blade is LONG across
                // the machine (x), reaches outward (y), and is thin in the sweep (z).
                var rot = Quaternion.Euler(i * (360f / blades), 0f, 0f);
                var blade = Part($"Blade{i}", PrimitiveType.Cube,
                                 hub.transform.localPosition + rot * new Vector3(0f, BladeReach * 0.5f, 0f),
                                 new Vector3(2.85f, BladeReach, 0.08f),
                                 rot, _bladeMat);
                blade.SetParent(_reel, worldPositionStays: true);
            }
        }

        /// <summary>How far a blade reaches from the axle. With the axle at <see cref="ReelForward"/>,
        /// this puts the blade tip at 2.35 m from the boss's centre — and its charge hurts you at 2.4 m
        /// (BigBermudaBoss.chargeContactRadius). The reach you can SEE is the reach that kills you.</summary>
        private const float BladeReach = 0.72f;

        private const float ReelForward = 1.63f;

        /// <summary>Axle height = blade reach, so the bottom of the arc lands exactly on the lawn.</summary>
        private const float ReelHeight = 0.72f;

        /// <summary>
        /// The face. Two lamps welded on by something that has never seen one: different sizes,
        /// different heights, sitting on crooked brackets, and one is noticeably bigger than the other.
        /// Mounted proud of the hood and tilted up, because a face on the FRONT of a machine is a face
        /// nobody at this camera angle will ever see.
        /// </summary>
        private void BuildEyes()
        {
            // BIG. The first cut of these was 44 cm and it was measured, not guessed, to be wrong: at the
            // camera the game is actually played at, the whole boss is about sixty pixels across, so a
            // 44 cm lamp is EIGHT — a dark speck on a dark machine. These are 60 and 82 cm, which is
            // eleven and fifteen pixels: two glowing dots you can see from across the yard, and two you
            // can tell apart.
            //
            // Mounted on the hood's top-front lip (its face is at z = 0.45, its roof at y = 1.80) and
            // standing proud of it, because a face on the FRONT of a machine is a face nobody at 72°
            // will ever see. The small one is high and the big one is low: it has no idea what a face is.
            _eyes[0] = Eye("EyeL", new Vector3(-0.58f, 1.78f, 0.42f), 0.6f, -14f);
            _eyes[1] = Eye("EyeR", new Vector3(0.62f, 1.5f, 0.52f), 0.82f, 11f);
        }

        private MeshRenderer Eye(string name, Vector3 at, float size, float bracketTilt)
        {
            // The weld. Near-black, part of the machine — it runs from inside the hood out to the lamp,
            // and without it the eyes read as painted on rather than bolted on by something in a hurry.
            var bracket = new Vector3(at.x * 0.92f, at.y - size * 0.12f, 0.12f);
            Part($"{name}Weld", PrimitiveType.Cube, bracket, new Vector3(size * 0.5f, size * 0.45f, 0.75f),
                 Quaternion.Euler(0f, 0f, bracketTilt));

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * size;

            var r = go.GetComponent<MeshRenderer>();

            // Additive and unlit: an eye is a LIGHT, not a painted ball. It has to stay bright when the
            // machine is in its own shadow, which — being near-black and lit by one warm key — it
            // mostly is. Shared VFX material + a property block per eye, so the two can burn different
            // colours without minting a material each (the Hutch's vents, YT-78, do exactly this).
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private void Wheel(int index, Vector3 at, float radius)
        {
            var t = Part($"Wheel{index}", PrimitiveType.Cylinder, at,
                         new Vector3(radius * 2f, 0.16f, radius * 2f),
                         Quaternion.Euler(0f, 0f, 90f));   // axle across the machine
            _wheels[index] = t;
            _wheelRadius[index] = radius;
        }

        /// <summary>One part of the machine. Given a real material, always — nothing built here may
        /// keep a primitive's default material, which has no URP subshader and ships MAGENTA (YT-38).</summary>
        private Transform Part(string name, PrimitiveType shape, Vector3 at, Vector3 scale,
                               Quaternion? rot = null, Material mat = null)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Strip(go);

            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat ?? _bodyMat;
            return go.transform;
        }

        /// <summary>
        /// Nothing on this machine can be shot or walked into.
        ///
        /// The boss's own CharacterController and box are the hitbox. An extra collider on the handle
        /// would silently eat water that was aimed at the boss, and Max would bump into a grass catcher
        /// that gameplay does not believe is there.
        /// </summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private ParticleSystem BuildExhaust(Vector3 at)
        {
            var go = new GameObject("Exhaust");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = at;

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // smoke is left BEHIND a charge
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startColor = exhaustColor;
            main.gravityModifier = -0.04f;
            main.maxParticles = 90;   // ambience budget: AmbiencePlayTests holds effects under 200

            var emission = ps.emission;
            emission.rateOverTime = 0f;   // asleep. Nothing is burning yet.

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.05f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // straight up out of the pipe

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

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
            noise.strength = 0.22f;
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
        /// followed it in Update would sit one frame behind its own hitbox — at a 16 m/s charge that is
        /// a quarter of a metre of daylight between the blades and the damage.
        /// </summary>
        private void LateUpdate()
        {
            if (_boss == null) return;

            Follow();

            if (_dying) { TickDeath(); return; }

            float dt = Time.deltaTime;

            _flash = Mathf.Max(0f, _flash - dt * 7f);
            if (_wakeTimer > 0f) _wakeTimer = Mathf.Max(0f, _wakeTimer - dt);

            TickTells(dt);
            SpinReel(dt);
        }

        /// <summary>
        /// The machine sits on the LAWN, under the boss, facing where the boss faces.
        ///
        /// Its own y is thrown away and the ground is used instead, exactly as the shockwave does
        /// (BossSpectacle.Shockwave). The boss is a 3 m cube balanced on a capsule whose height is a
        /// scaled-up 1 — wherever gravity settles that, it is not where a mower's wheels go, and a boss
        /// hovering a metre over its own shadow is worse than a boss that is a cube.
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

        private void TickTells(float dt)
        {
            Color target = TargetEyeColor();

            // A hit whites the lamps out for a moment. Same word the robots use (CharacterSkin.Flash) —
            // and on a boss with 1200 HP, "did that land?" is a question the player asks a lot.
            if (_flash > 0f) target = Color.Lerp(target, Color.white, _flash * 0.7f);

            _eyeColor = Color.Lerp(_eyeColor, target, 1f - Mathf.Exp(-eyeResponse * dt));
            ApplyEyes(_eyeColor);

            // The chassis heats WITH the eyes, but only a little: the body has to stay near-black or the
            // rim light — which is most of what separates this thing from the lawn — has nothing to
            // frame (MaterialLibrary.RimPower, YT-86).
            if (_bodyMat != null && _bodyMat.HasProperty(EmissionId))
            {
                float heat = Committed ? 1f : 0f;
                heat = Mathf.Max(heat, _flash);
                _bodyMat.SetColor(EmissionId, _eyeColor * (heat * chassisHeat));
            }

            if (_bladeMat != null && _bladeMat.HasProperty(EmissionId))
            {
                // The blades take the heat harder than the body does. They are the thing to be afraid of.
                _bladeMat.SetColor(EmissionId, _eyeColor * (Committed ? 0.7f : 0.08f));
            }

            if (_exhaust != null)
            {
                var emission = _exhaust.emission;
                emission.rateOverTime = !_awake ? 0f : (Committed ? exhaustChargeRate : exhaustIdleRate);
            }
        }

        /// <summary>Winding up or charging: the two states where being anywhere near the front of this
        /// machine is fatal, and the two it has to shout about.</summary>
        private bool Committed =>
            _awake && (_boss.Action == BossAction.ChargeWindup || _boss.Action == BossAction.Charge);

        private Color TargetEyeColor()
        {
            if (!_awake) return Color.black;

            // Waking. The lamps stutter — a machine that has been dead in a garden for a long time does
            // not come on cleanly. Cheap, and it lands right on top of the dust and the shockwave YT-55
            // throws at the same moment.
            if (_wakeTimer > 0f)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 34f, 0f);
                return EyeIdle * (flicker > 0.45f ? 1f : 0.05f);
            }

            bool rage = _boss.Enraged;
            if (Committed) return rage ? EyeRageWarn : EyeWarn;
            return rage ? EyeRage : EyeIdle;
        }

        /// <summary>The two lamps never quite agree. One is dimmer and a touch cooler than the other —
        /// they were salvaged from different machines, and a matched pair would read as a design.</summary>
        private void ApplyEyes(Color c)
        {
            for (int i = 0; i < _eyes.Length; i++)
            {
                var r = _eyes[i];
                if (r == null) continue;

                Color eye = i == 0 ? c * 0.78f : c;
                eye.a = 1f;

                r.GetPropertyBlock(_eyeMpb);
                _eyeMpb.SetColor(BaseColorId, eye);
                r.SetPropertyBlock(_eyeMpb);
            }
        }

        /// <summary>
        /// The reel screams up through the wind-up and coasts back down after — a flywheel with mass,
        /// not a switch. The spin-up IS the telegraph: you hear a mower before you see it, and this is
        /// the closest a silent build gets to that.
        /// </summary>
        private void SpinReel(float dt)
        {
            float target = !_awake ? 0f : (Committed ? reelChargeSpin : reelIdleSpin);

            // Up fast, down slow. A blade that spins down as sharply as it spins up feels like a fan;
            // one that keeps turning after the charge feels like it has weight.
            float rate = target > _reelSpin ? reelSpinUp : reelSpinUp * 0.35f;
            _reelSpin = Mathf.Lerp(_reelSpin, target, 1f - Mathf.Exp(-rate * dt));

            if (_reel != null) _reel.Rotate(Vector3.right, _reelSpin * dt, Space.Self);

            // The wheels turn because it is DRIVING at you, not sliding. Speed comes from how far the
            // boss actually moved, so nothing here has to know its charge speed or be re-tuned when
            // gameplay changes it.
            Vector3 pos = transform.position;
            float travelled = Vector3.Distance(pos, _lastPos);
            _lastPos = pos;

            if (dt > 0f)
            {
                for (int i = 0; i < _wheels.Length; i++)
                {
                    if (_wheels[i] == null) continue;
                    float degrees = travelled / (2f * Mathf.PI * _wheelRadius[i]) * 360f;
                    _wheels[i].Rotate(Vector3.up, degrees, Space.Self);   // its local up IS the axle
                }
            }
        }

        // ---------------------------------------------------------------- the fight's beats

        /// <summary>The factory is dead and it has woken up. YT-55 throws dust and a shockwave on this
        /// same signal; this is the machine underneath it coming on.</summary>
        private void OnEngaged(string name, int phases)
        {
            _awake = true;
            _wakeTimer = 0.9f;   // inside the boss's own 1.6 s intro — lit and running before it moves
            _lastHealth = 1f;

            if (_exhaust != null) _exhaust.Emit(22);   // it catches, hard
        }

        /// <summary>Health only ever falls, so a fall is a hit. The damage signal carries a position
        /// rather than a victim, so this is a cleaner read than guessing from proximity.</summary>
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
        /// ResultScreen sets timeScale = 0 in the same frame, so anything on scaled time is frozen solid
        /// before it moves. YT-55's defeat sequence runs unscaled for exactly this reason and this has
        /// to run alongside it.
        ///
        /// The boss deactivates its own GameObject the instant it dies — but this machine is not
        /// parented to it, so it can outlive it by half a second and actually DIE on screen: the lamps
        /// go out, the reel seizes, and the whole thing sags into the lawn while YT-55's three flashes
        /// and its big one go off on top of it. Then it is gone, before the result card settles.
        /// </summary>
        private void TickDeath()
        {
            const float duration = 0.55f;

            _dieTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_dieTimer / duration);

            // The lamps die first, and they die fast. Whatever was in there has left.
            _eyeColor = Color.Lerp(_eyeColor, Color.black, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            ApplyEyes(_eyeColor);

            // The reel seizes rather than coasting: something in it has caught.
            _reelSpin = Mathf.Lerp(_reelSpin, 0f, 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));
            if (_reel != null) _reel.Rotate(Vector3.right, _reelSpin * Time.unscaledDeltaTime, Space.Self);

            // And it sags — nose down into the grass it spent the whole fight cutting.
            //
            // These are ABSOLUTE offsets, not accumulating ones, and that is deliberate: Follow() has
            // already re-anchored the machine on the boss this frame, so each of these is applied to a
            // clean pose and driven entirely by t. Accumulate them instead and the sag would depend on
            // the frame rate — half a second of it at 30 fps would be half the tilt it is at 60.
            transform.rotation *= Quaternion.Euler(t * 9f, 0f, t * 4f);
            transform.position += Vector3.down * (t * t * 0.9f);

            if (_bodyMat != null && _bodyMat.HasProperty(EmissionId))
                _bodyMat.SetColor(EmissionId, Color.black);

            if (t >= 1f) gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            // Instances, and ours: nothing else points at them, so nothing else has to be told.
            if (_bodyMat != null) Destroy(_bodyMat);
            if (_bladeMat != null) Destroy(_bladeMat);
        }
    }
}
