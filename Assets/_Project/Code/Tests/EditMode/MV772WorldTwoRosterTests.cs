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
    /// clearance (<see cref="MapValidation.WorldGarrison"/>, MV-655) — both expected below are recomputed
    /// against the new shipped config, not hand-picked.
    ///
    /// MV-900 (Lee's signed-off V8 config) raised the total to 564 (a3 Up/a13/a13 Up/a12-a10 Up/a16/a19
    /// re-authored per the ticket's own "What changed" list) and moved 10 of a19's entries within their
    /// own authored cell — 9 for engine-side collider clearance and one restoring the Grate Lurker to its
    /// pre-V8 authored position (triage ruling, 2026-09-24) — so both expected values below are again
    /// recomputed against the new shipped config, not hand-picked.
    /// </summary>
    public sealed class MV772WorldTwoRosterTests
    {
        // MV-900 re-authored World 2 from Lee's V8 workbook and nudged 10 of a19's entries (9 for
        // engine-side collider clearance, 1 restoring the pre-V8 Grate Lurker position) — recomputed
        // against the shipped config, still a guard against an UNRELATED drift (not an authored constant:
        // derived from the real config, not hand-picked).
        private const uint ExpectedCoordinateHash = 1794408866u;

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

            Assert.AreEqual(564, total, "World 2's total garrison placement count (MV-900 V8 config)");

            int Count(string kind) => counts.TryGetValue(kind, out int n) ? n : 0;
            int worldTwoKinds = Count("sludger") + Count("charger") + Count("turret") + Count("lurker");
            double share = (double)worldTwoKinds / total;
            // MV-865 re-authored areas 1-14 straight from Lee's sheet, which freely mixes in plenty of
            // World 1-shared kinds on its own authority — dropping the resolved share from 57-64% to
            // 49.3% (203/412). MV-875's edits move it again, to 47.7% (194/407). MV-900's V8 config lands
            // at 46.3% (261/564) — still well inside the same range. The floor is widened to still
            // comfortably catch the thing this AC actually guards (a reverted conversion, at ~37%), not
            // loosened to the point of catching nothing.
            Assert.That(share, Is.InRange(0.45, 0.64),
                $"World 2's own kinds (sludger/charger/turret/lurker) must land between 45% and 64% of all " +
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
