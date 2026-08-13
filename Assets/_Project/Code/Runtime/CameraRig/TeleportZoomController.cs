using System.Collections;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.CameraRig
{
    /// <summary>
    /// Zooms the camera out while Teleport is being aimed, if the ability's blink range would land
    /// outside what's currently on screen (MV-371 parts 1-2). Listens on
    /// <see cref="HudSignals.TeleportAimStarted"/> / <see cref="HudSignals.TeleportAimEnded"/> rather
    /// than touching <c>TeleportJoystickControl</c> directly — the same decoupled hand-off
    /// <see cref="MaxWorlds.Feel.GameFeel"/> and <c>CombatVfx</c> already use for Max's teleport
    /// (MV-338), and it self-installs the same way <see cref="MaxWorlds.Feel.GameFeel"/> does, so no
    /// scene wiring is needed (per <c>docs/CODE_DRIVEN_SCENES.md</c>).
    ///
    /// Runs the lerp on UNSCALED time deliberately: MV-338's brief slow-mo (<c>GameFeel</c>'s
    /// <c>HitStop</c>, via <c>Time.timeScale</c>) fires on the very release that should also snap the
    /// zoom back in, and a lerp riding scaled time would itself crawl at exactly the wrong moment.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TeleportZoomController : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<TeleportZoomController>() != null) return;
            new GameObject("TeleportZoomController").AddComponent<TeleportZoomController>();
        }

        /// <summary>How much further out than the bare-minimum fit distance to pull, so the landing
        /// point isn't pinned to the very edge of frame (Lee: "a bit more so I can see where I'm
        /// going").</summary>
        public const float MarginFraction = 0.15f;

        /// <summary>Real seconds for the zoom in/out lerp — quick per AC4, not a slow creep.</summary>
        public const float ZoomSeconds = 0.18f;

        private FixedAngleCameraRig _rig;

        /// <summary>The distance to return to once aiming ends. Negative = we don't currently own the
        /// camera's distance — <see cref="FixedAngleCameraRig.Distance"/> is the truth.</summary>
        private float _restDistance = -1f;

        private Coroutine _running;

        private void OnEnable()
        {
            HudSignals.TeleportAimStarted += OnAimStarted;
            HudSignals.TeleportAimEnded += OnAimEnded;
        }

        private void OnDisable()
        {
            HudSignals.TeleportAimStarted -= OnAimStarted;
            HudSignals.TeleportAimEnded -= OnAimEnded;

            if (_running != null) { StopCoroutine(_running); _running = null; }
            if (_restDistance >= 0f && _rig != null) _rig.SetDistance(_restDistance);
            _restDistance = -1f;
        }

        private FixedAngleCameraRig Rig()
        {
            if (_rig == null) _rig = FindFirstObjectByType<FixedAngleCameraRig>();
            return _rig;
        }

        private void OnAimStarted(float maxRangeMetres)
        {
            var rig = Rig();
            var cam = Camera.main;
            if (rig == null || cam == null) return;

            // If a return-to-rest is already in flight, _restDistance still holds the TRUE resting
            // point — reuse it rather than re-capturing rig.Distance mid-lerp, or repeated fast
            // press/release cycles would drift the remembered rest distance toward the zoomed value.
            float rest = _restDistance >= 0f ? _restDistance : rig.Distance;

            // AC2: judge "does the range already fit" against the RAW range at the resting framing —
            // the margin below is extra headroom for the zoomed-out shot, not part of the trigger, or
            // a range that just grazes the edge of frame would zoom for a barely-there sliver.
            float restSafeRadius = TeleportZoomFraming.SafeVisibleRadius(
                rest, rig.Pitch, cam.fieldOfView, cam.aspect);
            if (maxRangeMetres <= restSafeRadius) return; // already fits — no zoom at all

            float desiredRadius = maxRangeMetres * (1f + MarginFraction);
            float target = TeleportZoomFraming.DistanceForVisibleRadius(
                desiredRadius, rig.Pitch, cam.fieldOfView, cam.aspect);
            target = Mathf.Clamp(target, FixedAngleCameraRig.MinDistance, FixedAngleCameraRig.MaxDistance);

            _restDistance = rest;
            StartLerp(rig, target, isReturning: false);
        }

        private void OnAimEnded()
        {
            if (_restDistance < 0f) return; // never zoomed for this hold — nothing to undo
            var rig = Rig();
            if (rig == null) { _restDistance = -1f; return; }
            StartLerp(rig, _restDistance, isReturning: true);
        }

        private void StartLerp(FixedAngleCameraRig rig, float target, bool isReturning)
        {
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(LerpDistance(rig, target, isReturning));
        }

        private IEnumerator LerpDistance(FixedAngleCameraRig rig, float target, bool isReturning)
        {
            float start = rig.Distance;
            float t = 0f;
            while (t < ZoomSeconds)
            {
                t += Time.unscaledDeltaTime;
                rig.SetDistance(Mathf.Lerp(start, target, Mathf.Clamp01(t / ZoomSeconds)));
                yield return null;
            }
            rig.SetDistance(target);
            _running = null;
            if (isReturning) _restDistance = -1f;
        }
    }
}
