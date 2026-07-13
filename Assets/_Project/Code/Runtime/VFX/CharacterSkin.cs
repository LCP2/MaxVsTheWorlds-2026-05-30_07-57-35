using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>What a body is, for colouring purposes.</summary>
    public enum CharacterRole
    {
        Player,
        Robot,
        Boss,
        Structure,
    }

    /// <summary>
    /// Owns one character's body colour, hit flash and wind-up tell (YT-61).
    ///
    /// Three problems this fixes, all found by driving the deployed build rather than the editor:
    ///
    /// 1. MAX AND THE ROBOTS WERE THE SAME COLOUR. Every damageable body got the same shared
    ///    material with a white base, so Max and the enemies both rendered as identical cream
    ///    capsules. In a twin-stick game where you read threats at a glance, that's not a polish
    ///    problem, it's a legibility failure. Max is now warm red (his hoodie, per the art bible)
    ///    and the robots are cold steel — opposite ends of the temperature axis, which is the one
    ///    thing that still reads when bodies are small and the screen is busy.
    ///
    /// 2. THE HIT FLASH AND WIND-UP TELL NEVER WORKED AT ALL. RobotEnemy.SetTell() early-returns
    ///    when its `tellRenderer` field is null — and nothing in the project ever assigns it, because
    ///    the enemies are built from primitives in code and that field can only be set by hand in an
    ///    inspector. So every hit flash and every body tell the enemy has ever "played" has been a
    ///    no-op. They're implemented here instead, in the art layer, where they don't depend on
    ///    inspector wiring that a code-driven scene can never supply.
    ///
    /// 3. NEWLY SPAWNED ENEMIES COULD RENDER MAGENTA. The material was applied by a once-a-second
    ///    sweep, so a robot could spawn and charge at you for up to a second still wearing Unity's
    ///    default material — which has no URP subshader and draws as the magenta error colour. The
    ///    skin now applies its material in OnEnable, and enemies are pooled (SetActive true/false),
    ///    so a respawn is skinned on the very frame it appears.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSkin : MonoBehaviour
    {
        /// <summary>Every live skin. A plain list so the damage signal can find the body it hit
        /// without a physics query or an allocation.</summary>
        public static readonly List<CharacterSkin> Active = new List<CharacterSkin>(64);

        // Warm vs cold is the axis that survives being small, busy and half-occluded. Max owns
        // warm; everything that wants to kill him owns cold.
        private static readonly Color PlayerBody = new Color(0.78f, 0.22f, 0.18f);   // Max's red hoodie
        private static readonly Color RobotBody = new Color(0.42f, 0.47f, 0.55f);    // cold steel
        private static readonly Color BossBody = new Color(0.24f, 0.28f, 0.32f);     // darker, heavier

        /// <summary>
        /// The Mower Hutch: painted steel, the same cold family as the robots it builds (YT-77).
        ///
        /// It was a warm taupe (0.40, 0.34, 0.27), and that was survivable while the yard was
        /// greybox. It stopped being survivable the moment the shed around it became actual timber:
        /// a brown box standing inside a brown timber shed is a crate, and the one thing the factory
        /// must never be is scenery — QA already failed once to find the objective (YT-38).
        ///
        /// So it goes to weathered galvanised steel: PALE, and a touch warm.
        ///
        /// Both of those are deliberate and were arrived at by looking. A neutral mid-grey machine
        /// came out INDIGO — it was the darkest large surface in the yard, so the grade's shadow tint
        /// (which leans firmly blue, by design) and the saturation boost on top of it took a grey box
        /// and made it violet. Paint that is lighter than the lawn and slightly warm sits above that
        /// tint instead of underneath it, and reads as metal rather than as plastic.
        ///
        /// Being the palest thing in the yard is also what makes it findable — it is now the one
        /// bright, cold, hard-edged object in a warm garden full of timber and leaves. Max stays the
        /// only warm-red thing on the screen; the robots stay cold steel, which is exactly the family
        /// their factory should belong to.
        /// </summary>
        private static readonly Color StructureBody = new Color(0.47f, 0.46f, 0.44f);

        /// <summary>Bright white — deliberately NOT magenta or any saturated hue. A hit flash has to
        /// read instantly as "that landed" and must never be mistakable for a render error.</summary>
        private static readonly Color FlashColor = new Color(1f, 1f, 1f);

        /// <summary>The wind-up colour. The ground ring (YT-53) is the primary tell; this backs it up
        /// on the body itself, which is what the gameplay always intended and never got.</summary>
        private static readonly Color WarnColor = new Color(1f, 0.35f, 0.12f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private CharacterRole role = CharacterRole.Robot;

        [Tooltip("How fast the hit flash falls away, in flash-units per second. This has to be FAST. " +
                 "The blaster is a sustained stream that lands a tick every 0.1s on every enemy it " +
                 "touches, so a slow flash never gets back to zero between hits and the enemy sits " +
                 "permanently washed white — which erases the very body colour that makes it " +
                 "readable as a threat. It must read as a shimmer under fire, not a white-out.")]
        [SerializeField] private float flashDecay = 16f;

        [Tooltip("How much of the flash bleeds into the body colour. Kept low on purpose: the flash " +
                 "lives mostly in emission, so a hit reads hot WITHOUT the body losing its identity.")]
        [Range(0f, 1f)]
        [SerializeField] private float flashTint = 0.45f;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private RobotEnemy _enemy;
        private Color _body;
        private float _flash;

        public CharacterRole Role => role;
        public Color BodyColor => _body;

        /// <summary>Configure in code (no inspector wiring) and dress immediately.</summary>
        public CharacterSkin Bind(CharacterRole r, Element element = Element.Neutral)
        {
            role = r;
            _renderer = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _enemy = GetComponent<RobotEnemy>();

            // The elemental hook from YT-50: a variant is a function of the base colour, so an
            // elemental robot is one argument away — no second material, no second asset.
            _body = ElementPalette.Recolor(BaseColorFor(r), element);

            // Register here as well as in OnEnable. Binding is the moment a body becomes real, and
            // relying on OnEnable alone means the registry is empty in edit mode (where OnEnable
            // never runs) — which quietly turns any test of the registry into a test of nothing.
            if (!Active.Contains(this)) Active.Add(this);

            Apply();
            return this;
        }

        public static Color BaseColorFor(CharacterRole r)
        {
            switch (r)
            {
                case CharacterRole.Player: return PlayerBody;
                case CharacterRole.Boss: return BossBody;
                case CharacterRole.Structure: return StructureBody;
                default: return RobotBody;
            }
        }

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
            Apply();   // pooled enemies re-enable on spawn — this is what closes the magenta window
        }

        private void OnDisable() => Active.Remove(this);

        private void OnDestroy() => Active.Remove(this);

        /// <summary>Put the stylised material and this body's colour on, right now.</summary>
        public void Apply()
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            if (_body == default) _body = BaseColorFor(role);

            var mat = MaterialFor(role);
            if (mat != null && _renderer.sharedMaterial != mat) _renderer.sharedMaterial = mat;

            if (IsMachine)
            {
                // A machine's colour is IN its material, so there is nothing to tint — and the block
                // has to go, or the Hutch's own Awake-time tint (MowerHutch.TintBody) multiplies
                // against an albedo that already carries the colour. See MaterialFor.
                _renderer.SetPropertyBlock(null);
                return;
            }

            Write(_body, Color.black);
        }

        /// <summary>
        /// Structures don't flash and don't telegraph, so they don't need a property block — and are
        /// much better off without one.
        ///
        /// Nothing routes a hit flash to a structure: CharacterSkinDirector only ever calls
        /// <see cref="NearestTo"/> with <see cref="CharacterRole.Robot"/>, and the wind-up tell reads
        /// a <see cref="RobotEnemy"/> that a factory doesn't have. So every frame, the Hutch's block
        /// was writing the same constant colour it wrote last frame.
        ///
        /// Dropping it is what lets the Hutch wear a real material (YT-77). It is also, bluntly, the
        /// safe side of a rendering bug I could not get to the bottom of: with a property block on the
        /// renderer, the triplanar shader lights this one object roughly four times too dark, while
        /// reporting — through every probe I could put in the shader — a correct base colour, a
        /// correct texture sample, full NdotL and no shadow. Every other surface in the yard on the
        /// same shader is right. Rather than ship a factory that is quietly wrong, the machine's
        /// colour lives in its material, where the shader demonstrably reads it: paint the material
        /// red and the Hutch goes red.
        /// </summary>
        private bool IsMachine => role == CharacterRole.Structure;

        /// <summary>
        /// The material a body wears. Everything that FIGHTS gets the character shader — outline, rim
        /// light, the things that make a body read as a body at a fixed top-down camera.
        ///
        /// The Mower Hutch is the exception, and YT-77 is where that stops being glossed over: it is
        /// not a character, it is a MACHINE. It doesn't move, it has no silhouette to protect, and it
        /// is the only structure in the yard — so it gets worn painted metal, with panels, rivets and
        /// thinning paint. Everything the skin does on top still works untouched: the wear is a
        /// GREYSCALE albedo, so <see cref="Write"/> continues to decide the hutch's colour, its
        /// damage state and its hit flash through exactly the same property block, on a shader that
        /// keeps _BaseColor and _EmissionColor for precisely that reason.
        /// </summary>
        private Material MaterialFor(CharacterRole r)
        {
            if (r == CharacterRole.Structure)
            {
                // Painted steel with panels, rivets and worn paint, in the machine's own colour — the
                // colour is baked into the albedo, exactly as it is for the timber and the paving, so
                // no property block is needed to put it there (see IsMachine).
                var metal = MaterialLibrary.Tinted(SurfaceKind.Metal, _body);
                if (metal != null) return metal;
                // No surface shader in this build — a factory with no grain still has to be visible.
            }

            return MaterialLibrary.Character();
        }

        /// <summary>Flash the body. Called when this character takes a hit.</summary>
        public void Flash() => _flash = 1f;

        private void LateUpdate()
        {
            if (_renderer == null) return;

            // A machine has no flash and no tell (see IsMachine), and MowerHutch re-tints itself in
            // its own Awake — so keep clearing the block, or that tint comes back and multiplies
            // against an albedo that already carries the colour.
            if (IsMachine)
            {
                if (_renderer.HasPropertyBlock()) _renderer.SetPropertyBlock(null);
                return;
            }

            // Wind-up: the body heats toward the warn colour as the strike lands, backing up the
            // ground ring. Reading the enemy's own state — nothing is written back to it.
            float windup = _enemy != null ? _enemy.TelegraphProgress : 0f;
            Color body = windup > 0f ? Color.Lerp(_body, WarnColor, windup) : _body;

            if (_flash > 0f)
            {
                _flash = Mathf.Max(0f, _flash - flashDecay * Time.deltaTime);
                body = Color.Lerp(body, FlashColor, _flash * flashTint);
            }

            // Emission carries most of the flash, so a hit reads hot without the body losing the
            // colour that tells you what it is.
            Color emission = FlashColor * (_flash * 0.9f) + WarnColor * (windup * 0.35f);
            Write(body, emission);
        }

        private void Write(Color body, Color emission)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, body);
            _mpb.SetColor(EmissionId, emission);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>The live skin nearest a world point, within <paramref name="maxDistance"/>.
        /// Used to route a damage event to the body that took it — the signal carries a position,
        /// not a reference. Allocation-free.</summary>
        public static CharacterSkin NearestTo(Vector3 point, float maxDistance, CharacterRole role)
        {
            CharacterSkin best = null;
            float bestSqr = maxDistance * maxDistance;

            for (int i = 0; i < Active.Count; i++)
            {
                var s = Active[i];
                if (s == null || s.role != role) continue;

                float d = (s.transform.position - point).sqrMagnitude;
                if (d > bestSqr) continue;
                bestSqr = d;
                best = s;
            }
            return best;
        }
    }
}
