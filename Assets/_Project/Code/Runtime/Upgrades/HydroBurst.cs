using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The Hydro burst (YT-215). Assembling the harness + condenser
    /// (<see cref="UpgradeState.HydroAssembled"/>) used to untether Max from the taps FOREVER — the
    /// permanent state deleted the leash's tension for the rest of the run, which was the boring
    /// late-game walkover. Now assembly only unlocks a burst button: pressing it frees Max, self-supplied
    /// (same power-cell draw as before, see <c>WaterBlaster.HydroActive</c>), for a short window; then
    /// the leash snaps back and the button goes on a cooldown. A resource, not a state.
    ///
    /// Static for the same reason as <see cref="UpgradeState"/> — one Max, several systems (the tether,
    /// the blaster, the HUD button) all reading the same clock without a reference threaded around.
    /// <see cref="Tick"/> must be called once a frame (by <see cref="MaxWorlds.Hose.HoseTether"/>, which
    /// already owns the leash and reads <see cref="Active"/> every frame) to advance the state machine.
    /// </summary>
    public static class HydroBurst
    {
        /// <summary>Authored burst length, seconds, before any dev override.</summary>
        public const float AuthoredSeconds = 10f;

        /// <summary>Authored cooldown after a burst ends before it can fire again, seconds.</summary>
        public const float AuthoredCooldown = 30f;

        /// <summary>Effective burst length this run — the DevTuning/Settings knob may be overriding it.</summary>
        public static float Seconds => DevTuning.Or(DevTuning.HydroBurstSeconds, AuthoredSeconds);

        /// <summary>Effective cooldown this run — the DevTuning/Settings knob may be overriding it.</summary>
        public static float Cooldown => DevTuning.Or(DevTuning.HydroBurstCooldown, AuthoredCooldown);

        private static float s_remaining;           // > 0 while bursting
        private static float s_cooldownRemaining;   // > 0 while cooling down, after a burst ends

        /// <summary>True while Max is free of the tap on his own hydro supply.</summary>
        public static bool Active => s_remaining > 0f;

        /// <summary>Seconds left in the current burst (0 when not bursting) — the HUD countdown.</summary>
        public static float RemainingSeconds => s_remaining;

        /// <summary>0 (ready) .. 1 (just spent) — the HUD button's cooldown wipe.</summary>
        public static float CooldownNormalized =>
            Cooldown > 0f ? Mathf.Clamp01(s_cooldownRemaining / Cooldown) : 0f;

        /// <summary>True when the button can be pressed: Hydro assembled, not already bursting, and off
        /// cooldown. The HUD button only appears at all once <see cref="UpgradeState.HydroAssembled"/>.</summary>
        public static bool Ready => UpgradeState.HydroAssembled && s_remaining <= 0f && s_cooldownRemaining <= 0f;

        /// <summary>Press the button. No-op if not <see cref="Ready"/> (assembly missing, already
        /// bursting, or still cooling down) — a kid mashing it can't double-dip or skip the cooldown.</summary>
        public static void Trigger()
        {
            if (!Ready) return;
            s_remaining = Seconds;
        }

        /// <summary>Advance the state machine one frame: burns the active burst down first, then starts
        /// the cooldown the instant it ends (the leash snap-back), then burns the cooldown down.</summary>
        public static void Tick(float dt)
        {
            if (s_remaining > 0f)
            {
                s_remaining = Mathf.Max(0f, s_remaining - dt);
                if (s_remaining <= 0f) s_cooldownRemaining = Cooldown;
            }
            else if (s_cooldownRemaining > 0f)
            {
                s_cooldownRemaining = Mathf.Max(0f, s_cooldownRemaining - dt);
            }
        }

        /// <summary>Drop everything (new run / test isolation) — a fresh run must not inherit a
        /// cooldown or an in-progress burst from whatever came before.</summary>
        public static void Reset()
        {
            s_remaining = 0f;
            s_cooldownRemaining = 0f;
        }
    }
}
