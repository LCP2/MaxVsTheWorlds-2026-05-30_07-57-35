using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1116 (Lee, device, TestFlight v0.11.7): "Issue with wall going through the middle of the
    /// corridor" -- walking the World 1 exit corridor, a dark wall slab ran straight across it about a
    /// third of the way along and continued far beyond it on both sides; Max walked through it.
    ///
    /// Root cause: <see cref="BackyardBackdrop"/> wraps a fence line around the WHOLE of World 1's map
    /// bounds at scene boot (MV-750), long before the exit corridor exists -- the corridor is only built
    /// later, when the final boss dies, 30 m out past a30's own E wall (<see cref="WorldJoinSequence.OpenExitDoor"/>),
    /// and nothing ever cut the backdrop's own east fence through for it.
    ///
    /// One test, every <see cref="WorldTransitions"/> row: the exit corridor is built through the real
    /// <see cref="WorldJoinSequence.OpenExitDoor"/> entry point (same idiom MV997WorldExitDoorTests
    /// already establishes for this exact gate) and the arrival shell through the real
    /// <see cref="WorldJoinSequence.InitializeArrival"/> entry point (same idiom MV1077ArrivalVisibilityTests
    /// already establishes for this exact shell). Asserts no enabled renderer and no enabled collider
    /// outside the sequence's own hierarchy (and never Max's own body) reaches more than 5 cm into the
    /// walkable volume -- the corridor's/shell's own length outward from the door mouth, its 3 m interior
    /// width, floor to wall height -- and names the offending object when one does.
    ///
    /// Fails on base commit d30d293: the World 1 exit corridor check names
    /// BackyardBackdrop's own east "HouseWall" fence renderer.
    /// </summary>
    public sealed class MV1116CorridorIntrusionTests
    {
        private const float IntrusionEpsilon = 0.05f;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardBackdrop>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(component, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private readonly struct Row
        {
            public readonly string FromKey;
            public readonly int FromWorldIndex;
            public readonly string ToKey;

            public Row(string fromKey, int fromWorldIndex, string toKey)
            {
                FromKey = fromKey; FromWorldIndex = fromWorldIndex; ToKey = toKey;
            }
        }

        [Test]
        public void NothingForeignIntrudesIntoAnyCorridorOrArrivalShellsWalkableVolume()
        {
            var rows = new[]
            {
                new Row(WorldLibrary.World1, fromWorldIndex: 0, toKey: WorldLibrary.World2),
                new Row(WorldLibrary.World2, fromWorldIndex: 1, toKey: WorldLibrary.World3),
            };

            var failures = new List<string>();

            foreach (Row row in rows)
            {
                CheckExitCorridor(row, failures);
                CheckArrivalShell(row, failures);
            }

            Assert.IsEmpty(failures, "MV-1116 corridor/shell intrusion violations:\n" + string.Join("\n", failures));
        }

        /// <summary>Builds whatever world-boot dressing (MV-1116: the actual culprit) a given world
        /// config normally gets at scene boot, before any corridor/arrival shell is built -- real
        /// RuntimeInitializeOnLoadMethod hooks never fire for a plain AddComponent in this project's
        /// EditMode harness (MV997/MV1077's own established idiom), so this drives Awake directly.</summary>
        private static void BuildWorldBootDressing(string worldKey, WorldConfig cfg, MapData map,
            Transform root, MapBuild built, List<GameObject> scratch)
        {
            if (worldKey == WorldLibrary.World1)
            {
                var pathGo = new GameObject("MV1116 BackyardPath");
                scratch.Add(pathGo);
                var path = pathGo.AddComponent<BackyardPath>();
                SetPrivateField(path, "_cfg", cfg);
                SetPrivateField(path, "_map", map);
                SetPrivateField(path, "_build", built);

                var backdropGo = new GameObject("MV1116 BackyardBackdrop");
                scratch.Add(backdropGo);
                var backdrop = backdropGo.AddComponent<BackyardBackdrop>();

                // Same collider-strip [Error] noise every full-world-build EditMode test in this suite
                // carries (BackyardBackdrop.Place strips each piece's own auto-added collider via
                // Destroy(), fine in Play mode, an error outside it) -- BackyardHomeShedTests/
                // MV1038PooledPickupRegateTests' own established idiom for this exact message.
                LogAssert.ignoreFailingMessages = true;
                try { InvokeAwake(backdrop); }
                finally { LogAssert.ignoreFailingMessages = false; }
            }
        }

        private static void CheckExitCorridor(Row row, List<string> failures)
        {
            GameObject root = null, playerGo = null;
            WorldJoinSequence sequence = null;
            var scratch = new List<GameObject>();
            try
            {
                WorldConfig cfg = WorldLibrary.Load(row.FromKey);
                Assert.IsNotNull(cfg, $"{row.FromKey} failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                WorldTransitionEntry entry = WorldTransitions.For(row.FromWorldIndex);
                Assert.IsNotNull(entry, $"world {row.FromWorldIndex} has no WorldTransitions row");

                WorldTransitions.ApplyExitDoorway(map, cfg, row.FromWorldIndex);
                root = new GameObject($"MV1116 Exit Root {row.FromKey}");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
                Assert.IsNotNull(built.ExitGate, $"world {row.FromWorldIndex} must build a real exit AreaGate");

                BuildWorldBootDressing(row.FromKey, cfg, map, root.transform, built, scratch);

                playerGo = new GameObject("MV1116 Max");
                playerGo.AddComponent<CharacterController>();
                PlayerController player = playerGo.AddComponent<PlayerController>();
                // OpenExitDoor only needs a live player to exist -- keep him well clear of the door so
                // he never factors into the intrusion scan himself.
                playerGo.transform.position = new Vector3(-5000f, 0f, -5000f);

                // --- the real entry point: the gate opens and the corridor is built behind it. ---
                WorldJoinSequence.OpenExitDoor(cfg, map, entry, row.FromWorldIndex, built.ExitGate);
                sequence = Object.FindFirstObjectByType<WorldJoinSequence>();
                Assert.IsNotNull(sequence, $"world {row.FromWorldIndex} must build a real corridor sequence behind the open gate");
                Assert.IsNotNull(sequence.CorridorSegmentRoot('A'), $"world {row.FromWorldIndex}'s corridor shell must exist");

                Vector2 doorMouth = entry.ExitDoorMouth(cfg);
                Bounds volume = WalkableVolume(doorMouth, entry.ExitWall, entry.CorridorLength,
                    WorldTransitionEntry.CorridorWidth, map.wallHeight);

                foreach (string hit in FindIntruders(volume, sequence.transform, player.transform, built.ExitGate.transform))
                    failures.Add($"world {row.FromWorldIndex} exit corridor: {hit}");
            }
            finally
            {
                if (sequence != null) Object.DestroyImmediate(sequence.gameObject);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                foreach (GameObject go in scratch) if (go != null) Object.DestroyImmediate(go);
                if (root != null) Object.DestroyImmediate(root);
                foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(stray.gameObject);
                WorldJoinDressing.Clear();
            }
        }

        private static void CheckArrivalShell(Row row, List<string> failures)
        {
            GameObject root = null, playerGo = null;
            WorldJoinSequence sequence = null;
            var scratch = new List<GameObject>();
            try
            {
                WorldConfig toCfg = WorldLibrary.Load(row.ToKey);
                Assert.IsNotNull(toCfg, $"{row.ToKey} failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(toCfg, out MapData map, out string reason), reason);

                WorldTransitionEntry entry = WorldTransitions.For(row.FromWorldIndex);
                Assert.IsNotNull(entry, $"world {row.FromWorldIndex} has no WorldTransitions row");

                root = new GameObject($"MV1116 Arrival Root {row.ToKey}");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms();

                BuildWorldBootDressing(row.ToKey, toCfg, map, root.transform, built, scratch);

                playerGo = new GameObject("MV1116 Max");
                playerGo.AddComponent<CharacterController>();
                PlayerController player = playerGo.AddComponent<PlayerController>();

                sequence = new GameObject("WorldJoinSequence (Arrival) MV1116").AddComponent<WorldJoinSequence>();
                sequence.InitializeArrival(toCfg, map, entry, row.FromWorldIndex, player, null);
                Assert.IsNotNull(sequence.ArrivalRoot, $"world {row.FromWorldIndex}'s arrival shell must exist");

                Vector2 doorMouth = entry.ArrivalDoorMouth(toCfg);
                Bounds volume = WalkableVolume(doorMouth, entry.ArrivalWall, entry.ArrivalShellLength,
                    WorldTransitionEntry.CorridorWidth, map.wallHeight);

                foreach (string hit in FindIntruders(volume, sequence.transform, player.transform))
                    failures.Add($"world {row.FromWorldIndex} arrival shell ({row.ToKey}): {hit}");
            }
            finally
            {
                if (sequence != null) Object.DestroyImmediate(sequence.gameObject);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                foreach (GameObject go in scratch) if (go != null) Object.DestroyImmediate(go);
                if (root != null) Object.DestroyImmediate(root);
                foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(stray.gameObject);
                WorldJoinDressing.Clear();
            }
        }

        /// <summary>Same outward-axis convention <see cref="WorldJoinSequence"/> itself uses internally
        /// (its own private <c>PointAtXZ</c>/<c>WalkableVolume</c>) -- every exit/arrival wall is N, E, S
        /// or W, so this is always an axis-aligned box, never a rotated one.</summary>
        private static Bounds WalkableVolume(Vector2 doorMouth, Wall wall, float length, float width, float wallHeight)
        {
            bool travelAlongX = wall == Wall.E || wall == Wall.W;
            bool positive = wall == Wall.N || wall == Wall.E;
            Rect footprint;
            if (travelAlongX)
            {
                float xMin = positive ? doorMouth.x : doorMouth.x - length;
                footprint = new Rect(xMin, doorMouth.y - width * 0.5f, length, width);
            }
            else
            {
                float yMin = positive ? doorMouth.y : doorMouth.y - length;
                footprint = new Rect(doorMouth.x - width * 0.5f, yMin, width, length);
            }

            return new Bounds(new Vector3(footprint.center.x, wallHeight * 0.5f, footprint.center.y),
                new Vector3(footprint.width, wallHeight, footprint.height));
        }

        private static List<string> FindIntruders(Bounds volume, params Transform[] ownRoots)
        {
            Bounds strict = volume;
            strict.Expand(-IntrusionEpsilon * 2f);

            var hits = new List<string>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;
                if (IsDescendantOfAny(r.transform, ownRoots)) continue;
                if (strict.Intersects(r.bounds))
                    hits.Add($"renderer '{r.name}' ({Path(r.transform)}), bounds {r.bounds}");
            }
            foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c == null || !c.enabled) continue;
                if (IsDescendantOfAny(c.transform, ownRoots)) continue;
                if (strict.Intersects(c.bounds))
                    hits.Add($"collider '{c.name}' ({Path(c.transform)}), bounds {c.bounds}");
            }
            return hits;
        }

        private static bool IsDescendantOfAny(Transform t, Transform[] ancestors)
        {
            foreach (Transform ancestor in ancestors)
            {
                if (ancestor == null) continue;
                for (Transform p = t; p != null; p = p.parent)
                    if (p == ancestor) return true;
            }
            return false;
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
