using System.Globalization;
using System.Text;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-772 — World 2's garrison was 63% World 1 roster (bolter/gunner/heavy/bruiser/blinker/launcher):
    /// a player crossing into the Stormdrain mostly met the enemies they had just learned in World 1, in a
    /// different colour. The ticket's conversion table (bolter/gunner→turret, heavy/bruiser→charger,
    /// launcher→sludger, blinker→lurker, brute untouched) re-authors <c>kind</c> only — x/z/level stay
    /// exactly as authored, with one documented exception: a handful of blinker→lurker conversions landed
    /// off any authored grate (<see cref="MapValidation.WorldLurkerGrates"/>, MV-688/MV-724, requires every
    /// lurker to sit on one). Per the ticket's own fallback, each such entry was either moved onto the
    /// nearest grate in its own area (a16 x2, a21 x1 — the only areas with off-grate conversions that also
    /// carry a grate) or left as a blinker (a9 x1, a15 x1, a19 x2 — areas with no grate at all to move onto).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) asserting RESOLVED values (Tier 2): the loaded
    /// composition share, that MapValidation still passes (which transitively proves every lurker is on a
    /// grate), and a hash of every entry's x/z guarding that nothing else moved.
    ///
    /// Proven to fail on d63a61c (pre-fix, World 2 still 63% World 1 roster): the four-kind share assertion
    /// below fails with "Expected: greater than or equal to 0.58 ... But was 0.369811..." (98 of 265
    /// placements were sludger/charger/turret/lurker before this ticket).
    /// </summary>
    public sealed class MV772WorldTwoRosterTests
    {
        private const uint ExpectedCoordinateHash = 1620379732u;

        [Test]
        public void WorldTwoRoster_MatchesTheMV772Conversion()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");

            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var hashInput = new StringBuilder();
            int total = 0;

            foreach (WorldArea area in cfg.areas)
            {
                if (area.garrison == null) continue;
                foreach (WorldGarrisonEntry entry in area.garrison)
                {
                    total++;
                    counts[entry.kind] = counts.TryGetValue(entry.kind, out int existing) ? existing + 1 : 1;
                    hashInput.Append(entry.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                             .Append(entry.z.ToString("F2", CultureInfo.InvariantCulture)).Append(';');
                }
            }

            Assert.AreEqual(265, total, "World 2's total garrison placement count must not change — only kind is re-authored");

            int Count(string kind) => counts.TryGetValue(kind, out int n) ? n : 0;
            int worldTwoKinds = Count("sludger") + Count("charger") + Count("turret") + Count("lurker");
            double share = (double)worldTwoKinds / total;
            Assert.That(share, Is.InRange(0.58, 0.64),
                $"World 2's own kinds (sludger/charger/turret/lurker) must land between 58% and 64% of all " +
                $"placements after the re-authoring, got {share:P1} ({worldTwoKinds}/{total})");

            double turretShare = (double)Count("turret") / total;
            Assert.That(turretShare, Is.LessThanOrEqualTo(0.20),
                $"turret must stay stationary area denial, not a fifth-plus of the whole garrison — got {turretShare:P1}");

            Assert.AreEqual(ExpectedCoordinateHash, Fnv1a32(hashInput.ToString()),
                "an entry's x/z moved that shouldn't have — the conversion must re-author kind only, aside " +
                "from the documented MV-772 grate-fix exceptions already baked into this hash");

            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string reason), reason);
        }

        private static uint Fnv1a32(string s)
        {
            uint hash = 0x811c9dc5;
            foreach (char c in s)
            {
                hash ^= c;
                hash *= 0x01000193;
            }
            return hash;
        }
    }
}
