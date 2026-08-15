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

        // --- MV-400: hedges stay solid but stop breaking a sight-line -----------------------------

        /// <summary>A single room with one hedge and one tree, spaced far enough apart (and from the
        /// room's own walls) that a ray drawn straight through one of them cannot also graze the
        /// other or a wall — the only thing this map exists to isolate is "does THIS piece of cover
        /// block the ray", nothing else standing in for it by accident.</summary>
        private static MapData HedgeVsTreeProbe()
        {
            return new MapData
            {
                name = "Hedge vs Tree Probe",
                zones = new[]
                {
                    new MapZone { id = "room", type = "open", x = 0f, z = 0f, width = 40f, depth = 20f },
                },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "hedge", kind = "cover", x = -10f, z = 0f,
                        width = 4.5f, height = 1.8f, depth = 1.3f, shape = "box", dressing = "hedge",
                    },
                    new MapEntity
                    {
                        id = "tree", kind = "cover", x = 10f, z = 0f,
                        width = 2.4f, height = 4.4f, depth = 2.4f, shape = "cylinder", dressing = "tree",
                    },
                },
            };
        }

        /// <summary>The whole ticket in one test: a hedge row keeps blocking a footstep (still a
        /// solid, non-trigger collider — nothing about MV-400 asks for that to change) but stops
        /// blocking a sight-line, while a tree — not asked to change — still blocks exactly as
        /// before. Sampled at 1.0 m, the same "below the shortest cover" eye height
        /// <see cref="EveryPieceOfCoverIsTallerThanTheSightLineItHasToBreak"/> already relies on, so
        /// both pieces are tall enough to matter if they were still on the Cover layer.</summary>
        [Test]
        public void AHedgeRow_StaysSolid_ButStopsBreakingTheSightLine()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            const float eyeHeight = 1.0f;
            MapData map = HedgeVsTreeProbe();
            var root = new GameObject("Hedge vs Tree Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                CoverPiece hedge = built.Cover.Find(p => p.Cover.Name == "hedge");
                CoverPiece tree = built.Cover.Find(p => p.Cover.Name == "tree");
                Assert.IsNotNull(hedge.Body, "the probe map's hedge was never built");
                Assert.IsNotNull(tree.Body, "the probe map's tree was never built");

                var hedgeCollider = hedge.Body.GetComponent<Collider>();
                Assert.IsNotNull(hedgeCollider, "a hedge carries no collider — nothing would block a footstep");
                Assert.IsFalse(hedgeCollider.isTrigger, "a hedge is a trigger, not a solid obstruction");

                Assert.AreNotEqual(CoverLayer.Index, hedge.Body.layer,
                    "a hedge is still on the Cover layer — robots would still be blind through it and " +
                    "a shot would still stop dead at a plant row");
                Assert.AreEqual(CoverLayer.Index, tree.Body.layer,
                    "a tree came off the Cover layer too — only a hedge is meant to stop blocking " +
                    "sight (MV-400); this would silently widen the change to all cover");

                Vector3 fromHedge = new Vector3(-14f, eyeHeight, 0f);
                Vector3 toHedge = new Vector3(-6f, eyeHeight, 0f);
                Assert.IsTrue(LineOfSight.Clear(fromHedge, toHedge),
                    "a hedge row still blocks the sight-line straight through it");

                Vector3 fromTree = new Vector3(6f, eyeHeight, 0f);
                Vector3 toTree = new Vector3(14f, eyeHeight, 0f);
                Assert.IsFalse(LineOfSight.Clear(fromTree, toTree),
                    "a tree stopped blocking the sight-line — only the hedge was meant to change");
            }
            finally
            {
                Object.DestroyImmediate(root);
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

        /// <summary>
        /// Losing sight is a clock, not a verdict.
        ///
        /// Perception used to decide when the robot gave up — <c>HasLostHim(2.5s)</c> — and that
        /// decision is gone (YT-93): "I have not seen him for 2.5 s" is true of every robot 2.5 s into
        /// the walk out of the shed it was born in, so they all stopped in the shed. What Perception
        /// owns is the FACT: how long since the sight-line last held. What to do about it belongs to
        /// the thing doing the walking.
        /// </summary>
        [Test]
        public void LosingSightStartsAClock_AndKeepsCounting()
        {
            var p = new Perception();
            p.Spawn(Vector3.zero);
            p.Tick(canSee: true, targetNow: Vector3.zero, dt: 0.1f);
            Assert.AreEqual(0f, p.TimeSinceSeen, 1e-4, "it can see him — nothing has been lost yet");

            p.Tick(canSee: false, targetNow: Vector3.zero, dt: 1.0f);
            Assert.AreEqual(1.0f, p.TimeSinceSeen, 1e-4);

            p.Tick(canSee: false, targetNow: Vector3.zero, dt: 2.0f);
            Assert.AreEqual(3.0f, p.TimeSinceSeen, 1e-4);
        }

        [Test]
        public void SteppingBackIntoViewCancelsTheSearchImmediately()
        {
            var p = new Perception();
            p.Spawn(Vector3.zero);
            p.Tick(canSee: false, targetNow: Vector3.zero, dt: 10f);
            Assert.AreEqual(10f, p.TimeSinceSeen, 1e-4, "precondition: it has not seen him in a while");

            p.Tick(canSee: true, targetNow: new Vector3(1f, 0f, 1f), dt: 0.1f);

            Assert.AreEqual(0f, p.TimeSinceSeen, 1e-4,
                "he walked back into the open and it didn't notice");
            Assert.IsTrue(p.HasSight);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), p.LastKnown, "the trail did not refresh on sight");
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
