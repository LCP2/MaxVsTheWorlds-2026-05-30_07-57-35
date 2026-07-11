using System.Collections;
using UnityEngine;

namespace MaxWorlds.Feel
{
    /// <summary>
    /// Micro time-freeze on impact (YT-52) — the "hit-stop" that makes a blow land.
    ///
    /// This is the one piece of the feel pass that touches global state (<c>Time.timeScale</c>),
    /// so it is deliberately paranoid about giving it back:
    ///
    /// * It refuses to start while the game is already paused. <see cref="MaxWorlds.UI.ResultScreen"/>
    ///   pauses with <c>timeScale = 0</c>; starting a hit-stop then would be meaningless.
    /// * It only restores time if it still *owns* the timescale. If the run ends mid-hit-stop and
    ///   the result screen pauses, a naive restore-to-1 would silently un-pause the result screen
    ///   and let the game keep playing behind the card.
    ///
    /// Everything waits on unscaled time, since scaled time is exactly what's frozen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStop : MonoBehaviour
    {
        private float _appliedScale = -1f;   // the timescale WE set; -1 = not currently stopping
        private Coroutine _running;

        /// <summary>True while a hit-stop is in effect.</summary>
        public bool IsStopping => _running != null;

        /// <summary>
        /// Freeze for <paramref name="seconds"/> of real time at <paramref name="scale"/>.
        /// A stronger request overrides a weaker one in flight; a weaker one is ignored, so a kill
        /// during a factory explosion doesn't shorten the explosion's stop.
        /// </summary>
        public void Request(float seconds, float scale)
        {
            if (seconds <= 0f) return;
            if (Time.timeScale == 0f && !IsStopping) return;   // game is paused — leave it alone

            scale = Mathf.Clamp(scale, 0.01f, 1f);

            if (IsStopping)
            {
                if (scale >= _appliedScale) return;            // already stopping at least this hard
                StopCoroutine(_running);
            }

            _running = StartCoroutine(Freeze(seconds, scale));
        }

        private IEnumerator Freeze(float seconds, float scale)
        {
            _appliedScale = scale;
            Time.timeScale = scale;

            yield return new WaitForSecondsRealtime(seconds);

            Release();
        }

        private void Release()
        {
            // Only hand time back if nothing else has claimed it since (e.g. the result screen
            // pausing the run). Otherwise we'd be un-pausing someone else's pause.
            if (Mathf.Approximately(Time.timeScale, _appliedScale))
            {
                Time.timeScale = 1f;
            }
            _appliedScale = -1f;
            _running = null;
        }

        private void OnDisable()
        {
            // Never leave the game frozen because this object went away mid-stop.
            if (IsStopping)
            {
                StopCoroutine(_running);
                Release();
            }
        }
    }
}
