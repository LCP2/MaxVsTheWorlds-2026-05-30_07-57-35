using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-350 — the fix, not the diagnostic. <see cref="RuntimeSurfaceDirector.Sweep"/> claimed a
    /// pooled robot's own renderers the instant it went inactive, because
    /// <c>GetComponentInParent&lt;IDamageable&gt;()</c> does not see an inactive parent — and claimed a
    /// missile's renderers unconditionally, because a missile is not <c>IDamageable</c> at all. Both
    /// wrote the same neutral white world-prop material over deliberate art, permanently for a robot
    /// (nothing ever revisits a renderer once <c>SurfaceSkinned</c> is stamped) and every single flight
    /// for a missile.
    ///
    /// Each test fails on the pre-fix sweep and passes once the guard is inactive-aware / the marker is
    /// present.
    /// </summary>
    public sealed class RuntimeSurfaceDirectorTests
    {
        [SetUp]
        public void SetUp() => MaterialLibrary.Palette = BiomePalette.Backyard;

        [Test]
        public void ASweepWhilePooled_LeavesARobotsPartsWearingTheirArchetypeMaterials()
        {
            var (enemy, _) = BuildAndDressRobot(EnemyKind.Bruiser);
            try
            {
                var renderers = enemy.GetComponentsInChildren<MeshRenderer>(true);
                Assert.IsNotEmpty(renderers, "a built rig must have at least one part to regress against");
                var before = Snapshot(renderers);

                enemy.gameObject.SetActive(false);   // pooled: exactly what a dead robot does
                RunSweep();
                enemy.gameObject.SetActive(true);    // respawned

                AssertUnchanged(before, renderers,
                    "RuntimeSurfaceDirector must never claim a robot's renderers, active or inactive — " +
                    "a part that changes here is the tan-robot bug (MV-350)");
            }
            finally { DestroyIgnoringEditModeDestroyWarnings(enemy.gameObject); }
        }

        [Test]
        public void ASweep_LeavesAMissilesPartsWearingTheirGunmetalAndWarnMaterials()
        {
            // HomingMissile.Strip() calls Object.Destroy on the primitives' colliders, which is only
            // valid in Play mode — it still works in edit mode (falls back to an immediate destroy) but
            // logs an error the test framework would otherwise fail on.
            var missile = InvokeFireIgnoringEditModeDestroyWarnings();
            try
            {
                var renderers = missile.GetComponentsInChildren<MeshRenderer>(true);
                Assert.IsNotEmpty(renderers,
                    "a fired missile must have at least a shaft/band/fins to regress against");
                var before = Snapshot(renderers);

                RunSweep();

                AssertUnchanged(before, renderers,
                    "RuntimeSurfaceDirector must never claim a missile's renderers — a part that " +
                    "changes here is the tan-missile bug (MV-350)");
            }
            finally { Object.DestroyImmediate(missile.gameObject); }
        }

        private static HomingMissile InvokeFireIgnoringEditModeDestroyWarnings()
        {
            LogAssert.ignoreFailingMessages = true;
            try { return HomingMissile.Fire(Vector3.zero, null, speed: 5f, damage: 1f, splashRadius: 1f); }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void RunSweep()
        {
            var go = new GameObject("sweep-test-director");
            try
            {
                var director = go.AddComponent<RuntimeSurfaceDirector>();
                typeof(RuntimeSurfaceDirector).GetMethod("Sweep", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(director, null);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static Material[] Snapshot(MeshRenderer[] renderers)
        {
            var mats = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) mats[i] = renderers[i].sharedMaterial;
            return mats;
        }

        private static void AssertUnchanged(Material[] before, MeshRenderer[] renderers, string message)
        {
            for (int i = 0; i < renderers.Length; i++)
                Assert.AreSame(before[i], renderers[i].sharedMaterial, $"{renderers[i].name}: {message}");
        }

        /// <summary>Same greybox-then-Apply-then-RobotRig sequence <c>RobotSkinDiagnosticsTests</c> uses
        /// — the exact shape a pooled robot actually wears in the field.</summary>
        private static (RobotEnemy enemy, RobotRig rig) BuildAndDressRobot(EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            var go = GameObject.CreatePrimitive(
                archetype.Shape == EnemyShape.Capsule ? PrimitiveType.Capsule : PrimitiveType.Cube);
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

        /// <summary>Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode — drive
        /// the private build step directly instead, same workaround <c>RobotSkinDiagnosticsTests</c>
        /// already relies on.</summary>
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
