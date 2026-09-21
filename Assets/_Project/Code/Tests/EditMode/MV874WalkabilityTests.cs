using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-874: <c>MapValidation.WorldReachability</c> proves the GATE GRAPH connects — it does not prove
    /// a body of Max's own width (1 m: CharacterController radius 0.5 m, skin width 0.08 —
    /// <c>Backyard_Slice.unity</c>) can actually WALK the floor behind a gate. World 2's a13 passes that
    /// old graph check clean even though its own exit gate (g31) sits behind a pocket sealed by three
    /// pipe covers (<c>a13_cover10/11/15</c>) — a flood fill of a13's floor, every obstacle inflated by
    /// Max's own body radius, reaches only part of the free floor from the entry gate (g30) and never
    /// reaches g31.
    ///
    /// Fails on base commit 68d962b: <see cref="MapValidation"/> carries no walkability rule at all yet,
    /// so <c>ValidateWorldConfig</c> returns TRUE for the shipped config and <c>Assert.IsFalse(ok)</c>
    /// below reads <c>Assert.IsFalse(true)</c> — a deterministic NUnit failure
    /// ("Expected: False  But was:  True"), quoted for real in this ticket's fix comment.
    ///
    /// Reads the SHIPPED <c>world2_config.json</c> text asset directly and calls
    /// <see cref="MapValidation.ValidateWorldConfig"/> on it — not <see cref="WorldLibrary.Load"/> or
    /// <see cref="WorldConfigLoader.TryLoad"/>, both of which now return null/false for World 2 once this
    /// rule fires, which would leave nothing to assert against.
    /// </summary>
    public sealed class MV874WalkabilityTests
    {
        [Test]
        public void ShippedWorldTwo_RefusesA13SealedExitGate()
        {
            var asset = Resources.Load<TextAsset>($"{WorldLibrary.ResourceRoot}/{WorldLibrary.World2}");
            Assert.IsNotNull(asset, "setup failure: world2_config.json must exist as a Resources text asset");

            WorldConfig cfg = JsonUtility.FromJson<WorldConfig>(asset.text);
            Assert.IsNotNull(cfg, "setup failure: world2_config.json must parse");

            bool ok = MapValidation.ValidateWorldConfig(cfg, out string reason);

            Assert.IsFalse(ok, "MV-874: the shipped World 2 config must be refused — a13's own exit gate " +
                                "sits behind a pocket sealed by pipe cover, which the old gate-graph-only " +
                                "check (WorldReachability) never saw. Reason: " + reason);
            StringAssert.Contains("a13", reason);
            StringAssert.Contains("g31", reason);
        }
    }
}
