using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Cooldown model for one ability slot (YT-30 HUD). Pure logic (no MonoBehaviour)
    /// so the radial-wipe maths is unit-testable. <see cref="Trigger"/> puts the slot
    /// on cooldown; <see cref="Tick"/> advances it; <see cref="RadialFill"/> drives the
    /// cooldown wipe overlay (1 → just triggered, 0 → ready) and <see cref="Ready"/>
    /// drives the "glowing border when ready" state the spec calls for.
    /// </summary>
    public sealed class AbilityCooldown
    {
        /// <summary>Total cooldown length in seconds. 0 = always ready.</summary>
        public float Cooldown { get; }

        /// <summary>Seconds left before the slot is ready again.</summary>
        public float Remaining { get; private set; }

        public AbilityCooldown(float cooldown)
        {
            Cooldown = Mathf.Max(0f, cooldown);
            Remaining = 0f;
        }

        /// <summary>True when the slot can fire (cooldown elapsed).</summary>
        public bool Ready => Remaining <= 0f;

        /// <summary>
        /// Fraction of the cooldown still to run, 0..1. This is the fill amount for the
        /// radial "wipe" overlay: full circle the instant the ability triggers, emptying
        /// to nothing as it becomes ready again.
        /// </summary>
        public float RadialFill => Cooldown > 0f ? Mathf.Clamp01(Remaining / Cooldown) : 0f;

        /// <summary>Put the slot on cooldown. No-op (stays ready) if already cooling down.</summary>
        /// <returns>True if the trigger fired (was ready), false if it was still cooling.</returns>
        public bool Trigger()
        {
            if (!Ready) return false;
            Remaining = Cooldown;
            return true;
        }

        /// <summary>Advance the cooldown by <paramref name="dt"/> seconds.</summary>
        public void Tick(float dt)
        {
            if (Remaining > 0f) Remaining = Mathf.Max(0f, Remaining - Mathf.Max(0f, dt));
        }
    }
}
