using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-378: Lee's own playtest of build 6fbe4fa found he (and robots) could walk straight through a
    /// gate that MV-364 was supposed to have made solid. <see cref="AreaGateTests"/> already pins the
    /// gate's DATA (width/position); this pins the thing MV-378 actually asks to be investigated --
    /// whether the BUILT gate's own <see cref="Collider"/> is really there, enabled, non-trigger, and
    /// sitting exactly where a moving <see cref="UnityEngine.CharacterController"/> would probe it --
    /// for every gate the game currently ships, not just a synthetic two-room fixture.
    /// </summary>
    public sealed class GateSolidityTests
    {
        /// <summary>Every gate the live game actually builds Max into (MV-270's World &amp; Difficulty
        /// Framework, <see cref="WorldLibrary.World1"/>) must physically block until it breaks -- not
        /// just the hand-written <c>backyard_slice.json</c> fixture the older tests use.</summary>
        [Test]
        public void EveryAreaGateInTheLiveWorldConfig_IsBuiltWithASolidNonTriggerCollider()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the live world config failed to load at all");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("Live World Gate Solidity Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                int gatesChecked = 0;
                foreach (MapEntity e in map.entities)
                {
                    if (e == null || e.Kind != EntityKind.AreaGate) continue;
                    gatesChecked++;

                    Assert.IsTrue(built.Actors.TryGetValue(e.id, out GameObject gate) && gate != null,
                        $"gate '{e.id}' is authored in the map but was never built");

                    var col = gate.GetComponent<Collider>();
                    Assert.IsNotNull(col, $"gate '{e.id}' carries no Collider at all");
                    Assert.IsTrue(col.enabled, $"gate '{e.id}' starts with its collider disabled");
                    Assert.IsFalse(col.isTrigger, $"gate '{e.id}' is a trigger, not a solid obstruction");

                    Collider[] hits = Physics.OverlapBox(gate.transform.position, Vector3.one * 0.05f,
                                                          gate.transform.rotation);
                    Assert.Contains(col, hits,
                        $"a physics query at gate '{e.id}' does not find its own collider there");
                }

                // The live config currently authors 18 gates (g0..g17) plus the boss gate (bg) -- if this
                // ever reads 0, the loop above passed by finding nothing to check, which would hide a
                // real regression (e.g. a kind string WorldMapLoader stops emitting correctly).
                Assert.Greater(gatesChecked, 0, "no area gates were found in the live world config at all");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
