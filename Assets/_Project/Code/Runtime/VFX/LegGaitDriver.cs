using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A cheap procedural walk cycle for a legged <see cref="RobotBodies.Body.Legs"/> array (MV-580).
    ///
    /// No animation clips, no rigging import, no IK — every leg pivot is a rigid hip joint (see
    /// <see cref="RobotBodies"/>'s private <c>AddLeg</c>), so "walking" is just rotating each pivot's
    /// local X by a sine wave. The wave's PHASE is driven by distance actually travelled, the same
    /// idiom <see cref="RobotRig.SpinWheels"/> already uses for wheels — never by wall-clock time alone
    /// — so a mover that stops never keeps cycling on the spot, and one that speeds up cycles faster
    /// without any extra tuning knob. <see cref="Tick"/> eases an amplitude term toward 0 whenever the
    /// mover is stationary, so the legs SETTLE to their neutral built pose rather than freezing
    /// mid-stride — the "slides with rigid limbs" look this ticket exists to avoid.
    ///
    /// Built for the roster, not for the Sentinel specifically (MV-580's own instruction): any future
    /// legged mover that exposes <see cref="RobotBodies.Body.Legs"/> can drive them the same way.
    /// </summary>
    public sealed class LegGaitDriver
    {
        /// <summary>Metres of travel per full swing cycle (2*PI of phase). Small — these are short
        /// stubby legs, not a stride built for covering ground fast.</summary>
        private const float StrideLength = 0.6f;

        /// <summary>Peak swing, degrees either side of the built rest pose.</summary>
        private const float SwingAmplitudeDeg = 26f;

        /// <summary>How fast the amplitude eases toward its target (1 while moving, 0 at rest), in
        /// units/second. Fast enough that a stop reads as a stop within a few frames, not a lingering
        /// twitch.</summary>
        private const float AmplitudeResponse = 8f;

        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private float _phase;
        private float _amplitude;

        /// <summary>The cycle's current phase, radians — advances only while moving. What a test
        /// samples to prove the cycle rate tracks speed without needing a live leg transform.</summary>
        public float Phase => _phase;

        /// <summary>Advance the gait by one frame and pose every <paramref name="legs"/> pivot.
        /// Allocates nothing per call: no <c>new</c>, only field writes and indexing into the
        /// caller-owned array <see cref="RobotBodies.Body.Legs"/> already returned once at build time.
        /// </summary>
        public void Tick(Transform[] legs, Vector3 worldPosition, float dt)
        {
            if (!_hasLastPosition) { _lastPosition = worldPosition; _hasLastPosition = true; }

            Vector3 delta = worldPosition - _lastPosition;
            _lastPosition = worldPosition;
            delta.y = 0f;
            float distance = delta.magnitude;
            bool moving = distance > 1e-5f;

            if (moving) _phase += distance / StrideLength * (Mathf.PI * 2f);

            float target = moving ? 1f : 0f;
            _amplitude = Mathf.MoveTowards(_amplitude, target, AmplitudeResponse * dt);

            if (legs == null) return;
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                // Legs offset evenly around the cycle so they don't all lift in lockstep — a tripod's
                // three legs 120 degrees apart is the same "never all airborne at once" rule a real
                // multi-legged gait needs to look planted rather than hopping.
                float legPhase = _phase + i * (Mathf.PI * 2f / legs.Length);
                float swing = SwingDegrees(legPhase) * _amplitude;
                legs[i].localRotation = Quaternion.Euler(swing, 0f, 0f);
            }
        }

        /// <summary>Pure: the swing angle (degrees) a leg at rest-amplitude 1 would hold at
        /// <paramref name="phase"/> radians. Exists so a test can sample the shape of the cycle without
        /// building a live leg transform.</summary>
        public static float SwingDegrees(float phase) => Mathf.Sin(phase) * SwingAmplitudeDeg;

        /// <summary>How far off the ground the body should bob at the current phase/amplitude — half
        /// the cycle rate of the leg swing (two steps per full stride), so the body dips on every
        /// plant rather than once per full leg cycle. Small on purpose: a bob big enough to read at
        /// this camera distance without looking like the chassis is bouncing.</summary>
        public float BobHeight(float amplitudeMetres = 0.02f) =>
            Mathf.Abs(Mathf.Sin(_phase)) * amplitudeMetres * _amplitude;
    }
}
