namespace MaxWorlds.VFX
{
    /// <summary>
    /// The single source of truth for how high every GAMEPLAY ground mark draws (MV-866).
    ///
    /// Floor dressing is scenery and always sits below every gameplay mark. World 2's stormdrain
    /// dressing (<see cref="MaxWorlds.Rendering.StormdrainKit"/>: CrackLift, StainLift +
    /// StainLayerGap, WaterMeniscusLift) tops out at <see cref="DressingCeiling"/> — that ceiling is
    /// informational only, read here in a comment, never in code: StormdrainKit's own constants were
    /// deliberately raised off the ground for depth-buffer precision (MV-791) and must not move
    /// again. Before this fix the gameplay ladder (reticle/shadow/ring/telegraph, then
    /// 0.006-0.030) sat entirely UNDER that dressing, so a floor stain drew over Max's aim reticle
    /// and the sentinel rings. The fix lifts the gameplay marks clear of the dressing instead of
    /// pushing the dressing back down.
    ///
    /// Order matters and is unchanged from before this ticket — it is a priority order, not a random
    /// spacing:
    ///   0.040  aim reticle       — bottom of the gameplay stack; drawn UNDER every other actor mark.
    ///   0.046  contact shadow    — under the actor, so under everything except the reticle beneath it.
    ///   0.052  anchor ring       — above its own shadow, below anything that demands a reaction.
    ///   0.060  danger telegraph  — always on top: an always-on decoration must never cover the one
    ///                              mark the player has to move away from.
    /// </summary>
    public static class GroundMarkHeights
    {
        /// <summary>Informational only — see the class remarks. Not read by any call site; the
        /// gameplay marks below simply all sit above it.</summary>
        public const float DressingCeiling = 0.026f;

        public const float AimReticleLift = 0.040f;
        public const float ContactShadowLift = 0.046f;
        public const float AnchorRingLift = 0.052f;
        public const float DangerTelegraphLift = 0.060f;
    }
}
