using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-734 — the COOLDOWN node (<c>e_cd</c>) sat on THE RIG costing parts across 5 levels with no
    /// runtime consumer at all: MV-728 retired the enum that once read it (<c>AbilityKind.WeaponCooldown</c>),
    /// but never retired the RIG node itself. Removing it risked silently orphaning <c>e_mag</c>, which was
    /// gated behind it (<c>e_mag.parent == "e_cd"</c>) — so this is two guards in one test, per
    /// CC_AUTONOMY.md's one-new-test rule: AC2 (the re-parent onto <c>e_cel</c> actually worked, and
    /// e_mag still reaches the ENERGY root) and AC3 (no node on the shipped board can regress to e_cd's
    /// own defect — one with no runtime consumer at all).
    ///
    /// Proven to fail on a35fd5f (pre-fix, before this ticket's changes): AC2 fails because
    /// <c>RigBoard.Parent("e_mag")</c> is still <c>"e_cd"</c>, not <c>"e_cel"</c>; AC3 fails because
    /// <c>e_cd</c> is on the board (<c>RigBoard.Exists("e_cd") == true</c>) with no hit for
    /// <c>"e_cd"</c> anywhere under Runtime outside <c>Runtime/Dev</c> (RigBoardConformance's own
    /// column-geometry comment, UiScreensDirector's capture fixture) — exactly the defect this ticket
    /// reports.
    /// </summary>
    public sealed class MV734CooldownRemovalTests
    {
        /// <summary>Runtime source that can legitimately "consume" a RIG node id — everywhere gameplay
        /// logic reads a node's level/ownership. <c>Runtime/Dev</c> is capture-fixture and conformance
        /// tooling only; MV-734's own root cause is that those files DO mention <c>e_cd</c> (a fixture
        /// that spends it, a comment about column geometry) while nothing under them ever applies its
        /// level to gameplay, so counting a Dev/ hit as a "consumer" would make this test blind to the
        /// exact bug it exists to catch.</summary>
        private static readonly string RuntimeConsumerRoot =
            Path.Combine(Application.dataPath, "_Project", "Code", "Runtime");

        private static readonly Regex CommentRegex = new Regex(
            @"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void EMagReparentsOntoEnergyCelAndEveryBoardNodeHasARuntimeConsumer()
        {
            RigBoard.ResetForTests(); // World 1 (index 0), rig_board.json

            // ---- AC2: e_mag must re-parent directly onto e_cel, and still reach the ENERGY root ----
            Assert.That(RigBoard.Parent("e_mag"), Is.EqualTo("e_cel"),
                "e_mag must re-parent directly onto e_cel now that e_cd is gone");

            var seen = new HashSet<string>();
            string id = "e_mag";
            while (true)
            {
                Assert.That(RigBoard.Exists(id), Is.True,
                    $"'{id}' is on e_mag's own ancestor chain but missing from rig_board.json");
                Assert.That(RigBoard.Category(id), Is.EqualTo("ENERGY"),
                    $"'{id}' fell out of the ENERGY branch while walking e_mag's ancestor chain to root");
                Assert.That(seen.Add(id), Is.True, $"cycle detected in e_mag's ancestor chain at '{id}'");
                // RigBoard.Parent is "" (not C# null) for a root — JsonUtility deserializes a JSON
                // `"parent": null` field to an empty string, never a null reference (RigState.IsReached
                // already accounts for this via string.IsNullOrEmpty, not a null check).
                string parent = RigBoard.Parent(id);
                if (string.IsNullOrEmpty(parent)) break;
                id = parent;
            }

            // ---- AC3: every ability node has a runtime consumer, or the removal left another do-nothing node ----
            Assert.IsTrue(Directory.Exists(RuntimeConsumerRoot), $"Runtime source root not found: {RuntimeConsumerRoot}");
            string combinedSource = string.Join("\n", Directory
                .GetFiles(RuntimeConsumerRoot, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Runtime/Dev/"))
                .Select(p => StripComments(File.ReadAllText(p))));

            var withoutConsumer = RigBoard.AllIds.Where(nodeId => !combinedSource.Contains($"\"{nodeId}\"")).ToList();
            Assert.That(withoutConsumer, Is.Empty,
                "RIG node(s) authored on rig_board.json with no runtime consumer outside Runtime/Dev tooling " +
                "(MV-734's own class of bug — a node the player pays parts for that does nothing): " +
                string.Join(", ", withoutConsumer));
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
