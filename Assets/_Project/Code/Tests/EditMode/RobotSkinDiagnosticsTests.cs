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
