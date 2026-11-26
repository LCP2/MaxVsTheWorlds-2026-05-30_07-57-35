using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1473 (the one new test, Rule 1): the City -> Reef exit corridor v2, built the real way through
    /// <see cref="WorldJoinSequence.Initialize"/> for the City row: the street slopes down to the waterfront
    /// as one blended run. Tier 2 only (resolved material at a raycast hit, built collider/renderer bounds,
    /// resolved light colour). Guards MV-1473.
    /// </summary>
    public sealed class MV1473CityReefCorridorV2Tests
    {
        private GameObject _camGo, _playerGo;
        private WorldJoinSequence _sequence;

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
            if (_sequence != null) Object.DestroyImmediate(_sequence.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;
        }

        private static Vector3 Out(Wall w) => w switch
        {
            Wall.N => Vector3.forward, Wall.S => Vector3.back, Wall.E => Vector3.right, _ => Vector3.left,
        };

        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        [Test]
        public void CityExitCorridor_V2_BlendedStreetToWaterfront()
        {
            int city = WorldCatalog.IndexOf(WorldIds.City);
            Assert.AreEqual(ExitCorridorDressingKind.ExitReef, WorldCatalog.Get(city).ExitDressing,
                "the City row must dress its exit corridor as ExitReef");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(city));
            Assert.IsNotNull(cfg);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            WorldTransitionEntry entry = WorldCatalog.Get(city).Entry;
            Vector2 mouth = entry.ExitDoorMouth(cfg);
            Vector3 dir = Out(entry.ExitWall);
            Vector3 across = new Vector3(-dir.z, 0f, dir.x);
            Vector3 origin = new Vector3(mouth.x, 0f, mouth.y);
            float length = entry.CorridorLength;
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            var player = _playerGo.AddComponent<PlayerController>();
            _sequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _sequence.Initialize(cfg, map, entry, city, player, () => { });
            Physics.SyncTransforms();

            Transform root = _sequence.transform.Find("World Join Corridor");
            Assert.IsNotNull(root, "the corridor root must exist");

            // --- v1's underpass is gone ---
            foreach (var t in root.GetComponentsInChildren<Transform>())
                Assert.IsFalse(t.name.Contains("Underpass"), $"v1 underpass piece survives: {t.name}");

            _playerGo.GetComponent<CharacterController>().enabled = false;   // Max's own capsule is not corridor
            Physics.SyncTransforms();

            // --- floor: tarmac -> tarmac/paving blend -> quay -> timber -> steel, read from the material the ray hits ---
            string[] expect = { "CR_Tarmac_", "CR_Blend_Tarmac_Paving_", "CR_Quay_", "CR_Timber_", "CR_Steel_" };
            float[] at = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
            for (int i = 0; i < at.Length; i++)
            {
                Vector3 p = origin + dir * (length * at[i]) + Vector3.up * 30f;
                Assert.IsTrue(Physics.Raycast(p, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Collide));
                string matName = hit.collider.GetComponent<Renderer>().sharedMaterial.name;
                StringAssert.StartsWith(expect[i], matName, $"floor at {at[i] * 100}% must be {expect[i]}");
            }

            // --- south side beyond 52%: a railing collider the whole way, and harbour water outside it ---
            var railings = new List<Collider>();
            MeshRenderer waterRend = null;
            foreach (var col in root.GetComponentsInChildren<Collider>())
                if (col.name.StartsWith("Sea Railing")) railings.Add(col);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                if (r.name == "Harbour Water") waterRend = r;
            Assert.IsNotNull(waterRend, "harbour water must be built");
            Assert.Less(Vector3.Dot(waterRend.bounds.center - origin, across), -halfWidth, "water must lie outside the railing");
            float wMin = Vector3.Dot(waterRend.bounds.min - origin, dir), wMax = Vector3.Dot(waterRend.bounds.max - origin, dir);
            Assert.LessOrEqual(Mathf.Min(wMin, Vector3.Dot(waterRend.bounds.max - origin, dir)), 0.52f * length);
            Assert.GreaterOrEqual(Mathf.Max(wMin, wMax), length - 0.05f);
            for (float along = 0.52f * length; along <= length - 0.5f; along += 1f)
            {
                bool covered = false;
                foreach (var rc in railings)
                {
                    float a = Vector3.Dot(rc.bounds.min - origin, dir), b = Vector3.Dot(rc.bounds.max - origin, dir);
                    if (Mathf.Min(a, b) <= along + 0.01f && Mathf.Max(a, b) >= along - 0.01f
                        && Vector3.Dot(rc.bounds.center - origin, across) < 0f) { covered = true; break; }
                }
                Assert.IsTrue(covered, $"no south railing collider at along {along:0.0}");
            }

            // --- light posts no more than 4 m apart along the whole length ---
            var lamps = new List<float>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("Corridor Lamp"))
                {
                    // the lamp root sits at the origin; the post's own built bounds say where the lamp stands
                    Transform post = t.Find("Post");
                    Assert.IsNotNull(post, $"{t.name} has no post");
                    lamps.Add(Vector3.Dot(post.GetComponent<Renderer>().bounds.center - origin, dir));
                }
            lamps.Sort();
            Assert.IsNotEmpty(lamps, "light posts must be built");
            Assert.LessOrEqual(lamps[0], 4f, "first light post too far from the City door");
            for (int i = 1; i < lamps.Count; i++)
                Assert.LessOrEqual(lamps[i] - lamps[i - 1], 4f, $"dark gap before light post at {lamps[i]:0.0} m");
            Assert.GreaterOrEqual(lamps[lamps.Count - 1], length - 4f, "last light post too far from the hatch");

            // --- walls: unbroken along both sides for the full length ---
            foreach (string wallName in new[] { "Wall 1", "Wall 2" })
            {
                var spans = new List<Vector2>();
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!r.name.EndsWith(wallName)) continue;
                    float a = Vector3.Dot(r.bounds.min - origin, dir), b = Vector3.Dot(r.bounds.max - origin, dir);
                    spans.Add(new Vector2(Mathf.Min(a, b), Mathf.Max(a, b)));
                }
                spans.Sort((p, q) => p.x.CompareTo(q.x));
                Assert.IsNotEmpty(spans, $"{wallName} must be built");
                Assert.LessOrEqual(spans[0].x, 0.05f, $"{wallName} must start at the City door frame");
                float reach = spans[0].y;
                for (int i = 1; i < spans.Count; i++)
                {
                    Assert.LessOrEqual(spans[i].x - reach, 0.05f, $"{wallName} has a gap before {spans[i].x:0.00} m");
                    reach = Mathf.Max(reach, spans[i].y);
                }
                Assert.GreaterOrEqual(reach, length - 0.05f, $"{wallName} must reach the Reef hatch end ({reach:0.00} of {length})");
            }

            // --- no void: a lit collider under every 1 m grid point, 8 m either side ---
            for (float along = 0f; along <= length + 0.001f; along += 1f)
                for (float side = -8f; side <= 8.001f; side += 1f)
                {
                    Vector3 p = origin + dir * along + across * side + Vector3.up * 30f;
                    Assert.IsTrue(Physics.Raycast(p, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Collide),
                        $"void at along {along}, side {side}");
                    var rend = hit.collider.GetComponent<Renderer>();
                    Assert.IsTrue(rend != null && rend.enabled, $"unlit collider at along {along}, side {side}: hit {hit.collider.name}");
                }

            // --- light: amber -> neutral -> teal, no step ---
            var lighting = Object.FindFirstObjectByType<BackyardLighting>();
            Assert.IsNotNull(lighting);
            var key = new Color[9];
            for (int i = 1; i <= 9; i++)
            {
                _playerGo.transform.position = origin + dir * (length * i / 10f);
                _sequence.Tick(0f);
                key[i - 1] = lighting.Look.KeyColor;
            }
            Assert.Greater(key[0].r, key[0].b, "10% must read warm");
            Assert.Less(Mathf.Abs(key[4].r - key[4].b), 0.1f, "50% must read neutral");
            Assert.IsTrue(key[8].b > key[8].r && key[8].g > key[8].r, "90% must read teal");
            for (int i = 1; i < 9; i++)
                Assert.LessOrEqual(Mathf.Abs(Lum(key[i]) - Lum(key[i - 1])) / Mathf.Max(0.01f, Lum(key[i - 1])), 0.35f,
                    $"lighting steps between {i * 10}% and {(i + 1) * 10}%");

            // --- no built-in cube under the corridor ---
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                Assert.AreNotEqual("Cube", mf.sharedMesh != null ? mf.sharedMesh.name : "", $"{mf.name} uses the built-in Cube mesh");
        }
    }
}
