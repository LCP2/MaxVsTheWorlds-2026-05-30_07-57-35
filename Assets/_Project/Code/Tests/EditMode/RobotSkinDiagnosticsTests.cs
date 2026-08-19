using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-350 — this round ships a diagnostic, not a fix (five prior fix attempts have each been
    /// disproved against a real deployed build). These tests cover the two new pieces of that
    /// diagnostic surface, not the still-unknown bug itself: <see cref="RobotSpawnSource"/> (which
    /// spawner tagged an instance) and <see cref="RobotRig"/>'s new read-outs
    /// (<see cref="RobotRig.BuildCount"/>, <see cref="RobotRig.CurrentBodyColor"/>) that
    /// <see cref="RobotSkinDiagnostics"/> reads to build its console line.
    /// </summary>
    public sealed class RobotSkinDiagnosticsTests
    {
        [Test]
        public void RobotSpawnSource_ReportsWhateverItWasMarkedWith()
        {
            var go = new GameObject("diagnostic-source-test");
            try
            {
                var source = go.AddComponent<RobotSpawnSource>();
                Assert.AreEqual("unknown", source.Source,
                    "an unmarked source must say so plainly, not silently claim a spawner it was never told about");

                source.Mark("EnemySpawner@MowerHutch");
                Assert.AreEqual("EnemySpawner@MowerHutch", source.Source);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RobotRig_BuildCount_IsOneAfterTheOneAndOnlyBuild_NotZeroOrRepeated()
        {
            var (e, rig) = BuildAndDress(EnemyKind.Bruiser);
            try
            {
                Assert.AreEqual(1, rig.BuildCount,
                    "EnsureBuilt is guarded to run once — the diagnostic exists to CONFIRM that on a " +
                    "live build, not assume it; a count that isn't exactly 1 here means the guard " +
                    "itself is the thing worth reporting");

                // A second call must not be a second build — this is what the diagnostic is actually
                // watching for out in the field.
                InvokeEnsureBuilt(rig);
                Assert.AreEqual(1, rig.BuildCount,
                    "a repeat call to EnsureBuilt must stay a no-op — a rig that rebuilds is exactly " +
                    "the kind of thing this diagnostic pass exists to catch");
            }
            finally { DestroyIgnoringEditModeDestroyWarnings(e.gameObject); }
        }

        [Test]
        public void RobotRig_CurrentBodyColor_MatchesTheArchetypeItWasBuiltFor()
        {
            var (e, rig) = BuildAndDress(EnemyKind.Gunner);
            try
            {
                CharacterRole role = CharacterSkin.RoleFor(EnemyKind.Gunner);
                Color expected = CharacterSkin.BaseColorFor(role);

                Assert.That(Vector4.Distance(expected, rig.CurrentBodyColor), Is.LessThan(0.01f),
                    $"CurrentBodyColor is what the diagnostic reports as 'actually wearing' — it must " +
                    $"read the same material RobotRig itself assigned (expected {expected}, was " +
                    $"{rig.CurrentBodyColor})");
            }
            finally { DestroyIgnoringEditModeDestroyWarnings(e.gameObject); }
        }

        [Test]
        public void BuildRendererReport_ListsEveryRendererWithItsMaterialShaderAndBlockState()
        {
            var (e, rig) = BuildAndDress(EnemyKind.Bruiser);
            try
            {
                var diag = e.gameObject.AddComponent<RobotSkinDiagnostics>();
                string report = diag.BuildRendererReport();

                Assert.That(report, Does.StartWith("[MV-350 skin] +1s renderers"),
                    "round 2 of the hunt needs the renderer dump on its own recognisable prefix, " +
                    "distinct from the existing spawn/+1s summary line");

                // The rig's own greybox renderer (disabled, but still a MeshRenderer under this root)
                // plus at least the body parts a bruiser is built from. MV-451: parts are generated
                // geometry now, named generically "Part" (see CharacterPart) rather than "Chassis" —
                // the material name is what still identifies which part is which in the dump.
                Assert.That(report, Does.Contain("material=Robot_Bruiser_Body"),
                    "the bruiser's body part must show up in the dump — this is the renderer round 1 " +
                    "could never see past its owning material");
                Assert.That(report, Does.Contain("selfDrivenTint=True"),
                    "every part RobotRig builds carries SelfDrivenTint — a part missing it here would " +
                    "be exactly the kind of second-writer risk this hunt is looking for");
                Assert.That(report, Does.Contain("shader="),
                    "the shader actually driving each renderer is the whole point of round 2 — " +
                    "CurrentBodyColor could never reveal it");
            }
            finally { DestroyIgnoringEditModeDestroyWarnings(e.gameObject); }
        }

        [Test]
        public void BuildRendererReport_ReportsAPropertyBlockWhenOneIsOverridingTheMaterial()
        {
            var (e, rig) = BuildAndDress(EnemyKind.Rusher);
            try
            {
                var diag = e.gameObject.AddComponent<RobotSkinDiagnostics>();
                var renderer = e.GetComponentInChildren<MeshRenderer>();
                Assert.IsNotNull(renderer, "a freshly built rig must have at least one part to test against");

                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(Shader.PropertyToID("_BaseColor"), Color.magenta);
                renderer.SetPropertyBlock(mpb);

                string report = diag.BuildRendererReport();

                Assert.That(report, Does.Contain("propertyBlock=#FF00FF"),
                    "a MaterialPropertyBlock overriding _BaseColor is exactly the failure mode " +
                    "RobotRig.CurrentBodyColor is blind to — the dump has to surface it explicitly");
            }
            finally { DestroyIgnoringEditModeDestroyWarnings(e.gameObject); }
        }

        [Test]
        public void DescribeShaderBootState_ReportsWhetherTheCharacterShaderResolved()
        {
            string line = RobotSkinDiagnostics.DescribeShaderBootState();

            Assert.That(line, Does.StartWith("[MV-350 skin] boot characterMaterial="),
                "the boot line has to be unmistakable in a console full of spawn/+1s lines, and has to " +
                "fire even if every robot that spawns afterward looks fine");
            Assert.That(line, Does.Contain("shader="),
                "the resolved (or NONE) shader name is the fact this line exists to report — " +
                "round 1 never checked whether the character shader was even in the build");
        }

        [Test]
        public void RobotRig_CurrentBodyColor_IsClearBeforeAnyBuild()
        {
            var go = new GameObject("unbuilt-rig-test");
            try
            {
                var rig = go.AddComponent<RobotRig>();
                // No RobotEnemy attached, so EnsureBuilt's own early-return keeps it unbuilt — the
                // exact "NO-RIG" case RobotSkinDiagnostics.Log reports for a robot it can't dress at
                // all (missing RobotEnemy would itself be a legitimate log line to see in the field).
                Assert.IsFalse(rig.Built);
                Assert.AreEqual(Color.clear, rig.CurrentBodyColor);
                Assert.AreEqual(0, rig.BuildCount);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Same greybox-then-Apply-then-RobotRig sequence <c>RobotSkinSpawnPathTests</c>
        /// already exercises for the spawn-path question — reused here because it's the exact shape
        /// <see cref="RobotSkinDiagnostics"/> will actually see attached to it in the field.</summary>
        private static (RobotEnemy enemy, RobotRig rig) BuildAndDress(EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            var go = GameObject.CreatePrimitive(
                archetype.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1f;
            cc.radius = 0.4f;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);
            go.SetActive(true);

            var rig = go.AddComponent<RobotRig>();
            InvokeEnsureBuilt(rig);
            return (e, rig);
        }

        /// <summary>Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode, same
        /// limitation <c>RobotSkinSpawnPathTests</c> already works around — drive the private build
        /// step directly instead.</summary>
        private static void InvokeEnsureBuilt(RobotRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void DestroyIgnoringEditModeDestroyWarnings(Object o)
        {
            LogAssert.ignoreFailingMessages = true;
            try { Object.DestroyImmediate(o); }
            finally { LogAssert.ignoreFailingMessages = false; }
        }
    }
}
