using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The layer that breaks a sight-line (YT-83) — the first custom layer this project has ever
    /// had, because until now nothing needed to ASK the world where the cover was. Robots bumped
    /// into props and slid along them (<see cref="MaxWorlds.Enemies.ObstacleSteering"/>); nothing
    /// ever looked.
    ///
    /// What goes on it: the cover props, the arena walls, and the Mower Hutch. The hutch matters
    /// more than it sounds — the shed is 2.4 m of plank wall built AROUND it as pure scenery with no
    /// collider of its own, so it is the most obviously hideable-behind object in the yard and,
    /// without this, the one thing that wouldn't work. Players would read that as a bug, and they'd
    /// be right. Putting the hutch in the mask makes the shed tell the truth without inventing a
    /// single new collider.
    ///
    /// What does NOT go on it: the ground (every ray would graze it), the actors (a robot must not
    /// be cover for another robot — a crowd would blind itself), and the dressing, which is scenery
    /// by an explicit rule that predates this ticket.
    /// </summary>
    public static class CoverLayer
    {
        public const string Name = "Cover";

        /// <summary>Layer index, or -1 if the layer is missing from TagManager.</summary>
        public static int Index => LayerMask.NameToLayer(Name);

        public static bool Exists => Index >= 0;

        /// <summary>
        /// The mask to cast against.
        ///
        /// Note the failure mode this has, and why a test guards it: if the layer were ever dropped
        /// from TagManager, Index is -1, the mask would be 0, every cast would hit nothing, every
        /// sight-line would come back CLEAR — and cover would silently stop working while the game
        /// carried on looking exactly the same. Nothing would throw. That is the worst kind of bug
        /// this codebase keeps producing, so <c>CoverLayerTests</c> asserts the layer exists.
        /// </summary>
        public static int Mask => Exists ? 1 << Index : 0;

        /// <summary>
        /// Put a body on the cover layer.
        ///
        /// Only the body itself, deliberately — NOT its children. A sight-line is a raycast, a
        /// raycast only ever hits colliders, and every collider in this game sits on the object that
        /// owns it. Recursing would sweep up the hutch's world-space health bar and its name label,
        /// which are children, carry no colliders, and have no business being told they're cover.
        /// </summary>
        public static void Assign(GameObject body)
        {
            if (body == null || !Exists) return;
            body.layer = Index;
        }
    }

    /// <summary>
    /// Can A see B (YT-83). The whole tactical layer rests on this one question.
    /// </summary>
    public static class LineOfSight
    {
        /// <summary>
        /// Where a sight-line is measured from and to.
        ///
        /// Both actors' origins already sit at the middle of their bodies — Max's at his capsule's
        /// centre (1.0 m), a rusher's at half its collider (0.7 m) — so the raw positions ARE chest
        /// height and no offset is needed. That is worth stating out loud because it is luck, not
        /// design, and it is the thing that would break: raise the sample to a human 1.7 m "eye
        /// height" and the ray would fly clean over the 1.6 m planter and the 1.8 m hedge, and
        /// two-thirds of the yard's cover would quietly stop working while the tree kept doing its
        /// job. <c>SightlineTests</c> pins the sample below the shortest cover for exactly that
        /// reason.
        /// </summary>
        public static Vector3 EyeOf(Transform actor) => actor.position;

        /// <summary>
        /// True if nothing on the cover layer stands between <paramref name="from"/> and
        /// <paramref name="to"/>.
        ///
        /// <paramref name="target"/> is the thing being looked AT, and it is not optional courtesy:
        /// the Mower Hutch is on the cover layer and is also the thing Max has to destroy. Cast
        /// naively and the factory blocks the sight-line to itself, so the blaster could never hurt
        /// it — cover would have made the win condition unreachable. So the first thing hit is
        /// allowed to be the target.
        /// </summary>
        public static bool Clear(Vector3 from, Vector3 to, Transform target = null)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-3f) return true;

            if (!Physics.Raycast(from, delta / dist, out var hit, dist,
                                 CoverLayer.Mask, QueryTriggerInteraction.Ignore))
            {
                return true;   // nothing in the way
            }

            // The only thing we hit is the thing we were looking at — that isn't cover, that's it.
            return target != null && (hit.transform == target || hit.transform.IsChildOf(target));
        }

        /// <summary>Sight-line between two actors, sampled at their body centres.</summary>
        public static bool Between(Transform looker, Transform target)
        {
            if (looker == null || target == null) return false;
            return Clear(EyeOf(looker), EyeOf(target), target);
        }
    }
}
