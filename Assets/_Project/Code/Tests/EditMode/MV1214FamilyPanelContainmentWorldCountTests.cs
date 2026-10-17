using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1214 (the one new test): <see cref="UiScreensDirector.RunFamilyPanelContainmentCheck"/>'s own
    /// world loop hard-coded <c>world &lt; 3</c>, so once <see cref="WorldCatalog"/> grew a fourth row
    /// (the Reef, MV-1176) the check silently stopped visiting it — never caught because nothing asserted
    /// the loop's own bound against <see cref="WorldCatalog.Count"/>.
    ///
    /// Asserts the set of world indices <see cref="UiScreensDirector.FamilyPanelContainmentWorldIndices"/>
    /// visits equals <c>0..WorldCatalog.Count-1</c> — compared against the live catalog count, never the
    /// literal 4, so this stays correct if a fifth world is ever added.
    ///
    /// Fails on base commit 440d446 (before this ticket): the loop bound is a literal 3, so with today's
    /// 4-row catalog the visited set is {0,1,2} while the expected set is {0,1,2,3} — missing index 3,
    /// the Reef.
    /// </summary>
    public sealed class MV1214FamilyPanelContainmentWorldCountTests
    {
        [Test]
        public void VisitsEveryWorldInTheCatalog()
        {
            int[] expected = Enumerable.Range(0, WorldCatalog.Count).ToArray();
            int[] visited = UiScreensDirector.FamilyPanelContainmentWorldIndices().ToArray();

            CollectionAssert.AreEqual(expected, visited,
                "the family-panel containment check must visit every world in WorldCatalog, not a stale literal bound");
        }
    }
}
