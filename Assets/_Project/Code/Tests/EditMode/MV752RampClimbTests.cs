using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-752: "Max can't go up ramps although enemies can" — two independent causes, both fixed here.
    /// (1) <see cref="PlayerController.Update"/> used to fold the horizontal walk and vertical gravity
    /// into ONE <c>CharacterController.Move</c> call; <c>CharacterController</c> projects that combined
    /// vector onto a ramp's slope, so the downward component cancelled most of the forward one and Max
    /// climbed at a tenth speed. <see cref="RobotEnemy"/> never had this bug — it already issues a
    /// horizontal and a vertical <c>Move</c> separately. (2) five World 2 ramps were authored steeper
    /// than Max's 45-degree slope limit could ever climb — <c>a18_ramp1</c> resolved to 68.2 degrees on
    /// the pre-fix `world2_config.json` (run 1 m against the world's 2.5 m deck height), unclimbable by
    /// anything with a slope limit at all.
    /// </summary>
    public sealed class MV752RampClimbTests
    {
        // Awake isn't reliably invoked for AddComponent/GameObject-constructor components outside
        // Play mode (same empirical finding MV503StuckDiagnosticTests already works around) — without
        // this, PlayerController's private _cc field is never set.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        /// <summary>The steepest ramp slope (degrees) across every shipped world config — the same
        /// quantity both <see cref="MV_EveryRampIsClimbable"/> and
        /// <see cref="MV_PlayerSlopeLimitCoversAuthoredRamps"/> need, computed once.</summary>
        private static float SteepestShippedRampDegrees(out string steepestId, out string steepestWorld)
        {
            float steepest = 0f;
            steepestId = null;
            steepestWorld = null;

            foreach (string key in WorldLibrary.Keys)
            {
                WorldConfig cfg = WorldLibrary.Load(key);
                Assert.IsNotNull(cfg, $"world '{key}' failed to load");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), $"world '{key}': {reason}");

                foreach (RampSlab ramp in MapGeometry.Ramps(map))
                {
                    if (ramp.SlopeDegrees <= steepest) continue;
                    steepest = ramp.SlopeDegrees;
                    steepestId = ramp.Id;
                    steepestWorld = key;
                }
            }

            return steepest;
        }

        /// <summary>AC1: every authored ramp, across every shipped world config, must be climbable by
        /// something with a sane slope limit. Fails on current main, naming <c>a18_ramp1</c> at 68.2
        /// degrees — the pre-fix `world2_config.json` gives it a 1 m run against the 2.5 m deck height
        /// (<c>atan(2.5/1) = 68.2</c>), steeper than any reasonable <c>CharacterController.slopeLimit</c>.</summary>
        [Test]
        public void MV_EveryRampIsClimbable()
        {
            foreach (string key in WorldLibrary.Keys)
            {
                WorldConfig cfg = WorldLibrary.Load(key);
                Assert.IsNotNull(cfg, $"world '{key}' failed to load");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), $"world '{key}': {reason}");
                Assert.IsTrue(MapValidation.Validate(map, out string mapReason), $"world '{key}': {mapReason}");

                foreach (RampSlab ramp in MapGeometry.Ramps(map))
                {
                    Assert.LessOrEqual(ramp.SlopeDegrees, 50f,
                        $"world '{key}': ramp '{ramp.Id}' resolves to {ramp.SlopeDegrees:0.#} degrees — " +
                        "steeper than the 50-degree ceiling every authored ramp must respect");
                }
            }
        }

        /// <summary>AC2: <see cref="PlayerController.Update"/> must issue its horizontal walk and its
        /// vertical gravity as two distinct <c>CharacterController.Move</c> calls, never summed into one
        /// — driven here via the private <c>ComputeSplitMotion</c> helper (same reflection idiom as
        /// <c>MV503StuckDiagnosticTests</c>), asserting the horizontal vector's Y is exactly 0 and the
        /// vertical vector's X/Z are exactly 0. A test that merely checks Max's position changed would
        /// pass on the broken single-Move code and is explicitly NOT acceptable per this ticket's AC.</summary>
        [Test]
        public void MV_PlayerAppliesGravitySeparately()
        {
            var go = new GameObject("MV-752 Split Motion Probe", typeof(CharacterController), typeof(PlayerController));
            try
            {
                PlayerController player = go.GetComponent<PlayerController>();
                InvokeAwake(player);

                MethodInfo compute = typeof(PlayerController).GetMethod("ComputeSplitMotion",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                object result = compute.Invoke(player, new object[] { new Vector3(2f, 0f, 3f), 0.1f });

                var resultType = result.GetType();
                var horizontal = (Vector3)resultType.GetField("Item1").GetValue(result);
                var vertical = (Vector3)resultType.GetField("Item2").GetValue(result);

                Assert.AreEqual(0f, horizontal.y,
                    "the horizontal motion must never carry a Y component -- summing it with gravity into " +
                    "one Move call is exactly the bug this ticket fixes");
                Assert.AreEqual(0f, vertical.x, "the vertical (gravity) motion must never carry an X component");
                Assert.AreEqual(0f, vertical.z, "the vertical (gravity) motion must never carry a Z component");
                Assert.AreNotEqual(0f, vertical.y, "gravity should still be accumulating downward");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>AC3: <c>PlayerController.Awake</c> must leave <c>slopeLimit</c> at or above the
        /// steepest ramp in any shipped world config, so raising it in code (from 45 to 55) actually
        /// covers what the level designs author, not just an arbitrary higher number.</summary>
        [Test]
        public void MV_PlayerSlopeLimitCoversAuthoredRamps()
        {
            var go = new GameObject("MV-752 Slope Limit Probe", typeof(CharacterController), typeof(PlayerController));
            float slopeLimit;
            try
            {
                InvokeAwake(go.GetComponent<PlayerController>());
                slopeLimit = go.GetComponent<CharacterController>().slopeLimit;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            float steepest = SteepestShippedRampDegrees(out string steepestId, out string steepestWorld);
            Assert.GreaterOrEqual(slopeLimit, steepest,
                $"Max's CharacterController.slopeLimit ({slopeLimit}) must cover the steepest authored ramp " +
                $"'{steepestId}' in world '{steepestWorld}' ({steepest:0.#} degrees) or he can never climb it");
        }
    }
}
