using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Big Bermuda — the Backyard boss (YT-27, slice version). A possessed industrial-mower
    /// mech that stays dormant beyond the gate until the Mower Hutch dies, then engages: it
    /// repositions, telegraphs, and charges across the arena leaving grass-clipping AoEs. At
    /// low HP it enrages — faster, and it rains mower blades on top of the charges (the slice's
    /// stand-in for the full M2 phase-2 choreography, spec §4.7). Takes Water-Blaster damage,
    /// drives the HUD boss bar (name card + phase segments) via <see cref="HudSignals"/>, and
    /// drops a guaranteed Rare gadget shard on death. Greybox body; VFX are code-driven.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class BigBermudaBoss : MonoBehaviour, IDamageable
    {
        private enum Phase { Dormant, Intro, Fight, Dead }

        // Every number this fight is made of lives in BossTuning (YT-94), NOT in a [SerializeField].
        //
        // It used to live in both, and the scene won: Backyard_Slice.unity carries a serialized copy of
        // each one, so the boss the code described was not the boss anyone fought. The last person to
        // change its HP had to RENAME the field to make the value take effect. A const cannot be
        // shadowed, and "the tuning values are easy to adjust" is an acceptance criterion, not a nicety.
        private const string BossName = "BIG BERMUDA";
        private const float Gravity = 20f;


        [Header("Intro")]
        [SerializeField] private float introTime = 1.6f;

        [Header("Tells")]
        [SerializeField] private Color idleColor = new Color(0.35f, 0.45f, 0.30f);
        [SerializeField] private Color windupColor = new Color(1f, 0.35f, 0.1f);
        [SerializeField] private Color grassColor = new Color(0.45f, 0.7f, 0.25f, 0.7f);
        [SerializeField] private Color bladeColor = new Color(0.8f, 0.8f, 0.85f, 0.8f);

        private Phase _phase = Phase.Dormant;
        private DestructibleHealth _health;
        private BigBermudaBrain _brain;
        private CharacterController _cc;
        private Transform _target;
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        private float _verticalVel;
        private float _introTimer;
        private Vector3 _chargeDir;
        private float _grassTimer;
        private float _bladeTimer;
        private bool _contactThisCharge;

        public bool IsAlive => _phase == Phase.Fight && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        // --- read-only fight state, for the art layer (YT-90) ---
        //
        // Big Bermuda is a MACHINE with moving parts, and what those parts are doing has to agree with
        // what the fight is doing: the reel spins up as it winds up, the eyes go hot as it commits, the
        // whole thing goes red when it enrages. None of that is inferable from the outside — a boss
        // standing still is winding up OR recovering, and those are opposite things to a player.
        //
        // So the fight says what it is doing, out loud. These are getters over state this class already
        // holds; nothing here decides anything, and nothing outside this file can write to the fight.
        // Same shape as MowerHutch.Normalized, which is what lets FactoryLife run the factory without
        // reaching into it.

        /// <summary>What the boss is doing this frame — drives the tells on the model (BigBermudaRig).</summary>
        public BossAction Action => _brain != null ? _brain.Current : BossAction.Reposition;

        /// <summary>True below the enrage threshold: phase 2, blade-rain, everything faster.</summary>
        public bool Enraged => _brain != null && _brain.Enraged;

        /// <summary>True from the moment it wakes until it dies. Dormant beyond the gate before that —
        /// the machine is standing there the whole time, and it should look asleep, not switched off.</summary>
        public bool Engaged => _phase == Phase.Intro || _phase == Phase.Fight;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _health = new DestructibleHealth(BossTuning.Health);
            _health.Destroyed += OnDeath;
            _brain = new BigBermudaBrain();
            AcquireTarget();
            SetTell(idleColor);
        }

        // It wakes when the SOURCES ARE ALL GONE, not when one of them is (YT-92). The slice had a
        // single factory, so "a factory died" and "the yard is clear" were the same event and this
        // could listen to the identity-less destruction signal. With two factories that signal fires
        // on the first kill — and a boss that woke up then would come through the gate while the
        // player still had a factory pumping robots at their back. FactoryCensus is the one thing that
        // knows how many sources the run has; it says when the last of them falls.
        private void OnEnable() => FactoryCensus.Cleared += OnFactoriesCleared;
        private void OnDisable() => FactoryCensus.Cleared -= OnFactoriesCleared;

        private void Start()
        {
            // Tell the HUD a real boss exists so it never engages/drains its stand-in boss.
            HudSignals.EmitBossRegistered();
        }

        private void OnFactoriesCleared()
        {
            if (_phase != Phase.Dormant) return;
            _phase = Phase.Intro;
            _introTimer = introTime;
            HudSignals.EmitBossEngaged(BossName, 2); // 2 phases -> HUD bar shows the 50% segment
            HudSignals.EmitBossHealth(1f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            switch (_phase)
            {
                case Phase.Intro: TickIntro(dt); break;
                case Phase.Fight: TickFight(dt); break;
            }
            ApplyGravity(dt);
        }

        private void TickIntro(float dt)
        {
            FaceTarget();
            _introTimer -= dt;
            if (_introTimer <= 0f) _phase = Phase.Fight;
        }

        private void TickFight(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            _brain.Tick(dt, _health.Normalized);
            if (_brain.JustEntered) OnEnterPhase(_brain.Current);

            float speedScale = _brain.Enraged ? BossTuning.EnrageMoveScale : 1f;
            switch (_brain.Current)
            {
                case BossAction.Reposition: Reposition(dt, speedScale); FaceTarget(); break;
                case BossAction.ChargeWindup: FaceTarget(); break;
                case BossAction.Charge: DoCharge(dt, speedScale); break;
                case BossAction.Recover: break;
            }

            // Enrage overlay: rain blades around Max on top of the charge cycle.
            if (_brain.Enraged)
            {
                _bladeTimer -= dt;
                if (_bladeTimer <= 0f) { _bladeTimer = BossTuning.BladeInterval; RainBlades(); }
            }
        }

        private void OnEnterPhase(BossAction action)
        {
            switch (action)
            {
                case BossAction.ChargeWindup:
                    Vector3 to = PlanarToTarget();
                    _chargeDir = to.sqrMagnitude > 0.001f ? to.normalized : transform.forward;
                    SetTell(windupColor);
                    break;
                case BossAction.Charge:
                    _contactThisCharge = false;
                    _grassTimer = 0f;
                    SetTell(windupColor);
                    break;
                default:
                    SetTell(idleColor);
                    break;
            }
        }

        private void Reposition(float dt, float speedScale)
        {
            Vector3 to = PlanarToTarget();
            float dist = to.magnitude;
            if (dist < 0.1f) return;
            Vector3 dir = to.normalized;
            // Approach until at desiredRange, then hold — keeps the boss circling, not hugging.
            if (dist > BossTuning.DesiredRange + 0.5f) _cc.Move(dir * BossTuning.MoveSpeed * speedScale * dt);
            else if (dist < BossTuning.DesiredRange - 0.5f) _cc.Move(-dir * BossTuning.MoveSpeed * speedScale * dt);
        }

        private void DoCharge(float dt, float speedScale)
        {
            _cc.Move(_chargeDir * BossTuning.ChargeSpeed * speedScale * dt);

            _grassTimer -= dt;
            if (_grassTimer <= 0f)
            {
                _grassTimer = BossTuning.GrassInterval;
                DamageZone.Spawn(transform.position, BossTuning.GrassRadius, BossTuning.GrassDamage, BossTuning.GrassLife,
                                 BossTuning.GrassArm, grassColor);
            }

            if (!_contactThisCharge && PlanarToTarget().magnitude <= BossTuning.ChargeContactRadius
                && _target.TryGetComponent<IDamageable>(out var d) && d.IsAlive)
            {
                _contactThisCharge = true;
                d.TakeDamage(new DamageInfo(BossTuning.ChargeContactDamage, transform.position, _chargeDir, Team.Enemy));
            }
        }

        private void RainBlades()
        {
            if (_target == null) return;
            Vector3 c = _target.position;
            for (int i = 0; i < BossTuning.BladeCount; i++)
            {
                Vector2 off = Random.insideUnitCircle * BossTuning.BladeSpread;
                Vector3 pos = new Vector3(c.x + off.x, 1f, c.z + off.y);

                // The arm delay is the tell. It was 0.55 s on a zone that then bit three times over
                // 1.2 s of life — 36 damage from one blade, three of them every 1.4 s, on top of the
                // charges. Now it warns for the best part of a second and bites twice at most (YT-94).
                DamageZone.Spawn(pos, BossTuning.BladeRadius, BossTuning.BladeDamage, BossTuning.BladeLife,
                                 BossTuning.BladeArm, bladeColor);
            }
        }

        // --- IDamageable ---
        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return; // invulnerable until engaged; nothing after death
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            HudSignals.EmitDamage(transform.position + Vector3.up * 2.5f, info.Amount);
            _health.TakeDamage(info.Amount);
            HudSignals.EmitBossHealth(_health.Normalized);
            SetTell(Color.white); // brief hit flash; next phase tick restores
        }

        private void OnDeath()
        {
            _phase = Phase.Dead;
            HudSignals.EmitBossHealth(0f);
            HudSignals.EmitBossDefeated();
            HudSignals.EmitPickup(transform.position + Vector3.up * 2.5f, "RARE SHARD", new Color(0.5f, 0.85f, 1f));
            // The death spectacle hangs off the BossDefeated signal (BossSpectacle, YT-55).
            gameObject.SetActive(false);
        }

        private void AcquireTarget()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _target = p.transform;
        }

        private Vector3 PlanarToTarget()
        {
            if (_target == null) return Vector3.zero;
            Vector3 to = _target.position - transform.position;
            to.y = 0f;
            return to;
        }

        private void FaceTarget()
        {
            Vector3 to = PlanarToTarget();
            if (to.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        private void ApplyGravity(float dt)
        {
            if (_cc == null || !_cc.enabled) return;
            if (_cc.isGrounded && _verticalVel < 0f) _verticalVel = -2f;
            _verticalVel -= Gravity * dt;
            _cc.Move(Vector3.up * _verticalVel * dt);
        }

        private void SetTell(Color c)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", c * 0.4f);
            _renderer.SetPropertyBlock(_mpb);
        }

    }
}
