using System;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Player
{
    /// <summary>
    /// Minimal player damage receiver (slice). Implements <see cref="IDamageable"/>
    /// so enemy contact damage (YT-36) has a target. HP binds to the HUD (YT-30) via
    /// <see cref="Normalized"/> + <see cref="Changed"/>.
    ///
    /// Since YT-80 it also trickles back up out of combat (<see cref="Regenerate"/>). Health only
    /// ever fell before, so a scrape in the first factory was carried to the boss, and the run was
    /// effectively decided minutes before it ended. The trickle pays out breathing room you had to
    /// earn by disengaging — and it is far too slow to stand in a pack and out-heal it.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable, IHealthReadout
    {
        [SerializeField] private float maxHealth = 500f;  // MV-658: baked from Lee's 2026-09-02 tuning pass (was 200, MV-315)

        // Regen is authored in PlayerTuning, NOT as [SerializeField]s — a serialized field on a
        // component that lives in Backyard_Slice.unity gets baked into the scene, and the scene then
        // silently outranks the code (YT-80).

        private float _health;
        private float _timeSinceDamage;

        /// <summary>Seconds left on the Pipe Turret's CORRODED status (MV-691) — 0 when not corroded.
        /// Ticked down in <see cref="Update"/>, refreshed (never stacked) by <see cref="ApplyCorroded"/>
        /// while Max stands in a <see cref="MaxWorlds.Enemies.CorrosionPuddle"/>.</summary>
        private float _corrodedTimer;

        public bool IsAlive => _health > 0f;
        public Team Team => Team.Player;

        /// <summary>Effective max HP — the dev tuning panel may be overriding it this session
        /// (YT-105). Everything downstream (regen ceiling, the HUD bar) reads through here, so a
        /// slider move is felt everywhere at once.</summary>
        public float Max => DevTuning.Or(DevTuning.PlayerMaxHealth, maxHealth);

        /// <summary>The authored max, ignoring any dev override — the panel's 100% reference.</summary>
        public float AuthoredMax => maxHealth;

        public float Current => _health;
        public float Normalized => Max > 0f ? _health / Max : 0f;

        /// <summary>Whether Max is currently CORRODED (MV-691) — the Pipe Turret's puddle status.</summary>
        public bool IsCorroded => CorrodedStatus.IsActive(_corrodedTimer);

        /// <summary>Seconds left on the CORRODED status, 0 when inactive — the resolved value a test
        /// reads, never an authored constant.</summary>
        public float CorrodedRemaining => _corrodedTimer;

        /// <summary>What <see cref="TakeDamage"/> actually multiplies an incoming hit by right now —
        /// 1.25x while corroded, 1x otherwise.</summary>
        public float DamageTakenMultiplier => CorrodedStatus.MultiplierFor(_corrodedTimer);

        /// <summary>Marks Max CORRODED for a fresh <see cref="CorrodedStatus.Duration"/> (MV-691) —
        /// called every tick a <see cref="MaxWorlds.Enemies.CorrosionPuddle"/> finds him standing
        /// inside it. Refreshes rather than stacks, same "never shortens, only ever refreshes to the
        /// authored ceiling" idiom as every other timed status in this project.</summary>
        public void ApplyCorroded() => _corrodedTimer = CorrodedStatus.Refresh();

        /// <summary>
        /// Re-settle current HP against a max that just changed underneath it (YT-105). Raising the
        /// ceiling leaves Max where he stood — you get headroom, not a free heal — while lowering it
        /// has to clamp or the bar would read over 100%. Either way the HUD is told, because it
        /// binds <see cref="Changed"/> and would otherwise keep drawing the old fraction.
        /// </summary>
        public void RefreshMax()
        {
            _health = Mathf.Min(_health, Max);
            Changed?.Invoke(_health);
        }

        /// <summary>Fired when HP changes (HUD subscribes). Arg = current HP.</summary>
        public event Action<float> Changed;

        /// <summary>Fired once, the instant HP reaches zero (MV-427) — what a death-continues-the-run
        /// flow hangs off instead of inferring death from <see cref="Changed"/> landing on zero. Not
        /// fired again until a subsequent <see cref="Revive"/> and a fresh fall, since
        /// <see cref="TakeDamage"/> already early-returns while <see cref="IsAlive"/> is false.</summary>
        public event Action Died;

        // --- IHealthReadout (YT-111): what the floating bar over Max reads. ---
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health;
        public string ReadoutName => "MAX";

        /// <summary>Metres above Max's origin his stack floats. His capsule is 2 m tall with its
        /// origin at the centre, so his head is at +1.0 — clears it by the same <c>HeadClearance</c>
        /// margin <see cref="MaxWorlds.Enemies.RobotEnemy"/> gives every robot kind (MV-473), so Max's
        /// class isn't a one-off number nobody can trace back to a rule.</summary>
        private const float BarHeight = 1.35f;
        private const float BarWidth = 2.1f;   // wider than a robot's — it's you; prominence comes from width now (YT-136)
        private static readonly Color WaterColor = new Color(0.20f, 0.62f, 0.92f); // #33A0EB

        private WaterBlaster _blaster;
        private PlayerAbilities _abilities;

        /// <summary>Max's abilities component, for the Force Field absorb hook below — resolved lazily
        /// and cached, same reason/shape as <see cref="WaterNormalized"/>'s <see cref="_blaster"/> read.</summary>
        private PlayerAbilities Abilities
        {
            get
            {
                if (_abilities == null) _abilities = GetComponent<PlayerAbilities>();
                return _abilities;
            }
        }

        private void Awake() => Initialize();

        /// <summary>
        /// Set starting HP and hang the floating status bar. Called from Awake; exposed publicly
        /// (MV-464) so an EditMode test can invoke it directly — Awake never runs as a side effect
        /// of AddComponent outside Play mode.
        /// </summary>
        public void Initialize()
        {
            _health = Max;

            // MV-722: no hit has ever landed yet, so the first one must resolve as an isolated
            // PROJECTILE hit, not CONTACT — see IsContactHit below.
            _timeSinceDamage = float.MaxValue;

            // Max's whole status lives over his head (YT-121): the water gauge stacked directly above
            // the life bar (MV-299, reinstating what MV-290 removed along with the primary's tank).
            WorldHealthBar.Attach(gameObject, this, BarHeight, BarWidth, alwaysShow: true,
                                  secondary: WaterNormalized, secondaryColor: WaterColor,
                                  isPlayerBar: true);
        }

        /// <summary>Max's blaster tank, 0..1, for the floating water gauge. Resolved lazily and
        /// cached — the blaster attaches itself to Max and may not exist on the frame this runs.</summary>
        private float WaterNormalized()
        {
            if (_blaster == null) _blaster = GetComponent<WaterBlaster>();
            return _blaster != null ? _blaster.WaterNormalized : 1f;
        }

        /// <summary>Every current contact-damage cooldown (<see cref="MaxWorlds.Enemies.RobotCompositionTuning.DefaultContactCooldown"/>,
        /// the boss's own <c>BossTuning.ContactCooldown</c>) is 1.0 s, so a repeat hit landing within
        /// this window — from the same attacker's next tick, or a second one in a crowd — still reads
        /// as ongoing contact rather than a fresh isolated strike. The margin above that raw 1.0 s
        /// absorbs frame-timing slack.</summary>
        private const float ContactCadenceSeconds = 1.5f;

        /// <summary>
        /// Which subtle hit effect a landing blow should raise (MV-722): true (CONTACT) when the
        /// previous hit landed less than <see cref="ContactCadenceSeconds"/> ago — the signature a
        /// crowd, a boss's contact tick or a beam/flood produce by hitting repeatedly — false
        /// (PROJECTILE) for a hit landing with nothing recent before it. Pure and static, same
        /// "testable without a live timer" idiom as <see cref="Regenerate"/>, so it can be told apart
        /// at the one site every hit already passes through, without touching any attacker.
        /// </summary>
        public static bool IsContactHit(float timeSinceLastHit) => timeSinceLastHit < ContactCadenceSeconds;

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (DevMode.IsInvincible) return;                      // dev/filming only; off by default (YT-60)
            if (!DamageRules.Applies(info.Attacker, Team)) return; // no friendly fire

            // MV-691: CORRODED amplifies the INCOMING hit itself (20 -> 25 at the ticket's own 1.25x),
            // applied before Force Field ever sees it — the shield still absorbs off the amplified
            // total, not the pre-corrosion one.
            float rawAmount = info.Amount * DamageTakenMultiplier;

            // MV-361: Force Field eats as much of the hit as its remaining budget allows, before HP
            // ever sees it — every damage source funnels through here, so the bubble needs no special
            // case per attacker (contact lunge, beam tick, missile splash all arrive as one DamageInfo).
            float amount = Abilities != null ? Abilities.AbsorbForceFieldDamage(rawAmount) : rawAmount;
            if (amount <= 0f) return;

            // MV-722: read the gap since the PREVIOUS hit before it resets below, so CombatVfx can
            // raise a small directional spark for an isolated hit or a subtler, continuous-feeling
            // read for one arriving on the heels of the last — feedback the health bar alone never gave.
            HudSignals.EmitPlayerHit(info.Point, info.Direction, IsContactHit(_timeSinceDamage));

            _health = Mathf.Max(0f, _health - amount);
            _timeSinceDamage = 0f;
            Changed?.Invoke(_health);
            if (_health <= 0f) Died?.Invoke();
        }

        /// <summary>Bring Max back after a death continues the run (MV-427) — full HP, same shape as a
        /// fresh <see cref="Awake"/>. <see cref="IsAlive"/> flips back true the instant this runs, so
        /// ordinary damage/regen resume on the next frame exactly as if he had never fallen.</summary>
        public void Revive()
        {
            _health = Max;
            _timeSinceDamage = 0f;
            Changed?.Invoke(_health);
        }

        /// <summary>
        /// HP after <paramref name="dt"/> seconds of regen (YT-80). Pure, so the trickle can be
        /// tested without a scene or a clock, per the house rule.
        ///
        /// Never revives a corpse and never overfills: at 0 HP the run is over, and Max is dead
        /// before this is ever reached.
        /// </summary>
        public static float Regenerate(float current, float max, float timeSinceDamage,
                                       float delay, float perSec, float dt)
        {
            if (current <= 0f || current >= max) return current;
            if (timeSinceDamage < delay) return current;
            return Mathf.Min(max, current + Mathf.Max(0f, perSec) * Mathf.Max(0f, dt));
        }

        private void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            _timeSinceDamage += dt;
            _corrodedTimer = CorrodedStatus.Tick(_corrodedTimer, dt);

            float before = _health;
            _health = Regenerate(_health, Max, _timeSinceDamage,
                                 PlayerTuning.RegenDelay, PlayerTuning.RegenPerSec, dt);
            if (!Mathf.Approximately(before, _health)) Changed?.Invoke(_health);
        }
    }
}
