using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1146 (the one new test, per CC_AUTONOMY's testing policy): a source-shape guard over
    /// Runtime/ and Tests/EditMode/ that fails, naming file/line/text, on any line matching one of
    /// the ticket's five literal-world-number patterns — the tripwire that stops the next
    /// "worldIndex &gt;= 2 means the Reef" assumption coming back once the City lands between
    /// today's World 2 and World 3.
    ///
    /// A tripwire, not a parser (ticket's own "Do not re-raise" — "whether to make the guard
    /// smarter than the five patterns: no"): it matches raw source text, so a handful of lines that
    /// already use item 1's own allowed forms ("the world after the one just played" as
    /// <c>index + 1</c>, "the last world" as <c>Count - 1</c>) still collide with the patterns' own
    /// bare <c>\b[1-9]\b</c> and are named on <see cref="AllowList"/> with a reason instead of
    /// rewritten a second time.
    ///
    /// Fails on the commit this ticket starts from: <c>Dev/CaptureDirector.cs</c>,
    /// <c>Dev/UiScreensDirector.cs</c>, <c>UI/HomeScreen.cs</c> and dozens of EditMode test files
    /// still set a world by literal number.
    /// </summary>
    public sealed class NoLiteralWorldNumbersTests
    {
        private static readonly string RuntimeRoot =
            Path.Combine(Application.dataPath, "_Project", "Code", "Runtime");

        private static readonly string TestsRoot =
            Path.Combine(Application.dataPath, "_Project", "Code", "Tests", "EditMode");

        /// <summary>The ticket's own five patterns, verbatim. Pattern 2 adds a <c>(?!=)</c> after the
        /// <c>[:=]</c> so it does not also fire on an <c>==</c> comparison already covered by pattern
        /// 1 — the ticket's own parenthetical ("an assignment or a named argument; not ==") only
        /// resolves to the intended lines with that guard in place; without it, a bare substring
        /// search matches the second '=' of "==" as pattern 2's own leading '='.</summary>
        private static readonly Regex[] Patterns =
        {
            new Regex(@"[wW]orldIndex *(==|!=|>=|<=|>|<) *[1-9]", RegexOptions.Compiled),
            new Regex(@"[wW]orldIndex *[:=](?!=) *[1-9]", RegexOptions.Compiled),
            new Regex(@"(ForWorld|ApplyWeaponCoreMorph|ApplyWorldLoadout|UseWorld|KeyForIndex|StartSlotWorld|OnWorldDevStart|WorldTransitions\.For|ConfigureWorld|EnabledInWorld|SnapshotMaxLevels)\([^)]*\b[1-9]\b", RegexOptions.Compiled),
            new Regex(@"(fromWorld|toWorld)(Index)? *(==|!=|>=|<=|>|<|:|=) *[1-9]", RegexOptions.Compiled),
            new Regex(@"AreEqual\( *[1-9] *, *[^)]*WorldIndex", RegexOptions.Compiled),
        };

        /// <summary>(file name, exact trimmed line, reason) — kept to the ticket's own cap of 12.
        /// Every entry here is a line that already expresses its number the way item 1 asks for; the
        /// patterns above are a blunt textual tripwire with no notion of "this digit is arithmetic,
        /// not a literal", so each one still fires and is named here instead.</summary>
        private static readonly (string File, string Line, string Reason)[] AllowList =
        {
            ("WorldCatalog.cs",
             "Entry = new WorldTransitionEntry(fromWorld: 1, exitWall: Wall.E, exitDoorPos: 0.509615384615f, corridorLength: 30f,",
             "the Stormdrain row's own FromWorld field, authored inside WorldCatalog.BuildShippedRows itself — " +
             "WorldCatalog.IndexOf(WorldIds.Stormdrain) can't be called here because s_rows (what IndexOf " +
             "searches) is still being constructed by this very array literal; FromWorld is also never read " +
             "anywhere outside this constructor (grep confirms no '.FromWorld' consumer exists)."),

            ("WorldFinaleGate.cs",
             "WeaponSystemState.ApplyWeaponCoreMorph(_weaponBeatPlayedWorld + 1);",
             "item 1's own allowed form for \"the world after the one just played\" (index + 1) — the pattern's " +
             "bare \\b[1-9]\\b matches the arithmetic literal \"1\" regardless."),

            ("WorldTransitionCoverageTests.cs",
             "WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(i + 1));",
             "loops over every from-world generically (i + 1 is \"the next world\", item 1's own allowed " +
             "form) — there is no single named world to substitute since this runs once per transition."),

            ("WorldTransitionCoverageTests.cs",
             "Assert.IsNull(WorldTransitions.For(WorldLibrary.Count - 1),",
             "item 1's own allowed form for \"the last world\" (Count - 1) — the pattern's bare \\b[1-9]\\b " +
             "matches the arithmetic literal \"1\" regardless."),
        };

        [Test]
        public void NoLiteralWorldNumberRemainsOutsideTheAllowList()
        {
            Assert.LessOrEqual(AllowList.Length, 12, "the allow-list must stay at or under the ticket's own cap of 12 entries");

            var offenders = new List<string>();
            offenders.AddRange(ScanRoot(RuntimeRoot));
            offenders.AddRange(ScanRoot(TestsRoot));

            Assert.IsEmpty(offenders,
                $"{offenders.Count} line(s) outside the allow-list still set a world by literal number " +
                "(MV-1146). First twenty offenders:\n" + string.Join("\n", offenders.Take(20)));
        }

        private static IEnumerable<string> ScanRoot(string root)
        {
            Assert.IsTrue(Directory.Exists(root), $"guard root not found: {root}");

            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(path);
                if (fileName == "NoLiteralWorldNumbersTests.cs") continue; // the allow-list's own quoted offender text is not a live site

                string[] lines = File.ReadAllLines(path);

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("//")) continue;

                    bool matches = Patterns.Any(p => p.IsMatch(trimmed));
                    if (!matches) continue;

                    bool allowed = AllowList.Any(a => a.File == fileName && a.Line == trimmed);
                    if (allowed) continue;

                    yield return $"{fileName}:{i + 1}: {trimmed}";
                }
            }
        }
    }
}
