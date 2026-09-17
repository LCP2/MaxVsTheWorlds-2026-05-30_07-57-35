using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-829 — World 2's hatches never locked. <c>MapRuntime.BuildHatch</c>'s own comment said
    /// "unlocked until MV-703 exists to read a real condition" and was never revisited once MV-703
    /// actually landed <see cref="GateCondition"/>, so every hatch defaulted to unlocked regardless of
    /// its authored <c>opensWith</c> — and the flat 0.3 m panel it built would not have physically
    /// blocked Max even if it had been locked. Separately, <c>a3_hatch2</c> shipped on the WEST edge of
    /// <c>a3_deck2</c> while <c>a3_ramp2</c> climbs to the deck's EAST edge, so even a correctly-locked
    /// barrier there would have guarded the wrong side of the gantry.
    ///
    /// Fails on base commit fb55432 (before this ticket): every hatch's <see cref="AreaGate.Locked"/>
    /// reads false immediately after <see cref="WorldRunner.Configure"/> (nothing ever set it), and
    /// <c>a3_hatch2</c>'s own rect sits at area-local x 25 — the deck's west edge, opposite where
    /// <c>a3_ramp2</c> actually arrives.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) loading the real, shipped World 2 config
    /// through <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>/<see cref="WorldRunner"/> — no
    /// hand-set <see cref="AreaGate.Locked"/>/opensWith anywhere in this file — asserting three RESOLVED
    /// values (Rule 2, Tier 2): (a) every hatch is Locked with a real, tall barrier collider (AC1);
    /// (b) destroying every Replicator authored into a12 unlocks only <c>a12_hatch1</c>, leaving
    /// <c>a3_hatch1</c> locked (AC2); (c) <see cref="MapValidation"/> refuses a copy of the config with
    /// <c>a3_hatch2</c> moved back to its old, wrong-edge position, and passes the shipped one (AC3).
    /// </summary>
    public sealed class MV829HatchLockTests
    {
        private static readonly string[] AllHatchIds =
        {
            "a3_hatch1", "a3_hatch2", "a6_hatch1", "a6_hatch2", "a11_hatch1", "a11_hatch2", "a12_hatch1",
        };

        /// <summary>Awake never runs as a side effect of AddComponent outside Play mode (repo-wide
        /// convention — see e.g. AreaGateTests.InvokeAwake) — without this, a hatch's AreaGate never
        /// builds its threshold collider or health, so Locked/ForceOpen would silently do nothing.</summary>
        private static void InvokeAwake(AreaGate gate) =>
            typeof(AreaGate).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gate, null);

        [Test]
        public void World2Hatches_LockOnLoad_UnlockOnTheirOwnCondition_AndValidationCatchesTheWrongEdge()
        {
            // Same BuildBody collider-strip [Error] every Replicator EditMode test in this suite carries
            // once Build() actually runs (below) — see MV775ReplicatorStagingTests' own note.
            LogAssert.ignoreFailingMessages = true;

            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string loadReason), loadReason);

            var host = new GameObject("MV829 host");
            try
            {
                MapBuild build = MapRuntime.Build(map, host.transform);

                // Real production wiring for every gate/hatch AreaGate this map built (threshold
                // collider, health, leaf collider forced solid) — see InvokeAwake's own doc comment.
                foreach (AreaGate g in host.GetComponentsInChildren<AreaGate>(true)) InvokeAwake(g);

                // Real production wiring for every Replicator this map built (health, spawner stop) —
                // AddComponent's own Awake never runs here either (Replicator.Build's own doc comment).
                foreach (Replicator r in build.Replicators) r.Build();

                var runner = host.AddComponent<WorldRunner>();
                runner.Configure(w2cfg, map, build, null); // no AreaAccumulationDirector needed for AC1/AC2

                // === AC1: every hatch is Locked, with a real, tall barrier collider ===
                foreach (string id in AllHatchIds)
                {
                    Assert.IsTrue(build.Actors.TryGetValue(id, out GameObject hatchGo) && hatchGo != null,
                        $"MapRuntime never built hatch '{id}'");

                    AreaGate gate = hatchGo.GetComponent<AreaGate>();
                    Assert.IsNotNull(gate, $"'{id}' carries no AreaGate");
                    Assert.IsTrue(gate.Locked, $"MV-829 AC1: '{id}' must be Locked immediately after load");

                    Collider barrier = hatchGo.GetComponent<Collider>();
                    Assert.IsNotNull(barrier, $"'{id}' carries no collider");
                    Assert.IsTrue(barrier.enabled, $"MV-829 AC1: '{id}'s barrier collider must be enabled while locked");

                    // Collider.bounds can lag a manual transform change under -batchmode -nographics
                    // (no physics sync tick) — Renderer.bounds is mesh-driven and always current, the
                    // same idiom every other MapRuntime size assertion in this suite uses (e.g.
                    // MV771WorldTwoWallHeightTests' own wall/gate height checks).
                    Renderer barrierRenderer = hatchGo.GetComponent<Renderer>();
                    Assert.IsNotNull(barrierRenderer, $"'{id}' carries no renderer");
                    Assert.GreaterOrEqual(barrierRenderer.bounds.size.y, 1.5f,
                        $"MV-829 AC1: '{id}'s barrier resolves {barrierRenderer.bounds.size.y:F2} m tall, under the 1.5 m floor");
                }

                // === AC2: destroying every Replicator authored into a12 unlocks only a12_hatch1 ===
                WorldArea a12 = w2cfg.Area("a12");
                Assert.IsNotNull(a12, "setup failure: World 2 must author area 'a12'");
                Assert.Greater(a12.replicators.Length, 0, "setup failure: a12 must author at least one Replicator");

                foreach (WorldReplicator r in a12.replicators)
                {
                    Assert.IsTrue(build.Actors.TryGetValue(r.id, out GameObject repGo) && repGo != null,
                        $"setup failure: MapRuntime never built Replicator '{r.id}'");
                    Replicator replicator = repGo.GetComponent<Replicator>();
                    Assert.IsNotNull(replicator, $"setup failure: '{r.id}' carries no Replicator");
                    Assert.IsTrue(replicator.IsAlive, $"setup failure: '{r.id}' must start alive for its death to prove anything");
                    replicator.TakeDamage(new DamageInfo(99999f, repGo.transform.position, Vector3.forward, Team.Player));
                    Assert.IsFalse(replicator.IsAlive, $"setup failure: '{r.id}' must actually die from lethal Player damage");
                }

                runner.RefreshConditionGates();

                AreaGate a12Gate = build.Actors["a12_hatch1"].GetComponent<AreaGate>();
                Assert.IsFalse(a12Gate.Locked, "MV-829 AC2: a12_hatch1 must unlock once every a12 Replicator is destroyed");
                Assert.IsFalse(a12Gate.ThresholdObject.GetComponent<Collider>().enabled,
                    "MV-829 AC2: a12_hatch1's threshold collider must disable once it force-opens");

                AreaGate a3Gate = build.Actors["a3_hatch1"].GetComponent<AreaGate>();
                Assert.IsTrue(a3Gate.Locked, "MV-829 AC2: a3_hatch1 must stay locked — its condition is unrelated to a12");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            // === AC3: MapValidation catches a3_hatch2 back on the wrong edge, and passes the shipped config ===
            Assert.IsTrue(MapValidation.ValidateWorldConfig(w2cfg, out string shippedReason),
                $"MV-829 AC3: the shipped World 2 config must pass validation: {shippedReason}");

            WorldConfig broken = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(broken, "setup failure: World 2 must load a second, independent copy");
            WorldHatch hatch2 = broken.Area("a3").hatches.First(h => h.id == "a3_hatch2");
            hatch2.x = 25f; // the old, wrong-edge position this ticket moved off of

            Assert.IsFalse(MapValidation.ValidateWorldConfig(broken, out string brokenReason),
                "MV-829 AC3: a3_hatch2 back on the deck's wrong edge must fail validation");
            StringAssert.Contains("a3_hatch2", brokenReason);
        }
    }
}
