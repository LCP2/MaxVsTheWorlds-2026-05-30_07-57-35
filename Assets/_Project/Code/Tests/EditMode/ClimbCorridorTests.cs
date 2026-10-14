using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1178 (testing policy Rule 1, one new test): World 2's own exit corridor now climbs into the
    /// City, not the Reef. Built through the real <see cref="WorldJoinSequence.Initialize"/>/
    /// <see cref="WorldJoinSequence.InitializeArrival"/> entry points (the same idiom
    /// MV965/MV967CorridorDressingTests already establish), exit side from the Stormdrain, arrival side
    /// into the City's own config -- unlike MV967CorridorDressingTests, which (pre-MV-1176's world
    /// reorder) loaded the Reef's own world3_config for its arrival side.
    ///
    /// Fails to compile on the MV-1176 merge commit (f559a56 plus MV-1175/MV-1176): CS0103 "The name
    /// 'DressExitCity' does not exist in the current context" / "The name 'ExitCity' does not exist in
    /// the current context" -- there is no City climb-corridor dressing to build against yet, the same
    /// fail-first shape MarketStreetBuildTests already used for CityDressing itself.
    /// </summary>
    public sealed class ClimbCorridorTests
    {
        private GameObject _camGo;
        private GameObject _exitPlayerGo;
        private GameObject _arrivalPlayerGo;
        private WorldJoinSequence _exitSequence;
        private WorldJoinSequence _arrivalSequence;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_exitSequence != null) Object.DestroyImmediate(_exitSequence.gameObject);
            if (_arrivalSequence != null) Object.DestroyImmediate(_arrivalSequence.gameObject);
            if (_exitPlayerGo != null) Object.DestroyImmediate(_exitPlayerGo);
            if (_arrivalPlayerGo != null) Object.DestroyImmediate(_arrivalPlayerGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        private static PlayerController SpawnPlayer(out GameObject go)
        {
            go = new GameObject("Max");
            go.AddComponent<CharacterController>();
            return go.AddComponent<PlayerController>();
        }

        [Test]
        public void WorldTwoClimbsIntoTheCityNotTheReef()
        {
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);

            // ---- build the exit side (World 2's own row, now into the City) ----
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(fromCfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData fromMap, out string fromReason), fromReason);

            WorldTransitionEntry entry = WorldTransitions.For(stormdrainIndex);
            Assert.IsNotNull(entry, "World 2 must author a WorldTransitions entry into the City.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            PlayerController exitPlayer = SpawnPlayer(out _exitPlayerGo);
            _exitSequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _exitSequence.Initialize(fromCfg, fromMap, entry, fromWorldIndex: stormdrainIndex, exitPlayer, () => { });

            Transform segA = _exitSequence.CorridorSegmentRoot('A');
            Transform segB = _exitSequence.CorridorSegmentRoot('B');
            Transform segC = _exitSequence.CorridorSegmentRoot('C');
            Assert.IsNotNull(segA, "segment A must have been built");
            Assert.IsNotNull(segB, "segment B must have been built");
            Assert.IsNotNull(segC, "segment C must have been built");

            AreaGate farGate = _exitSequence.FarGate;
            Assert.IsNotNull(farGate, "the exit corridor must build its own far gate");

            // ---- build the arrival side (the City's own stub, not the Reef's) ----
            WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(toCfg, "city_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(toCfg, out MapData toMap, out string toReason), toReason);

            PlayerController arrivalPlayer = SpawnPlayer(out _arrivalPlayerGo);
            _arrivalSequence = new GameObject("WorldJoinSequence (Arrival)").AddComponent<WorldJoinSequence>();
            _arrivalSequence.InitializeArrival(toCfg, toMap, entry, fromWorldIndex: stormdrainIndex, arrivalPlayer, () => { });

            Transform arrivalRoot = _arrivalSequence.ArrivalRoot;
            Assert.IsNotNull(arrivalRoot, "the arrival shell must have been built");

            AreaGate arrivalGate = _arrivalSequence.GetComponentInChildren<AreaGate>(true);
            Assert.IsNotNull(arrivalGate, "the City stub must build its own arrival door");

            // ---- AC: segment C holds a "Pool" renderer flat on the floor; no renderer under any
            // segment root reads as the Reef (Observation/Ocean/Hull) any more. ----
            Renderer[] segCRenderers = segC.GetComponentsInChildren<Renderer>(true);
            Renderer pool = segCRenderers.FirstOrDefault(r => r.name == "Pool");
            Assert.IsNotNull(pool, "segment C's own street lamp must build a renderer named 'Pool'");
            Assert.LessOrEqual(pool.bounds.center.y, 0.05f, "the street lamp's pool must sit flat on the floor");

            foreach (Transform seg in new[] { segA, segB, segC })
            {
                foreach (Renderer r in seg.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.IsFalse(r.name.Contains("Observation") || r.name.Contains("Ocean") || r.name.Contains("Hull"),
                        $"'{r.name}' under '{seg.name}' still reads as the Reef — World 2 now climbs into the City");
                }
            }

            // ---- AC: dressing carries no collider; every renderer bound inside the 2.0 m centre lane
            // over d 10-16 stays at or below 0.35 m; a downward raycast at d 11/13/15 on the centre line
            // hits the real floor at y 0 (within 1 cm). ----
            foreach (var (label, seg) in new[] { ("A", segA), ("B", segB), ("C", segC) })
            {
                int colliderCount = seg.GetComponentsInChildren<Collider>(true).Length;
                Assert.AreEqual(3, colliderCount,
                    $"segment {label} must carry exactly its own floor + two wall colliders, no dressing colliders — found {colliderCount}");
            }

            var allCorridorRenderers = segA.GetComponentsInChildren<Renderer>(true)
                .Concat(segB.GetComponentsInChildren<Renderer>(true))
                .Concat(segC.GetComponentsInChildren<Renderer>(true));
            foreach (Renderer r in allCorridorRenderers)
            {
                float along = r.bounds.center.x - doorMouth.x;
                float across = r.bounds.center.z - doorMouth.y;
                if (along < 10f || along >= 16f || Mathf.Abs(across) >= 1.0f) continue;
                Assert.LessOrEqual(r.bounds.max.y, 0.35f,
                    $"'{r.name}' sits inside the 2.0 m centre lane over d 10-16 but measures {r.bounds.max.y:F2} m tall");
            }

            Physics.SyncTransforms();
            foreach (float d in new[] { 11f, 13f, 15f })
            {
                Vector3 origin = new Vector3(doorMouth.x + d, 5f, doorMouth.y);
                Assert.IsTrue(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f),
                    $"a downward raycast at d {d} on the centre line hit nothing");
                Assert.AreEqual(0f, hit.point.y, 0.01f,
                    $"a downward raycast at d {d} on the centre line must hit the real floor at y 0, hit '{hit.transform.name}' at y {hit.point.y:F3} instead");
            }

            // ---- AC: the far gate and the City stub's arrival gate each carry at least 10 slat renderers. ----
            foreach (var (label, gate) in new[] { ("far", farGate), ("arrival", arrivalGate) })
            {
                int slatCount = gate.GetComponentsInChildren<Renderer>(true).Count(r => r.name.StartsWith("Slat"));
                Assert.GreaterOrEqual(slatCount, 10, $"the {label} gate must carry at least 10 slat renderers — found {slatCount}");
            }

            // ---- AC: every unlit/emissive/additive material under the corridor, the arrival shell and
            // both gates gives CityKit.IsReservedRelayColour == false. ----
            var everyRenderer = allCorridorRenderers
                .Concat(arrivalRoot.GetComponentsInChildren<Renderer>(true))
                .Concat(farGate.GetComponentsInChildren<Renderer>(true))
                .Concat(arrivalGate.GetComponentsInChildren<Renderer>(true));

            foreach (Renderer r in everyRenderer)
            {
                Material mat = r.sharedMaterial;
                if (mat == null || mat.shader == null) continue;
                bool unlitOrAdditive = mat.shader.name.Contains("Unlit");
                bool emissive = mat.IsKeywordEnabled("_EMISSION");
                if (!unlitOrAdditive && !emissive) continue;

                Color c = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor")
                        : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                        : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;

                Assert.IsFalse(CityKit.IsReservedRelayColour(c),
                    $"'{r.name}' (material '{mat.name}', colour {c}) wears a reserved relay colour");
            }
        }
    }
}
