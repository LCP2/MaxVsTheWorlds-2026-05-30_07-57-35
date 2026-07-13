using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Cover that actually breaks a chase (YT-83). The perception maths and the two facts the whole
    /// mechanic silently depends on: that the cover layer exists at all, and that the sight-line is
    /// sampled LOW enough to be stopped by the cover the yard actually has.
    /// </summary>
    public sealed class SightlineTests
    {
        // --- The two silent-failure guards -------------------------------------------------------

        /// <summary>
        /// If the Cover layer went missing from TagManager, <c>Mask</c> would be 0, every raycast
        /// would hit nothing, every sight-line would come back CLEAR, and cover would quietly stop
        /// working — with the game still running, still building, and looking exactly the same.
        /// Nothing would throw. This is the only thing standing between that and a shipped build.
        /// </summary>
        [Test]
        public void TheCoverLayerExists()
        {
            Assert.IsTrue(CoverLayer.Exists,
                $"the '{CoverLayer.Name}' layer is gone from TagManager. Every sight-line now reads " +
                "CLEAR, so no robot can ever lose Max and the blaster shoots through walls — and " +
                "nothing anywhere will throw to tell you.");
            Assert.AreNotEqual(0, CoverLayer.Mask, "an empty mask blocks nothing");
        }

        /// <summary>
        /// The sight-line is sampled at the actors' body centres, which works only because both are
        /// SHORT: Max's origin is his capsule's centre at 1.0 m and a rusher's is half its collider
        /// at 0.7 m. Raise that sample to a human 1.7 m "eye height" and the ray sails clean over the
        /// 1.6 m planter and the 1.8 m hedge — two-thirds of the yard's cover would silently stop
        /// working while the 4.4 m tree carried on fine, which is a bug you would chase for a day.
        /// </summary>
        [Test]
        public void EveryPieceOfCoverIsTallerThanTheSightLineItHasToBreak()
        {
            const float maxCentre = 1.0f;    // Max's origin: capsule centre, and the HIGHEST sample
            const float clearance = 0.3f;    // don't let it be marginal

            foreach (var c in BackyardCover.Default)
            {
                Assert.Greater(c.Size.y, maxCentre + clearance,
                    $"'{c.Name}' is {c.Size.y} m tall — the sight-line runs at {maxCentre} m, so this " +
                    "prop is cover you can see straight over. It will look like cover and do nothing.");
            }
        }

        // --- Perception: what a robot knows, vs where Max is --------------------------------------

        [Test]
        public void SeeingHimRefreshesTheTrail()
        {
            var p = new Perception();
            p.Spawn(Vector3.zero);

            p.Tick(canSee: true, targetNow: new Vector3(5f, 0f, 5f), dt: 0.1f);

            Assert.IsTrue(p.HasSight);
            Assert.AreEqual(new Vector3(5f, 0f, 5f), p.LastKnown);
            Assert.AreEqual(0f, p.TimeSinceSeen, 1e-4);
        }

        [Test]
        public void LosingHimFreezesTheTrailWhereHeWas_NotWhereHeIs()
        {
            // The entire mechanic in one assertion. Max keeps running; the robot's idea of him does
            // not. If LastKnown tracked the live position, cover would be decoration again.
            var p = new Perception();
            p.Spawn(Vector3.zero);
            p.Tick(canSee: true, targetNow: new Vector3(3f, 0f, 0f), dt: 0.1f);

            p.Tick(canSee: false, targetNow: new Vector3(9f, 0f, 9f), dt: 0.1f);
            p.Tick(canSee: false, targetNow: new Vector3(20f, 0f, 20f), dt: 0.1f);

            Assert.AreEqual(new Vector3(3f, 0f, 0f), p.LastKnown,
                "the robot is tracking Max through solid cover — it still knows exactly where he is");
            Assert.IsFalse(p.HasSight);
        }

        [Test]
        public void ItWalksToTheStaleSpot_NotToMax()
        {
            var p = new Perception();
            p.Spawn(Vector3.zero);
            p.Tick(canSee: true, targetNow: new Vector3(3f, 0f, 0f), dt: 0.1f);
            p.Tick(canSee: false, targetNow: new Vector3(30f, 0f, 30f), dt: 0.1f);

            Assert.AreEqual(new Vector3(3f, 0f, 0f), p.Destination(new Vector3(30f, 0f, 30f)));
        }

        [Test]
        public void WhileItCanSeeHim_TheDestinationIsSimplyHim()
        {
            var p = new Perception();
            p.Spawn(Vector3.zero);
            p.Tick(canSee: true, targetNow: new Vector3(7f, 0f, 2f), dt: 0.1f);

            Assert.AreEqual(new Vector3(7f, 0f, 2f), p.Destination(new Vector3(7f, 0f, 2f)));
        }

        [Test]
        public void ItGivesUpOnlyAfterSearching_NotTheInstantItLosesSight()
        {
            // Both edges. Give up instantly and hiding is a free reset — step behind the tree, step
            // straight back out, forgotten. Never give up and cover does nothing. The delay IS the
            // price of hiding, so it has to actually elapse.
            var p = new Perception();
            p.Spawn(Vector3.zero);
            p.Tick(canSee: true, targetNow: Vector3.zero, dt: 0.1f);

            p.Tick(canSee: false, targetNow: Vector3.zero, dt: 1.0f);
            Assert.IsFalse(p.HasLostHim(2.5f), "it gave up after one second of losing sight");

            p.Tick(canSee: false, targetNow: Vector3.zero, dt: 2.0f);
            Assert.IsTrue(p.HasLostHim(2.5f), "it will hunt a stale spot forever — contact never breaks");
        }

        [Test]
        public void SteppingBackIntoViewCancelsTheSearchImmediately()
        {
            var p = new Perception();
            p.Spawn(Vector3.zero);
            p.Tick(canSee: false, targetNow: Vector3.zero, dt: 10f);
            Assert.IsTrue(p.HasLostHim(2.5f), "precondition: it has lost him");

            p.Tick(canSee: true, targetNow: new Vector3(1f, 0f, 1f), dt: 0.1f);

            Assert.IsFalse(p.HasLostHim(2.5f), "he walked back into the open and it didn't notice");
            Assert.IsTrue(p.HasSight);
        }

        [Test]
        public void AFreshRobotIsDispatchedTowardTheFight_NotBornBlind()
        {
            // Without the spawn seed a new robot has never seen anything, has nowhere to go, and
            // stands in the factory mouth — which is exactly what happens now that the hutch it just
            // walked out of is itself blocking its view.
            var p = new Perception();
            p.Spawn(new Vector3(2f, 0f, 8f));

            Assert.IsTrue(p.HasTrail, "a fresh robot has nowhere to walk and will stand in the doorway");
            Assert.AreEqual(new Vector3(2f, 0f, 8f), p.Destination(new Vector3(2f, 0f, 8f)));
            Assert.IsFalse(p.HasSight, "it hasn't actually SEEN him — it was just pointed at him");
        }
    }
}
