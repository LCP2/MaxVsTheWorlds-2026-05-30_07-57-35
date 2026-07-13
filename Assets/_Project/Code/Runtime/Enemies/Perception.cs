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

        /// <summary>
        /// It has been looking for <paramref name="searchSeconds"/> and found nothing: contact is
        /// broken and it stops hunting.
        ///
        /// This is the moment the player is actually paying for when they duck behind the tree — and
        /// it's also why the timer exists at all rather than "gives up on arrival at LastKnown". A
        /// robot that gave up the instant it reached an empty spot would be trivially exploitable
        /// (step behind cover, step straight back out, it's already forgotten you); one that never
        /// gave up would make cover pointless. The delay is the cost of hiding.
        /// </summary>
        public bool HasLostHim(float searchSeconds) => !HasSight && TimeSinceSeen >= searchSeconds;
    }
}
