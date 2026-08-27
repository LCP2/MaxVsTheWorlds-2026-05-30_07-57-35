using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-580: the sentinel was a raw <c>GameObject.CreatePrimitive(PrimitiveType.Cylinder)</c> —
    /// Unity's built-in default material, no URP subshader, so a player build drew it magenta (see
    /// <c>RuntimeSurfaceDirector</c>'s doc: it explicitly skips anything under an
    /// <see cref="MaxWorlds.Core.IDamageable"/>, and <see cref="Sentinel"/> is one, so the corrective
    /// sweep can never reach it). It is now built from the same shared body geometry every Backyard
    /// robot uses (<see cref="RobotBodies.Build"/>), in a distinct palette with a friendly eye colour.
    /// </summary>
    public sealed class SentinelBodyTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static Sentinel NewSentinel(GameObject go, Vector3 position)
        {
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, 60f, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
            return sentinel;
        }

        // ---------------------------------------------------------------------------- AC1

        /// <summary>
        /// Source-shape guard, not a behavioural one (same idiom as MV527AllocationGuardTests' AC1) —
        /// proven to fail on the base commit, where <c>BuildBody</c> literally reads
        /// <c>GameObject.CreatePrimitive(PrimitiveType.Cylinder)</c>.
        /// </summary>
        [Test]
        public void SentinelCsHasNoCreatePrimitiveCallAnywhereInIt()
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime", "Arena", "Sentinel.cs");
            Assert.IsTrue(File.Exists(path), $"Sentinel.cs not found at {path}");

            // Line-comment stripped, not raw text: this fix's own doc comments explain, in prose, what
            // BuildBody used to call — the same trap MV527AllocationGuardTests' AC1 already hit and
            // solved (a scan that doesn't strip comments flags its own explanation as the bug). Sentinel.cs
            // carries no block (/* */) comments, so truncating each line at its first "//" is sufficient.
            string code = string.Join("\n", File.ReadAllLines(path).Select(StripLineComment));
            Assert.IsFalse(code.Contains("CreatePrimitive"),
                "Sentinel.cs still calls GameObject.CreatePrimitive — that's the MV-580 magenta defect " +
                "(built-in default material, no URP subshader).");
        }

        private static string StripLineComment(string line)
        {
            int i = line.IndexOf("//", System.StringComparison.Ordinal);
            return i < 0 ? line : line.Substring(0, i);
        }

        // ---------------------------------------------------------------------------- AC2

        /// <summary>Every renderer the sentinel spawns must carry a real URP-based material — either
        /// the project's stylised character shader (also URP-based, just custom-named) or a literal
        /// "Universal Render Pipeline/..." shader — never the built-in default that ships magenta.
        /// Asserts on the shader NAME, not merely that a material is non-null, per the ticket's own AC.</summary>
        [Test]
        public void EverySpawnedRendererHasARealUrpShader_NeverTheDefaultMagentaOne()
        {
            var go = new GameObject("Sentinel");
            try
            {
                NewSentinel(go, Vector3.zero);

                var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                Assert.IsNotEmpty(renderers, "the sentinel built no visible parts at all");

                foreach (var r in renderers)
                {
                    var mat = r.sharedMaterial;
                    Assert.IsNotNull(mat, $"{r.name} has no material at all");
                    Assert.IsNotNull(mat.shader, $"{r.name}'s material has no shader");

                    bool isUrp = mat.shader.name.Contains("Universal Render Pipeline")
                                 || mat.shader.name == MaterialLibrary.CharacterShaderName;
                    Assert.IsTrue(isUrp,
                        $"{r.name} wears shader '{mat.shader.name}' — not a URP shader, so a player " +
                        "build would draw it magenta (the exact MV-580 defect).");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------------------- AC3

        /// <summary>The body must actually be <see cref="RobotBodies.Build"/>'s output — proven by
        /// reading back the LEGGED tripod it hands over (three hip pivots, the Gunner's own shape),
        /// not by trusting a comment — and the eye's live, RESOLVED colour must read as friendly, not
        /// as any of the enemy roster's own tell colours (<c>RobotRig</c>'s idle gold, warn orange, or
        /// hit-flash white).</summary>
        [Test]
        public void BodyIsALeggedRobotBodiesTripod_WithAFriendlyEyeColour_NotAnEnemyTell()
        {
            var go = new GameObject("Sentinel");
            try
            {
                var sentinel = NewSentinel(go, Vector3.zero);

                FieldInfo bodyField = typeof(Sentinel).GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(bodyField, "Sentinel._body went missing");
                var body = (RobotBodies.Body)bodyField.GetValue(sentinel);

                Assert.That(body.Legs, Is.Not.Null.And.Not.Empty,
                    "the body has no leg pivots — it was not built as a legged RobotBodies kind");
                Assert.That(body.Eyes, Is.Not.Null.And.Not.Empty,
                    "the body has no eye lens — RobotBodies.Build did not run");

                var eyeMpb = new MaterialPropertyBlock();
                body.Eyes[0].GetPropertyBlock(eyeMpb);
                Color eye = eyeMpb.GetColor("_BaseColor");

                FieldInfo idleField = typeof(RobotRig).GetField("EyeIdle", BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo warnField = typeof(RobotRig).GetField("EyeWarn", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(idleField, "RobotRig.EyeIdle went missing");
                Assert.IsNotNull(warnField, "RobotRig.EyeWarn went missing");
                Color enemyIdle = (Color)idleField.GetValue(null);
                Color enemyWarn = (Color)warnField.GetValue(null);

                Assert.That(ColourDistance(eye, enemyIdle), Is.GreaterThan(0.15f),
                    $"the sentinel's eye ({eye}) reads too close to the enemy roster's idle gold tell ({enemyIdle})");
                Assert.That(ColourDistance(eye, enemyWarn), Is.GreaterThan(0.15f),
                    $"the sentinel's eye ({eye}) reads too close to the enemy roster's warn-orange tell ({enemyWarn})");
                Assert.That(ColourDistance(eye, Color.white), Is.GreaterThan(0.15f),
                    $"the sentinel's eye ({eye}) reads too close to the hit-flash white every tell uses");
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static float ColourDistance(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        // ---------------------------------------------------------------------------- AC4

        private static void InvokeUpdate(Sentinel sentinel) =>
            typeof(Sentinel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(sentinel, null);

        private static RobotEnemy NewTarget(Vector3 position)
        {
            var go = new GameObject("Target Robot");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState();
            return e;
        }

        /// <summary>The whole sentinel still turns to face whatever it is firing at — unaffected by
        /// the body swap, since the turn is <c>transform.rotation</c> on the sentinel's own root, not
        /// anything <see cref="RobotBodies.Build"/> touches.</summary>
        [Test]
        public void TurretStillRotatesToFaceItsTargetAfterTheBodySwap()
        {
            var go = new GameObject("Sentinel");
            RobotEnemy target = null;
            try
            {
                var sentinel = NewSentinel(go, Vector3.zero);
                target = NewTarget(new Vector3(0f, 0f, 3f)); // due +Z

                Physics.SyncTransforms();
                InvokeUpdate(sentinel);

                Vector3 expected = new Vector3(0f, 0f, 3f).normalized;
                float dot = Vector3.Dot(sentinel.transform.forward, expected);
                Assert.That(dot, Is.GreaterThan(0.99f),
                    $"the sentinel did not turn to face its target — forward is {sentinel.transform.forward}, " +
                    $"expected close to {expected}");
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (target != null) Object.DestroyImmediate(target.gameObject);
            }
        }
    }
}
