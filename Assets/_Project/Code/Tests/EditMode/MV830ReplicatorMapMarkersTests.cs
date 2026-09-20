using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-830 — <c>MapScreen</c> drew a shed marker for every <see cref="MaxWorlds.Arena.EntityKind.Factory"/>
    /// but nothing at all for <see cref="MaxWorlds.Arena.EntityKind.Replicator"/>, so World 2's Replicators
    /// (and the two hatches gated on all of them dying) had no way to be found on the map.
    ///
    /// Fails on base commit 1ac7f95 (before this ticket): <c>MapScreen</c> built zero GameObjects named
    /// "Replicator" for a World 2 map with authored Replicator entities.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) loading the real, shipped World 2 config
    /// through <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>, opening a real <see cref="MapScreen"/>
    /// against it — no hand-authored map anywhere in this file — asserting RESOLVED values (Rule 2,
    /// Tier 2): (AC1) exactly as many Replicator marker Images exist as World 2 authors, each showing the
    /// alive fill colour; (AC2) killing one and reopening the map flips only that marker's resolved
    /// colour (fill alpha 0, outline #8C8C8C) while every other one stays unchanged; (AC3) the legend
    /// carries both new rows, by label text.
    ///
    /// Counts updated by MV-852 (World 2 re-layout): a7 and a13 were deleted outright along with their
    /// Replicators, dropping the count MV-700 originally authored (25) to 23. Updated again by MV-865
    /// (World 2 re-author): areas 1-14 were rebuilt from Lee's sheet with one Replicator per drawn box,
    /// raising the total to 47 (see MV700World2ConfigTests' own note on this same count).
    /// </summary>
    public sealed class MV830ReplicatorMapMarkersTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo MapField = typeof(BackyardPath).GetField("_map", NonPublicInstance);
        private static readonly FieldInfo BuildField = typeof(BackyardPath).GetField("_build", NonPublicInstance);

        private GameObject _mapRoot;
        private GameObject _pathGo;
        private GameObject _screenGo;

        [SetUp]
        public void SetUp()
        {
            // Same BuildBody collider-strip [Error] every Replicator EditMode test in this suite carries
            // once Build() actually runs.
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_screenGo != null) Object.DestroyImmediate(_screenGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_mapRoot != null) Object.DestroyImmediate(_mapRoot);
        }

        private static Image[] ReplicatorMarkers(GameObject screenGo) =>
            screenGo.GetComponentsInChildren<Image>(true)
                .Where(i => i.gameObject.name == "Replicator")
                .ToArray();

        [Test]
        public void MapShowsEveryReplicator_AndFlipsOnlyTheDeadOneOnReopen()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string loadReason), loadReason);

            _mapRoot = new GameObject("MV830 map root");
            MapBuild build = MapRuntime.Build(map, _mapRoot.transform);
            // Real production wiring for every Replicator this map built (health, spawner stop) —
            // AddComponent's own Awake never runs outside Play mode (Replicator.Build's own doc comment).
            foreach (Replicator r in build.Replicators) r.Build();
            Assert.AreEqual(47, build.Replicators.Count, "setup failure: World 2's shipped config must author 47 Replicators");

            _pathGo = new GameObject("MV830 backyard path");
            var path = _pathGo.AddComponent<BackyardPath>();
            Assert.IsNotNull(MapField, "BackyardPath._map went missing");
            Assert.IsNotNull(BuildField, "BackyardPath._build went missing");
            MapField.SetValue(path, map);
            BuildField.SetValue(path, build);

            _screenGo = new GameObject("MV830 map screen");
            var screen = _screenGo.AddComponent<MapScreen>();
            screen.Open();

            // === AC1: exactly 47 Replicator markers, each showing the alive fill colour ===
            Image[] markers = ReplicatorMarkers(_screenGo);
            Assert.AreEqual(47, markers.Length, "MV-830 AC1: World 2 authors 47 Replicators — one map marker apiece");
            foreach (Image marker in markers)
                Assert.AreEqual(MapScreenDesign.Replicator, marker.color,
                    $"MV-830 AC1: '{marker.transform.parent.name}' must show the alive fill colour before anything dies");

            // === AC2: kill one, reopen the map, only that marker's RESOLVED colours flip ===
            Replicator victim = build.Replicators[0];
            victim.TakeDamage(new DamageInfo(99999f, victim.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(victim.IsAlive, "setup failure: the victim must actually die from lethal Player damage");

            screen.Close();
            screen.Open();

            markers = ReplicatorMarkers(_screenGo);
            Assert.AreEqual(47, markers.Length, "MV-830 AC2: killing one must not remove or duplicate any marker");

            int deadCount = 0, aliveCount = 0;
            foreach (Image marker in markers)
            {
                Image outline = marker.transform.Find("Outline").GetComponent<Image>();
                Assert.IsNotNull(outline, $"'{marker.gameObject.name}' carries no Outline child");

                if (Mathf.Approximately(marker.color.a, 0f))
                {
                    deadCount++;
                    Assert.AreEqual(MapScreenDesign.ReplicatorDestroyed, outline.color,
                        "MV-830 AC2: a destroyed marker's outline must resolve to #8C8C8C");
                }
                else
                {
                    aliveCount++;
                    Assert.AreEqual(MapScreenDesign.Replicator, marker.color,
                        "MV-830 AC2: every untouched marker must still show the alive fill colour");
                }
            }
            Assert.AreEqual(1, deadCount, "MV-830 AC2: exactly the killed Replicator's marker must have flipped to destroyed");
            Assert.AreEqual(46, aliveCount, "MV-830 AC2: the other 46 markers must be unchanged");

            // === AC3: the legend carries both new rows, by label text ===
            string[] labels = _screenGo.GetComponentsInChildren<Text>(true).Select(t => t.text).ToArray();
            Assert.Contains("Replicator", labels, "MV-830 AC3: legend must carry a 'Replicator' row");
            Assert.Contains("Replicator (destroyed)", labels, "MV-830 AC3: legend must carry a 'Replicator (destroyed)' row");
        }
    }
}
