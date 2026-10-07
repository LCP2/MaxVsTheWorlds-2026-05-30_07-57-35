using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1138: LogViolations re-emitted the same warnings, each with a full stack trace, on
    /// every call - hundreds of EditMode tests and every world load re-logged the same few hundred
    /// messages, writing roughly 137,000 warnings into a single test run's log. Pins that a message is
    /// logged once per distinct violation and never again until <see cref="LevelDesignVerifier.ResetLoggedForTests"/>
    /// is called, against a fixture that actually breaks the checked-in minRoomDimensionMetres
    /// constraint so the precondition is asserted, not assumed.</summary>
    public sealed class LevelDesignVerifierLogOnceTests
    {
        [Test]
        public void LogViolations_LogsEachMessageOnlyOnce_UntilReset()
        {
            var cfg = new WorldConfig
            {
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "tiny",
                        index = 1,
                        role = "normal",
                        origin = new WorldAreaOrigin { x = 0, z = 0 },
                        size = new WorldAreaSize { w = 5, d = 5 },
                        hasShed = false,
                        garrisonDensity = "none",
                    },
                },
            };

            Assert.IsTrue(LevelDesignVerifier.TryLoadConstraints(out LevelDesignConstraints constraints, out string reason), reason);
            List<string> violations = LevelDesignVerifier.Violations(cfg, constraints);
            Assert.IsTrue(violations.Count > 0, "fixture must actually break a constraint, or this test proves nothing");

            LevelDesignVerifier.ResetLoggedForTests();
            Assert.AreEqual(violations.Count, CountWarnings(cfg),
                "the first LogViolations after a reset must log one warning per distinct violation");

            Assert.AreEqual(0, CountWarnings(cfg),
                "a second LogViolations with no reset must log nothing new - this is what fails on base");

            LevelDesignVerifier.ResetLoggedForTests();
            Assert.AreEqual(violations.Count, CountWarnings(cfg),
                "after ResetLoggedForTests, the same messages must log again");
        }

        private static int CountWarnings(WorldConfig cfg)
        {
            int count = 0;

            void Handler(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.StartsWith("[LevelDesignVerifier]")) count++;
            }

            Application.logMessageReceived += Handler;
            try
            {
                LevelDesignVerifier.LogViolations(cfg);
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }

            return count;
        }
    }
}
