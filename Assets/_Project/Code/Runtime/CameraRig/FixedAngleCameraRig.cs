using UnityEngine;
using Unity.Cinemachine;

namespace MaxWorlds.CameraRig
{
    /// <summary>
    /// Sets the fixed-angle camera's follow distance in code (YT-46). The ~72° top-down pitch
    /// is load-bearing and stays fixed; this only controls how far back/up the rig sits, driven
    /// by one tunable <see cref="cameraDistance"/> (metres along the view ray) so the framing is
    /// easy to nudge without touching the scene. Applied to the <see cref="CinemachineFollow"/>
    /// offset on Awake, so a fresh clone / the WebGL build always uses the committed value.
    ///
    /// Pulled back from the original ~13.7 m after playtest feedback that it felt too zoomed in, and
    /// again to 25.1 m for YT-82 — the yard was reading as a corridor you were stuck inside rather
    /// than an arena you could move around and read. 25.1 is √1.5 × 20.5, i.e. exactly half again
    /// the visible ground (see <see cref="CameraFraming"/>); it is a starting value, and the whole
    /// point of the dev-mode nudge keys is that Lee sets the final one by eye.
    /// </summary>
    public sealed class FixedAngleCameraRig : MonoBehaviour
    {
        /// <summary>Bounds for the live nudge. Not taste — sanity: closer than the low end and the
        /// near clip starts eating Max, past the high end he's an ant in a wide shot.</summary>
        public const float MinDistance = 12f;
        public const float MaxDistance = 45f;

        /// <summary>Phone-class default (YT-106): the tighter framing Lee dialed in on-device. Only
        /// phones use it — desktop keeps the serialized wide value, because on a monitor the wider
        /// shot read fine (if anything a touch zoomed-in). The panel reads whichever default this
        /// device ends up with, so "Reset to defaults" returns to the right one per device.</summary>
        public const float PhoneDistance = 23f;

        /// <summary>Test seam: force the device class. Null = ask the platform.</summary>
        public static bool? SimulatePhoneClass;

        /// <summary>True on a handheld build (iOS/Android/TestFlight, and a mobile WebGL browser).</summary>
        public static bool IsPhoneClass => SimulatePhoneClass ?? Application.isMobilePlatform;

        [Tooltip("Fixed top-down pitch. Load-bearing for the AI-art pipeline — do NOT change (YT-33).")]
        [SerializeField] private float pitchDegrees = 72f;

        [Tooltip("Distance from the follow target to the camera, in metres. Bigger = more arena " +
                 "visible around Max. THE zoom knob (YT-82) — nudge it live in dev mode with [ and ], " +
                 "read the number off the dev overlay, then commit it here. Keep the pitch fixed.")]
        [SerializeField] private float cameraDistance = 25.1f;

        /// <summary>Current pull-back, in metres. Read by the dev overlay so the number Lee dials in
        /// by eye is the number he can paste back into the field above.</summary>
        public float Distance => cameraDistance;

        /// <summary>Fixed pitch, degrees. Exposed read-only so a test can prove YT-82 left it alone.</summary>
        public float Pitch => pitchDegrees;

        /// <summary>
        /// Move the camera in or out by <paramref name="delta"/> metres and re-apply immediately
        /// (YT-82's live zoom). Clamped, and it never touches the pitch — the angle is load-bearing
        /// for the art pipeline, so the one thing this knob must not be able to do is tilt the
        /// camera.
        /// </summary>
        public void Nudge(float delta) => SetDistance(cameraDistance + delta);

        /// <summary>
        /// Set the pull-back directly, clamped (YT-105's tuning panel slider). Same contract as
        /// <see cref="Nudge"/> — it cannot touch the pitch — so the two ways of dialling the framing
        /// share one clamp and one apply, and the slider and the bracket keys agree.
        /// </summary>
        public void SetDistance(float metres)
        {
            cameraDistance = Mathf.Clamp(metres, MinDistance, MaxDistance);
            Apply();
        }

        private void Awake()
        {
            // Per-device-class default (YT-106): a phone sits closer than the desktop framing this
            // field was authored for. Done here, before the first Apply, so the value the tuning
            // panel captures as its 100% reference is already the per-device one.
            if (IsPhoneClass) cameraDistance = PhoneDistance;
            Apply();
        }

        /// <summary>Push the current distance/pitch to the Cinemachine follow offset + vcam pitch.</summary>
        public void Apply()
        {
            transform.rotation = Quaternion.Euler(pitchDegrees, 0f, 0f); // keep the fixed angle
            if (TryGetComponent<CinemachineFollow>(out var follow))
            {
                follow.FollowOffset = ComputeOffset(cameraDistance, pitchDegrees);
            }
        }

        /// <summary>
        /// Follow offset for a camera <paramref name="distance"/> metres from the target at a
        /// downward <paramref name="pitchDegrees"/> pitch: up by distance·sin(pitch), back by
        /// distance·cos(pitch). Scaling distance keeps the pitch (height:back ratio = tan pitch)
        /// exactly fixed. Pure + unit-testable.
        /// </summary>
        public static Vector3 ComputeOffset(float distance, float pitchDegrees)
        {
            float rad = pitchDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, distance * Mathf.Sin(rad), -distance * Mathf.Cos(rad));
        }
    }
}
