using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1227 — the Mothership Assembly Cradle's look (gantry frame, build-stage bay, turrets).
    /// Fails on base commit e0c0fc7 (MV-1223): that commit's <see cref="AssemblyCradle"/> builds no
    /// visual at all, so <c>TurretHousings</c> is never populated (null, throwing on the foreach
    /// below) and <c>FrameStage</c>/<c>PlatesStage</c>/<c>DoneStage</c> never exist, let alone diverge.
    /// Tier 2 (resolved values): every assertion reads a renderer's own resolved <c>enabled</c> flag
    /// or a renderer's own resolved world-space bounds, never an authored constant.
    /// </summary>
    // Guards MV-1227
    public sealed class MV1227AssemblyCradleLookTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-83456f, 0f, 61234f);

        private GameObject _cradleGo;

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void BuildStages_HaveDistinctResolvedRendererSets_AndTurretHousingsResolveToTicketWidth()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.name = "AssemblyCradleLook";
            _cradleGo.transform.position = RigOrigin;
            _cradleGo.transform.localScale = new Vector3(3f, 3f, 3f); // the ticket's own 3x3x3 m footprint

            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build(); // AddComponent's own Awake never runs outside Play mode

            var spawner = _cradleGo.GetComponent<EnemySpawner>();
            spawner.ConfigureAreaComposition(new WorldComposition { rusher = 1 });
            cradle.SetMaxInArea(true);

            // Frame stage (1.5 <= t < 3.0): drive the timer to 1.6 s.
            cradle.Tick(1.6f);
            Assert.AreEqual(AssemblyCradle.BuildStage.Frame, cradle.Stage, "1.6s must read as the Frame stage");
            AssertOnlyStageVisible(cradle, cradle.FrameStage);
            bool[] frameSnapshot = SnapshotEnabled(cradle);

            // Plates stage (3.0 <= t < 4.0): advance to 3.2 s.
            cradle.Tick(1.6f);
            Assert.AreEqual(AssemblyCradle.BuildStage.Plates, cradle.Stage, "3.2s must read as the Plates stage");
            AssertOnlyStageVisible(cradle, cradle.PlatesStage);
            bool[] platesSnapshot = SnapshotEnabled(cradle);

            // Done stage (t >= 4.0): advance past 4.0 s. ApplyBuildStageVisual runs before the cycle
            // resets/spawns, so the Done-stage renderers are left enabled for this snapshot even though
            // the timer itself has already been zeroed for the next cycle by the time Tick returns.
            cradle.Tick(1.0f);
            AssertOnlyStageVisible(cradle, cradle.DoneStage);
            bool[] doneSnapshot = SnapshotEnabled(cradle);

            // MV-1227 AC1: the bay's visible child set changes — resolved renderer-enabled flags differ
            // between every pair of stages.
            CollectionAssert.AreNotEqual(frameSnapshot, platesSnapshot, "Frame and Plates must show different renderers");
            CollectionAssert.AreNotEqual(platesSnapshot, doneSnapshot, "Plates and Done must show different renderers");
            CollectionAssert.AreNotEqual(frameSnapshot, doneSnapshot, "Frame and Done must show different renderers");

            // MV-1227 AC1: each turret's resolved housing width is 0.3 m +/- 0.05.
            Assert.AreEqual(2, cradle.TurretHousings.Length, "the cradle must have exactly two turrets");
            foreach (Transform housing in cradle.TurretHousings)
            {
                var rend = housing.GetComponent<Renderer>();
                Assert.NotNull(rend, "a turret housing must carry its own renderer");
                float width = rend.bounds.size.x;
                Assert.That(width, Is.EqualTo(0.3f).Within(0.05f),
                    $"turret housing '{housing.name}' resolved width must be ~0.3m, was {width}");
            }
        }

        /// <summary>Every renderer under <paramref name="visibleStage"/> must be enabled, and every
        /// renderer under the cradle's OTHER two stage roots must be disabled.</summary>
        private static void AssertOnlyStageVisible(AssemblyCradle cradle, Transform visibleStage)
        {
            foreach (Transform stage in new[] { cradle.FrameStage, cradle.PlatesStage, cradle.DoneStage })
            {
                bool shouldBeVisible = stage == visibleStage;
                foreach (Renderer r in stage.GetComponentsInChildren<Renderer>(true))
                    Assert.AreEqual(shouldBeVisible, r.enabled,
                        $"{stage.name}/{r.name} enabled must be {shouldBeVisible} when {visibleStage.name} is the active stage");
            }
        }

        /// <summary>The resolved enabled/disabled flag of every renderer across all three stage roots,
        /// in a stable order (the same GameObjects persist for the whole build cycle — only their
        /// <c>enabled</c> flags change between snapshots).</summary>
        private static bool[] SnapshotEnabled(AssemblyCradle cradle)
        {
            var list = new List<bool>();
            foreach (Transform stage in new[] { cradle.FrameStage, cradle.PlatesStage, cradle.DoneStage })
                foreach (Renderer r in stage.GetComponentsInChildren<Renderer>(true))
                    list.Add(r.enabled);
            return list.ToArray();
        }
    }
}
