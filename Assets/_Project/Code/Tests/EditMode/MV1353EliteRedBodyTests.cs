using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1353 AC1+AC2: an elite's built rig resolves its body material colour to the pure elite red
    /// (0.88, 0.06, 0.10); the same kind spawned non-elite keeps its own body colour; and a pooled robot
    /// re-spawned as non-elite goes back to that normal colour (no red left over).
    /// </summary>
    public sealed class MV1353EliteRedBodyTests
    {
        private static void Tick(RobotRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, null);
                typeof(RobotRig).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static RobotRig Make(Transform root, EnemyArchetype a, out RobotEnemy enemy)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(root, false);
            go.AddComponent<CharacterController>();
            enemy = go.AddComponent<RobotEnemy>();
            enemy.Apply(a);
            return go.AddComponent<RobotRig>();
        }

        private static void AssertRed(Color c, string msg)
        {
            Assert.AreEqual(0.88f, c.r, 1e-3f, msg + " (r)");
            Assert.AreEqual(0.06f, c.g, 1e-3f, msg + " (g)");
            Assert.AreEqual(0.10f, c.b, 1e-3f, msg + " (b)");
        }

        [Test]
        public void EliteBodyIsRed_NonEliteIsNot_AndPooledReuseRevertsToNormal_MV1353()
        {
            var root = new GameObject("MV1353 Probe Root");
            try
            {
                EnemyArchetype plain = EnemyArchetype.Of(EnemyKind.Rusher);
                RobotRig normalRig = Make(root.transform, plain, out _);
                Tick(normalRig);
                Color normal = normalRig.CurrentBodyColor;

                RobotRig eliteRig = Make(root.transform, plain.WithElite(EliteAffix.Shielded), out RobotEnemy eliteEnemy);
                Tick(eliteRig);
                AssertRed(eliteRig.CurrentBodyColor, "an elite's resolved body colour must be the elite red");
                Assert.AreNotEqual(0.88f, normal.r, "the non-elite Rusher must keep its own body colour");

                eliteEnemy.Apply(plain);   // pool re-use as non-elite
                Tick(eliteRig);
                Assert.AreEqual(normal.r, eliteRig.CurrentBodyColor.r, 1e-3f, "pooled non-elite must lose the red (r)");
                Assert.AreEqual(normal.g, eliteRig.CurrentBodyColor.g, 1e-3f, "pooled non-elite must lose the red (g)");
                Assert.AreEqual(normal.b, eliteRig.CurrentBodyColor.b, 1e-3f, "pooled non-elite must lose the red (b)");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
