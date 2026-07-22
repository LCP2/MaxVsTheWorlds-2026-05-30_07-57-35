using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
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

        // --- the brood volley: the second attack (YT-157). Runs ALONGSIDE the charge cycle. ---
        private BroodVolley _volley;
        private Transform _addsRoot;                    // world-space, unit scale — NOT under the moving boss
        private readonly List<AddInFlight> _inFlight = new List<AddInFlight>(8);
        private readonly Stack<RobotEnemy> _addPool = new Stack<RobotEnemy>(8);
        private int _liveAdds;                          // landed + chasing; capped by MaxConcurrentAdds
        private Collider[] _playerColliders;

        /// <summary>One robot mid-throw: it is visible but its own logic is switched off, so the boss
        /// drives it along the parabola until it lands and becomes a normal robot.</summary>
        private struct AddInFlight
        {
            public RobotEnemy Robot;
            public Vector3 From;
            public Vector3 To;
            public float T;   // 0..1 along the arc
        }

        public bool IsAlive => _phase == Phase.Fight && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        /// <summary>Re-read the Boss-health slider and retune live (YT-126). Raising it gives
        /// headroom, not a heal; lowering clamps. Pushes the new fraction to the HUD boss bar.</summary>
        public void RefreshMax()
        {
            if (_health == null) return;
            _health.Retune(DevTuning.Or(DevTuning.BossHealth, BossTuning.Health));
            HudSignals.EmitBossHealth(_health.Normalized);
        }

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

        /// <summary>The brood-volley spawn telegraph, 0 shut … 1 flung (YT-157). This is the ONE getter
        /// <see cref="MaxWorlds.VFX.BigBermudaRig"/> reads to open the side hatches — the gameplay says
        /// when the swarm is coming, the rig shows it, and nothing writes back the other way. 0 whenever
        /// the volley is dormant (asleep, intro, between waves, dead).</summary>
        public float SpawnWindup01 => _volley != null ? _volley.SpawnWindup01 : 0f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _health = new DestructibleHealth(DevTuning.Or(DevTuning.BossHealth, BossTuning.Health));
            _health.Destroyed += OnDeath;
            _brain = new BigBermudaBrain();
            _volley = new BroodVolley();
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

            // The second attack (YT-157): the brood volley, on its own cadence beside the charge cycle.
            TickVolley(dt);
            AdvanceAdds(dt);
        }

        /// <summary>
        /// The brood volley — the boss's signature side-hatch add-spawner (YT-157). The pure
        /// <see cref="BroodVolley"/> owns the cadence and the telegraph; this executes the fling on its
        /// <see cref="BroodVolley.JustFired"/> edge, the same shape as the blade rain above.
        ///
        /// The volley is vetoed (<c>canVent = false</c>) while it is committing to a charge — so the
        /// spawn read and the charge read never overlap — and while the arena already holds its cap of
        /// adds, which is the whole kiteability guarantee for a fight where no factory is left to bound
        /// the robot count.
        /// </summary>
        private void TickVolley(float dt)
        {
            bool committing = _brain.Current == BossAction.ChargeWindup || _brain.Current == BossAction.Charge;
            bool enraged = _brain.Enraged;
            bool phaseAllows = enraged || BossTuning.VolleyFiresBeforeEnrage;

            int maxAdds = Mathf.Max(0, Mathf.RoundToInt(DevTuning.Or(DevTuning.BossMaxAdds, BossTuning.MaxConcurrentAdds)));
            int onField = _liveAdds + _inFlight.Count;

            bool canVent = !committing && phaseAllows && onField < maxAdds;
            _volley.Tick(dt, enraged, canVent);

            if (_volley.JustFired)
            {
                int want = _volley.RobotsThisVolley(enraged);
                LaunchVolley(Mathf.Min(want, Mathf.Max(0, maxAdds - onField)));
            }
        }

        /// <summary>Throw <paramref name="count"/> robots out of the side hatches, alternating flanks so
        /// both hatches disgorge and the wave fans out rather than stacking. Each starts at a hatch
        /// mouth and begins its arc; it is not a live robot yet — see <see cref="AdvanceAdds"/>.</summary>
        private void LaunchVolley(int count)
        {
            if (count <= 0) return;

            Vector3 pos = transform.position;
            Quaternion facing = transform.rotation;
            EnemyArchetype archetype = EnemyArchetype.Rusher;   // "reuse the standard robot" (YT-157)

            for (int i = 0; i < count; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;   // L, R, L, R…
                float spread = (i / 2) * BossTuning.VolleyLandingSpread;

                Vector3 from = BroodArc.Muzzle(pos, facing, side,
                    BossTuning.HatchMuzzleSide, BossTuning.HatchMuzzleHeight);
                Vector3 to = BroodArc.Landing(pos, facing, side,
                    BossTuning.VolleyLandingSide, BossTuning.VolleyLandingForward,
                    archetype.SpawnHeight, spread);

                RobotEnemy add = TakeAdd(archetype);
                add.transform.position = from;
                add.gameObject.SetActive(true);   // VISIBLE for the throw…
                add.enabled = false;              // …but its own chase/gravity is off while the boss flies it
                _inFlight.Add(new AddInFlight { Robot = add, From = from, To = to, T = 0f });
            }
        }

        /// <summary>Fly every in-flight add one step along its parabola. On landing it re-enables the
        /// robot — <see cref="RobotEnemy"/>'s OnEnable resets it into Chase and it self-acquires Max — and
        /// lets the player walk through it, so from that instant it is an ordinary robot.</summary>
        private void AdvanceAdds(float dt)
        {
            if (_inFlight.Count == 0) return;
            float arcTime = Mathf.Max(0.05f, BossTuning.VolleyArcTime);

            for (int i = _inFlight.Count - 1; i >= 0; i--)
            {
                AddInFlight a = _inFlight[i];
                if (a.Robot == null) { _inFlight.RemoveAt(i); continue; }

                a.T += dt / arcTime;
                if (a.T >= 1f)
                {
                    a.Robot.transform.position = a.To;
                    Vector3 outward = a.To - a.From; outward.y = 0f;
                    if (outward.sqrMagnitude > 0.0001f)
                        a.Robot.transform.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);

                    a.Robot.enabled = true;   // OnEnable -> ResetState -> Chase, full health, acquires Max
                    LetThePlayerThrough(a.Robot.gameObject);
                    _liveAdds++;
                    _inFlight.RemoveAt(i);
                }
                else
                {
                    a.Robot.transform.position = BroodArc.PointAt(a.From, a.To, BossTuning.VolleyArcApex, a.T);
                    _inFlight[i] = a;   // struct — write the advanced T back
                }
            }
        }

        private RobotEnemy TakeAdd(in EnemyArchetype archetype)
            => _addPool.Count > 0 ? _addPool.Pop() : CreateAdd(archetype);

        /// <summary>Build one greybox add, sized exactly the way <see cref="EnemySpawner"/> builds a
        /// factory robot (YT-74 metre-space collider un-scaling) but parented to the boss's own adds
        /// root rather than a factory. Born inactive; a volley activates it on landing.</summary>
        private RobotEnemy CreateAdd(in EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"Brood Add {a.Kind}";
            go.transform.SetParent(AddsRoot(), false);
            go.transform.localScale = a.BodyScale;

            var cc = go.AddComponent<CharacterController>();
            float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
            cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
            cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
            cc.center = Vector3.zero;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(a);
            e.Died += OnAddDied;
            e.gameObject.SetActive(false);
            return e;
        }

        private void OnAddDied(RobotEnemy e)
        {
            _liveAdds = Mathf.Max(0, _liveAdds - 1);
            _addPool.Push(e);   // back to the pool, reused on the next volley — no GC churn
        }

        /// <summary>The container the adds live in. Top-level and unit-scaled ON PURPOSE: the boss MOVES,
        /// and adds parented under it would be dragged across the arena as it charges. It also cancels
        /// nothing (scale 1), so the robots are authored in metres, straight.</summary>
        private Transform AddsRoot()
        {
            if (_addsRoot == null) _addsRoot = new GameObject("Brood Adds").transform;
            return _addsRoot;
        }

        /// <summary>Adds must never body-block Max any more than factory robots do (YT-74). Re-applied on
        /// every landing because Unity drops an ignored pair when the collider is toggled.</summary>
        private void LetThePlayerThrough(GameObject enemy)
        {
            if (_playerColliders == null || _playerColliders.Length == 0)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _playerColliders = p.GetComponents<Collider>();
            }

            foreach (var ec in enemy.GetComponents<Collider>())
            {
                if (ec == null) continue;
                foreach (var pc in _playerColliders)
                    if (pc != null) Physics.IgnoreCollision(ec, pc, true);
            }
        }

        private void OnDestroy()
        {
            // The adds root is ours and outlives nothing — tear it (and every add under it) down with the
            // boss so a torn-down fight leaves no robots pathing after a player who is gone.
            if (_addsRoot != null) Destroy(_addsRoot.gameObject);
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
            // Reposition speed only — the charge stays on the authored number, because the charge is
            // a telegraphed attack whose dodge window is timed against it (YT-105).
            float move = DevTuning.Or(DevTuning.BossMoveSpeed, BossTuning.MoveSpeed);
            // Approach until at desiredRange, then hold — keeps the boss circling, not hugging.
            if (dist > BossTuning.DesiredRange + 0.5f) _cc.Move(dir * move * speedScale * dt);
            else if (dist < BossTuning.DesiredRange - 0.5f) _cc.Move(-dir * move * speedScale * dt);
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
