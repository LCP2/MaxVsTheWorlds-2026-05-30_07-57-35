using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-997 (the one new test, per CC_AUTONOMY's testing policy): loops every <see cref="WorldTransitions"/>
    /// row against the real, shipped configs (World 1's a30 E wall, World 2's a21 E wall) and proves the
    /// exit door is real map geometry from boot, not <see cref="WorldJoinSequence"/>'s old runtime
    /// <c>CutWallGap</c>: closed at build time, no live <see cref="StructuralWall"/> collider or enabled
    /// renderer crossing the 3 m door span, exactly one closed <see cref="AreaGate"/> filling it -- then
    /// drives that world's real, map-built final boss to death and proves that SAME gate opens, with the
    /// corridor shell built behind it.
    ///
    /// Fails on base commit 8c86feb: <see cref="WorldTransitions"/> row 0's <c>ExitWall</c> is
    /// <c>Wall.N</c> (a30's door mouth never resolves against the E wall this test probes),
    /// <see cref="MapData"/> carries no <c>exitDoorway</c> field at all (CS1061), and <see cref="MapBuild"/>
    /// carries no <c>ExitGate</c> (CS1061).
    /// </summary>
    public sealed class MV997WorldExitDoorTests
    {
        [SetUp]
        public void SetUp()
        {
            // Same defensive sweep MV845WorldJoinSequenceTests/MV849CorridorBlendTests/MV965/967 already
            // make: a stray WorldJoinSequence left by an earlier fixture in this EditMode batch would
            // make OpenExitDoor's own "already running" guard bail silently, with no exception and no
            // useful assertion message.
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            // MV-1079: Beat A/Beat B's own scratch VFX/UI are never torn down by OnDisable here -- same
            // "OnDisable isn't reliably invoked for AddComponent outside Play mode" note the
            // WorldJoinSequence/BackyardLighting sweeps above already carry.
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void InvokeOnEnable(Object component) =>
            component.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        private static Vector3 AlongDoorProbe(Vector2 doorMouth, Wall wall, float wallHeight, float delta) =>
            (wall == Wall.N || wall == Wall.S)
                ? new Vector3(doorMouth.x + delta, wallHeight * 0.5f, doorMouth.y)
                : new Vector3(doorMouth.x, wallHeight * 0.5f, doorMouth.y + delta);

        private readonly struct Row
        {
            public readonly string WorldKey;
            public readonly int WorldIndex;
            public readonly string BossId;
            public readonly Vector2 DoorMouth;

            public Row(string worldKey, int worldIndex, string bossId, Vector2 doorMouth)
            {
                WorldKey = worldKey; WorldIndex = worldIndex; BossId = bossId; DoorMouth = doorMouth;
            }
        }

        [Test]
        public void EveryWorldExitDoorIsRealClosedGeometry_AndOpensOnItsOwnFinalBossDeath()
        {
            var rows = new[]
            {
                new Row(WorldLibrary.World1, worldIndex: 0, bossId: "a30_boss1", doorMouth: new Vector2(338f, 106f)),
                // MV-1050: a21 grew to 76x76 (origin z -12..64); its E wall moved from x=136 to
                // x=168, and WorldTransitions' own exitDoorPos was retuned (54/76) to hold the door
                // mouth's absolute Z at the same 42 it sat at before the resize.
                new Row(WorldLibrary.World2, worldIndex: 1, bossId: "sludgequeen", doorMouth: new Vector2(168f, 42f)),
            };

            foreach (Row row in rows)
            {
                GameObject root = null, pathGo = null, gateGo = null, payoffGo = null, sequenceGo = null, playerGo = null;
                try
                {
                    WorldConfig cfg = WorldLibrary.Load(row.WorldKey);
                    Assert.IsNotNull(cfg, $"{row.WorldKey} failed to load — see the error log above.");
                    Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                    WorldTransitionEntry entry = WorldTransitions.For(row.WorldIndex);
                    Assert.IsNotNull(entry, $"world {row.WorldIndex} has no WorldTransitions row");
                    Assert.AreEqual(Wall.E, entry.ExitWall,
                        $"world {row.WorldIndex}'s exit must run east, into an arrival that also runs east");

                    Vector2 doorMouth = entry.ExitDoorMouth(cfg);
                    Assert.AreEqual(row.DoorMouth.x, doorMouth.x, 0.01f, $"world {row.WorldIndex}'s door mouth X");
                    Assert.AreEqual(row.DoorMouth.y, doorMouth.y, 0.01f, $"world {row.WorldIndex}'s door mouth Z");

                    // --- build that world's REAL map through the normal MapRuntime path. ---
                    WorldTransitions.ApplyExitDoorway(map, cfg, row.WorldIndex);
                    root = new GameObject($"MV997 Root {row.WorldKey}");
                    MapBuild built = MapRuntime.Build(map, root.transform);
                    Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                    Assert.IsNotNull(built.ExitGate, $"world {row.WorldIndex} must build a real exit AreaGate");
                    // AddComponent never fires Awake as a side effect inside this project's synchronous
                    // EditMode harness (confirmed empirically for AreaGate — AreaGateTests.InvokeAwake's
                    // own note) — without this, _health stays null and ForceOpen() silently no-ops later.
                    InvokeAwake(built.ExitGate);
                    Assert.IsFalse(built.ExitGate.IsOpen, $"world {row.WorldIndex}'s exit gate must start closed");

                    // --- the resolved state at the door mouth, at mid wall height: no live StructuralWall
                    // collider/renderer and no enabled renderer bounds overlap the 3 m door span. ---
                    foreach (float delta in new[] { -1.0f, 0f, 1.0f })
                    {
                        Vector3 probe = AlongDoorProbe(doorMouth, entry.ExitWall, map.wallHeight, delta);

                        foreach (StructuralWall w in root.GetComponentsInChildren<StructuralWall>(true))
                        {
                            var col = w.GetComponent<Collider>();
                            if (col != null && col.enabled)
                                Assert.IsFalse(col.bounds.Contains(probe),
                                    $"world {row.WorldIndex}: a StructuralWall collider still covers the door span (delta {delta})");

                            var rend = w.GetComponent<Renderer>();
                            if (rend != null && rend.enabled)
                                Assert.IsFalse(rend.bounds.Contains(probe),
                                    $"world {row.WorldIndex}: a StructuralWall renderer still covers the door span (delta {delta})");
                        }
                    }

                    // --- exactly one AreaGate occupies the door span, closed. ---
                    Vector3 centerProbe = AlongDoorProbe(doorMouth, entry.ExitWall, map.wallHeight, 0f);
                    AreaGate[] gatesAtDoor = root.GetComponentsInChildren<AreaGate>(true)
                        .Where(g =>
                        {
                            var col = g.GetComponent<Collider>();
                            return col != null && col.bounds.Contains(centerProbe);
                        }).ToArray();
                    Assert.AreEqual(1, gatesAtDoor.Length,
                        $"world {row.WorldIndex}: exactly one AreaGate must occupy the door span");
                    Assert.AreSame(built.ExitGate, gatesAtDoor[0],
                        $"world {row.WorldIndex}: the gate occupying the door span must be MapRuntime's own ExitGate");

                    // --- drive that world's real, map-built final boss(es) to death. ---
                    Assert.IsTrue(built.Actors.TryGetValue(row.BossId, out GameObject bossGo) && bossGo != null,
                        $"{row.WorldKey}'s a{cfg.dials.areaCount} boss ('{row.BossId}') was not built");
                    var boss = bossGo.GetComponent<BigBermudaBoss>();
                    Assert.IsNotNull(boss, $"world {row.WorldIndex}'s final boss must build as a BigBermudaBoss (MapRuntime.BuildBoss)");

                    List<BigBermudaBoss> finalBosses = built.Bosses
                        .Where(b => b != null && map.ZoneAt(b.transform.position.x, b.transform.position.z)?.AreaIndex == cfg.dials.areaCount)
                        .ToList();
                    Assert.IsNotEmpty(finalBosses, $"world {row.WorldIndex} built no boss inside its own final area");

                    pathGo = new GameObject($"MV997 BackyardPath {row.WorldKey}");
                    var path = pathGo.AddComponent<BackyardPath>();
                    // BackyardPath.Awake is never invoked here (this project's EditMode runner does not
                    // reliably fire it for a plain AddComponent, and it would try to reload/rebuild the
                    // whole world itself) -- wire the fields it would have set directly, same idiom
                    // MV836FloodOffTests already uses for this exact component.
                    SetPrivateField(path, "_cfg", cfg);
                    SetPrivateField(path, "_map", map);
                    SetPrivateField(path, "_build", built);

                    var areaDirector = pathGo.AddComponent<AreaAccumulationDirector>();
                    areaDirector.ConfigureWorld(cfg, row.WorldIndex);

                    // WorldJoinSequence.OpenExitDoor needs a live player to walk through the door -- it
                    // silently no-ops without one (same guard a scene with no player at all hits).
                    playerGo = new GameObject("MV997 Max");
                    playerGo.AddComponent<CharacterController>();
                    playerGo.AddComponent<PlayerController>();

                    // Neither Awake nor OnEnable is reliably invoked for a plain AddComponent outside
                    // Play mode (this project's own established EditMode idiom) -- drive both explicitly
                    // so _cfg/_map/_entry/_exitGate actually resolve and the BossDefeated subscription is
                    // live, unlike Mv915WorldOneFinaleSequenceTests/MV959World2FinaleChainTests, which
                    // deliberately leave Awake unfired to test the geometry-independent, always-null path.
                    gateGo = new GameObject("WorldFinaleGate Test");
                    var gate = gateGo.AddComponent<WorldFinaleGate>();
                    InvokeAwake(gate);
                    InvokeOnEnable(gate);

                    // MV-1078: the Core is what actually matters now, not the boss dying -- needs a
                    // real BossVictoryPayoff to drop it (this test previously had none, since it never
                    // needed one before).
                    payoffGo = new GameObject("BossVictoryPayoff Test");
                    var payoff = payoffGo.AddComponent<BossVictoryPayoff>();
                    InvokeOnEnable(payoff);

                    foreach (BigBermudaBoss b in finalBosses)
                        BossCensus.Register(b, "TEST BOSS", phases: 1, current: 100f, max: 100f, areaIndex: cfg.dials.areaCount);

                    Assert.IsFalse(gate.IsOpen, $"world {row.WorldIndex}'s gate must stay shut before its own boss dies");

                    foreach (BigBermudaBoss b in finalBosses) InvokeOnDeath(b);

                    // MV-1078: the boss dying no longer opens the exit by itself -- the Core must be
                    // collected first (this director never calls Configure/EnterArea, so its own
                    // per-area robot count is 0, and the exit opens as soon as the Core is taken).
                    Assert.IsFalse(gate.IsOpen,
                        $"world {row.WorldIndex}: boss death alone must no longer open the exit");

                    var pickupDirector = PickupDirector.EnsureInstalled();
                    Pickup core = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)
                        .Single(p => p.Kind == PickupKind.WeaponCore);
                    InvokeCollect(pickupDirector, core);

                    // MV-1079: collecting the Core now starts Beat A (2.5s; this director's own per-area
                    // robot count is 0, so clean-up finds zero left and moves straight to Beat B, 1.0s of
                    // which is what actually calls WorldFinaleGate.Open()).
                    gate.TickWeaponBeat(2.5f);
                    gate.TickExitBeat(1.0f);

                    Assert.IsTrue(gate.IsOpen, $"world {row.WorldIndex}'s gate must open once the Core is collected");
                    Assert.IsTrue(built.ExitGate.IsOpen,
                        $"world {row.WorldIndex}: the SAME real map gate MapRuntime built must be the one that opened");

                    var sequence = Object.FindFirstObjectByType<WorldJoinSequence>();
                    Assert.IsNotNull(sequence, $"world {row.WorldIndex} must build a real corridor sequence behind the open gate");
                    sequenceGo = sequence.gameObject;
                    Assert.IsNotNull(sequence.CorridorSegmentRoot('A'),
                        $"world {row.WorldIndex}'s corridor shell must exist behind the open gate");
                }
                finally
                {
                    if (sequenceGo != null) Object.DestroyImmediate(sequenceGo);
                    if (gateGo != null) Object.DestroyImmediate(gateGo);
                    if (payoffGo != null) Object.DestroyImmediate(payoffGo);
                    if (playerGo != null) Object.DestroyImmediate(playerGo);
                    if (pathGo != null) Object.DestroyImmediate(pathGo);
                    if (root != null) Object.DestroyImmediate(root);
                    foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                        Object.DestroyImmediate(stray.gameObject);
                    WorldJoinDressing.Clear();
                    StormdrainKit.Clear();
                    MaterialLibrary.Clear();
                    BossCensus.Reset();
                }
            }
        }
    }
}
