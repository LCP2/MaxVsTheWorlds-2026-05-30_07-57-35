using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-973 — "improve fps/heat with no difference to the quality of the game" (Lee's rule, 2026-09-26).
    /// Two of the ticket's own three sub-changes resolve to values an EditMode test CAN assert without a
    /// graphics device: the URP asset's cascade count/soft-shadow flag (Tier 2 — a resolved asset
    /// property), and the physics-mode switch's effect on where a moved CharacterController ends up
    /// (also Tier 2 — a resolved position, not an authored constant).
    ///
    /// The ticket's own AC1(a) asks for a THIRD assertion this project's Testing policy (CLAUDE.md,
    /// MV-465 Rule 2) forbids an EditMode test from making at all: "renders ... to a RenderTexture ...
    /// and asserts the per-pixel difference" is a rendered-pixel assertion, and "no EditMode test may
    /// assert a rendered pixel ... EditMode asserts resolved values; the conformance harness asserts
    /// rendered ones" is that policy's own wording, written after a real defect (WeaponsScreen's
    /// fontSize/resizeTextMaxSize mismatch) shipped past exactly this kind of test. It is also not
    /// computable under this project's own `-batchmode -nographics` EditMode run regardless — see
    /// ShaderKitTests.EveryPassThatPositionsAPlant_BendsItTheSameWay's own comment ("pass enumeration
    /// needs a graphics device and the whole verify runs headless") for the same limitation hit before.
    /// Left unasserted here by policy, not by oversight; recorded in this ticket's Jira comment.
    /// </summary>
    public sealed class MV973PerfZeroVisualChangeTests
    {
        private const string MobileAssetPath = "Assets/Settings/Mobile_RPAsset.asset";

        [Test]
        public void UrpHasOneCascadeWithSoftShadowsUnchanged_AndPhysicsScriptModeMatchesAutoSimulation()
        {
            // --- (b) shadows: one cascade, soft shadows unchanged ---
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAssetPath);
            Assert.IsNotNull(urp, $"Mobile URP asset missing: {MobileAssetPath}");

            Assert.AreEqual(1, urp.shadowCascadeCount,
                "MV-973 asks for one shadow cascade at the MV-969 distance — near-field shadow texel " +
                "density should be equal or better than today's first cascade with three fewer cascade " +
                "splits to evaluate per pixel.");
            Assert.IsTrue(urp.supportsSoftShadows,
                "MV-973 keeps soft shadows as they are — only the cascade count changes.");

            // --- (c) physics: SimulationMode.Script must move nothing differently from auto-simulation ---
            SimulationMode originalMode = Physics.simulationMode;
            bool originalRequiresManualSimulate = PhysicsSimulationDriver.RequiresManualSimulate;
            try
            {
                Vector3[] autoEnds = RunCrowdThroughDoorway(useScriptDriver: false);
                Vector3[] scriptEnds = RunCrowdThroughDoorway(useScriptDriver: true);

                Assert.AreEqual(autoEnds.Length, scriptEnds.Length);
                for (int i = 0; i < autoEnds.Length; i++)
                {
                    float dist = Vector3.Distance(autoEnds[i], scriptEnds[i]);
                    Assert.That(dist, Is.LessThanOrEqualTo(0.01f),
                        $"actor {i} ended {dist:F4} m apart between SimulationMode.Script " +
                        $"({scriptEnds[i]}) and auto-simulation ({autoEnds[i]}) — MV-973's physics-mode " +
                        "switch must not move Max or a robot by a single centimetre.");
                }
            }
            finally
            {
                Physics.simulationMode = originalMode;
                PhysicsSimulationDriver.RequiresManualSimulate = originalRequiresManualSimulate;
            }
        }

        /// <summary>Max plus a 20-robot crowd, all CharacterControllers, moving through a doorway gap
        /// for 120 frames (2s at 60fps) — the exact scenario AC1(c) names. Returns each actor's final
        /// position (index 0 is Max). <paramref name="useScriptDriver"/> true drives the frame with
        /// <see cref="PhysicsSimulationDriver.Tick"/> (MV-973's shipped mode); false leaves
        /// <see cref="Physics.simulationMode"/> at <see cref="SimulationMode.FixedUpdate"/> (today's
        /// baseline) and never calls <see cref="Physics.Simulate"/> or <see cref="Physics.SyncTransforms"/>
        /// at all — nothing in this scene is a Rigidbody, so nothing here was ever going to depend on
        /// either call for its own motion; the comparison is what proves that.</summary>
        private static Vector3[] RunCrowdThroughDoorway(bool useScriptDriver)
        {
            const float dt = 1f / 60f;
            const int frames = 120;
            const int robotCount = 20;

            GameObject[] walls = BuildDoorwayWalls();
            (GameObject go, CharacterController cc) max = BuildController("MV-973 Max Probe", new Vector3(0f, 1f, -6f));

            var robots = new (GameObject go, CharacterController cc)[robotCount];
            for (int i = 0; i < robotCount; i++)
            {
                float x = -2f + (i % 5) * 1f;
                float z = -6f - (i / 5) * 1f;
                robots[i] = BuildController($"MV-973 Robot Probe {i}", new Vector3(x, 1f, z));
            }

            Physics.SyncTransforms();
            Physics.simulationMode = useScriptDriver ? SimulationMode.Script : SimulationMode.FixedUpdate;
            PhysicsSimulationDriver.RequiresManualSimulate = false;

            // Max walks straight down the middle of the gap; the robots converge on it from a
            // clustered start while advancing at the same pace — a genuine crowd funnelling through
            // one doorway, not 21 non-interacting parallel lanes.
            Vector3 maxVelocity = new Vector3(0f, 0f, 3f);
            Vector3 robotVelocity = new Vector3(0.15f, 0f, 3f);

            try
            {
                for (int f = 0; f < frames; f++)
                {
                    if (useScriptDriver) PhysicsSimulationDriver.Tick(dt);

                    CharacterControllerMotion.SafeMove(max.cc, maxVelocity * dt);
                    for (int i = 0; i < robotCount; i++) CharacterControllerMotion.SafeMove(robots[i].cc, robotVelocity * dt);
                }

                var ends = new Vector3[robotCount + 1];
                ends[0] = max.go.transform.position;
                for (int i = 0; i < robotCount; i++) ends[i + 1] = robots[i].go.transform.position;
                return ends;
            }
            finally
            {
                Object.DestroyImmediate(max.go);
                for (int i = 0; i < robotCount; i++) Object.DestroyImmediate(robots[i].go);
                foreach (GameObject wall in walls) Object.DestroyImmediate(wall);
            }
        }

        private static (GameObject go, CharacterController cc) BuildController(string name, Vector3 startPos)
        {
            var go = new GameObject(name, typeof(CharacterController));
            var cc = go.GetComponent<CharacterController>();
            cc.center = Vector3.up * 1f;
            cc.height = 2f;
            cc.radius = 0.35f;
            go.transform.position = startPos;
            return (go, cc);
        }

        /// <summary>A 1.2 m gap at x=0 in an otherwise solid wall along the x axis at z=0 — Max's start
        /// x (0) sits centred in the gap; several robots' clustered start x values (-2..2) do not, so
        /// some of them genuinely collide with a wall face rather than all cleanly threading it.</summary>
        private static GameObject[] BuildDoorwayWalls()
        {
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.name = "MV-973 Doorway Wall Left";
            left.transform.position = new Vector3(-3.6f, 1.5f, 0f);
            left.transform.localScale = new Vector3(6f, 3f, 0.4f);

            var right = GameObject.CreatePrimitive(PrimitiveType.Cube);
            right.name = "MV-973 Doorway Wall Right";
            right.transform.position = new Vector3(3.6f, 1.5f, 0f);
            right.transform.localScale = new Vector3(6f, 3f, 0.4f);

            Physics.SyncTransforms();
            return new[] { left, right };
        }
    }
}
