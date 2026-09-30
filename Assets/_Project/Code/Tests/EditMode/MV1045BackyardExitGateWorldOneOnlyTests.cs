using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1045 (the one new test, per CC_AUTONOMY's testing policy): <see cref="BackyardExitGate"/>'s
    /// YT-153 garden-gate decoration must only ever build in World 1. Every world shares the same
    /// <see cref="BackyardPath"/> and <see cref="BigBermudaBoss"/> pair, since every boss uses
    /// <see cref="BigBermudaBoss"/> underneath -- but since MV-956/964 the real exit is each world's
    /// own finale door, so this decoration is obsolete everywhere except the Backyard's own look. Per
    /// World 3's V6 layout (MV-1030), <c>MapLayoutBridge.ToLayout</c>'s <c>ArenaEndZ</c> (read off the
    /// first Boss zone) runs through a1 near the entrance, so the old ungated gate read in play as a
    /// brown wooden wall across the start area.
    ///
    /// Fails on base commit a67d035: <c>BackyardExitGate.Install</c> gates only on "a BackyardPath and
    /// a BigBermudaBoss exist somewhere in the scene" -- true for every world -- so it builds the
    /// World 1 gate object (and, once <c>Awake</c> runs, its <c>PostL</c>/<c>PostR</c>/<c>Lintel</c>
    /// SurfaceKind.Wood posts) in World 2 and World 3 as well as World 1.
    /// </summary>
    public sealed class MV1045BackyardExitGateWorldOneOnlyTests
    {
        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<BackyardExitGate>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<BackyardExitGate>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
        }

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static void SetResolvedWorldIndex(BackyardPath path, int index) =>
            typeof(BackyardPath).GetProperty(nameof(BackyardPath.ResolvedWorldIndex))
                .SetValue(path, index);

        private static void InvokeInstall() =>
            typeof(BackyardExitGate).GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        /// <summary>Which of our materials this is, by the key baked into its name -- same idiom
        /// <see cref="SurfaceMaterialTests"/>' own KindOfMaterial helper uses.</summary>
        private static SurfaceKind? KindOfMaterial(Material m)
        {
            if (m == null) return null;
            foreach (SurfaceKind k in System.Enum.GetValues(typeof(SurfaceKind)))
                if (m.name.Contains($":{k}:") || m.name.EndsWith($":{k}")) return k;
            return null;
        }

        private readonly struct Row
        {
            public readonly string WorldKey;
            public readonly int WorldIndex;
            public readonly string BossId;
            public Row(string worldKey, int worldIndex, string bossId)
            { WorldKey = worldKey; WorldIndex = worldIndex; BossId = bossId; }
        }

        [Test]
        public void ExitGateOnlyBuildsInWorldOne_WorldsTwoAndThreeStayClearOfIt()
        {
            var rows = new[]
            {
                new Row(WorldLibrary.World1, worldIndex: 0, bossId: "a30_boss1"),
                new Row(WorldLibrary.World2, worldIndex: 1, bossId: "sludgequeen"),
                new Row(WorldLibrary.World3, worldIndex: 2, bossId: "anchorhead"),
            };

            foreach (Row row in rows)
            {
                GameObject root = null, pathGo = null;
                try
                {
                    // --- build that world's REAL map through the normal MapRuntime path (same idiom
                    // MV997WorldExitDoorTests/MV1018AnchorheadBossTests already use). ---
                    WorldConfig cfg = WorldLibrary.Load(row.WorldKey);
                    Assert.IsNotNull(cfg, $"{row.WorldKey} failed to load — see the error log above.");
                    Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
                    WorldTransitions.ApplyExitDoorway(map, cfg, row.WorldIndex);

                    root = new GameObject($"MV1045 Root {row.WorldKey}");
                    MapBuild built = MapRuntime.Build(map, root.transform);
                    Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                    Assert.IsTrue(built.Actors.TryGetValue(row.BossId, out GameObject bossGo) && bossGo != null,
                        $"{row.WorldKey}'s boss ('{row.BossId}') was not built");
                    Assert.IsNotNull(bossGo.GetComponent<BigBermudaBoss>(),
                        $"{row.WorldKey}'s boss must be a BigBermudaBoss -- the exit gate's own boss-exists gate");

                    // BackyardPath.Awake is never invoked here (this project's EditMode runner does not
                    // reliably fire it for a plain AddComponent, and it would try to reload/rebuild the
                    // whole world itself) -- wire the fields it would have set directly, same idiom
                    // MV997WorldExitDoorTests/MV836FloodOffTests already use for this exact component.
                    pathGo = new GameObject($"MV1045 BackyardPath {row.WorldKey}");
                    var path = pathGo.AddComponent<BackyardPath>();
                    SetPrivateField(path, "_cfg", cfg);
                    SetPrivateField(path, "_map", map);
                    SetPrivateField(path, "_build", built);
                    SetPrivateField(path, "_layout", MapLayoutBridge.ToLayout(map));
                    SetResolvedWorldIndex(path, row.WorldIndex);

                    InvokeInstall();

                    var gate = Object.FindFirstObjectByType<BackyardExitGate>();
                    if (row.WorldIndex == 0)
                        Assert.IsNotNull(gate, "World 1 must still build its own exit-gate object (unchanged behaviour)");
                    else
                        Assert.IsNull(gate,
                            $"{row.WorldKey} (index {row.WorldIndex}) must never build the World 1 exit-gate object");

                    // Awake is what actually builds PostL/PostR/Lintel -- drive it explicitly (same
                    // "AddComponent doesn't reliably fire Awake" idiom the rest of this suite uses) so
                    // this proves the posts themselves, not just the wrapper object.
                    if (gate != null) InvokeAwake(gate);

                    foreach (string name in new[] { "PostL", "PostR", "Lintel" })
                    {
                        bool exists = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                            .Any(t => t.name == name);
                        if (row.WorldIndex == 0)
                            Assert.IsTrue(exists, $"World 1 must still build its '{name}'");
                        else
                            Assert.IsFalse(exists,
                                $"{row.WorldKey}: a stray '{name}' from the World 1 exit gate must not exist");
                    }

                    if (row.WorldIndex == 2)
                    {
                        // The hypothesis this ticket confirms: World 3's a1 (near the entrance) must
                        // carry no Wood-surfaced renderer at all -- not just none named PostL/PostR/Lintel.
                        // MapZone.id is the runtime "area<N>" form (WorldMapLoader.TryLoad), not the
                        // authored WorldConfig area id ("a1") -- MapZone.AreaIndex is what parses that
                        // back out (MapEnums.AreaIndexOf).
                        MapZone a1 = map.zones.FirstOrDefault(z => z.AreaIndex == 1);
                        Assert.IsNotNull(a1, "World 3 must author an area1 zone (authored id 'a1')");
                        Rect footprint = a1.Footprint;

                        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                        {
                            if (KindOfMaterial(r.sharedMaterial) != SurfaceKind.Wood) continue;
                            Bounds b = r.bounds;
                            var rendererFootprint = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                            Assert.IsFalse(footprint.Overlaps(rendererFootprint),
                                $"World 3: renderer '{r.name}' uses a Wood material and lies inside a1's footprint");
                        }
                    }
                }
                finally
                {
                    if (pathGo != null) Object.DestroyImmediate(pathGo);
                    foreach (var stray in Object.FindObjectsByType<BackyardExitGate>(FindObjectsSortMode.None))
                        Object.DestroyImmediate(stray.gameObject);
                    if (root != null) Object.DestroyImmediate(root);
                    WorldJoinDressing.Clear();
                    StormdrainKit.Clear();
                    MaterialLibrary.Clear();
                    BossCensus.Reset();
                }
            }
        }
    }
}
