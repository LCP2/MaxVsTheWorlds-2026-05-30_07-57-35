using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Animation state for a HUD bar (YT-54): a fill that eases toward its value, and a "ghost"
    /// that lags behind it after a hit.
    ///
    /// The ghost is the point. A bar that snaps to its new value tells you nothing about what just
    /// happened — you see the result, not the event. Holding the old value for a beat and then
    /// draining it shows you *how much you just lost*, which is the information the player actually
    /// wants. It's the standard fighting-game chip-damage bar.
    ///
    /// Plain C#, driven by an explicit dt, so it is unit-testable with no canvas and no clock.
    /// </summary>
    public sealed class BarState
    {
        /// <summary>The main fill, 0..1.</summary>
        public float Fill { get; private set; } = 1f;

        /// <summary>The trailing "damage taken" fill, 0..1. Always >= <see cref="Fill"/>.</summary>
        public float Ghost { get; private set; } = 1f;

        /// <summary>0..1, decaying — drives a white flash on the bar the instant it drops.</summary>
        public float Flash { get; private set; }

        private float _holdLeft;

        /// <param name="fillSpeed">Fill units per second (1 = a full bar per second).</param>
        /// <param name="ghostSpeed">How fast the ghost drains once it lets go.</param>
        /// <param name="hold">Seconds the ghost holds the old value before draining.</param>
        public void Update(float target, float dt, float fillSpeed = 3.5f,
                           float ghostSpeed = 0.85f, float hold = 0.35f, float flashDecay = 4f)
        {
            target = Mathf.Clamp01(target);

            bool dropped = target < Fill - 1e-4f;
            if (dropped)
            {
                _holdLeft = hold;
                Flash = 1f;
            }

            Fill = Mathf.MoveTowards(Fill, target, fillSpeed * dt);

            if (_holdLeft > 0f) _holdLeft -= dt;
            else Ghost = Mathf.MoveTowards(Ghost, Fill, ghostSpeed * dt);

            // Healing (or a refill) must not leave a ghost stranded *below* the fill, which would
            // render as an inverted chip bar.
            if (Ghost < Fill) Ghost = Fill;

            Flash = Mathf.Max(0f, Flash - flashDecay * dt);
        }

        /// <summary>Jump straight to a value with no animation (initial bind, respawn).</summary>
        public void Snap(float value)
        {
            Fill = Ghost = Mathf.Clamp01(value);
            Flash = 0f;
            _holdLeft = 0f;
        }
    }
}
