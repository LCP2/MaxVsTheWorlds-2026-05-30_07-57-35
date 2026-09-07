using UnityEngine;
using MaxWorlds.Feel;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-584: shrinks the GameObject it's attached to down to nothing over a fixed duration, then
    /// destroys it. Used only for the Blinker teleport's departure ghost — a temporary body clone left
    /// behind at the point a robot blinked FROM, with no owner left to tick it once the real robot has
    /// already snapped away to its landing point (<see cref="RobotRig"/> disposes of the original the
    /// instant it's pooled, which the ghost must survive independently of).
    ///
    /// MV-684: the hand-rolled accumulate/clamp/lerp is now the <see cref="AnimSequence"/> substrate's
    /// proof consumer — a single linear step over <c>duration</c>, evaluated the same way.
    /// </summary>
    public sealed class TeleportGhostCollapse : MonoBehaviour
    {
        private AnimSequence _sequence;
        private Vector3 _startScale;

        public void Begin(float duration)
        {
            float safeDuration = Mathf.Max(duration, 0.01f);
            _startScale = transform.localScale;
            _sequence = new AnimSequence(new[] { new AnimStep(0f, safeDuration, AnimEase.Linear) });
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>Split out from <see cref="Update"/> so a test can drive explicit dt values instead
        /// of the real, uncontrollable <c>Time.deltaTime</c> (MV-684).</summary>
        private void Advance(float dt)
        {
            _sequence.Tick(dt);
            float u = _sequence.Progress(0);
            transform.localScale = Vector3.Lerp(_startScale, Vector3.zero, u);
            if (_sequence.IsComplete) Destroy(gameObject);
        }
    }
}
