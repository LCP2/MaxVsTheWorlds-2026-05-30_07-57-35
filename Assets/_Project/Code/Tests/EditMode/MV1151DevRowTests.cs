using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1151 (the one new test, per CC_AUTONOMY's testing policy): the Home screen's DEV row must
    /// grow one world button per <see cref="WorldCatalog"/> row, labelled "Wn", with no HomeScreen edit
    /// needed when a row is added — and each of those worlds must own a real, reachable finale. Fails
    /// on the base commit two ways: there is no "W1" button (only DEV - WORLD 2/3 + DEV: FINAL AREA),
    /// and <see cref="WorldRunner.ResolveFinaleArea"/> does not exist yet, so the test fails to compile
    /// — same "new API is its own base-commit failure" bar MV-524/MV-1057/MV-960's own tests used.
    ///
    /// Tier 2 (resolved values): builds the real Home screen (same idiom as <see cref="MV960HomeLayoutTests"/>)
    /// and reads real <see cref="Button"/> components off it, plus each world's real, loaded
    /// <see cref="WorldConfig"/> — never an authored constant restated, never a rendered pixel.
    /// </summary>
    public sealed class MV1151DevRowTests
    {
        private string _dir;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv1151");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            for (int i = 0; i < SaveSystem.SlotCount; i++) SaveSystem.Delete(i);   // every card starts Empty
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            Time.timeScale = 1f;
        }

        [Test]
        public void DevRow_HasOneInteractableButtonPerCatalogRow_AndEachResolvesAReachableBossFinale()
        {
            _go = new GameObject("HomeScreen");
            var home = _go.AddComponent<HomeScreen>();
            home.Open();

            Button[] buttons = home.GetComponentsInChildren<Button>(true);

            for (int i = 0; i < WorldCatalog.Count; i++)
            {
                string label = "W" + (i + 1);
                Button button = buttons.FirstOrDefault(b => b.name == label);
                Assert.IsNotNull(button,
                    $"MV-1151: HomeScreen must expose a dev entry labelled '{label}' for WorldCatalog row {i}");
                Assert.IsTrue(button.interactable,
                    $"MV-1151: '{label}' must be interactable on every slot, including an empty one");

                WorldConfig cfg = WorldLibrary.Load(WorldCatalog.Get(i).ConfigKey);
                Assert.IsNotNull(cfg, $"MV-1151: row {i}'s own ConfigKey must load");

                WorldArea finale = WorldRunner.ResolveFinaleArea(cfg);
                Assert.IsNotNull(finale,
                    $"MV-1151: row {i} ('{WorldCatalog.Get(i).Id}')'s finale resolver must return a boss-role area");
                Assert.IsTrue(finale.IsBossRole,
                    $"MV-1151: row {i}'s resolved finale area '{finale.id}' must be boss-role");
                Assert.AreEqual(cfg.dials.areaCount, finale.index,
                    $"MV-1151: row {i}'s resolved finale area must sit at dials.areaCount");

                bool hasGateIntoFinale = cfg.gates != null && cfg.gates.Any(g => g?.to != null && g.to.area == finale.id);
                Assert.IsTrue(hasGateIntoFinale,
                    $"MV-1151: row {i}'s config must author a gate leading into its finale area '{finale.id}'");
            }
        }
    }
}
