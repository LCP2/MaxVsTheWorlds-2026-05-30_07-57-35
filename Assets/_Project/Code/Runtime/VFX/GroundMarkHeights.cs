using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The single source of truth for how high every GAMEPLAY ground mark draws (MV-866), and for
    /// WHERE on the map "the ground" actually is for one (MV-1000).
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

        /// <summary>The XZ-flattened surface a ground mark at <paramref name="p"/> should sit on: the
        /// deck top when <paramref name="p"/> is over an authored deck at deck height, the area floor
        /// otherwise — never a fixed floor plane (MV-898's fix for <see cref="GroundAnchorVfx"/>,
        /// extended here to every ground-mark call site so the two can never drift apart again;
        /// MV-1000 found <see cref="TelegraphVfx"/> still flattening to a fixed y=0 after MV-898 only
        /// touched <see cref="GroundAnchorVfx"/>). Falls back to the floor when there is no map to
        /// consult (an EditMode fixture with no <see cref="MaxWorlds.Arena.BackyardPath"/> in the
        /// scene).</summary>
        public static Vector3 SurfaceAt(Vector3 p)
        {
            MapData map = EnemyNavigation.Map;
            float y = map != null ? map.SurfaceHeightAt(p) : 0f;
            return new Vector3(p.x, y, p.z);
        }
    }
}
