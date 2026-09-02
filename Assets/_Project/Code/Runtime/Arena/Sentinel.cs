using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Max's deployable Sentinel (MV-362, restructured MV-422: "Delete the Wall Sentinel entirely...
    /// one sentinel only — the Gunner, now just 'Sentinel'"). A low-HP turret — "a hose pipe on a
    /// stick", reusing the primary's own blue — that auto-fires at the nearest robot in range and,
    /// once its Move axis is leveled, follows Max at a standoff distance. Placed at an aimed point,
    /// with its own HP.
    ///
    /// Six RIG axes (<c>u_sen</c>'s children, MV-422) replace the old three tracks: Damage
    /// (<c>u_dmg</c>), Range (<c>u_rng</c>), Health (<c>u_hp</c>) — all direct children of
    /// <c>u_sen</c> — then Move (<c>u_mov</c>), Cost (<c>u_cst</c>), Slots (<c>u_slt</c>) behind
    /// Damage/Range/Health respectively. MV-653 REPEALS the prior rule that a shot's damage must
    /// stay below Max's CURRENT primary output: that fraction-of-Max's-live-output model made the
    /// whole Damage track imperceptible once display rounding collapsed adjacent levels to the same
    /// integer (Lee, device, 2026-09-02). Every shot's damage is now a flat, integer value
    /// independent of Max's own primary damage — see <see cref="AbilityTuning.SentinelDamagePerShot"/>.
    ///
    /// Deployed sentinels are permanent until destroyed — no player-triggered repair (DECISION, Lee
    /// 15 Aug 2026) — so unlike <see cref="MaxWorlds.Enemies.RobotEnemy"/> it is never pooled;
    /// <see cref="Die"/> destroys the GameObject outright, the same one-shot lifecycle
    /// <see cref="MaxWorlds.Factories.MowerHutch"/> uses for its own death. MV-398 (same day)
    /// reversed only the "no repair" half: a damaged-but-alive sentinel now passively regens HP once
    /// left unhit for a while — see <see cref="Update"/> — but a destroyed one still never comes
    /// back, and there is still no manual repair action. MV-604 later added the one exception to "no
    /// recall": redeploying at the Slots cap recalls whichever sentinel is furthest from Max rather
    /// than refusing — see <see cref="Recall"/>. A recall is deliberately NOT routed through
    /// <see cref="Die"/> — it must never count as a death (no kill counter, no death VFX, no on-death
    /// payout).
    ///
    /// <see cref="Team"/> is <see cref="Team.Player"/> — Max's own device. <see cref="DamageRules"/>'s
    /// same-team rejection means a robot (Team.Enemy) CAN hit it, and Max's own primary (Team.Player)
    /// CANNOT — <c>WaterBlaster.FireTick</c> already skips every <c>Team.Player</c> receiver, so
    /// nothing extra is needed to stop Max from shooting his own sentinel.
    /// </summary>
    public sealed class Sentinel : MonoBehaviour, IDamageable, IHealthReadout
    {
        private static readonly List<Sentinel> _active = new List<Sentinel>(8);

        /// <summary>Every sentinel deployed right now — what <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s
        /// retargeting reads to find the nearest one, and what
        /// <see cref="MaxWorlds.Weapons.PlayerAbilities"/> counts against the Slots (u_slt) cap.</summary>
        public static IReadOnlyList<Sentinel> Active => _active;

        /// <summary>Empties the registry ONLY — mirrors <see cref="MaxWorlds.Enemies.RobotEnemy.ResetRegistry"/>'s
        /// list-only contract for test isolation. Does not destroy any GameObject; see
        /// <see cref="DestroyAllActive"/> for the real teardown a fresh level or an area crossing needs.</summary>
        public static void ResetRegistry() => _active.Clear();

        /// <summary>Player-facing toggle for Attack Mode (MV-636 HUD button, gated on Move/u_mov &gt;= 1
        /// — same visibility gate <see cref="AbilityTuning.SentinelCanMove"/> already answers for the
        /// Move ability itself). A single flag shared by every deployed sentinel, not per-instance, so a
        /// multi-sentinel loadout acts in unison — same "one player choice, felt everywhere" shape
        /// <see cref="MaxWorlds.Weapons.WeaponSystemState.WaterBalloonAutoFireEnabled"/> uses for its own
        /// toggle. Read every frame by <see cref="Update"/> (movement) and <see cref="NearestRobotInRange"/>
        /// (fire targeting). Defaults off — the existing standoff-follow/nearest-overall behaviour is
        /// unchanged until the player actively opts in.</summary>
        public static bool AttackModeEnabled { get; set; }

        /// <summary>Destroys every deployed sentinel and empties the registry. Sentinels aren't
        /// pooled (unlike robots), so a full reset has to tear the GameObjects down too, not just
        /// forget them. Two call sites: <see cref="MaxWorlds.Arena.Map.MapRuntime"/> on a fresh level
        /// build, and <see cref="WorldRunner"/> on player death/restore. MV-579 (26 Aug 2026 DECISION)
        /// removed the third call site this used to have — <c>BackyardPath</c>'s
        /// <see cref="MaxWorlds.Enemies.AreaAccumulationDirector.PlayerCrossedIntoArea"/> handler,
        /// which used to wipe every sentinel on an area crossing (MV-362/MV-396) unless the f_skr
        /// fusion was forged. Sentinels now persist across an area crossing unconditionally; the
        /// "refund" is still automatic wherever this IS still called, since the Slots cap is always
        /// checked live against <see cref="Active"/>.Count, never a separately-tracked balance.</summary>
        public static void DestroyAllActive()
        {
            if (_active.Count == 0) return;
            var snapshot = new List<Sentinel>(_active);
            _active.Clear();
            foreach (Sentinel s in snapshot)
            {
                if (s == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(s.gameObject);
                else UnityEngine.Object.DestroyImmediate(s.gameObject);
            }
        }

        private static readonly Color BodyColor = new Color(0.35f, 0.55f, 0.75f); // the primary's blue

        /// <summary>MV-580: the second of the sentinel's own two body tones (the Warm slot in
        /// <see cref="RobotPalette"/>) — a lighter, cooler tint of <see cref="BodyColor"/> rather than
        /// the enemy roster's shared <see cref="CharacterSkin.RobotCool"/>, so the sentinel reads as
        /// one machine built in Max's own colour family, not a robot wearing the enemy shed's grey.</summary>
        private static readonly Color BodyAccent = new Color(0.62f, 0.78f, 0.90f);

        /// <summary>MV-580: the eye. Deliberately the SAME cyan family as the water beam's own colour
        /// (Max's own primary weapon colour) and nowhere near the enemy roster's tell colours — gold
        /// idle, warn orange, white flash (see <see cref="RobotRig"/>) — so the one glowing lens on
        /// this body never reads as "about to hit you". "Friendly eye colour rather than the enemies'
        /// [tell]", per the ticket.</summary>
        private static readonly Color EyeColor = new Color(0.55f, 0.9f, 1f);

        /// <summary>MV-580: the body's silhouette is <see cref="RobotBodies.Build"/>'s Gunner — the
        /// closest existing body to Lee's reference (a squat, domed, multi-legged walker) of anything
        /// already in the shared builder, per the ticket's "do not author new geometry" instruction.
        /// Never <see cref="RobotBodies.Build"/> a wheeled kind here — the sentinel must WALK (see
        /// <see cref="_gait"/>), and giving it wheels instead was explicitly rejected on the ticket.</summary>
        private const MaxWorlds.Enemies.EnemyKind BodyKind = MaxWorlds.Enemies.EnemyKind.Gunner;

        /// <summary>The old primitive's exact footprint (0.5 x 0.6 x 0.5 local scale on a unit
        /// cylinder), rebuilt as a plain collider so removing <c>CreatePrimitive</c> (MV-580) changes
        /// nothing about how a robot or Max collides with a deployed sentinel — the linked
        /// sentinel-behaviour ticket owns retuning this shape, not this one (see the class doc on
        /// <see cref="BuildBody"/>).</summary>
        private const float ColliderRadius = 0.25f;
        private const float ColliderHeight = 1.2f;

        /// <summary>MV-624: Unity's 0.3 default step offset would let the sentinel climb small props
        /// (pots/crates) instead of being stopped by them — which fails this ticket's AC2 while
        /// appearing to pass a "does it collide at all" check. Half the default, well under the
        /// smallest prop this needs to be blocked by.</summary>
        private const float ControllerStepOffset = 0.1f;

        /// <summary>MV-624: bounds a sidestep whose target sits inside a solid — with collision now
        /// live, <see cref="_sidestepTarget"/>'s arrival test can never fire if the target is
        /// unreachable, which would otherwise latch the sentinel mid-dodge forever (worse than the bug
        /// this ticket fixes). A fixed timeout, several times the ~0.3s an unobstructed sidestep
        /// (<see cref="AbilityTuning.DefaultSentinelSidestepDistance"/> at
        /// <see cref="AbilityTuning.DefaultSentinelSidestepSpeed"/>) normally takes, so it never cuts a
        /// real dodge short.</summary>
        private const float SidestepTimeoutSeconds = 1f;

        private RobotBodies.Body _body;
        private LegGaitDriver _gait;
        private Transform _model;
        private Material[] _ownedMaterials;

        private static readonly Collider[] s_hits = new Collider[16];

        /// <summary>Scratch buffers for <see cref="NearestRobotInRangeAttackMode"/> — cleared and
        /// refilled every fire-eligible frame rather than allocated fresh, same idiom as <see cref="s_hits"/>.</summary>
        private static readonly List<RobotEnemy> s_candidateRobots = new List<RobotEnemy>(16);
        private static readonly List<Vector3> s_candidatePositions = new List<Vector3>(16);

        /// <summary>Scratch buffer for <see cref="SeparateFromOtherSentinels"/> — cleared and refilled
        /// every Update rather than allocated fresh, same idiom as <see cref="s_hits"/>.</summary>
        private static readonly List<Vector3> s_otherSentinelPositions = new List<Vector3>(8);

        // MV-395: the shot itself was invisible — damage landed but nothing was ever drawn from the
        // turret to its target. MV-616 replaced the original bare two-point LineRenderer tracer (a
        // flat-coloured "laser") with the SAME water VFX Max's own jet fires (WaterVfx, see
        // Runtime/VFX/WaterVfx.cs) — the sentinel fires the same substance from a smaller nozzle, so
        // it should read as the same material at a smaller scale, not a second hand-authored effect.
        private const float MuzzleHeight = 1.1f; // near the top of the built body (see BuildBody)

        /// <summary>The beam's nozzle radius, fed to <see cref="WaterVfx.Init"/> — deliberately well
        /// under the jet's own 1.1 m visual radius (<c>WaterBlaster.streamVisualRadius</c>) since this
        /// is "a hose pipe on a stick" (class doc), not Max's two-handed blaster.</summary>
        private const float SentinelBeamRadius = 0.28f;

        /// <summary>The beam's cone half-angle, fed to <see cref="WaterVfx.Init"/>. The turret aims a
        /// single locked-on shot at one target (see <see cref="Update"/>) rather than spraying a wide
        /// arc like Max's cone weapon, so this is kept tight — <see cref="WaterVfx.SprayHalfAngleFor"/>
        /// floors the visible stream at 1 degree regardless, i.e. effectively a straight jet.</summary>
        private const float SentinelBeamConeHalfAngle = 3f;

        /// <summary>Cosmetic damage value handed to <see cref="WaterVfx.Splash"/> purely to size the
        /// impact droplet count (<see cref="WaterVfxTuning.SplashDroplets"/>) — NOT the shot's actual
        /// damage, which already landed on the target before this VFX call runs (see <see cref="Update"/>).</summary>
        private const float SentinelBeamSplashWeight = 2f;

        /// <summary>How long the beam stays visible per shot. Capped below the fire interval so a
        /// fast-firing turret's beam never runs into the next shot's own flash.</summary>
        private float BeamVisibleSeconds => Mathf.Min(0.12f, _fireInterval * 0.9f);

        private WaterVfx _beamVfx;
        private float _beamTimer;

        public string ReadoutName => "SENTINEL";

        private float _range;
        private float _fireInterval;
        private float _fireCooldown;
        private float _moveSpeed;
        private float _standoffDistance;
        private Transform _followTarget;

        private Collider _bodyCollider;

        /// <summary>MV-624: what every movement write now routes through (<see cref="CharacterControllerMotion.SafeMove"/>)
        /// instead of assigning <c>transform.position</c> directly — the fix for the sentinel walking
        /// straight through walls/pots/crates it should collide with, the same way Max already does
        /// (see <see cref="MaxWorlds.Player.PlayerController"/>). Matches <see cref="_bodyCollider"/>'s
        /// footprint exactly (built alongside it in <see cref="BuildBody"/>) so the silhouette doesn't
        /// change.</summary>
        private CharacterController _controller;

        /// <summary>Set while the sentinel is mid-dodge (MV-579) — null the rest of the time, including
        /// while the ordinary standoff-follow step below is running.</summary>
        private Vector3? _sidestepTarget;

        /// <summary>MV-624: seconds spent on the CURRENT <see cref="_sidestepTarget"/> — reset to 0 the
        /// instant a new one is set, bounds a sidestep that collision has made unreachable (see
        /// <see cref="SidestepTimeoutSeconds"/>).</summary>
        private float _sidestepElapsed;

        private DestructibleHealth _health;
        private float _timeSinceDamage;

        public bool IsAlive => _health != null && _health.IsAlive;

        public Team Team => Team.Player;

        public float Normalized => _health?.Normalized ?? 0f;
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;

        /// <summary>The health ceiling in effect right now (MV-604: live off Health/u_hp — see
        /// <see cref="RefreshFromRigState"/>), not just whatever was passed to <see cref="Init"/>.</summary>
        public float HealthMax => _health?.Max ?? 0f;

        /// <summary>The auto-fire reach in effect right now (MV-604: live off Range/u_rng — see
        /// <see cref="RefreshFromRigState"/>), not just whatever was passed to <see cref="Init"/>.</summary>
        public float Range => _range;

        /// <summary>The follow speed in effect right now (MV-604: live off Move/u_mov — see
        /// <see cref="RefreshFromRigState"/>), not just whatever was passed to <see cref="Init"/>.</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>Fired once, the instant this sentinel is destroyed.</summary>
        public event Action<Sentinel> Died;

        /// <summary>Places and builds the turret. <paramref name="range"/>/<paramref name="moveSpeed"/>
        /// are the Range (u_rng)/Move (u_mov) axes' CURRENT values at deploy time — read fresh from
        /// <see cref="WeaponSystemState"/>-adjacent <c>RigState</c> lookups by the caller, not cached
        /// here beyond this one deploy (matching the old Gunner's own "read fresh each shot" rule for
        /// damage). <paramref name="followTarget"/>/<paramref name="standoffDistance"/> drive the
        /// Move axis's follow behaviour (MV-422) — null/0 speed leaves it stationary, the pre-MV-422
        /// behaviour.</summary>
        public void Init(Vector3 position, float maxHp, float range, float fireInterval,
            float moveSpeed, float standoffDistance, Transform followTarget)
        {
            transform.position = position;
            _range = range;
            _fireInterval = fireInterval;
            _moveSpeed = moveSpeed;
            _standoffDistance = standoffDistance;
            _followTarget = followTarget;
            InitHealth(maxHp);
            BuildBody();
            IgnorePlayerCollision();
            WorldHealthBar.Attach(gameObject, this, 1.9f, 1.2f, alwaysShow: true);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)

            // MV-604: subscribed here, not in OnEnable — Unity does not reliably call OnEnable for a
            // freshly-scripted GameObject outside Play mode (the same reason InitHealth's _active.Add
            // above is belt-and-braces rather than relying on OnEnable alone). OnDestroy is the
            // matching, PROVEN-reliable teardown point in edit mode (see its own doc).
            RigState.Changed += RefreshFromRigState;
        }

        /// <summary>
        /// MV-580: was a raw <c>GameObject.CreatePrimitive(PrimitiveType.Cylinder)</c> — Unity's
        /// built-in default material, no URP subshader, so a player build drew a magenta capsule (see
        /// <see cref="RuntimeSurfaceDirector"/>'s doc for why the corrective sweep can never reach it:
        /// it explicitly skips anything under an <see cref="IDamageable"/>, and <see cref="Sentinel"/>
        /// is one). Now built from the same shared, hand-authored body geometry every Backyard robot
        /// uses (<see cref="RobotBodies.Build"/>) — reusing the Gunner's tripod silhouette, the closest
        /// thing already in that builder to Lee's reference (a squat, domed, multi-legged walker) — in
        /// a distinct palette (<see cref="BodyColor"/>/<see cref="BodyAccent"/>, <see cref="EyeColor"/>)
        /// so it reads as Max's own machine, never as one more robot. Every part gets a real URP
        /// material explicitly, because <see cref="RuntimeSurfaceDirector"/> never dresses an
        /// <see cref="IDamageable"/> and nothing else will.
        ///
        /// Legged AND mobile — the first body in the roster that is both (see
        /// <see cref="_gait"/>/<see cref="LegGaitDriver"/>) — so the collider is rebuilt as a plain
        /// primitive-free <see cref="CapsuleCollider"/> matching the old cylinder's exact footprint,
        /// not left to whatever <see cref="RobotBodies.Build"/> happens to produce (it builds visual
        /// meshes only, never a collider — see <see cref="CharacterPart"/>). Collision TUNING belongs
        /// to the linked sentinel-behaviour ticket; this only keeps today's collision unchanged.
        /// </summary>
        private void BuildBody()
        {
            _model = ParentScale.MakeMetreSpace(new GameObject("Model").transform, transform);

            var warm = NewMaterial("Sentinel_Warm", BodyColor);
            var accent = NewMaterial("Sentinel_Accent", BodyAccent);
            var dark = NewMaterial("Sentinel_Dark", CharacterSkin.RobotDark);
            var gold = NewMaterial("Sentinel_Gold", CharacterSkin.RobotGold);
            _ownedMaterials = new[] { warm, accent, dark, gold };
            var palette = new RobotPalette(warm, accent, dark, gold);

            _body = RobotBodies.Build(BodyKind, _model, palette);
            ApplyEyeColor();
            _gait = new LegGaitDriver();

            var col = gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, ColliderHeight * 0.5f, 0f);
            col.height = ColliderHeight;
            col.radius = ColliderRadius;
            col.isTrigger = false; // solid — robots route around it like any wall (ObstacleSteering);
                                    // Max himself is carved back out of that below (IgnorePlayerCollision).
            _bodyCollider = col;

            // MV-624: same footprint as the capsule above, so movement can go through a swept, blocking
            // CharacterController.Move (via CharacterControllerMotion.SafeMove) instead of the direct
            // transform.position writes that let the sentinel walk through walls and pots. stepOffset
            // is deliberately lower than PlayerController's own (Unity's 0.3 default) so a small prop
            // stops it rather than lets it climb over; slopeLimit/skinWidth are left at Unity's default,
            // matching PlayerController, which never sets either.
            _controller = gameObject.AddComponent<CharacterController>();
            _controller.center = col.center;
            _controller.height = col.height;
            _controller.radius = col.radius;
            _controller.stepOffset = ControllerStepOffset;

            // The capsule (what robots steer around, and what damage resolves against) and the
            // controller (itself a Collider) would otherwise catch on each other's own body.
            Physics.IgnoreCollision(_bodyCollider, _controller, true);
        }

        /// <summary>A material instance this sentinel owns and destroys — never the shared template
        /// <see cref="MaterialLibrary.Character()"/> itself, which every character in the yard wears;
        /// tinting this one machine would tint the whole cast. Falls back to the plain surface shader
        /// (a look regression, never a magenta one) if the stylised character shader is unavailable —
        /// the same degrade <see cref="RobotRig"/> uses.</summary>
        private static Material NewMaterial(string name, Color color)
        {
            var template = MaterialLibrary.Character();
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        /// <summary>Tints every lens <see cref="RobotBodies.Build"/> handed back — set once, at build
        /// time: unlike an enemy's tell (<see cref="RobotRig.TellColorFor"/>), a deployed sentinel has
        /// no wind-up to telegraph, so a single friendly colour is the whole story.</summary>
        private void ApplyEyeColor()
        {
            if (_body.Eyes == null) return;
            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < _body.Eyes.Length; i++)
            {
                var eye = _body.Eyes[i];
                if (eye == null) continue;
                eye.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", EyeColor);
                eye.SetPropertyBlock(mpb);
            }
        }

        /// <summary>MV-579: a deployed sentinel must never physically block Max, however it got in his
        /// way — standing in a doorway, following him into a gate mouth (there is no recall — see the
        /// class doc), or simply placed on his path. <see cref="Physics.IgnoreCollision(Collider,Collider,bool)"/>
        /// against Max's own <see cref="CharacterController"/> is the same fix
        /// <see cref="MaxWorlds.Weapons.ForceFieldBubble.Init"/> already uses for exactly this shape of
        /// problem (a solid collider that must stop everyone EXCEPT its owner) — a per-pair exemption,
        /// not a physics-layer change, so nothing else the sentinel's layer touches (robots) is
        /// affected. <see cref="_followTarget"/> is Max's own transform (see <see cref="Init"/>'s
        /// caller, <c>PlayerAbilities.TryDeploySentinel</c>) regardless of whether the Move (u_mov)
        /// axis has been leveled — this guard must hold even for a sentinel that never moves at all.</summary>
        private void IgnorePlayerCollision()
        {
            if (_followTarget == null) return;
            if (!_followTarget.TryGetComponent<CharacterController>(out var playerCc)) return;

            // MV-624: the new CharacterController is itself a Collider that must be exempted too —
            // exempting only _bodyCollider (the pre-MV-624 fix) would leave Max blocked by the very
            // component that now actually drives the sentinel's movement, reintroducing MV-579.
            if (_bodyCollider != null) Physics.IgnoreCollision(_bodyCollider, playerCc, true);
            if (_controller != null) Physics.IgnoreCollision(_controller, playerCc, true);
        }

        protected void InitHealth(float maxHp)
        {
            _health = new DestructibleHealth(maxHp);
            _health.Destroyed += Die;

            // Registered here, not left to OnEnable alone: a sentinel is only "deployed" once Init
            // has actually run, and this guarantees the registry sees it the instant deployment
            // completes regardless of Unity's own OnEnable timing for a freshly-scripted GameObject
            // (belt-and-braces — OnEnable below still covers the ordinary enable/disable case).
            if (!_active.Contains(this)) _active.Add(this);
        }

        private void OnEnable()
        {
            if (!_active.Contains(this)) _active.Add(this);
        }

        private void OnDisable() => _active.Remove(this);

        /// <summary>MV-604: keeps an already-deployed sentinel's Move/Range/Health axes current with
        /// their RIG levels — the same "read fresh, never cache across an upgrade" rule the firing
        /// path's Damage read (<c>u_dmg</c>, see <see cref="Update"/>) already followed; Move and
        /// Range were the two that didn't, which is why buying <c>u_mov</c> after deployment used to
        /// do nothing for a sentinel already on the field. Refreshes on <see cref="RigState.Changed"/>
        /// rather than every frame — this runs on iOS. No-op before <see cref="Init"/> has run.</summary>
        private void RefreshFromRigState()
        {
            if (_health == null) return;

            _range = AbilityTuning.SentinelRange(
                RigState.Level("u_rng"), AbilityTuning.DefaultSentinelRange, AbilityTuning.DefaultSentinelRangePerLevel);
            _moveSpeed = AbilityTuning.SentinelMoveSpeed(
                RigState.Level("u_mov"), AbilityTuning.DefaultSentinelMoveSpeedPerLevel);

            // Retune raises the ceiling only — Current is left exactly where it was, never refilled
            // (MV-604 item 5: raising the cap must not be a free heal).
            float newMaxHp = AbilityTuning.SentinelMaxHp(
                RigState.Level("u_hp"), AbilityTuning.DefaultSentinelBaseHp, AbilityTuning.DefaultSentinelHpPerLevel);
            _health.Retune(newMaxHp);
        }

        /// <summary>MV-604: reclaims this sentinel's deployment slot because a redeploy at the Slots
        /// cap chose it as the furthest from Max — NOT because it died. Mirrors
        /// <see cref="DestroyAllActive"/>'s per-item teardown (registry removal + GameObject destroy)
        /// rather than going through <see cref="Die"/>: no <see cref="Died"/> event, no
        /// <see cref="DestructibleHealth.Destroyed"/> event, so nothing wired to "a sentinel died" —
        /// a kill counter, death VFX, an on-death payout — ever fires for a recall. Emits
        /// <see cref="HudSignals.SentinelRecalled"/> instead, so the vanish still reads as an event
        /// rather than a silent pop, however far off-screen it happens.</summary>
        public void Recall()
        {
            _active.Remove(this);
            HudSignals.EmitSentinelRecalled(transform.position);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            if (info.Amount > 0f) _timeSinceDamage = 0f; // MV-398: (re)starts the regen delay below
            _health.TakeDamage(info.Amount);
        }

        /// <summary>HP after <paramref name="dt"/> seconds of passive regen (MV-398) — same
        /// delay-gated linear trickle as <see cref="PlayerHealth.Regenerate"/> (never revives a
        /// destroyed sentinel, never overfills past <paramref name="max"/>), reused rather than
        /// reinvented since the ticket's tuning is deliberately aliased to Max's own. Pure, so the
        /// trickle is unit-testable without a live sentinel.</summary>
        public static float Regenerate(float current, float max, float timeSinceDamage, float delay, float perSec, float dt) =>
            PlayerHealth.Regenerate(current, max, timeSinceDamage, delay, perSec, dt);

        private void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            _timeSinceDamage += dt;

            float regenPerSec = AbilityTuning.SentinelRegenPerSec(
                RigState.Level("u_hp"), RigBoard.MaxLevel("u_hp"),
                AbilityTuning.DefaultSentinelRegenPerSecAtLevel1, AbilityTuning.DefaultSentinelRegenPerSec);
            float next = Regenerate(_health.Current, _health.Max, _timeSinceDamage,
                AbilityTuning.DefaultSentinelRegenDelaySeconds, regenPerSec, dt);
            float healAmount = next - _health.Current;
            if (healAmount > 0f) _health.Heal(healAmount);

            TickMovement(dt);

            // MV-580: the walk cycle. Driven off the sentinel's OWN world position, after the movement
            // above has already updated it this frame — so a mover that just stepped shows legs that
            // moved, and one that didn't (no follow target, or already at its standoff distance) shows
            // legs settling, on the very same frame that decided which case it was.
            if (_gait != null && _model != null)
            {
                _gait.Tick(_body.Legs, transform.position, dt);
                Vector3 modelPos = _model.localPosition;
                modelPos.y = _gait.BobHeight();
                _model.localPosition = modelPos;
            }

            if (_beamTimer > 0f)
            {
                _beamTimer -= dt;
                if (_beamTimer <= 0f && _beamVfx != null) _beamVfx.SetStreaming(false);
            }

            _fireCooldown -= dt;
            if (_fireCooldown > 0f) return;

            RobotEnemy target = NearestRobotInRange();
            if (target == null) return;

            // MV-426 OVERCHARGE (f_ovc): "runs off your cells: double rate of fire while you have
            // charge to spend" — forged AND a power cell banked halves the interval before the next shot.
            bool overchargeActive = RigFusionState.IsForged("f_ovc") && PickupWallet.PowerCells > 0;
            _fireCooldown = AbilityTuning.SentinelFireInterval(_fireInterval, overchargeActive);

            int damageLevel = RigState.Level("u_dmg");
            float damage = AbilityTuning.SentinelDamagePerShot(
                damageLevel,
                AbilityTuning.DefaultSentinelBaseDamage,
                AbilityTuning.DefaultSentinelDamagePerLevel);

            Vector3 dir = target.transform.position - transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // the turret tracks its target

            if (damage > 0f)
            {
                target.TakeDamage(new DamageInfo(damage, target.transform.position, dir, Team,
                    source: DamageSource.Ability));
                FireBeam(target.transform.position);
            }
        }

        /// <summary>MV-624: the sidestep/standoff-follow/separation movement, split out of <see cref="Update"/>
        /// so it can be driven with an explicit <paramref name="dt"/> under test — the same shape
        /// <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s own <c>TickChase</c> already uses, since
        /// <see cref="Time.deltaTime"/> is not reliably non-zero outside Play mode. Behaviour is
        /// unchanged from the inline version this replaces.</summary>
        private void TickMovement(float dt)
        {
            if (_followTarget != null)
            {
                float reactDistSq = AbilityTuning.DefaultSentinelReactDistance * AbilityTuning.DefaultSentinelReactDistance;
                if (_sidestepTarget == null && (transform.position - _followTarget.position).sqrMagnitude < reactDistSq)
                {
                    // MV-579: this reaction is independent of the Move (u_mov) axis and of
                    // IgnorePlayerCollision above — Max can never be BLOCKED either way, but a static
                    // turret standing there while he walks straight through it still reads as broken,
                    // so it visibly gets out of his way every time regardless of whether it can follow.
                    _sidestepTarget = AbilityTuning.SentinelSidestepTarget(transform.position,
                        _followTarget.position, _followTarget.forward, AbilityTuning.DefaultSentinelSidestepDistance);
                    _sidestepElapsed = 0f;
                }
            }

            if (_sidestepTarget.HasValue)
            {
                Vector3 next2 = Vector3.MoveTowards(transform.position, _sidestepTarget.Value,
                    AbilityTuning.DefaultSentinelSidestepSpeed * dt);
                // MV-624: SafeMove (a swept, blocking CharacterController.Move), not a direct
                // transform.position write — that direct write was a teleport with no collision test,
                // which is exactly how the sentinel walked through walls/pots despite carrying a solid
                // collider (see _controller's own doc).
                Vector3 displacement2 = next2 - transform.position;
                if (displacement2 != Vector3.zero) CharacterControllerMotion.SafeMove(_controller, displacement2);

                // MV-624: bound the latch — collision can now stop the sentinel short of a target that
                // sits inside a solid, and the old arrival-only test would then never clear.
                _sidestepElapsed += dt;
                bool arrived = (transform.position - _sidestepTarget.Value).sqrMagnitude < 0.0025f;
                if (arrived || _sidestepElapsed >= SidestepTimeoutSeconds) _sidestepTarget = null;
            }
            else if (_followTarget != null && _moveSpeed > 0f)
            {
                (Vector3 goalPoint, float goalStandoff) = AbilityTuning.SentinelFollowGoal(
                    AttackModeEnabled, _followTarget.position, _followTarget.forward,
                    _standoffDistance, AbilityTuning.DefaultSentinelAttackModeAheadDistance);
                Vector3 next3 = AbilityTuning.SentinelStandoffStep(
                    transform.position, goalPoint, goalStandoff, _moveSpeed, dt);
                Vector3 displacement3 = next3 - transform.position;
                if (displacement3 != Vector3.zero) CharacterControllerMotion.SafeMove(_controller, displacement3);
            }

            SeparateFromOtherSentinels(dt);
        }

        /// <summary>MV-615: keeps this sentinel at least <see cref="PlayerAbilities.SentinelPlacementClearance"/>
        /// from every OTHER active sentinel, every frame — not just at deploy time. The standoff-follow
        /// step above walks every following sentinel onto the same ring around Max with no regard for
        /// its neighbours, so two placed close together used to converge onto the same point on that
        /// ring; this runs after that step to push them back apart, using
        /// <see cref="AbilityTuning.SentinelSeparationStep"/>'s nearest-neighbour repulsion.</summary>
        private void SeparateFromOtherSentinels(float dt)
        {
            s_otherSentinelPositions.Clear();
            foreach (Sentinel s in _active)
            {
                if (s == null || s == this || !s.IsAlive) continue;
                s_otherSentinelPositions.Add(s.transform.position);
            }
            if (s_otherSentinelPositions.Count == 0) return;

            Vector3 next = AbilityTuning.SentinelSeparationStep(transform.position, s_otherSentinelPositions,
                PlayerAbilities.SentinelPlacementClearance, AbilityTuning.DefaultSentinelSidestepSpeed, dt);
            // MV-624: SafeMove, not a direct transform.position write — see the note on the sidestep
            // step above; this is the third and last of the sentinel's three direct-write movement paths.
            Vector3 displacement = next - transform.position;
            if (displacement != Vector3.zero) CharacterControllerMotion.SafeMove(_controller, displacement);
        }

        /// <summary>Fires the water VFX from the turret's muzzle to the point it just hit. Cosmetic
        /// only — the damage above has already landed regardless of whether this draws. Reuses
        /// <see cref="WaterVfx"/> (MV-616) rather than a second hand-authored effect: each shot re-Inits
        /// the same built instance at the CURRENT target distance (cheap — see <c>WaterVfx.Refit</c>,
        /// the same path a nozzle upgrade takes on the jet), so the stream always lands exactly on the
        /// robot it just hit rather than a fixed authored range.</summary>
        private void FireBeam(Vector3 targetPosition)
        {
            if (_beamVfx == null) _beamVfx = BuildBeamVfx();

            Vector3 muzzle = transform.position + Vector3.up * MuzzleHeight;
            Vector3 end = new Vector3(targetPosition.x, muzzle.y, targetPosition.z);
            float distance = Mathf.Max(0.5f, Vector3.Distance(muzzle, end));

            _beamVfx.Init(distance, SentinelBeamRadius, SentinelBeamConeHalfAngle);
            _beamVfx.SetStreaming(true);

            Vector3 dir = end - muzzle;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
            _beamVfx.Splash(end, dir, SentinelBeamSplashWeight);

            _beamTimer = BeamVisibleSeconds;
        }

        /// <summary>Built once and reused for this turret's whole life (a Sentinel is never pooled —
        /// see the class doc), parented at <see cref="MuzzleHeight"/> with identity local rotation so
        /// it inherits the turret's own facing — <see cref="Update"/> already rotates the whole body
        /// to look at the target before calling <see cref="FireBeam"/>, so the water needs no separate
        /// aim logic of its own.</summary>
        private WaterVfx BuildBeamVfx()
        {
            var go = new GameObject("BeamOrigin");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, MuzzleHeight, 0f);
            go.transform.localRotation = Quaternion.identity;
            return go.AddComponent<WaterVfx>();
        }

        private RobotEnemy NearestRobotInRange()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _range, s_hits, ~0, QueryTriggerInteraction.Ignore);

            if (AttackModeEnabled && _followTarget != null) return NearestRobotInRangeAttackMode(count);

            RobotEnemy best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hits[i].TryGetComponent<RobotEnemy>(out var robot) || !robot.IsAlive) continue;
                float d = (robot.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = robot; }
            }
            return best;
        }

        /// <summary>MV-636 Attack Mode: prioritises the nearest robot within Max's forward cone over the
        /// globally-nearest one, wrapping <see cref="SentinelTargeting.SelectAttackModeTargetIndex"/>'s
        /// pure priority rule around the live <see cref="s_hits"/> query <see cref="NearestRobotInRange"/>
        /// already ran.</summary>
        private RobotEnemy NearestRobotInRangeAttackMode(int hitCount)
        {
            s_candidateRobots.Clear();
            s_candidatePositions.Clear();
            for (int i = 0; i < hitCount; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hits[i].TryGetComponent<RobotEnemy>(out var robot) || !robot.IsAlive) continue;
                s_candidateRobots.Add(robot);
                s_candidatePositions.Add(robot.transform.position);
            }

            int index = SentinelTargeting.SelectAttackModeTargetIndex(transform.position, s_candidatePositions,
                _followTarget.position, _followTarget.forward, SentinelTargeting.AttackModeForwardConeHalfAngleDegrees);
            return index >= 0 ? s_candidateRobots[index] : null;
        }

        private void Die()
        {
            // Remove from the registry BEFORE Destroy/DestroyImmediate, not after — OnDisable below
            // would eventually do this too, but Destroy() defers OnDisable to the end of the current
            // frame in play mode (MV-397: a dead sentinel still counted against the Slots cap for
            // the rest of that frame, so an immediate redeploy attempt read the slot as still full).
            // Removing here makes the slot free the instant the sentinel dies, matching "read live
            // off Active, never a separately-tracked balance" above.
            _active.Remove(this);
            Died?.Invoke(this);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>MV-580: the four palette instances <see cref="NewMaterial"/> made are ours and
        /// nothing else points at them — same reasoning as <see cref="RobotRig.OnDestroy"/> for the
        /// enemy roster's own material instances. Play/edit-gated like every other teardown in this
        /// class (<see cref="Die"/>, <see cref="DestroyAllActive"/>) rather than a bare <c>Destroy</c>:
        /// this fires from <c>OnDestroy</c> itself, which a test's own <c>DestroyImmediate(gameObject)</c>
        /// reaches in edit mode, and a bare <c>Destroy</c> there logs "may not be called from edit
        /// mode" and fails every EXISTING Sentinel test's teardown, not just this ticket's own.</summary>
        private void OnDestroy()
        {
            // MV-604: unsubscribes the live-upgrade refresh Init() wired up — the matching half of
            // that subscription, and the one teardown point proven to fire reliably in edit mode (see
            // the comment on that subscription). A no-op if Init() never ran (RigState.Changed -= a
            // handler that was never added is harmless).
            RigState.Changed -= RefreshFromRigState;

            if (_ownedMaterials == null) return;
            for (int i = 0; i < _ownedMaterials.Length; i++)
            {
                if (_ownedMaterials[i] == null) continue;
                if (Application.isPlaying) Destroy(_ownedMaterials[i]);
                else DestroyImmediate(_ownedMaterials[i]);
            }
        }
    }
}
