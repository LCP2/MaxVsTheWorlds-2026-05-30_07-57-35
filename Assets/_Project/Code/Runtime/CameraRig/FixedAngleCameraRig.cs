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
    /// Pulled back from the original ~13.7 m after playtest feedback that it felt too zoomed in.
    /// </summary>
    public sealed class FixedAngleCameraRig : MonoBehaviour
    {
        [Tooltip("Fixed top-down pitch. Load-bearing for the AI-art pipeline — do NOT change (YT-33).")]
        [SerializeField] private float pitchDegrees = 72f;

        [Tooltip("Distance from the follow target to the camera, in metres. Bigger = more arena " +
                 "visible around Max. Tune this to taste; keep the pitch fixed.")]
        [SerializeField] private float cameraDistance = 20.5f;

        private void Awake() => Apply();

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
