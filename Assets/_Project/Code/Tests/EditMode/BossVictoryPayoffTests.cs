using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The geometry of the boss-death payoff (YT-152): where the flung parts land, when Max counts as
    /// having walked through the exit gate, and that a part's throw is a real arc. Pure maths, so it is
    /// pinned here without building a scene; the end-to-end behaviour lives in the PlayMode test.
    /// </summary>
    public sealed class BossVictoryPayoffTests
    {
        private static readonly BackyardPathLayout Layout = BackyardPathLayout.Default;

        [Test]
        public void ScatterTargets_LandInsideTheArena_ClearOfTheExitDoor()
        {
            Vector3 origin = Layout.ArenaCenter;
            const int count = 5;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = BossVictoryPayoff.ScatterTarget(i, count, origin, Layout);

                Assert.LessOrEqual(Mathf.Abs(p.x), Layout.ArenaHalfWidth,
                    $"part {i} landed outside the arena's side walls");
                Assert.Greater(p.z, Layout.GateZ,
                    $"part {i} landed behind the gate, out of the arena");
                Assert.Less(p.z, Layout.ArenaEndZ,
                    $"part {i} landed in the arena's back wall");

                // A part must never land where collecting it would be read as walking out the door.
                Assert.IsFalse(BossVictoryPayoff.IsAtDoor(p, Layout, 2.5f),
                    $"part {i} landed inside the exit doorway — collecting it would end the run");
            }
        }

        [Test]
        public void ScatterTargets_AreSpreadOut_NotAllOnTheSameSpot()
        {
            Vector3 origin = Layout.ArenaCenter;
            Vector3 a = BossVictoryPayoff.ScatterTarget(0, 5, origin, Layout);
            Vector3 b = BossVictoryPayoff.ScatterTarget(1, 5, origin, Layout);
            Vector3 c = BossVictoryPayoff.ScatterTarget(2, 5, origin, Layout);

            Assert.Greater((a - b).magnitude, 1.5f, "parts 0 and 1 landed on top of each other");
            Assert.Greater((b - c).magnitude, 1.5f, "parts 1 and 2 landed on top of each other");
        }

        [Test]
        public void IsAtDoor_TrueInTheGateway_FalseOutInTheArena()
        {
            // Dead centre of the doorway, right up against the back wall.
            Assert.IsTrue(BossVictoryPayoff.IsAtDoor(
                new Vector3(0f, 0f, Layout.ArenaEndZ - 0.5f), Layout, 2.5f));

            // Standing in the middle of the arena is not "at the door".
            Assert.IsFalse(BossVictoryPayoff.IsAtDoor(Layout.ArenaCenter, Layout, 2.5f));

            // Hard against the back wall but off to the side, past the doorway's width.
            Assert.IsFalse(BossVictoryPayoff.IsAtDoor(
                new Vector3(Layout.GateHalfWidth + 3f, 0f, Layout.ArenaEndZ - 0.5f), Layout, 2.5f));
        }

        [Test]
        public void Arc_LeavesTheBlast_ArcsUp_AndLandsOnTarget()
        {
            var from = new Vector3(0f, 1.4f, 33f);
            var to = new Vector3(6f, 0.6f, 30f);
            const float hop = 2.6f;

            Vector3 start = BossVictoryPayoff.Arc(from, to, 0f, hop);
            Vector3 end = BossVictoryPayoff.Arc(from, to, 1f, hop);
            Vector3 mid = BossVictoryPayoff.Arc(from, to, 0.5f, hop);

            Assert.AreEqual(from.x, start.x, 0.001f);
            Assert.AreEqual(from.z, start.z, 0.001f);
            Assert.AreEqual(from.y, start.y, 0.001f, "the throw should start at the blast height");

            Assert.AreEqual(to.x, end.x, 0.001f);
            Assert.AreEqual(to.z, end.z, 0.001f);
            Assert.AreEqual(to.y, end.y, 0.001f, "the throw should finish exactly on the landing spot");

            Assert.Greater(mid.y, Mathf.Max(from.y, to.y),
                "the part should rise above both endpoints at the top of its arc");
        }
    }
}
