using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MapValidation used to return on the first rule it failed, so a map carrying three simultaneous,
    /// independent violations only ever surfaced one — the rest stayed invisible until that one was
    /// fixed and the whole loop ran again (MV-875 paid for this four times over on a single ticket).
    ///
    /// Fails on base commit ef91084: <see cref="MapValidation.Validate"/> still short-circuits
    /// (<c>Structure(map, out reason) &amp;&amp; Links(...) &amp;&amp; Actors(...) &amp;&amp;
    /// Reachable(...) &amp;&amp; Cover(...)</c>), so only the Actors-phase violation ('gate_x' opening on
    /// a non-factory key) is ever reported — the Reachable-phase ('vault' unreachable) and Cover-phase
    /// ('arena' pinched shut) violations, both present in the very same map, never appear in
    /// <c>reason</c> at all.
    /// </summary>
    public sealed class MV879MultiViolationReportingTests
    {
        /// <summary>Three rooms, three unrelated defects, all present at once: a gate that opens on a
        /// key that is not a factory (Actors), a boss zone no chain of links reaches (Reachable), and
        /// cover that pinches a fight room's free channel below the 2 m floor (Cover) — one violation
        /// per phase, so this exercises three different rules, not three instances of one.</summary>
        private static MapData ThreeSimultaneousViolations()
        {
            return new MapData
            {
                name = "MV-879 fixture",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "start", type = "entry", x = 0f, z = 0f, width = 20f, depth = 20f },
                    new MapZone { id = "arena", type = "open", x = 0f, z = 20f, width = 20f, depth = 20f },
                    new MapZone { id = "vault", type = "boss", x = 50f, z = 0f, width = 20f, depth = 20f },
                },
                // 'vault' is deliberately named by no link at all — the flood fill from the spawn can
                // never reach it (the Reachable-phase violation).
                links = new[] { new MapLink { from = "start", to = "arena", doorway = 4f, gate = "gate_x" } },
                entities = new[]
                {
                    new MapEntity { id = "spawn0", kind = "playerSpawn", x = 0f, z = 0f },

                    // Actors-phase violation: opens on a key that names no entity at all.
                    new MapEntity
                    {
                        id = "gate_x", kind = "gate", x = 0f, z = 10f, height = 3f, depth = 0.6f,
                        opensOn = "ghost_key",
                    },

                    // Cover-phase violation: two hedges leave only 1.8 m of free channel across
                    // 'arena' (20 m wide) at z=19-21 — under the 2 m MinFreeChannel floor.
                    new MapEntity
                    {
                        id = "hedgeA", kind = "cover", x = -5.45f, z = 20f, width = 9.1f, depth = 2f, height = 2f,
                    },
                    new MapEntity
                    {
                        id = "hedgeB", kind = "cover", x = 5.45f, z = 20f, width = 9.1f, depth = 2f, height = 2f,
                    },
                },
            };
        }

        [Test]
        public void Validate_ReportsAllThreeSimultaneousViolationsTogether()
        {
            bool valid = MapValidation.Validate(ThreeSimultaneousViolations(), out string reason);

            Assert.IsFalse(valid, "a map with three known-bad rules must not validate");

            // Actors: the gate names a key that is not a factory.
            StringAssert.Contains("gate_x", reason);
            StringAssert.Contains("ghost_key", reason);

            // Reachable: the boss zone 'vault' has no chain of links reaching it.
            StringAssert.Contains("vault", reason);
            StringAssert.Contains("cannot be walked to", reason);

            // Cover: 'arena' is pinched below the free-channel floor.
            StringAssert.Contains("arena", reason);
            StringAssert.Contains("pinches", reason);
        }
    }
}
