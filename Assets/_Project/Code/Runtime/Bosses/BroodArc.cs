using UnityEngine;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// The maths for a robot flung out of a brood-hatch (YT-157). Pure and static, in the same
    /// "no MonoBehaviour, so it is unit-testable" spirit as <see cref="MaxWorlds.Enemies.FactoryMouth"/>
    /// and <see cref="BossFight"/>.
    ///
    /// An add does not walk out of the boss the way a factory robot walks out of a doorway — it is
    /// THROWN. It leaves a side hatch, arcs up and out over the lawn, and lands a few metres off the
    /// boss's flank, where it becomes a normal robot and comes for Max. The arc is the read: at the
    /// 72° camera a thing rising off the boss's side and coming down onto the grass says "the swarm is
    /// being launched" in a way a robot sliding along the ground never could.
    /// </summary>
    public static class BroodArc
    {
        /// <summary>
        /// A point on the parabola from <paramref name="from"/> to <paramref name="to"/> at
        /// <paramref name="t"/> in 0..1, peaking <paramref name="apex"/> metres above the straight line
        /// at the midpoint. The horizontal is a straight lerp; only the height bulges — so the robot
        /// travels out to its landing spot at an even rate and the arc is purely the lift.
        /// </summary>
        public static Vector3 PointAt(Vector3 from, Vector3 to, float apex, float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 p = Vector3.Lerp(from, to, t);
            // 4·t·(1-t) is 0 at both ends and 1 at t=0.5 — a clean parabola whose only tunable is height.
            p.y += apex * 4f * t * (1f - t);
            return p;
        }

        /// <summary>
        /// The mouth of a side hatch in world space: off the boss's LEFT or RIGHT flank
        /// (<paramref name="side"/> = -1 or +1) at hatch height. Uses the boss's own facing so the
        /// hatches stay on its sides however it has turned — the swarm always spills from the flanks,
        /// never from the face the player is reading the charge off.
        /// </summary>
        public static Vector3 Muzzle(Vector3 bossPos, Quaternion bossFacing, float side,
                                     float sideDistance, float height)
        {
            Vector3 right = bossFacing * Vector3.right;
            return bossPos + right * (side * sideDistance) + Vector3.up * height;
        }

        /// <summary>
        /// Where a flung robot lands: out to the boss's <paramref name="side"/> and a little forward,
        /// on the ground (y = <paramref name="groundY"/>). <paramref name="spread"/> walks successive
        /// robots of one volley further out so they do not stack into a single point. Landing to the
        /// SIDES rather than on top of Max is what keeps the attack fair (Craft Bible §3): the adds come
        /// down where the player can see them and then close the distance, they are not dropped on him.
        /// </summary>
        public static Vector3 Landing(Vector3 bossPos, Quaternion bossFacing, float side,
                                      float sideDistance, float forward, float groundY, float spread)
        {
            Vector3 right = bossFacing * Vector3.right;
            Vector3 fwd = bossFacing * Vector3.forward;
            Vector3 p = bossPos
                        + right * (side * (sideDistance + spread))
                        + fwd * forward;
            p.y = groundY;
            return p;
        }
    }
}
