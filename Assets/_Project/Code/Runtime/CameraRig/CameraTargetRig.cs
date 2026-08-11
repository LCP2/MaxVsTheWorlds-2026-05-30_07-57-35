using UnityEngine;

namespace MaxWorlds.CameraRig
{
    /// <summary>
    /// Drives a follow-target transform that leads the subject toward its
    /// movement direction (and, later, aim), giving the fixed-angle camera its
    /// look-ahead bias <em>without ever rotating the camera</em> (YT-33).
    ///
    /// The <c>CinemachineCamera</c> should Follow this rig's transform — not the
    /// subject directly — so Cinemachine's position damping smooths the motion
    /// while this script supplies the lead offset.
    /// </summary>
    public sealed class CameraTargetRig : MonoBehaviour
    {
        [Tooltip("The thing the camera ultimately tracks (Max). Placeholder for the slice.")]
        [SerializeField] private Transform subject;

        [Tooltip("How far ahead of the subject the camera biases along the screen's WIDE axis " +
                 "(world X, left/right), in metres. The narrow axis (world Z, top/bottom) is " +
                 "scaled down from this by the screen's aspect ratio — see ComputeLead.")]
        [SerializeField] private float lookAheadDistance = 3f;

        [Tooltip("Higher = the lead offset snaps to the new direction faster.")]
        [SerializeField] private float lookAheadSmoothing = 6f;

        [Tooltip("Planar speed (m/s) below which no look-ahead is applied (idle).")]
        [SerializeField] private float velocityDeadzone = 0.15f;

        private Vector3 _lastSubjectPos;
        private Vector3 _smoothedLead;

        private void Awake()
        {
            if (subject != null)
            {
                _lastSubjectPos = subject.position;
            }
        }

        private void LateUpdate()
        {
            if (subject == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector3 delta = subject.position - _lastSubjectPos;
            _lastSubjectPos = subject.position;

            Vector3 planar = new Vector3(delta.x, 0f, delta.z);
            Vector3 velocity = dt > 0f ? planar / dt : Vector3.zero;

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1f;
            Vector3 desiredLead = ComputeLead(velocity, lookAheadDistance, aspect, velocityDeadzone);

            float t = 1f - Mathf.Exp(-lookAheadSmoothing * dt);
            _smoothedLead = Vector3.Lerp(_smoothedLead, desiredLead, t);

            transform.position = subject.position + _smoothedLead;
        }

        /// <summary>
        /// The lead offset for a given planar velocity (MV-332). Biases toward whichever direction
        /// the subject is actually moving — including retreat, away from an oncoming threat, not
        /// only advance toward one; the direction alone decides it, so a robot chasing Max from the
        /// north gets the same lead-away-from-it as one he's charging at.
        ///
        /// The landscape screen shows far less of the world along its narrow axis (vertical on
        /// screen, world Z / "north-south") than its wide one (horizontal, world X / "east-west").
        /// Leading by the same fixed distance on both axes therefore eats a much bigger SLICE of the
        /// little vertical room there is, boxing Max against the frame with barely anything visible
        /// behind him whenever he retreats from robots spawned above/below. Dividing the Z lead by
        /// the aspect ratio keeps the lead a constant FRACTION of what is actually visible in that
        /// direction, so retreating north/south leaves the same proportional margin behind him as
        /// retreating east/west does.
        /// </summary>
        public static Vector3 ComputeLead(
            Vector3 planarVelocity, float lookAheadDistance, float aspect, float velocityDeadzone)
        {
            if (planarVelocity.magnitude <= velocityDeadzone)
            {
                return Vector3.zero;
            }

            Vector3 direction = planarVelocity.normalized;
            float aspectSafe = aspect > 0f ? aspect : 1f;
            return new Vector3(
                direction.x * lookAheadDistance,
                0f,
                direction.z * lookAheadDistance / aspectSafe);
        }

        /// <summary>Rebinds the tracked subject (used when Max spawns in YT-34).</summary>
        public void SetSubject(Transform newSubject)
        {
            subject = newSubject;
            _lastSubjectPos = newSubject != null ? newSubject.position : Vector3.zero;
            _smoothedLead = Vector3.zero;
        }
    }
}
