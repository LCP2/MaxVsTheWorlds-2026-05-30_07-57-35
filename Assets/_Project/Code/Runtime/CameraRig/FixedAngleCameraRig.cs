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
    ///
    /// MV-276 then dialled it 10% closer/tighter ("110% zoom") from that 25.1 m — both the desktop
    /// and phone defaults now sit at 1/1.1 of their previous distance; see <see cref="ZoomFactor"/>.
    ///
    /// MV-315 re-baked the desktop/WebGL default again, to 108% of the MV-276 number — Lee's
    /// dialled-in tuning-panel value from the 0.6.2 WebGL playtest. Phone is untouched.
    /// </summary>
    public sealed class FixedAngleCameraRig : MonoBehaviour
    {
        /// <summary>Bounds for the live nudge. Not taste — sanity: closer than the low end and the
        /// near clip starts eating Max, past the high end he's an ant in a wide shot.</summary>
        public const float MinDistance = 12f;
        public const float MaxDistance = 45f;

        /// <summary>Bounds for the live pitch nudge (MV-450). Not taste — sanity: below the low end
        /// is nearer side-on than this game has ever been art-directed for, above the high end is
        /// back to the shipped 72° with no room left to explore.</summary>
        public const float MinPitch = 45f;
        public const float MaxPitch = 80f;

        /// <summary>The committed distances before MV-276's zoom bump — kept as named baselines so
        /// the "110% zoom" claim is checkable arithmetic instead of a remembered pair of numbers.
        /// MV-315 scaled the desktop baseline itself (25.1 -> 27.108, i.e. 108%) so the derived
        /// desktop default stays this one line to change.</summary>
        private const float PreZoomDesktopDistance = 27.108f;
        private const float PreZoomPhoneDistance = 16.1f;

        /// <summary>MV-276 tuning: "110% zoom" reads as 10% closer/tighter, i.e. both device
        /// defaults sit at 1/1.1 of their previous distance. Holds the pitch fixed like every other
        /// knob on this rig.</summary>
        public const float ZoomFactor = 1.1f;

        /// <summary>Phone-class default (YT-106, re-baked YT-200 — was 23, retuned MV-276): the tighter
        /// framing Lee dialed in on-device. Only phones use it — desktop keeps the serialized wide value,
        /// because on a monitor the wider shot read fine (if anything a touch zoomed-in). The panel reads
        /// whichever default this device ends up with, so "Reset to defaults" returns to the right
        /// one per device.</summary>
        public const float PhoneDistance = PreZoomPhoneDistance / ZoomFactor;

        /// <summary>Test seam: force the device class. Null = ask the platform.</summary>
        public static bool? SimulatePhoneClass;

        /// <summary>True on a handheld build (iOS/Android/TestFlight, and a mobile WebGL browser).</summary>
        public static bool IsPhoneClass => SimulatePhoneClass ?? Application.isMobilePlatform;

        [Tooltip("Fixed top-down pitch. Load-bearing for the AI-art pipeline — do NOT change this " +
                 "SERIALIZED default (YT-33). MV-450 adds a dev-only live nudge (SetPitch/NudgePitch) " +
                 "that changes this at runtime for a session but never rewrites the committed scene/asset value.")]
        [SerializeField] private float pitchDegrees = 72f;

        [Tooltip("Distance from the follow target to the camera, in metres. Bigger = more arena " +
                 "visible around Max. THE zoom knob (YT-82) — nudge it live in dev mode with [ and ], " +
                 "read the number off the dev overlay, then commit it here. Keep the pitch fixed.")]
        [SerializeField] private float cameraDistance = PreZoomDesktopDistance / ZoomFactor;

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

        /// <summary>
        /// Nudge the dev-mode pitch knob (MV-450) by <paramref name="delta"/> degrees.
        /// </summary>
        public void NudgePitch(float delta) => SetPitch(pitchDegrees + delta);

        /// <summary>
        /// Set the pitch directly, clamped to <see cref="MinPitch"/>/<see cref="MaxPitch"/>
        /// (MV-450's dev-only tuning control — the shipped 72° default and the two
        /// <c>CameraFramingTests</c> assertions pinning it are untouched, this only lets Lee sweep the
        /// angle live to judge one by eye before a second ticket bakes it).
        ///
        /// Holds the visible ground area constant: at a lower pitch the same distance shows less
        /// depth, so changing the angle without also moving the camera would make Lee judge pitch and
        /// zoom at once and be unable to tell which one he's reacting to. <see cref="Camera.main"/>'s
        /// FOV/aspect feed the same <see cref="TeleportZoomFraming"/> maths
        /// <see cref="TeleportZoomController"/> already trusts for the same job; with no camera to ask
        /// (e.g. a bare test rig) the pitch still moves, just without the area correction.
        /// </summary>
        public void SetPitch(float degrees)
        {
            float newPitch = Mathf.Clamp(degrees, MinPitch, MaxPitch);
            var cam = Camera.main;
            if (cam != null && !Mathf.Approximately(newPitch, pitchDegrees))
            {
                float targetDistance = DistanceHoldingVisibleArea(
                    cameraDistance, pitchDegrees, newPitch, cam.fieldOfView, cam.aspect);
                cameraDistance = Mathf.Clamp(targetDistance, MinDistance, MaxDistance);
            }
            pitchDegrees = newPitch;
            Apply();
        }

        /// <summary>
        /// The distance whose <see cref="TeleportZoomFraming.SafeVisibleRadius"/> at
        /// <paramref name="newPitchDegrees"/> matches what <paramref name="oldDistance"/> showed at
        /// <paramref name="oldPitchDegrees"/> — the pure maths behind <see cref="SetPitch"/>'s
        /// area-preserving nudge, exposed standalone so it's testable without a live camera.
        /// </summary>
        public static float DistanceHoldingVisibleArea(
            float oldDistance, float oldPitchDegrees, float newPitchDegrees,
            float verticalFovDegrees, float aspect)
        {
            float radius = TeleportZoomFraming.SafeVisibleRadius(
                oldDistance, oldPitchDegrees, verticalFovDegrees, aspect);
            return TeleportZoomFraming.DistanceForVisibleRadius(
                radius, newPitchDegrees, verticalFovDegrees, aspect);
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
