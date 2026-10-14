using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1184: a30 (The Great Lawn)'s garrison moved off the old top exit (z 124.5-130.5,
    /// where Max could reach Big Bermuda 2 and the exit without passing it) into the centre of the
    /// upper lawn, so Max has to fight through it to reach Big Bermuda 2 by the exit corridor. Loads
    /// the real shipped <c>world1_config.json</c> so this cannot drift out of sync with what ships.</summary>
    public sealed class MV1184A30GarrisonCentredOnUpperLawnTests
    {
        [Test]
        public void A30_GarrisonSitsInTheUpperLawnCentreAndBossesAreUnmoved()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the shipped world1_config.json failed to load — see the error log above");

            WorldArea area = cfg.Area("a30");
            Assert.IsNotNull(area, "world1_config.json has no area a30");

            Assert.AreEqual(30, area.garrison.Length, "a30 must author exactly 30 garrison positions");

            int gunner = 0, bruiser = 0, blinker = 0, brute = 0, bolter = 0, launcher = 0, heavy = 0;
            foreach (WorldGarrisonEntry e in area.garrison)
            {
                Assert.That(e.x, Is.InRange(312f, 325.5f), $"a30 garrison entry ({e.x}, {e.z}) is outside the upper-lawn-centre x range");
                Assert.That(e.z, Is.InRange(102.5f, 119.5f), $"a30 garrison entry ({e.x}, {e.z}) is outside the upper-lawn-centre z range");

                switch (e.kind)
                {
                    case "gunner": gunner++; break;
                    case "bruiser": bruiser++; break;
                    case "blinker": blinker++; break;
                    case "brute": brute++; break;
                    case "bolter": bolter++; break;
                    case "launcher": launcher++; break;
                    case "heavy": heavy++; break;
                    default: Assert.Fail($"a30 garrison entry at ({e.x}, {e.z}) has unexpected kind '{e.kind}'"); break;
                }
            }

            Assert.AreEqual(4, gunner, "a30 garrison gunner count");
            Assert.AreEqual(5, bruiser, "a30 garrison bruiser count");
            Assert.AreEqual(3, blinker, "a30 garrison blinker count");
            Assert.AreEqual(3, brute, "a30 garrison brute count");
            Assert.AreEqual(8, bolter, "a30 garrison bolter count");
            Assert.AreEqual(3, launcher, "a30 garrison launcher count");
            Assert.AreEqual(4, heavy, "a30 garrison heavy count");

            WorldBoss[] bosses = area.Bosses();
            Assert.AreEqual(2, bosses.Length, "a30 must resolve exactly two bosses");
            Assert.AreEqual(329f, bosses[0].x, 0.01f, "a30 boss 1 x (Big Bermuda 2)");
            Assert.AreEqual(110f, bosses[0].z, 0.01f, "a30 boss 1 z (Big Bermuda 2)");
            Assert.AreEqual(320f, bosses[1].x, 0.01f, "a30 boss 2 x (Big Bermuda 1)");
            Assert.AreEqual(86f, bosses[1].z, 0.01f, "a30 boss 2 z (Big Bermuda 1)");
        }
    }
}
