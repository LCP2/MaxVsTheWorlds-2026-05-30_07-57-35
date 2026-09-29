using System;
using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-1021: three TestFlight crashes (World 1, a20-a23) all share the same native top frame —
    /// <c>PhysicsCommands::PhysX::CreateCharacterController</c> — a NULL deref Unity 6000.4.9f1 never
    /// guards. PhysX's <c>PxControllerManager::createController</c> returns NULL when the controller
    /// desc is invalid: a non-finite pose, a zero/degenerate world-scaled size, or a
    /// <see cref="CharacterController.stepOffset"/> that exceeds height + 2*radius. C# cannot catch the
    /// resulting SIGSEGV — it has to be refused BEFORE the create, which happens on
    /// <c>AddComponent&lt;CharacterController&gt;()</c>, on <c>SetActive(true)</c>/<c>enabled = true</c>
    /// for a GameObject that already carries one, and it must be re-checked on the reposition idiom every
    /// mover uses (disable, move the transform, re-enable) since a bad move corrupts the cached desc the
    /// next re-enable recreates from.
    /// </summary>
    public static class CharacterControllerSafety
    {
        /// <summary>Mirrors the crash logs' altitude — Lee's authored geometry never approaches this;
        /// anything past it is corrupt, not just "a long way from home".</summary>
        public const float MaxAbsCoordinate = 100000f;

        /// <summary>Same floor <see cref="MaxWorlds.Core.ParentScale.Unscale"/> already treats as "may as
        /// well be zero" — a lossyScale axis at or below this collapses the controller to a degenerate
        /// sliver PhysX refuses.</summary>
        public const float MinScaleComponent = 1e-4f;

        /// <summary>
        /// True only when every one of the ticket's five conditions holds. <paramref name="cc"/> may be
        /// null — the <c>AddComponent&lt;CharacterController&gt;()</c> sites call this BEFORE the
        /// component exists, when only the transform can be checked; Unity's own default size
        /// (radius 0.5, height 2, stepOffset 0.3) is safe by construction, so the size/step checks are
        /// skipped rather than faked.
        /// </summary>
        public static bool CanCreate(Transform t, CharacterController cc, out string reason)
        {
            if (t == null)
            {
                reason = "null-transform";
                return false;
            }

            Vector3 pos = t.position;
            if (!IsFinite(pos) || Mathf.Abs(pos.x) >= MaxAbsCoordinate ||
                Mathf.Abs(pos.y) >= MaxAbsCoordinate || Mathf.Abs(pos.z) >= MaxAbsCoordinate)
            {
                reason = "non-finite-position";
                return false;
            }

            if (!IsFinite(t.rotation))
            {
                reason = "non-finite-rotation";
                return false;
            }

            Vector3 scale = t.lossyScale;
            if (!IsFinite(scale) || Mathf.Abs(scale.x) <= MinScaleComponent ||
                Mathf.Abs(scale.y) <= MinScaleComponent || Mathf.Abs(scale.z) <= MinScaleComponent)
            {
                reason = "degenerate-scale";
                return false;
            }

            if (cc != null)
            {
                // Same axis convention as Unity's own capsule scaling: height follows Y, radius follows
                // the larger of X/Z (a controller's footprint is round, so an asymmetric XZ scale still
                // needs one radius — the larger axis is the one PhysX would actually clip against).
                float worldRadius = cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float worldHeight = cc.height * Mathf.Abs(scale.y);

                if (!(worldRadius > 0f) || !(worldHeight > 0f))
                {
                    reason = "non-positive-world-size";
                    return false;
                }

                if (cc.stepOffset > worldHeight + 2f * worldRadius)
                {
                    reason = "step-offset-exceeds-bounds";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// The shared "disable, move the transform, re-enable" idiom every mover in this project uses
        /// (<c>RobotEnemy</c>, <c>Sentinel</c>, <c>PlayerController</c>) — centralised here so the
        /// re-enable is always preceded by a check. Refused: the object is left exactly where it was
        /// (never moved) with the controller re-enabled, and one refusal row is logged.
        /// </summary>
        public static void SafeReposition(CharacterController cc, Vector3 newPosition, string site)
        {
            if (cc == null) return;

            Transform t = cc.transform;
            Vector3 oldPosition = t.position;
            cc.enabled = false;
            t.position = newPosition;

            if (CanCreate(t, cc, out string reason))
            {
                cc.enabled = true;
            }
            else
            {
                t.position = oldPosition;
                cc.enabled = true;
                LogRefusal(site, t.name, reason, newPosition, t.lossyScale);
            }
        }

        /// <summary>The root-cause evidence row for Lee's phone (MV-1021 change 2) — one line through
        /// the same events CSV <see cref="PerfSessionRecorder.RecordEvent"/> already writes
        /// (<c>Bootstrap.ActiveSessionRecorder</c>), tagged <c>cct-refused</c> so it is unambiguous
        /// against every other event name in that file. A no-op off a live session (EditMode, a test
        /// that never installs Bootstrap) — same null-conditional guard <see cref="Bootstrap.ActiveSessionRecorder"/>'s
        /// other call sites already use.</summary>
        public static void LogRefusal(string site, string objectName, string reason, Vector3 position, Vector3 lossyScale)
        {
            string context = $"{site},{objectName},{reason},{position},{lossyScale}";
            Bootstrap.ActiveSessionRecorder?.RecordEvent(DateTime.UtcNow, "cct-refused", context);
        }

        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        private static bool IsFinite(Quaternion q) =>
            IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
