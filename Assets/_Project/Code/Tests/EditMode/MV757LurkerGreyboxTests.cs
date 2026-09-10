using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-757: the Grate Lurker's magenta greybox stand-in (a bare-primitive capsule, YT-58)
    /// used to come back ON every emerge. <see cref="RobotRig"/>'s build step only DISABLED its root
    /// <see cref="MeshRenderer"/>, and <see cref="RobotEnemy"/>'s SetBodyVisible sweeps
    /// <c>GetComponentsInChildren&lt;Renderer&gt;(includeInactive: true)</c> and re-enables everything it
    /// finds — including that disabled greybox — the instant a Lurker reaches
    /// <see cref="MaxWorlds.Enemies.LurkerCycle.Phase.Emerged"/>. That is the pink tower flashing on and
    /// off Lee reported. Fails on base commit 279498e: the root <see cref="MeshRenderer"/> survives
    /// <c>RobotRig.EnsureBuilt</c> (merely disabled) and a <c>SetBodyVisible(true)</c> call switches it
    /// back on.</summary>
    public sealed class MV757LurkerGreyboxTests
    {
        [Test]
        public void MV_LurkerHasNoGreyboxRenderer()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();

            // The same stand-in EnemySpawner/AreaAccumulationDirector actually build: a bare primitive
            // whose default material has no URP subshader, which is what ships it magenta (YT-58).
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Enemy Lurker";
            var primitiveCollider = go.GetComponent<Collider>();
            if (primitiveCollider != null) Object.DestroyImmediate(primitiveCollider);

            var cc = go.AddComponent<CharacterController>();
            var enemy = go.AddComponent<RobotEnemy>();
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(enemy, cc);
            enemy.Apply(EnemyArchetype.Lurker);

            var rig = go.AddComponent<RobotRig>();

            try
            {
                // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (same note
                // as RobotRigTests) — drive the private build step directly, and swallow whatever it
                // logs about a missing character shader in this test environment.
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(rig, null);
                }
                finally { LogAssert.ignoreFailingMessages = false; }

                Assert.IsNull(go.GetComponent<MeshRenderer>(),
                    "the greybox MeshRenderer must be DESTROYED, not merely disabled, or SetBodyVisible " +
                    "can switch it back on every emerge.");

                // Drive a submerge/emerge cycle through the exact method that used to re-enable it.
                var setBodyVisible = typeof(RobotEnemy).GetMethod("SetBodyVisible",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                setBodyVisible.Invoke(enemy, new object[] { false });
                setBodyVisible.Invoke(enemy, new object[] { true });

                Assert.IsNull(go.GetComponent<MeshRenderer>(),
                    "the greybox must still be gone after a submerge/emerge cycle driven through " +
                    "SetBodyVisible — it must never be able to come back.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                RobotEnemy.ResetRegistry();
                DevTuning.Reset();
            }
        }
    }
}
