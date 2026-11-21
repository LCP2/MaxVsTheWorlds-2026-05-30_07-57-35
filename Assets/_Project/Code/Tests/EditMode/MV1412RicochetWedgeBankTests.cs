using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1412 (the one new test, Rule 1): builds a single SE 3 m wedge through the real
    /// <see cref="MapRuntime.BuildCover"/>, fires a RICOCHET disc along +X into its slanted (NW-facing)
    /// face, and asserts the disc's RESOLVED direction after the bounce is +Z within 3 degrees
    /// (MV-465 Tier 2: the direction the engine's own collider normal produced, not an authored value).
    ///
    /// Fails on base commit cef5ebd (and ee24573): <c>CoverShape.Wedge</c> and
    /// <c>MapEntity.corner</c> do not exist there, so the fixture does not compile.
    /// </summary>
    public sealed class MV1412RicochetWedgeBankTests
    {
        private static readonly FieldInfo DirectionField =
            typeof(RicochetDisc).GetField("_direction", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            RicochetDisc.DestroyPoolForTests();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void DiscFiredAlongPlusXIntoSeWedgeSlantedFaceLeavesAlongPlusZ()
        {
            _root = new GameObject("MV1412 Root");
            MapRuntime.BuildCover(_root.transform, new MapEntity
            {
                id = "wedge", kind = "cover", shape = "wedge", corner = "SE",
                x = 0f, z = 0f, width = 3f, depth = 3f, height = 1.8f,
            });
            Physics.SyncTransforms();

            LogAssert.ignoreFailingMessages = true; // one-time VfxMaterials shader log, as in MV1195
            RicochetDisc disc;
            try
            {
                disc = RicochetDisc.Spawn(new Vector3(-5f, 0.9f, 0f), Vector3.right, Ricochet.DefaultSpeed,
                    bounces: 2, powerLevel: 1, aimLevel: 0, damage: 12f, bankBonusPerBounce: 0.2f,
                    turnRateDegPerSec: 0f, lookaheadMetres: 0f, Ricochet.DefaultRadius,
                    Ricochet.DefaultLifetimeSeconds);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsNotNull(disc, "test precondition: the pool must have room for one disc");

            const float step = 1f / 60f;
            for (int i = 0; i < 240 && disc.IsActiveDisc && disc.BouncesTaken == 0; i++) disc.Tick(step);

            Assert.AreEqual(1, disc.BouncesTaken, "the disc must bank off the wedge's slanted face");
            var dir = (Vector3)DirectionField.GetValue(disc);
            float angle = Vector3.Angle(dir, Vector3.forward);
            Assert.LessOrEqual(angle, 3f,
                $"a disc fired along +X into an SE wedge's 45-degree face must leave along +Z; left along {dir}");
        }
    }
}
