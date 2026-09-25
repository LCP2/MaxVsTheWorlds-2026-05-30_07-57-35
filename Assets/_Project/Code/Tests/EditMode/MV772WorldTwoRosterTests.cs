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
    ///
    /// MV-865 (World 2 re-author) rebuilt areas 1-14 straight from Lee's sheet — his own drawn cell
    /// counts, not a re-run of this ticket's conversion table — raising the total to 412 placements and,
    /// since the sheet freely mixes in plenty of rusher/gunner/blinker/heavy/bruiser/brute/launcher/
    /// bolter cells on its own authority, settling World 2's own kinds (sludger/charger/turret/lurker)
    /// at 49.3% (203/412) rather than the narrower 57-64% this ticket's conversion alone produced. The
    /// floor is widened to comfortably admit that, while staying well clear of the ~37% a genuine
    /// full-revert-to-World-1 would read at — the actual regression this assertion exists to catch.
    ///
    /// MV-875 (workbook edits + a1-a13 conversion repair) dropped the total to 407 (a9 brute 2→1, a11
    /// turret 4→3/gunner 25→24/sludger 14→13/launcher 1→0) and moved a13's garrison and 45 other entries
    /// (a7/a8/a10/a11/a12/a13) by 0.02-0.35 m within their own authored cell for engine-side collider
    /// clearance (<see cref="MapValidation.WorldGarrison"/>, MV-655) — both expected below were recomputed
    /// against the shipped config at the time, not hand-picked.
    ///
    /// MV-900 (World 2 V10, Lee's signed-off config) re-authors a3/a13/a16/a18/a19 and the a10-a12 decks,
    /// raising the total to 572 (ticket's own "572 robots, 41 Replicators") and moving a great many
    /// entries' x/z — both expected below are recomputed again against the V10 shipped config, still a
    /// plain sum/hash over real data, not hand-picked.
    ///
    /// MV-942 (Lee, 2026-09-25) converts every a16 turret/sludger (25 + 18 = 43 entries) to bolter — a16's
    /// deck robots misbehaved on TestFlight and Lee's own decision replaces them outright, kind only, so
    /// the coordinate hash is unchanged. That drops the World 2 kinds share from 48.8% (279/572) to 41.3%
    /// (236/572), below the old 45% floor. The floor is widened to 40% — still comfortably clear of the
    /// ~37% reverted-conversion signal this assertion actually guards, per the same reasoning MV-865/875
    /// used each time a legitimate re-author moved this number.
    /// </summary>
    public sealed class MV772WorldTwoRosterTests
    {
        // Recomputed against the V10 shipped config (MV-900) — still a guard against an UNRELATED drift,
        // not an authored constant: derived from the real config, not hand-picked.
        private const uint ExpectedCoordinateHash = 3219086536u;

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

            Assert.AreEqual(572, total, "World 2's total garrison placement count (MV-900 V10 config)");

            int Count(string kind) => counts.TryGetValue(kind, out int n) ? n : 0;
            int worldTwoKinds = Count("sludger") + Count("charger") + Count("turret") + Count("lurker");
            double share = (double)worldTwoKinds / total;
            // MV-865 re-authored areas 1-14 straight from Lee's sheet, which freely mixes in plenty of
            // World 1-shared kinds on its own authority — dropping the resolved share from 57-64% to
            // 49.3% (203/412). MV-875's edits move it again, to 47.7% (194/407). MV-942 converts a16's
            // 43 turret/sludger entries to bolter, dropping it again to 41.3% (236/572). The floor is
            // widened each time to still comfortably catch the thing this AC actually guards (a reverted
            // conversion, at ~37%), not loosened to the point of catching nothing.
            Assert.That(share, Is.InRange(0.40, 0.64),
                $"World 2's own kinds (sludger/charger/turret/lurker) must land between 40% and 64% of all " +
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
