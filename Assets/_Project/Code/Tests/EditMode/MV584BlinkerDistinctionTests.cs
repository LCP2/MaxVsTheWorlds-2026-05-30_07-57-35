using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-584 — the Blinker reads like a Rusher in a mixed pack: <c>RoleFor</c> had no Blinker case (so
    /// it fell through to <see cref="CharacterRole.Robot"/> and wore the Rusher's turquoise), and
    /// <c>BuildBlinker</c>'s faceted-crystal-on-legs body was small and mostly <c>p.Cool</c>/<c>p.Dark</c>,
    /// so at gameplay distance it read as another dark blob. One test for both halves of the fix: the
    /// colour mapping, and the built body's actual silhouette (an enlarged core in the body colour, plus
    /// three orbiting shards nothing else in the roster has).
    /// </summary>
    public sealed class MV584BlinkerDistinctionTests
    {
        [Test]
        public void Blinker_HasItsOwnRoleColourAndBody_DistinctFromTheRusher()
        {
            Assert.AreEqual(CharacterRole.Blinker, CharacterSkin.RoleFor(EnemyKind.Blinker),
                "the Blinker must map to its own role, not fall through to CharacterRole.Robot (the Rusher's)");

            EnemyArchetype archetype = EnemyArchetype.Of(EnemyKind.Blinker);
            var go = GameObject.CreatePrimitive(
                archetype.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1f;
            cc.radius = 0.4f;
            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);
            go.SetActive(true);

            var rig = go.AddComponent<RobotRig>();

            // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode — the same
            // reflection-driven pattern MV578BolterDistinctionTests already establishes.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            try
            {
                Assert.IsTrue(rig.Built, "RobotRig must finish building the instant it's attached");

                Color expected = CharacterSkin.BaseColorFor(CharacterRole.Blinker);
                Color rusherColour = CharacterSkin.BaseColorFor(CharacterRole.Robot);
                Assert.That(Vector4.Distance(expected, rig.CurrentBodyColor), Is.LessThan(0.01f),
                    $"the Blinker must wear its own authored magenta, not the Rusher's turquoise " +
                    $"(expected {expected}, was {rig.CurrentBodyColor})");
                Assert.That(Vector4.Distance(rusherColour, rig.CurrentBodyColor), Is.GreaterThan(0.3f),
                    "the built Blinker must not be wearing the Rusher's RobotBody colour");

                // MV-969: RobotRig now folds every static part it built (Shard/CrystalCore included)
                // into one combined mesh, so there is no more "Shard"/"CrystalCore" Transform left under
                // the live rig to look up by name. RobotBodies.BuildBlinker's OWN silhouette — the thing
                // this test actually guards — is independent of whatever RobotRig later does with its
                // output, so it's proven here against a raw, uncombined build instead.
                AssertBlinkerSilhouette();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                try { Object.DestroyImmediate(go); }
                finally { LogAssert.ignoreFailingMessages = false; }
            }
        }

        /// <summary>MV-969: RobotBodies.BuildBlinker's own silhouette, proven directly rather than
        /// through a live RobotRig — the combine step folds these named parts away (see the caller's own
        /// comment), but the geometry BuildBlinker emits is unchanged and independently testable.</summary>
        private static void AssertBlinkerSilhouette()
        {
            var warm = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var cool = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var dark = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var gold = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var root = new GameObject("RawBlinkerBody").transform;
            try
            {
                RobotBodies.Build(EnemyKind.Blinker, root, new RobotPalette(warm, cool, dark, gold));

                var parts = root.GetComponentsInChildren<Transform>(true).ToList();
                Assert.AreEqual(3, parts.Count(t => t.name == "Shard"),
                    "the Blinker body must be built with three floating crystal shards orbiting the " +
                    "core — the at-a-glance tell that this one is not a wheeled robot");

                var coreParts = parts.Where(t => t.name == "CrystalCore").ToList();
                Assert.AreEqual(2, coreParts.Count,
                    "the Blinker's faceted crystal core must be built from its two authored facets");
                foreach (var part in coreParts)
                {
                    var renderer = part.GetComponent<MeshRenderer>();
                    Assert.IsNotNull(renderer, $"{part.name} must carry a MeshRenderer");
                    Assert.AreSame(warm, renderer.sharedMaterial,
                        "the crystal core must wear the BODY material (p.Warm), not the shared " +
                        "p.Cool/p.Dark tones every other part of the roster uses");
                }
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
                Object.DestroyImmediate(warm);
                Object.DestroyImmediate(cool);
                Object.DestroyImmediate(dark);
                Object.DestroyImmediate(gold);
            }
        }
    }
}
