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
    /// MV-578 — the Bolter and the Rusher read identically at gameplay distance: <c>RoleFor</c> had no
    /// Bolter case (so it fell through to <see cref="CharacterRole.Robot"/> and wore the Rusher's
    /// turquoise <c>RobotBody</c>), and <c>BuildBolter</c> reused the Rusher's exact wheel AND
    /// body-cone-lathe geometry — only the small head spikes differed, invisible at camera distance.
    /// One test for both halves of the fix: the colour mapping, and the built body's actual silhouette.
    /// </summary>
    public sealed class MV578BolterDistinctionTests
    {
        [Test]
        public void Bolter_HasItsOwnRoleColourAndBody_DistinctFromTheRusher()
        {
            Assert.AreEqual(CharacterRole.Bolter, CharacterSkin.RoleFor(EnemyKind.Bolter),
                "the Bolter must map to its own role, not fall through to CharacterRole.Robot (the Rusher's)");

            EnemyArchetype archetype = EnemyArchetype.Of(EnemyKind.Bolter);
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
            // reflection-driven pattern RobotSkinSpawnPathTests/RobotBodySizeTests already establish.
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

                Color expected = CharacterSkin.BaseColorFor(CharacterRole.Bolter);
                Color rusherColour = CharacterSkin.BaseColorFor(CharacterRole.Robot);
                Assert.That(Vector4.Distance(expected, rig.CurrentBodyColor), Is.LessThan(0.01f),
                    $"the Bolter must wear its own authored orange, not the Rusher's turquoise " +
                    $"(expected {expected}, was {rig.CurrentBodyColor})");
                Assert.That(Vector4.Distance(rusherColour, rig.CurrentBodyColor), Is.GreaterThan(0.3f),
                    "the built Bolter must not be wearing the Rusher's RobotBody colour");

                var names = e.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
                Assert.AreEqual(6, names.Count(n => n == "Spike"),
                    "the Bolter body must be built with 6 large radial spike parts around the drum's " +
                    "equator, not the Rusher's two shared body-cone lathes");
                Assert.AreEqual(1, names.Count(n => n == "Barrel"),
                    "the Bolter must carry one forward rod-launcher barrel part — the weapon tell");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                try { Object.DestroyImmediate(go); }
                finally { LogAssert.ignoreFailingMessages = false; }
            }
        }
    }
}
