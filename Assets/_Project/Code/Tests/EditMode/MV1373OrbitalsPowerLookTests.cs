using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1373 (the one new test, Rule 1; Tier 2 resolved values): at POWER 5, sampled at pulse phase 0,
    /// the resolved glow Renderer.bounds diameter is no more than 0.42 m x 1.02 (base: 0.60 m), and the
    /// orb's live TrailRenderer.time resolves to 0.60 s (base: 0.20 s).
    /// </summary>
    public sealed class MV1373OrbitalsPowerLookTests
    {
        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Mothership));
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
        }

        [Test]
        public void AtPower5OrbStaysSmallAndTrailIsLong()
        {
            WeaponSystemState.SecondaryKind = SecondaryKind.Orbitals;
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_odm", 5 } }, new[] { "SECONDARY" });

            var maxGo = new GameObject("Max");
            var orbitals = maxGo.AddComponent<Orbitals>();
            var visuals = maxGo.AddComponent<OrbitalsVisuals>();
            try
            {
                LogAssert.ignoreFailingMessages = true; // VfxMaterials' one-time shader-resolve log line
                visuals.SetPulsePhaseForTests(0f);
                visuals.Tick();
                LogAssert.ignoreFailingMessages = false;

                Assert.Greater(orbitals.OrbCount, 0, "test precondition: at least one orb must be live");

                float diameter = visuals.ResolvedOrbBounds(0).size.x;
                Assert.LessOrEqual(diameter, 0.42f * 1.02f,
                    $"POWER 5 resolved glow bounds diameter must be <= 0.4284 m, was {diameter:0.000} m");

                float trail = visuals.ResolvedTrailTime(0);
                Assert.AreEqual(0.60f, trail, 0.001f, $"POWER 5 resolved trail time must be 0.60 s, was {trail:0.000} s");
            }
            finally { Object.DestroyImmediate(maxGo); }
        }
    }
}
