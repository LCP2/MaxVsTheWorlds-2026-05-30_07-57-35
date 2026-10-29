using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1315 (the one new test, per CC_AUTONOMY's testing policy): MV-1308's original "3 m from
    /// cover/gates" pylon-clearance ask could not be met anywhere in the authored 6-10 m ring for 11 of
    /// the City's 98 required pylon instances (Lee's own full 360° x 6-10 m sweep, this ticket's
    /// comments) — not a search-quality miss, and not fixable by thinning Lee's authored alley-room
    /// cover (docs/DECISIONS.md section 4, Authority). Lee decided 29 Oct 2026: 2.0 m for every pylon,
    /// with one named exemption — the pylon feeding a5_rep1 at its measured best, 1.85 m.
    ///
    /// Tier 2 (Rule 2): both halves of the assertion read a RESOLVED value —
    /// <see cref="MapValidation.RequiredPylonCoverClearance"/>'s own return, and a pylon count derived
    /// from city_config.json's real per-area relay counts under MV-1308's own banding rule (a2-a12 one
    /// pylon per relay, a13-a29 two) — never a bare authored constant echoed back at itself.
    ///
    /// Fails on 6dc9bed (the commit before this ticket): <c>MapValidation</c> carries no pylon-clearance
    /// rule at all, so <c>RequiredPylonCoverClearance</c> does not exist to call — a compile error. See
    /// the fix comment for the captured failure output.
    /// // Guards MV-1315
    /// </summary>
    public sealed class MV1315PylonClearanceRuleTests
    {
        [Test]
        public void RequiredPylonCoverClearance_Is2mForEveryRelayExceptTheNamedA5Rep1Exemption()
        {
            // --- the rule itself: 1.85 m only for the named exemption, 2.0 m for every other relay,
            // including the other four relays MV-1308's own measurement found could not clear 3.0 m.
            Assert.AreEqual(1.85f, MapValidation.RequiredPylonCoverClearance("a5_rep1"), 0.001f);

            foreach (string feedsId in new[]
                     { "a11_rep1", "a11_rep2", "a17_rep1", "a19_rep2", "a25_rep2", "a25_rep3", "a2_rep1" })
            {
                Assert.AreEqual(MapValidation.MinPylonCoverClearance,
                    MapValidation.RequiredPylonCoverClearance(feedsId), 0.001f,
                    $"{feedsId} is not the named exemption and must resolve to the general bar");
            }

            // --- the rule must cover every City relay that will actually need a pylon: a2-a29, 1 pylon
            // per relay for a2-a12 and 2 for a13-a29 (MV-1308's own banding), summed off the REAL
            // per-area replicator counts in city_config.json — not a hardcoded total.
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load — see the error log above.");

            int requiredPylons = 0;
            foreach (WorldArea area in city.areas)
            {
                if (area.index < 2 || area.index > 29) continue;
                int perRelay = area.index <= 12 ? 1 : 2;
                requiredPylons += (area.replicators?.Length ?? 0) * perRelay;
            }

            Assert.AreEqual(98, requiredPylons,
                "MV-1308's 98-pylon figure no longer matches city_config.json's real relay counts");
        }
    }
}
