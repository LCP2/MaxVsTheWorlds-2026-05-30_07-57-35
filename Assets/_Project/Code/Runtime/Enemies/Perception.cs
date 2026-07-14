using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// What a robot knows about where Max is (YT-83) — as opposed to where he actually is, which
    /// until now was the same thing.
    ///
    /// Every robot has been omniscient since YT-36: <c>TickChase</c> read <c>target.position</c>
    /// straight off the transform, every frame, forever. That is what made cover decoration. You
    /// could stand behind the tree and the pack would walk around it and take you anyway, because
    /// the tree was never something they had to see past — it was something they bumped into.
    ///
    /// So the robot no longer chases Max. It chases the last place it SAW him, and it has to see him
    /// to update that. Which is the whole mechanic: break the sight-line and the pack commits to a
    /// stale spot, gives up, and mills there — and you get to leave.
    ///
    /// Pure: no transforms, no physics, no clock. The vision cone this feeds on is answered by
    /// <see cref="MaxWorlds.Arena.LineOfSight"/>; what to DO about the answer is here, and testable.
    /// </summary>
    public sealed class Perception
    {
        /// <summary>Can it see Max right now.</summary>
        public bool HasSight { get; private set; }

        /// <summary>Where Max was when it last had him. Meaningless until <see cref="HasTrail"/>.</summary>
        public Vector3 LastKnown { get; private set; }

        /// <summary>Whether there is anywhere to go at all.</summary>
        public bool HasTrail { get; private set; }

        /// <summary>Seconds since the sight-line last held. Zero while it can see him.</summary>
        public float TimeSinceSeen { get; private set; }

        /// <summary>
        /// A robot's first idea of where Max is, handed to it when the factory spits it out.
        ///
        /// Without this a fresh robot has never seen anything, has nowhere to go, and stands in the
        /// factory mouth — which is what happens the moment cover exists, because the hutch it just
        /// walked out of is itself blocking its view. Seeding it means robots are DISPATCHED toward
        /// the fight rather than being born omniscient: they walk to where Max was when they
        /// spawned, and if he isn't there any more, that is exactly the mistake we want them to be
        /// able to make.
        /// </summary>
        public void Spawn(Vector3 whereMaxWas)
        {
            HasSight = false;
            HasTrail = true;
            LastKnown = whereMaxWas;
            TimeSinceSeen = 0f;
        }

        /// <summary>Advance one frame. <paramref name="canSee"/> comes from the sight-line test.</summary>
        public void Tick(bool canSee, Vector3 targetNow, float dt)
        {
            HasSight = canSee;

            if (canSee)
            {
                LastKnown = targetNow;      // the trail is only ever refreshed by actually looking
                HasTrail = true;
                TimeSinceSeen = 0f;
                return;
            }

            TimeSinceSeen += Mathf.Max(0f, dt);
        }

        /// <summary>Where to walk: Max if it can see him, otherwise the last place it did.</summary>
        public Vector3 Destination(Vector3 targetNow) =>
            HasSight ? targetNow : (HasTrail ? LastKnown : targetNow);

        // There is deliberately no "has it given up" here any more (YT-93).
        //
        // There used to be: !HasSight && TimeSinceSeen >= searchTime. It read as a fact about
        // perception and it isn't one — it is a decision about the CHASE, and taken on the wrong
        // evidence. Every robot is born out of sight of Max, in a shed on the far side of the yard, so
        // "hasn't seen him for 2.5 s" was true of every robot 2.5 s into a thirty-second walk, and they
        // all stopped dead in the shed. What actually means "I have lost him" is "I am not getting any
        // closer to the place I am hunting", and only the thing doing the walking knows that. It lives
        // in RobotEnemy, where the walking is.
    }
}
