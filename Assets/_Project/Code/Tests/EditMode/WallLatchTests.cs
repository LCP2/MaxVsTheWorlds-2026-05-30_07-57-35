using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-447 causes 1 and 2: <c>RobotEnemy</c>'s old <c>_wallNormal</c>/<c>_wallTimer</c> duo
    /// (YT-68) released a slide 0.2s after the last touch (cause 1 — a limit cycle: the desired
    /// direction swings straight back into the wall it had just cleared, touches again, repeats at
    /// 3-5 Hz) and kept only the LAST hit normal reported per frame (cause 2 — with no ordering
    /// guarantee across simultaneous <c>OnControllerColliderHit</c> calls at a corner, whichever
    /// contact "won" flipped from frame to frame, so the remembered normal — and the steered
    /// direction — swung close to 90 degrees frame to frame). <see cref="WallLatch"/> replaces both.
    /// </summary>
    public sealed class WallLatchTests
    {
        [Test]
        public void SimultaneousCornerHits_SummedNotOverwritten_OutputStaysStableRegardlessOfOrder()
        {
            // The cause-2 repro: two hit normals ~90 degrees apart (a corner) reported every tick, in
            // an order that flips tick to tick — exactly what physics gives no guarantee against. The
            // OLD "last hit wins" rule would have the stored normal flip with the call order and the
            // steered direction swing close to 90 degrees. Summing makes the combined normal — and
            // therefore the output direction — the same regardless of order.
            var latch = new WallLatch();
            Vector3 desired = Vector3.forward;
            Vector3 normalA = new Vector3(-1f, 0f, -0.3f).normalized;
            Vector3 normalB = new Vector3(1f, 0f, -0.3f).normalized;
            Vector3 pos = Vector3.zero;

            Vector3? prevDir = null;
            for (int tick = 0; tick < 12; tick++)
            {
                if (tick % 2 == 0) { latch.NoteHit(normalA); latch.NoteHit(normalB); }
                else { latch.NoteHit(normalB); latch.NoteHit(normalA); }

                Vector3 dir = latch.Tick(desired, pos, 0.016f, 1f);

                if (prevDir.HasValue)
                {
                    float angle = Vector3.Angle(prevDir.Value, dir);
                    Assert.Less(angle, 25f,
                        $"tick {tick}: direction swung {angle:F1} degrees from the previous tick — the " +
                        "same pair of simultaneous hits must not steer differently by call order");
                }
                prevDir = dir;
            }
        }

        [Test]
        public void LastWriterWins_OnTheSameAlternatingInput_SwingsFarPast25Degrees()
        {
            // Sanity check that the scenario above is a real regression, not a vacuous bound: replaying
            // the same two corner normals through f2aab92's rule (whichever hit was reported LAST
            // simply overwrites the stored normal) swings well past the bound the fix must stay under.
            Vector3 desired = Vector3.forward;
            Vector3 normalA = new Vector3(-1f, 0f, -0.3f).normalized;
            Vector3 normalB = new Vector3(1f, 0f, -0.3f).normalized;

            Vector3 dirWhenBWasLast = ObstacleSteering.SlideAlongWall(desired, normalB, 1f);
            Vector3 dirWhenAWasLast = ObstacleSteering.SlideAlongWall(desired, normalA, 1f);

            float angle = Vector3.Angle(dirWhenBWasLast, dirWhenAWasLast);
            Assert.Greater(angle, 25f,
                "overwriting with whichever hit came last really does swing past the 25-degree bound " +
                "the fix must stay under — otherwise this isn't reproducing MV-447 cause 2 at all");
        }

        [Test]
        public void ClearingContact_DoesNotReleaseTheLatch()
        {
            // Cause 1: the old timer released the moment contact was lost for `wallMemory` (0.2s). The
            // latch must hold on losing contact — only progress-distance or the duration ceiling may
            // release it.
            var latch = new WallLatch();
            var wallNormal = new Vector3(0f, 0f, -1f);
            Vector3 desired = Vector3.right;

            latch.NoteHit(wallNormal);
            latch.Tick(desired, Vector3.zero, 0.016f, 1f);
            Assert.IsTrue(latch.IsActive, "the first tick with a hit must latch");

            // Many ticks with no further hits, no meaningful displacement — contact is gone, but the
            // latch must still be held.
            for (int i = 0; i < 20; i++)
                latch.Tick(desired, Vector3.zero, 0.05f, 1f); // 1s total, under the 2.5s ceiling

            Assert.IsTrue(latch.IsActive, "losing contact released the latch — this is the MV-447 limit cycle");
        }

        [Test]
        public void ProgressPastTheThreshold_ReleasesTheLatch()
        {
            var latch = new WallLatch();
            var wallNormal = new Vector3(0f, 0f, -1f);
            Vector3 desired = Vector3.right; // slides along +X, i.e. along the wall

            latch.NoteHit(wallNormal);
            Vector3 pos = Vector3.zero;
            Vector3 dir = latch.Tick(desired, pos, 0.016f, 1f);
            Assert.IsTrue(latch.IsActive);

            // Walk it along the latched direction until it should have cleared the obstacle.
            for (int i = 0; i < 200 && latch.IsActive; i++)
            {
                pos += dir * 0.02f; // 4 m/s * 5ms-ish steps
                dir = latch.Tick(desired, pos, 0.005f, 1f);
            }

            Assert.IsFalse(latch.IsActive,
                $"displacement past {WallLatch.ProgressDistance} m along the wall must release the latch");
        }

        [Test]
        public void HardDurationCeiling_ReleasesTheLatchEvenWithNoProgress()
        {
            var latch = new WallLatch();
            var wallNormal = new Vector3(0f, 0f, -1f);
            Vector3 desired = Vector3.forward; // straight into the wall: head-on, no along-wall progress
            Vector3 pos = Vector3.zero;

            latch.NoteHit(wallNormal);
            latch.Tick(desired, pos, 0.016f, 1f);
            Assert.IsTrue(latch.IsActive);

            // Pin the robot in place (no displacement at all) and just burn time past the ceiling.
            const float dt = 0.1f;
            float elapsed = 0f;
            bool released = false;
            while (elapsed < WallLatch.MaxDuration + 0.2f)
            {
                latch.Tick(desired, pos, dt, 1f);
                elapsed += dt;
                if (!latch.IsActive) { released = true; break; }
            }

            Assert.IsTrue(released, "the hard duration ceiling must release the latch even with zero progress");
        }

        [Test]
        public void JitterUnderTheReplaceAngle_KeepsTheSteeredDirectionStable()
        {
            // A normal that wobbles a few degrees around the same physical surface (numeric noise, a
            // slightly curved collider) must not be treated as a new surface — cause 2's "genuinely
            // different" test is the angle threshold, not any change at all.
            var latch = new WallLatch();
            var baseNormal = new Vector3(0f, 0f, -1f);
            var jitteredNormal = Quaternion.Euler(0f, 10f, 0f) * baseNormal; // well under the 35 degree bound
            Vector3 desired = Vector3.right;
            Vector3 pos = Vector3.zero;

            latch.NoteHit(baseNormal);
            Vector3 dir1 = latch.Tick(desired, pos, 0.016f, 1f);

            latch.NoteHit(jitteredNormal);
            Vector3 dir2 = latch.Tick(desired, pos, 0.016f, 1f);

            Assert.Less(Vector3.Angle(dir1, dir2), 5f,
                "small jitter on the same surface must not visibly change the steered direction");
        }
    }
}
