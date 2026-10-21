using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    // Guards the cc-verify EditMode shard partition (MV-1271): the regex patterns
    // in cc-verify-shards.txt must place every test full name in exactly one
    // shard. Every shard is an explicit, exact-length pattern - there is no
    // synthesized "catch everything else" complement, because Unity's
    // -testFilter silently runs the WHOLE suite unfiltered for a filter whose
    // own match can be zero-width or of variable length (see
    // cc-verify-shard-filter.ps1's header comment). Exercised against a fixed
    // sample rather than the live test tree so this stays fast, deterministic,
    // and independent of whatever fixtures happen to exist on a given day.
    public class MV1271ShardFilterPartitionTests
    {
        private static readonly string[] SampleFullNames =
        {
            "MaxWorlds.Tests.EditMode.MV999FakeTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.MV1300FutureTicketTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.Mv505DiagnosticOverlayTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.AbilityTuningTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.HomingMissileTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.IntroVideoTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.SurfaceMaterialTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.WeaponSystemStateTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.WorldBarTests.SomeMethod",
            "MaxWorlds.Tests.EditMode.ZzShardProbe.AlwaysFails",
            "MaxWorlds.Tests.GeneratedAudioTests.SomeMethod",
        };

        [Test]
        public void EveryFullNameLandsInExactlyOneShard()
        {
            string repoRoot = Directory.GetParent(Application.dataPath).FullName;
            string shardsFile = Path.Combine(repoRoot, "cc-verify-shards.txt");
            Assert.IsTrue(File.Exists(shardsFile), $"cc-verify-shards.txt not found at {shardsFile}");

            List<(string Label, string Pattern)> shards = ReadShards(shardsFile);
            Assert.That(shards.Count, Is.GreaterThan(0), "cc-verify-shards.txt has no pattern lines");

            foreach (string fullName in SampleFullNames)
            {
                var matched = new List<string>();
                foreach ((string label, string pattern) in shards)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(fullName, pattern))
                    {
                        matched.Add(label);
                    }
                }

                Assert.That(matched.Count, Is.EqualTo(1),
                    $"'{fullName}' matched {matched.Count} shard(s): [{string.Join(", ", matched)}] - expected exactly 1");
            }
        }

        private static List<(string Label, string Pattern)> ReadShards(string shardsFile)
        {
            var shards = new List<(string Label, string Pattern)>();
            foreach (string line in File.ReadAllLines(shardsFile))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                Assert.That(parts.Length, Is.EqualTo(2), $"malformed shard line (expected '<name>\\t<regex>'): {line}");
                shards.Add((parts[0], parts[1]));
            }
            return shards;
        }
    }
}
