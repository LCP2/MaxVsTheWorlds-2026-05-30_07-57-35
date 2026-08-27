using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-587 — Big Bermuda's brood volley flung each add as a raw <c>GameObject.CreatePrimitive</c>
    /// capsule, dressed only by <see cref="RobotEnemy.Apply"/>, never through the
    /// <see cref="RobotRig"/>/<see cref="CharacterSkin"/> body pipeline factory robots get via
    /// <see cref="MaxWorlds.Enemies.EnemySpawner.CreateInstance"/>. A primitive's default material has no
    /// URP subshader, so every flung add drew Unity's magenta missing-shader colour instead of its
    /// kind's real body (Lee, 2026-08-26, on device: "spawns pink cylinders" — MV-587.png).
    /// </summary>
    public sealed class MV587BroodAddBodyTests
    {
        /// <summary>Channel-wise, not <c>Color.Equals</c>: a colour written into a material and read
        /// back through <see cref="RobotRig.CurrentBodyColor"/> can pick up a hair of float error on
        /// the round trip, which a bit-exact <c>Assert.AreEqual(Color, Color)</c> is not built to
        /// tolerate.</summary>
        private static void AssertColorApprox(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, message + " (r)");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, message + " (g)");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, message + " (b)");
        }

        [Test]
        public void CreatedAdd_MatchesTheFactoryBodyPipeline_AndKeepsItAcrossPooledReuse()
        {
            GameObject bossGo = null;
            GameObject spawnerGo = null;
            RobotEnemy add = null;
            RobotEnemy factoryRobot = null;
            Transform addsRoot = null;
            try
            {
                bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var stray = bossGo.GetComponent<BoxCollider>();
                if (stray != null) Object.DestroyImmediate(stray);
                var boss = bossGo.AddComponent<BigBermudaBoss>();

                EnemyArchetype archetype = EnemyArchetype.Rusher;   // one of the kinds LaunchVolley can fling (YT-157/MV-588)

                // --- build one brood add through the real, private BigBermudaBoss.CreateAdd ----------
                MethodInfo createAdd = typeof(BigBermudaBoss).GetMethod(
                    "CreateAdd", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(createAdd, "BigBermudaBoss.CreateAdd went missing");

                LogAssert.ignoreFailingMessages = true;
                try { add = (RobotEnemy)createAdd.Invoke(boss, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                var addRig = add.GetComponent<RobotRig>();
                Assert.IsNotNull(addRig, "a created add has no RobotRig — it still wears its raw primitive body");

                LogAssert.ignoreFailingMessages = true;
                try
                {
                    typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(addRig, null);
                }
                finally { LogAssert.ignoreFailingMessages = false; }
                Assert.IsTrue(addRig.Built, "the add's RobotRig never finished building a body");

                // --- build one factory robot of the SAME kind through the real EnemySpawner.CreateInstance,
                // for a direct comparison against "the same body pipeline factory robots use" ------------
                spawnerGo = new GameObject("MV-587 test spawner");
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                MethodInfo createInstance = typeof(EnemySpawner).GetMethod(
                    "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(createInstance, "EnemySpawner.CreateInstance went missing");

                LogAssert.ignoreFailingMessages = true;
                try { factoryRobot = (RobotEnemy)createInstance.Invoke(spawner, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                var factoryRig = factoryRobot.GetComponent<RobotRig>();
                Assert.IsNotNull(factoryRig, "the factory spawn path lost its own RobotRig");

                LogAssert.ignoreFailingMessages = true;
                try
                {
                    typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(factoryRig, null);
                }
                finally { LogAssert.ignoreFailingMessages = false; }

                Color expected = CharacterSkin.BaseColorFor(CharacterSkin.RoleFor(EnemyKind.Rusher));
                AssertColorApprox(expected, addRig.CurrentBodyColor,
                    "a flung Rusher's resolved body colour must equal the Rusher's real CharacterSkin " +
                    "colour, not a primitive's default (magenta) material");
                AssertColorApprox(factoryRig.CurrentBodyColor, addRig.CurrentBodyColor,
                    "a flung Rusher's resolved body colour must match a shed Rusher's exactly");

                // --- pooled reuse: the boss pushes a dead add back into its kind's pool in _addPools and
                // TakeAdd pops it straight back out (never through CreateAdd a second time). A
                // re-activated pooled add must keep the body it already built, not rebuild or lose it.
                var poolsField = typeof(BigBermudaBoss).GetField(
                    "_addPools", BindingFlags.NonPublic | BindingFlags.Instance);
                var pools = (Dictionary<EnemyKind, Stack<RobotEnemy>>)poolsField.GetValue(boss);
                if (!pools.TryGetValue(archetype.Kind, out Stack<RobotEnemy> pool))
                    pools[archetype.Kind] = pool = new Stack<RobotEnemy>();
                pool.Push(add);

                MethodInfo takeAdd = typeof(BigBermudaBoss).GetMethod(
                    "TakeAdd", BindingFlags.NonPublic | BindingFlags.Instance);
                RobotEnemy pooled = (RobotEnemy)takeAdd.Invoke(boss, new object[] { archetype });
                Assert.AreSame(add, pooled, "TakeAdd built a NEW add instead of reusing the pooled one");

                // OnEnable calls EnsureBuilt on every reactivation (a pooled robot re-enabling); simulate
                // that directly rather than relying on edit-mode SetActive to invoke it synchronously.
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(addRig, null);
                }
                finally { LogAssert.ignoreFailingMessages = false; }

                Assert.AreEqual(1, addRig.BuildCount,
                    "a pooled add's body was rebuilt on reactivation instead of kept");
                AssertColorApprox(expected, addRig.CurrentBodyColor,
                    "a pooled add re-activated lost its resolved body colour");

                var addsRootField = typeof(BigBermudaBoss).GetField(
                    "_addsRoot", BindingFlags.NonPublic | BindingFlags.Instance);
                addsRoot = (Transform)addsRootField.GetValue(boss);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                if (add != null) Object.DestroyImmediate(add.gameObject);
                if (factoryRobot != null) Object.DestroyImmediate(factoryRobot.gameObject);
                if (addsRoot != null) Object.DestroyImmediate(addsRoot.gameObject);
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (bossGo != null) Object.DestroyImmediate(bossGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
