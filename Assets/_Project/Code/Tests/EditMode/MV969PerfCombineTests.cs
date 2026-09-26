using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Editor;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-969 AC1 — root-cause review 2026-09-26 (device: GPU 17.3ms on a quiet World 2 walkway,
    /// 43.2ms in a11 combat, iOS thermal "serious"). Builds the REAL, shipped World 2 config through
    /// area a11 and World 1 through area a5, garrison spawned exactly as a live run would (the same
    /// <see cref="AreaAccumulationDirector"/> path MV966ParkByReachTests already drives), and asserts
    /// every one of this ticket's fixes actually landed on the BUILT scene — never an authored constant
    /// read back off source (Rule 2): the SSAO feature is inactive on the shipped mobile renderer data,
    /// every low-profile (&lt;=0.3m) renderer under World 2's map root has shadowCastingMode Off, every
    /// spawned robot combines down to at most 6 enabled renderers with materials shared per kind (not
    /// cloned per robot), and nothing in the built scene has the dissolve keyword compiled in while at
    /// rest.
    ///
    /// Must fail on the base commit this branch was cut from: <c>ScreenSpaceAmbientOcclusion.isActive</c>
    /// reads true on Mobile_Renderer.asset, <c>MapStaticBatchRoot</c> carries no shadow-casting pass at
    /// all (every renderer keeps Unity's default On), <c>RobotRig</c> builds 15-40 individual renderers
    /// per robot from a fresh per-instance material clone, and no shader keyword named _DISSOLVE_ON
    /// exists for <c>StylizedCharacter.shader</c> to have compiled out.
    /// </summary>
    public sealed class MV969PerfCombineTests
    {
        private const float LowProfileHeight = 0.3f;
        private const int MaxEnabledRenderersPerRobot = 6;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyWorldArtifacts();
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void PerfFixesHold_OnWorld2Area11AndWorld1Area5_WithGarrisonSpawned()
        {
            AssertSsaoInactive();

            LogAssert.ignoreFailingMessages = true;
            GameObject root2 = null;
            try
            {
                root2 = BuildWorldAtArea(WorldLibrary.World2, enterUpTo: 11, currentArea: 11, dressWorld2: true);
                AssertShadowCastingOnLowProfileGeometry(root2.transform);
                AssertRobotCombiningAndSharedMaterials();
                AssertNoDissolveKeywordEnabled();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                DestroyWorldArtifacts();
                if (root2 != null) Object.DestroyImmediate(root2);
                RobotEnemy.ResetRegistry();
            }

            AssertSsaoInactive();

            LogAssert.ignoreFailingMessages = true;
            GameObject root1 = null;
            try
            {
                root1 = BuildWorldAtArea(WorldLibrary.World1, enterUpTo: 5, currentArea: 5, dressWorld2: false);
                AssertRobotCombiningAndSharedMaterials();
                AssertNoDissolveKeywordEnabled();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                if (root1 != null) Object.DestroyImmediate(root1);
            }
        }

        /// <summary>The build's own shipped mobile renderer — the one WebGL/iOS actually use — never the
        /// PC tier, which keeps SSAO (see <see cref="Stage76RenderScaffold"/>'s own doc comment on why
        /// this is Mobile-only). Same asset-file read <c>MsaaRenderSettingsTests</c> already establishes.</summary>
        private static void AssertSsaoInactive()
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Stage76RenderScaffold.MobileRendererPath);
            Assert.IsNotNull(data, $"renderer asset missing: {Stage76RenderScaffold.MobileRendererPath}");

            ScreenSpaceAmbientOcclusion ssao = Stage76RenderScaffold.Find(data);
            Assert.IsNotNull(ssao, "SSAO renderer feature is missing from the mobile renderer");
            Assert.IsFalse(ssao.isActive,
                "SSAO renderer feature is active on the mobile renderer data — MV-969 turned this off " +
                "for GPU cost (a depth copy plus AO passes every frame, mostly invisible under fog)");
        }

        /// <summary>Builds <paramref name="worldKey"/>'s real, shipped config/map through
        /// <see cref="AreaAccumulationDirector"/> exactly as a live run would — same setup
        /// <see cref="MV966ParkByReachTests"/> already establishes — then drives every Awake/Start step
        /// that a real scene load guarantees but an EditMode <c>[Test]</c> does not: <see cref="MapStaticBatchRoot.Start"/>
        /// (which runs this ticket's own <c>CombineZoneGeometry</c>/shadow-casting pass) and, per spawned
        /// robot, <see cref="RobotRig.EnsureBuilt"/> (which runs this ticket's own static-part combine).</summary>
        private static GameObject BuildWorldAtArea(string worldKey, int enterUpTo, int currentArea, bool dressWorld2)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey}'s own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject($"MV969 {worldKey} Root");
            var directorGo = new GameObject($"MV969 {worldKey} Area Director");

            MapBuild built = MapRuntime.Build(map, root.transform);

            // MV-887/MV-934: the dressing kit is a SIBLING of the map root, built before
            // MapStaticBatchRoot.Start ever runs — the exact BackyardPath.Awake order MV934CombinedMeshTests
            // already establishes. World 2's dressing (kerbs/pipes/lamps/panel joints — the ticket's own
            // "16,106-renderer" figure) is exactly the geometry item 2's shadow-casting fix targets.
            if (dressWorld2) StormdrainDressing.Dress(root.transform, map, built.Cover);

            var director = directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, built.Cover);
            for (int i = 2; i <= enterUpTo; i++) director.EnterArea(i);
            director.SetCurrentArea(currentArea);

            Transform mapRoot = root.transform.Find($"Map: {map.name}");
            Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
            var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
            Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");
            typeof(MapStaticBatchRoot).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(batchRoot, null);

            RobotEnemy[] robots = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(robots,
                $"setup failure: {worldKey} must have placed robots by area{currentArea} for this test to mean anything");

            foreach (RobotEnemy e in robots)
            {
                var rig = e.GetComponent<RobotRig>();
                Assert.IsNotNull(rig, $"{e.name} has no RobotRig — the real spawn path always attaches one");
                if (rig.Built) continue;
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
                Assert.IsTrue(rig.Built, $"{e.name}'s RobotRig never finished building");
            }

            return root;
        }

        /// <summary>AC1's shadow-casting half — item 2's fix. A height test, not a per-kind allowlist,
        /// matching <c>MapStaticBatchRoot.ApplyLowProfileShadowCasting</c>'s own reasoning exactly.</summary>
        private static void AssertShadowCastingOnLowProfileGeometry(Transform mapRoot)
        {
            int checkedCount = 0;
            foreach (Renderer r in mapRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r.bounds.size.y > LowProfileHeight) continue;
                checkedCount++;
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode,
                    $"'{r.name}' (bounds.size.y={r.bounds.size.y:F3}) under the map root casts a shadow " +
                    "nobody can see at the play camera's distance");
            }
            Assert.Greater(checkedCount, 0,
                "setup failure: no low-profile (<=0.3m) renderer existed under the map root to check");
        }

        /// <summary>AC1's robot-combining half — item 3's fix. Reads <c>RobotRig</c>'s own private
        /// <c>_bodyMat</c> field (Tier 2: a resolved reference the engine actually renders with, not a
        /// re-derived colour that could agree by coincidence) to prove SHARING, not merely matching
        /// colour.</summary>
        private static void AssertRobotCombiningAndSharedMaterials()
        {
            FieldInfo bodyMatField = typeof(RobotRig).GetField("_bodyMat", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(bodyMatField, "RobotRig._bodyMat went missing");

            var sharedMaterialByKind = new Dictionary<EnemyKind, Material>();
            int robotsChecked = 0;

            foreach (RobotEnemy e in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var rig = e.GetComponent<RobotRig>();
                if (rig == null || !rig.Built) continue;
                robotsChecked++;

                int enabledRenderers = e.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled);
                Assert.LessOrEqual(enabledRenderers, MaxEnabledRenderersPerRobot,
                    $"{e.name} ({e.Kind}) carries {enabledRenderers} enabled renderers — over the " +
                    $"{MaxEnabledRenderersPerRobot}-renderer MV-969 combining budget");

                var bodyMat = (Material)bodyMatField.GetValue(rig);
                Assert.IsNotNull(bodyMat, $"{e.name} ({e.Kind}) built with no body material");

                if (sharedMaterialByKind.TryGetValue(e.Kind, out Material sharedMat))
                {
                    Assert.AreSame(sharedMat, bodyMat,
                        $"{e.name} ({e.Kind}) wears its own body material instance, not the one shared " +
                        $"by every other {e.Kind} — MV-969's per-kind sharing regressed to a per-robot clone");
                }
                else
                {
                    sharedMaterialByKind[e.Kind] = bodyMat;
                }
            }

            Assert.Greater(robotsChecked, 0, "setup failure: no built robot existed to check");
        }

        /// <summary>AC1's dissolve half — item 4's fix. "In the scene", not "under a map root" (AC1's own
        /// wording): every character-shaded renderer, wherever it lives. Filtered by <c>_Dissolve</c>
        /// (only <c>StylizedCharacter.shader</c> declares it — <c>StylizedSurface.shader</c>, the world
        /// dressing's own shader, has no such property), so this only ever inspects the robots/Max/boss
        /// the keyword actually applies to.</summary>
        private static void AssertNoDissolveKeywordEnabled()
        {
            int checkedCount = 0;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null || !mat.HasProperty("_Dissolve")) continue;
                    checkedCount++;
                    Assert.IsFalse(mat.IsKeywordEnabled("_DISSOLVE_ON"),
                        $"'{r.name}' wears a material with _DISSOLVE_ON enabled while nothing is dissolving " +
                        "— the noise lattice and its clip() are compiled in for every frame this renders");
                }
            }
            Assert.Greater(checkedCount, 0, "setup failure: no character-shaded renderer existed to check");
        }

        private static void DestroyWorldArtifacts()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);

            foreach (var directorGo in Object.FindObjectsByType<AreaAccumulationDirector>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (directorGo != null) Object.DestroyImmediate(directorGo.gameObject);
            }
        }
    }
}
