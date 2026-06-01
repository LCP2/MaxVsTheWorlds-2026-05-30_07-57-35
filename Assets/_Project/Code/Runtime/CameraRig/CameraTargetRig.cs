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

        [Tooltip("How far ahead of the subject the camera biases, in metres.")]
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

            Vector3 desiredLead = velocity.magnitude > velocityDeadzone
                ? planar.normalized * lookAheadDistance
                : Vector3.zero;

            float t = 1f - Mathf.Exp(-lookAheadSmoothing * dt);
            _smoothedLead = Vector3.Lerp(_smoothedLead, desiredLead, t);

            transform.position = subject.position + _smoothedLead;
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
