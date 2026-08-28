using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-600 — LegacyRuntime.ttf (<c>Resources.GetBuiltinResource&lt;Font&gt;</c>, wired up by
    /// <c>HudFont.Get()</c>) has no glyph for U+2014 EM DASH, so any player-facing string carrying one
    /// renders as a blank gap instead of a dash: first spotted in <see cref="DeathOverlay"/>'s body copy
    /// and the world-JSON area names it interpolates, then corroborated in the MV-450 dev tuning header.
    /// <c>WeaponsScreen.cs:858</c> already established the fix pattern (ASCII hyphen, not a font swap —
    /// out of scope per the ticket).
    ///
    /// One <c>[Test]</c> method, two assertions, per CC_AUTONOMY.md's testing policy ("at most one new
    /// test per ticket"): a source-shape scan over <c>Runtime/UI/*.cs</c> and the world JSON's
    /// <c>"name"</c> fields (AC1 — the ticket's own words: "the criterion that stops the next em-dash
    /// arriving"), plus a resolved-value check on <see cref="DeathOverlay.BodyText"/> for a real,
    /// JSON-sourced area name (AC3). Both assertions guard the same regression; splitting them into two
    /// <c>[Test]</c> methods would not cover anything this one doesn't already assert.
    /// </summary>
    public sealed class MV600AsciiOnlyPlayerFacingTextTests
    {
        /// <summary>
        /// Non-ASCII codepoints confirmed safe to keep, keyed by the file they appear in. Not a
        /// loophole — every entry is a codepoint MV-600's own investigation did not find broken and was
        /// told not to touch (see the ticket's "Do not re-raise" section).
        ///
        /// MapScreen.cs / WeaponsScreen.cs '×' (U+00D7): the dingbat CLOSE glyph. MapScreen.cs:220's own
        /// comment already documents it as deliberate ("U+00D7: HudFont's LegacyRuntime.ttf has no
        /// coverage for a dingbat X") — the ticket names this exact line as the allow-list precedent.
        /// WeaponsScreen.cs's CLOSE button uses the identical glyph.
        ///
        /// WeaponsScreen.cs '·' (U+00B7): the FORGED/CELLS · SLOT and draft-caption middle-dot
        /// separators. Latin-1 range — not the em-dash bug's General Punctuation block, and not in
        /// MV-600's "every occurrence found" list.
        ///
        /// RunStats.cs '…' (U+2026): <c>Title</c>'s fallback arm for <c>RunOutcome.InProgress</c>. Same
        /// General Punctuation block as the em-dash bug, so plausibly the same LegacyRuntime.ttf gap —
        /// but <c>RunStats.Seal</c> (RunStats.cs:59) refuses to ever set <c>Outcome</c> to
        /// <c>InProgress</c>, and <c>ResultScreen</c> only calls <c>Show(stats)</c> after a successful
        /// <c>Seal</c> (RunTracker.cs:132), so this branch cannot reach a player's screen. Flagged in the
        /// MV-600 fix comment as a tracked, out-of-scope look-alike rather than fixed here — the ticket's
        /// scope is the four named code sites and five area names, not a general dash/ellipsis sweep.
        /// </summary>
        private static readonly Dictionary<string, string> AllowedNonAsciiByFile = new Dictionary<string, string>
        {
            ["MapScreen.cs"] = "×",
            ["WeaponsScreen.cs"] = "×·",
            ["RunStats.cs"] = "…",
        };

        private static readonly Regex StringLiteral = new Regex(
            @"\$?@?""(?:[^""\\]|\\.)*""", RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex CommentRegex = new Regex(
            @"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>Debug.Log/LogWarning/LogError arguments are console text, never routed through
        /// HudFont — not "player-facing UI text" under AC1's own wording.</summary>
        private static readonly Regex DebugLogCallTail = new Regex(
            @"Debug\s*\.\s*Log(Warning|Error)?\s*\(\s*$", RegexOptions.Compiled);

        private static readonly Regex JsonNameField = new Regex(
            @"""name""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        [Test]
        public void NoNonAsciiCodepointInPlayerFacingUiTextOrWorldAreaNames()
        {
            var offenders = new List<string>();

            // ---- AC1a: source-shape scan of Runtime/UI/*.cs string literals ----
            string uiRoot = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime", "UI");
            Assert.IsTrue(Directory.Exists(uiRoot), $"Runtime/UI root not found: {uiRoot}");

            foreach (string path in Directory.GetFiles(uiRoot, "*.cs", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(path);
                string raw = File.ReadAllText(path);
                if (raw.All(c => c < 128)) continue; // cheap ASCII pre-check skips the common case

                string stripped = StripComments(raw);
                string allowedChars = AllowedNonAsciiByFile.TryGetValue(fileName, out string a) ? a : string.Empty;

                foreach (Match m in StringLiteral.Matches(stripped))
                {
                    if (DebugLogCallTail.IsMatch(stripped.Substring(0, m.Index))) continue;

                    string offendingChars = new string(m.Value.Where(c => c >= 128 && allowedChars.IndexOf(c) < 0).ToArray());
                    if (offendingChars.Length == 0) continue;

                    int line = CountLines(stripped, m.Index);
                    offenders.Add($"{fileName}:{line} has U+{(int)offendingChars[0]:X4} in {Truncate(m.Value)}");
                }
            }

            // ---- AC1b: "name" fields in Resources/Worlds/*.json ----
            string worldsRoot = Path.Combine(Application.dataPath, "_Project", "Resources", "Worlds");
            Assert.IsTrue(Directory.Exists(worldsRoot), $"Resources/Worlds root not found: {worldsRoot}");

            var areaNames = new List<string>();
            foreach (string path in Directory.GetFiles(worldsRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(path);
                foreach (Match m in JsonNameField.Matches(File.ReadAllText(path)))
                {
                    string name = UnescapeJsonString(m.Groups[1].Value);
                    if (name.Any(c => c >= 128))
                        offenders.Add($"{fileName}: \"name\": \"{name}\" has a non-ASCII codepoint");
                    else if (name.StartsWith("Area "))
                        areaNames.Add(name);
                }
            }

            Assert.IsEmpty(offenders,
                $"{offenders.Count} player-facing string(s) carry a codepoint LegacyRuntime.ttf can't draw " +
                "(MV-600). First offenders:\n" + string.Join("\n", offenders.Take(20)));

            // ---- AC3: DeathOverlay.BodyText resolves to ASCII for a real, JSON-sourced area name ----
            string representativeAreaName = areaNames.FirstOrDefault(n => n.Contains(" - "));
            Assert.IsNotNull(representativeAreaName,
                "expected at least one \"Area N - ...\" name (one of MV-600's five fixed area names) in Resources/Worlds/*.json");

            string body = DeathOverlay.BodyText(representativeAreaName, gateRecloses: true);
            Assert.IsTrue(body.All(c => c < 128),
                $"DeathOverlay.BodyText(\"{representativeAreaName}\") resolved to a non-ASCII codepoint: \"{body}\"");
        }

        private static string UnescapeJsonString(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i == s.Length - 1) { sb.Append(s[i]); continue; }
                i++;
                switch (s[i])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: sb.Append(s[i]); break;
                }
            }
            return sb.ToString();
        }

        private static string Truncate(string s) => s.Length <= 80 ? s : s.Substring(0, 80) + "...";

        private static int CountLines(string text, int uptoIndex)
        {
            int line = 1;
            for (int i = 0; i < uptoIndex && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private static string StripComments(string text) =>
            CommentRegex.Replace(text, m =>
            {
                var blanked = new char[m.Length];
                for (int i = 0; i < m.Length; i++)
                    blanked[i] = text[m.Index + i] == '\n' ? '\n' : ' ';
                return new string(blanked);
            });
    }
}
