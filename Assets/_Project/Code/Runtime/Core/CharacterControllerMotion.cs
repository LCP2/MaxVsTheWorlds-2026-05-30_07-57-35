using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-386: the working hypothesis (this ticket's own investigation notes) is that
    /// <see cref="CharacterController.Move"/> can tunnel through a thin solid (a gate/fence wall is
    /// <c>MapData.wallThickness</c>, 0.4 m by default) when asked to cover an unusually large distance
    /// in one call — the kind of spike a single frame's <see cref="Time.deltaTime"/> gets from a WebGL
    /// tab losing focus, a GC pause, or a shader/asset compile stall, none of which an Editor session or
    /// a Windows-standalone smoke build reliably hits.
    ///
    /// <b>This has NOT been confirmed as the actual mechanism.</b> A set of isolated EditMode physics
    /// probes (<see cref="MaxWorlds.Tests.EditMode.CharacterControllerMotionTunnelingTests"/>) tried a
    /// raw oversized <c>cc.Move()</c> against an unattached wall at increasing distance, a wall thinner
    /// than the map format's own minimum, a huge diagonal displacement dominated by a WebGL-stall-sized
    /// fall, and 240 frames of continuous grinding contact — none of them reproduced a pass-through;
    /// Unity's own swept collision test caught every one. The live bug has only ever reproduced on a
    /// deployed WebGL build, which this worker cannot drive (CC_AUTONOMY.md forbids PlayMode and there
    /// is no browser access here), so the isolated-probe result doesn't rule the theory out either — it
    /// may need real map geometry, a live browser stall, or some other frame-order factor a synthetic
    /// two-collider scene can't reproduce.
    ///
    /// <see cref="SafeMove"/> is shipped anyway as a genuinely safe, zero-downside hardening: it is a
    /// drop-in replacement for <c>cc.Move(displacement)</c> that keeps every individual physics query
    /// under <see cref="MaxSafeStep"/>, splitting one oversized call into several smaller ones that sum
    /// to the same total displacement (normal frames cost exactly one <c>Move()</c> call, same as
    /// before). It also logs whenever it has to split, so a live WebGL playtest's browser console can
    /// show whether an oversized single-frame displacement is actually occurring at the moment a
    /// pass-through is observed — the correlation this ticket's own AC asks for, which only a live
    /// build can supply.
    /// </summary>
    public static class CharacterControllerMotion
    {
        /// <summary>Largest displacement, in metres, any single <see cref="CharacterController.Move"/>
        /// call is trusted to sweep-test correctly. Half the minimum wall/gate thickness
        /// (<c>MapData.wallThickness</c> defaults to 0.4 m) leaves a 2x margin against a collider
        /// that's already at the thin end.</summary>
        public const float MaxSafeStep = 0.2f;

        /// <summary>Moves <paramref name="cc"/> by <paramref name="displacement"/>, splitting it into
        /// <see cref="MaxSafeStep"/>-sized steps when it's larger than that. Each step is its own swept
        /// collision test, so a stall-inflated single-frame displacement can't skip past a thin
        /// collider the way one oversized <c>Move()</c> call can.</summary>
        public static void SafeMove(CharacterController cc, Vector3 displacement)
        {
            float dist = displacement.magnitude;
            if (dist <= MaxSafeStep)
            {
                cc.Move(displacement);
                return;
            }

            // MV-386 diagnostic: this is the exact spike under investigation. Logged (not just
            // silently handled) so a live WebGL browser console can show whether this fires at the
            // same moment Lee sees a pass-through -- the correlation the ticket's own AC asks for.
            Debug.LogWarning($"[CharacterControllerMotion] {cc.name}: oversized single-frame Move " +
                              $"({dist:F2} m) split into {Mathf.CeilToInt(dist / MaxSafeStep)} steps");

            int steps = Mathf.CeilToInt(dist / MaxSafeStep);
            Vector3 step = displacement / steps;
            for (int i = 0; i < steps; i++)
            {
                cc.Move(step);
            }
        }
    }
}
