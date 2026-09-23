using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-918 — Lee, 2026-09-23, playing a fresh build: "this issue with the maze and this invisible
    /// barrier that Max can't navigate is still there," at a11's and a13's west-gate corners, surviving
    /// MV-895's <c>minMoveDistance</c> fix (that fix was real and stays — it was never the whole cause).
    ///
    /// Root cause (measured, per this ticket's own Jira comment — not re-derived here): MV-759 gave
    /// World 2's <see cref="AreaGate"/> a cosmetic Stormdrain skin — a sliding double door
    /// (<see cref="AreaGate.ApplyStormdrainGateSkin"/>) that hides the plain leaf's own renderer and
    /// reads as fully clear the instant the gate opens, but never touches the leaf's own collider. That
    /// collider still rides the pre-MV-759 hinge-swing physics (<see cref="AreaGate.StartHingeSwing"/>),
    /// deliberately left solid through the swing by MV-386 — correct back when the swinging leaf was
    /// itself the visible thing you'd expect to still block, wrong now that nothing visible marks where
    /// it ends up. For g28 (a11's west gate) and g30 (a13's, an identical doorway) that swing lands the
    /// leaf's own collider at world X[222,225.8] Z[105.58,106.88] (measured via this exact test, pre-fix)
    /// — squarely across the lane along a11's/a13's own west wall, right at the corner where it turns
    /// into the doorway, no renderer left enabled anywhere to show it.
    ///
    /// Fixed in <see cref="AreaGate.OnStormdrainOpened"/>/<see cref="AreaGate.OnStormdrainClosed"/>: the
    /// leaf collider now drops (and restores) alongside the cosmetic slide — parity with the threshold
    /// collider's own <see cref="AreaGate.Open"/>/<see cref="AreaGate.Reclose"/> contract. Never touched
    /// for a non-Stormdrain gate (that handler only exists once <see cref="AreaGate.ApplyStormdrainGateSkin"/>
    /// has wired it), so MV-386's still-visible-swinging-leaf contract is untouched for World 1.
    ///
    /// Fails on df5dd09 (current main at pickup): the probe never reaches its destination in either area,
    /// stalling in the corner once it meets the swung, invisible leaf.
    /// </summary>
    public sealed class MV918InvisibleGateLeafCornerTests
    {
        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void OpenedWestGate_LeafNeverBlocksTheCorner_InA11OrA13()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV918 Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                StormdrainDressing.Dress(root.transform, map, built.Cover);
                foreach (AreaGate g in Object.FindObjectsByType<AreaGate>(FindObjectsSortMode.None))
                    g.ApplyStormdrainGateSkin();

                Transform mapRoot = root.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");

                // Break g28 (a11's west gate) and g30 (a13's) the way sustained primary fire would.
                // AddComponent doesn't reliably fire Awake outside Play mode (see
                // WaterBlasterGateDamageTests.InvokeAwake's own note, the project's established
                // workaround for this exact gap) — driven directly here so everything from ForceOpen on
                // is the real production state machine, not a stand-in for it.
                OpenGate(mapRoot, "g28");
                OpenGate(mapRoot, "g30");
                Physics.SyncTransforms();

                AssertProbeReachesFarSide("a11 (g28)", startX: 223f, startZ: 104f, destX: 223f, destZ: 115f);

                // a13 (MV-875's own "maze" area) packs authored pipe cover (a13_cover3 X[272,274],
                // a13_rep1 X[274,276] Z>=107, a13_cover8's long fence at X[276,277]) tightly enough
                // around its own west gate that no straight lane a metre wide survives all the way from
                // open ground to the doorway — a real player picks a way through that maze, which is out
                // of this ticket's scope (MV-875 stands). What IS this ticket's scope is the strip the
                // authored data actually leaves clear immediately east of the doorway, Z 105.58-106.88 /
                // X up to 275.8 (a13_cover3/a13_rep1 both start north/east of it) — exactly where the
                // opened gate's leaf swings to (measured pre-fix at world X[271.94,275.80]
                // Z[105.58,106.88]). Crossing that strip is the corner; asserting the probe crosses it is
                // "through the corner", the same as a11's above.
                AssertProbeReachesFarSide("a13 (g30)", startX: 275.7f, startZ: 105.8f, destX: 274.3f, destZ: 105.8f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void OpenGate(Transform mapRoot, string gateId)
        {
            AreaGate gate = mapRoot.GetComponentsInChildren<AreaGate>(true)
                .FirstOrDefault(g => g.gameObject.name == gateId);
            Assert.IsNotNull(gate, $"setup failure: World 2 must build its own '{gateId}'");
            InvokePrivate(gate, "Awake");
            gate.ForceOpen();
            Assert.IsTrue(gate.IsOpen, $"setup failure: '{gateId}' must actually break open for this test to mean anything");

            // The hinge swing itself only advances inside Update() by Time.deltaTime, which is 0 outside
            // Play mode (no game loop ticking this test) — so Update() never actually rotates the leaf
            // here. Replicate its own k=1 (fully open) pose directly, the exact maths Update applies once
            // _hingeT >= HingeDuration, so the leaf collider ends up exactly where a live game's swing
            // settles rather than frozen at its closed pose.
            Transform t = gate.transform;
            System.Type type = typeof(AreaGate);
            var closedPos = (Vector3)type.GetField("_closedPosition", Flags).GetValue(gate);
            var closedRot = (Quaternion)type.GetField("_closedRotation", Flags).GetValue(gate);
            var pivot = (Vector3)type.GetField("_hingePivot", Flags).GetValue(gate);
            var sign = (float)type.GetField("_hingeSign", Flags).GetValue(gate);
            var swingDeg = (float)type.GetField("HingeSwingDegrees", Flags | System.Reflection.BindingFlags.Static).GetValue(null);
            t.position = closedPos;
            t.rotation = closedRot;
            t.RotateAround(pivot, Vector3.up, sign * swingDeg);
        }

        /// <summary>Drives a CharacterController-sized probe (radius 0.5, matching Max's own
        /// <see cref="MaxWorlds.Enemies.EnemyArchetype.PlayerRadius"/>) in a straight line through the
        /// corner where the opened gate's leaf swings to, and asserts it reaches the far side — the same
        /// probe idiom <c>MV831World2ColliderAuditTests</c> already uses for the same reason (a resolved
        /// position, Rule 2/Tier 2, never a presence check).</summary>
        private static void AssertProbeReachesFarSide(string label, float startX, float startZ, float destX, float destZ)
        {
            var probeGo = new GameObject($"MV918 Probe {label}", typeof(CharacterController));
            try
            {
                var cc = probeGo.GetComponent<CharacterController>();
                cc.radius = 0.5f;
                cc.height = 1.6f;
                cc.center = Vector3.up * 0.8f;
                probeGo.transform.position = new Vector3(startX, 0.1f, startZ);
                Physics.SyncTransforms();

                var dest = new Vector3(destX, 0.1f, destZ);
                for (int i = 0; i < 600 && (dest - probeGo.transform.position).sqrMagnitude > 0.05f * 0.05f; i++)
                {
                    Vector3 to = dest - probeGo.transform.position; to.y = 0f;
                    cc.Move(to.normalized * Mathf.Min(0.05f, to.magnitude));
                }

                Assert.LessOrEqual(Vector3.Distance(probeGo.transform.position, dest), 0.5f,
                    $"MV-918: a CharacterController-sized probe (radius 0.5) walking through {label}'s " +
                    $"west-gate corner from ({startX},{startZ}) toward ({destX},{destZ}) must reach it; it " +
                    $"stalled at {probeGo.transform.position}");
            }
            finally
            {
                Object.DestroyImmediate(probeGo);
            }
        }

        private const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, Flags).Invoke(target, null);
    }
}
