using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A small turret mounted on a shed roof corner (MV-547, shed roadmap stage 2) — as the game
    /// progresses, sheds carry weapon fittings that are independently destroyable, on a FIXED authored
    /// curve (per the World &amp; Difficulty Framework, never scaled to Max), the same way every other
    /// area-authored hazard already is.
    ///
    /// Each fitting has its own <see cref="DestructibleHealth"/>, IDamageable like <see cref="MowerHutch"/>,
    /// and drops NO loot and counts in NO economy (composition THV, MV-375 large-robot count) — it is
    /// invisible to both by construction, the same way <see cref="MowerHutch"/> is: it never implements
    /// <see cref="MaxWorlds.Enemies.RobotEnemy"/> and never raises
    /// <see cref="MaxWorlds.Pickups.DropSignals.RobotDied"/>.
    ///
    /// Bound to the shed it rides on via <see cref="Bind"/>: there is no public "I died" event on
    /// <see cref="MowerHutch"/> to subscribe to, so this polls <see cref="MowerHutch.IsAlive"/> every
    /// tick and takes itself out the instant it flips false — the same idiom <c>FactoryHusk</c> already
    /// uses for the same reason.
    /// </summary>
    public sealed class ShedFitting : MonoBehaviour, IDamageable
    {
        /// <summary>The per-type table from the ticket: range/cadence/HP, fixed and never scaled.</summary>
        public readonly struct Stats
        {
            public readonly float Range;
            public readonly float Cadence;
            public readonly float Hp;
            public Stats(float range, float cadence, float hp) { Range = range; Cadence = cadence; Hp = hp; }
        }

        public static Stats StatsFor(ShedFittingKind kind) => kind switch
        {
            ShedFittingKind.Spiker => new Stats(range: 8f, cadence: 2.5f, hp: 40f),
            ShedFittingKind.Laser => new Stats(range: 12f, cadence: 6f, hp: 60f),
            ShedFittingKind.Missile => new Stats(range: 14f, cadence: 8f, hp: 80f),
            _ => new Stats(0f, 0f, 0f),
        };

        // --- Spiker: the Bolter's own bolt-flight numbers (EnemyArchetype.Bolter) reused verbatim so
        // the fitting's shot behaves exactly like the enemy's. ---
        private const float SpikerBoltSpeed = 14f;
        private const float SpikerHitRadius = 0.35f;

        // --- Laser: the Gunner's own telegraph/beam numbers (EnemyArchetype.Gunner) reused verbatim.
        // Cadence (6s) is the FULL cycle; recover is whatever's left after telegraph + beam. ---
        private const float LaserDps = 18f;
        private const float LaserTelegraphTime = 0.5f;
        private const float LaserBeamTime = 1.1f;

        // --- Missile: the Launcher's own splash numbers (EnemyArchetype.Launcher) reused verbatim. ---
        private const float MissileSpeed = 4.5f;
        private const float MissileDamage = 22f;
        private const float MissileSplashRadius = 2f;

        // ---------------------------------------------------------------- MV-911: world-independent colour
        //
        // Bug (Lee, live build, World 1): a general lighting darkening left the Spiker fitting "heavily
        // blackened" and its spikes unreadable. The fitting cube (built by MapRuntime.BuildShedFittings)
        // is IDamageable and mounted under the shed's own MowerHutch, so CharacterSkinDirector classifies
        // it CharacterRole.Structure and paints it with a plain MaterialLibrary.Tinted metal material —
        // no emission, so it is entirely dependent on scene lighting for how bright it reads. That is the
        // exact defect MV-857 (Max) and MV-861 (the Launcher missile) already fixed: give the surface its
        // own emission, computed from the active BackyardLook, so it stops depending on the scene light.
        //
        // This also has to keep CharacterSkinDirector's own sweep off the fitting (SelfDrivenTint, the
        // same "whoever drives a block owns it" marker MowerHutch's VulnerableCore already uses), or that
        // director claims the renderer a frame later and overwrites this material with its own undressed
        // metal-tint version.

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>The fitting's own colour — the same pale galvanised steel every other part of the
        /// shed wears (<see cref="CharacterRole.Structure"/>), read live rather than duplicated so a
        /// future retune of the shed's own colour flows through here automatically.</summary>
        private static readonly Color FittingColor = CharacterSkin.BaseColorFor(CharacterRole.Structure);

        /// <summary>Exposes <see cref="FittingColor"/> for <c>MV547ShedFittingTests</c> (MV-911), same
        /// shape as <see cref="MaxWorlds.Enemies.HomingMissile.ShaftColorForTests"/>.</summary>
        public static Color FittingColorForTests => FittingColor;

        /// <summary>Gives the fitting's own renderer a private material carrying MV-857's own
        /// world-compensation emission, and marks it so CharacterSkinDirector never re-dresses it back to
        /// a plain, lighting-only surface. A private clone (never the shared <see cref="MaterialLibrary"/>
        /// cache entry) — MowerHutch tints the exact same <see cref="FittingColor"/> tone, and writing
        /// emission onto the cached instance directly would leak this fitting's glow onto the shed body
        /// too (the same trap <see cref="MaxWorlds.Enemies.HomingMissile"/>'s own EmissiveInstance avoids).</summary>
        private static void BuildVisual(GameObject go)
        {
            go.AddComponent<SelfDrivenTint>();

            var renderer = go.GetComponent<MeshRenderer>();
            Material template = MaterialLibrary.Tinted(SurfaceKind.Metal, FittingColor);
            if (renderer == null || template == null) return;

            var mat = new Material(template) { name = "ShedFitting", hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty(EmissionId))
            {
                BackyardLook activeLook = BackyardLook.ForWorld(BackyardLighting.WorldIndexFromPalette());
                mat.SetColor(EmissionId, MaxRig.WorldCompensationEmission(FittingColor, activeLook));
            }
            renderer.sharedMaterial = mat;
        }

        // ---------------------------------------------------------------- MV-913: reusable missile rig
        //
        // Missile only (ticket scope) — Spiker/Laser stay on BuildVisual's plain tinted cube above until
        // their own tickets convert them. Builds MissileLauncherRig (Runtime/VFX/MissileLauncherRig.cs),
        // a generated-mesh rig with no reference to sheds/MowerHutch/ShedFitting of its own, and hands it
        // a palette carrying the SAME MV-857/861/911 world-compensation emission BuildVisual gives every
        // other fitting — so the missile launcher reads no darker in World 2 fog than a Spiker sitting
        // right next to it. One material per part (never MaterialLibrary's own cached instance), same
        // "clone before you tint" rule BuildVisual already follows.

        private static Material MissilePartMaterial(string name, Color tone, BackyardLook activeLook)
        {
            Material template = MaterialLibrary.Tinted(SurfaceKind.Metal, tone);
            if (template == null) return null;
            var mat = new Material(template) { name = name, hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty(EmissionId)) mat.SetColor(EmissionId, MaxRig.WorldCompensationEmission(tone, activeLook));
            return mat;
        }

        private static MissileLauncherRig BuildMissileVisual(GameObject go)
        {
            go.AddComponent<SelfDrivenTint>();

            // MapRuntime.BuildShedFittings has already set go.transform.localScale to FittingSize
            // (0.5) by the time Bind() runs — the same "1x1x1 unit cube, shrunk by the parent's own
            // scale" trick the old CreatePrimitive cube relied on. MissileLauncherRig authors its parts
            // in real metres, so building it straight under go.transform would scale it down AGAIN
            // (YT-71/YT-74's exact trap — see ParentScale's own doc comment). MakeMetreSpace cancels it.
            Transform metreSpace = ParentScale.MakeMetreSpace(new GameObject("MissileLauncherVisual").transform, go.transform);

            BackyardLook activeLook = BackyardLook.ForWorld(BackyardLighting.WorldIndexFromPalette());
            var palette = new MissileLauncherPalette(
                MissilePartMaterial("ShedFittingMissileBase", FittingColor, activeLook),
                MissilePartMaterial("ShedFittingMissileArm", FittingColor, activeLook),
                MissilePartMaterial("ShedFittingMissileTip", FittingColor * 0.6f, activeLook));

            return MissileLauncherRig.Build(metreSpace, FittingSizeForRig, palette);
        }

        /// <summary>The fitting's own authored roof-corner size in real metres — <c>MapRuntime.FittingSize</c>,
        /// kept as a private copy rather than a cross-file constant reference, so this file never needs to
        /// know that name.</summary>
        private const float FittingSizeForRig = 0.5f;

        private enum Phase { Idle, Telegraph, Beam }

        private ShedFittingKind _kind;
        private MowerHutch _hutch;
        private DestructibleHealth _health;
        private Transform _target;
        private IDamageable _targetDamageable;
        private Phase _phase;
        private float _phaseTimer;
        private float _cooldownTimer;

        /// <summary>MV-913: non-null only for <see cref="ShedFittingKind.Missile"/> — the reusable rig
        /// this fitting drives (facing + fire recoil) but never builds the logic for; see
        /// <see cref="BuildMissileVisual"/>.</summary>
        private MissileLauncherRig _missileRig;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Water Blaster (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;
        public ShedFittingKind Kind => _kind;
        public float Range => StatsFor(_kind).Range;
        public float Cadence => StatsFor(_kind).Cadence;

        /// <summary>Wire this fitting to the shed it rides on and its authored kind (MV-547). Public and
        /// explicit — like <see cref="MowerHutch.Build"/> — so an EditMode test can drive it directly
        /// outside Play mode, with no reliance on Awake (which AddComponent never fires in Edit mode).</summary>
        public void Bind(MowerHutch hutch, ShedFittingKind kind)
        {
            _hutch = hutch;
            _kind = kind;
            _health = new DestructibleHealth(StatsFor(kind).Hp);
            if (kind == ShedFittingKind.Missile) _missileRig = BuildMissileVisual(gameObject);
            else BuildVisual(gameObject);
        }

        /// <summary>Point this fitting at what it should shoot — a test's synthetic Max, or (via
        /// <see cref="Update"/>) the real Player tag lookup. Public and explicit for the same reason
        /// <see cref="Bind"/> is.</summary>
        public void SetTarget(Transform target, IDamageable damageable = null)
        {
            _target = target;
            _targetDamageable = damageable ?? (target != null ? target.GetComponent<IDamageable>() : null);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // robots can't wreck a fitting either
            HudSignals.EmitDamage(transform.position + Vector3.up * 0.4f, info.Amount);
            _health.TakeDamage(info.Amount);
            if (!IsAlive) OnDestroyed();
        }

        /// <summary>Destroying the shed takes every surviving fitting with it (the ticket's own rule) —
        /// bypasses <see cref="DamageRules"/> entirely since this isn't combat damage, it's the fitting's
        /// mount going away.</summary>
        private void KillWithShed()
        {
            if (!IsAlive) return;
            _health.TakeDamage(_health.Max);
            OnDestroyed();
        }

        private void OnDestroyed()
        {
            HudSignals.EmitFittingDestroyed(transform.position);
            // MV-913: GetComponentsInChildren, not GetComponent — Missile's MissileLauncherRig renders
            // through CHILD parts (base/arm/tip), not a renderer on this root, so a root-only lookup used
            // to leave a "destroyed" launcher's whole generated body still visible.
            foreach (var rend in GetComponentsInChildren<Renderer>())
            {
                rend.enabled = false;
                // MV-972: same "never come back" guard MowerHutch.ApplyDestructionEffects gives its own
                // destroyed body — the area gate must not re-enable a destroyed fitting.
                MapStaticBatchRoot.Active?.MarkPermanentlyHidden(rend);
            }
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        private void Update()
        {
            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) SetTarget(p.transform);
            }

            Tick(Time.deltaTime);
        }

        /// <summary>Advances this fitting one step — dt-parameterized, like
        /// <see cref="MowerHutch.TickMobility"/>, so an EditMode test can drive it directly without a
        /// live scene or <see cref="Time.deltaTime"/>. Checks the shed's own pulse FIRST, every tick:
        /// there is no public "I died" event on <see cref="MowerHutch"/> to subscribe to instead (the
        /// same poll idiom <c>FactoryHusk</c> already uses), so this is also what makes "destroying the
        /// shed destroys surviving fittings" testable with a single explicit call rather than needing a
        /// live <see cref="Update"/> loop.</summary>
        public void Tick(float dt)
        {
            if (_hutch != null && !_hutch.IsAlive) { KillWithShed(); return; }
            if (!IsAlive || _kind == ShedFittingKind.None) return;

            // MV-913: the rig ticks (facing + recoil decay) every step this fitting is alive, regardless
            // of which combat phase below returns early — a turret that only turned to face while firing
            // would sit frozen aimed at whatever it last shot, which is not "tracks its current target".
            if (_missileRig != null)
            {
                if (_target != null) _missileRig.Face(_target.position - transform.position);
                _missileRig.Tick(dt);
            }

            switch (_phase)
            {
                case Phase.Idle:
                    _cooldownTimer -= dt;
                    if (_cooldownTimer > 0f || !InRangeAndSighted()) return;
                    BeginAttack();
                    break;
                case Phase.Telegraph:
                    TickTelegraph(dt);
                    break;
                case Phase.Beam:
                    TickBeam(dt);
                    break;
            }
        }

        private bool InRangeAndSighted()
        {
            if (_target == null) return false;
            Vector3 to = _target.position - transform.position; to.y = 0f;
            if (to.magnitude > StatsFor(_kind).Range) return false;
            return LineOfSight.Between(transform, _target);
        }

        private void BeginAttack()
        {
            if (_kind == ShedFittingKind.Laser)
            {
                _phase = Phase.Telegraph;
                _phaseTimer = 0f;
                return;
            }

            Fire();
            _phase = Phase.Idle;
            _cooldownTimer = StatsFor(_kind).Cadence;
        }

        private void TickTelegraph(float dt)
        {
            _phaseTimer += dt;
            if (_phaseTimer < LaserTelegraphTime) return;
            _phase = Phase.Beam;
            _phaseTimer = 0f;
        }

        private void TickBeam(float dt)
        {
            _phaseTimer += dt;

            if (InRangeAndSighted())
            {
                if (_targetDamageable != null && _targetDamageable.IsAlive)
                {
                    Vector3 dir = _target.position - transform.position; dir.y = 0f;
                    _targetDamageable.TakeDamage(new DamageInfo(
                        LaserDps * dt, transform.position, dir.normalized, Team.Enemy));
                }
            }

            if (_phaseTimer < LaserBeamTime) return;
            _phase = Phase.Idle;
            _cooldownTimer = Mathf.Max(0f, StatsFor(_kind).Cadence - LaserTelegraphTime - LaserBeamTime);
        }

        private void Fire()
        {
            if (_target == null) return;
            switch (_kind)
            {
                case ShedFittingKind.Spiker:
                    BolterBolt.Fire(transform.position, _target, SpikerBoltSpeed, StatsFor(_kind).Range, SpikerHitRadius);
                    break;
                case ShedFittingKind.Missile:
                    HomingMissile.Fire(transform.position, _target, MissileSpeed, MissileDamage, MissileSplashRadius);
                    _missileRig?.Fire(); // MV-913: the visible recoil — no diff to the missile's own flight numbers above
                    break;
            }
        }
    }
}
