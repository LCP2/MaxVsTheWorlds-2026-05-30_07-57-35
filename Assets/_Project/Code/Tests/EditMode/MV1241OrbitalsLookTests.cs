using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1241 (the one new test, Rule 1): proves Orbitals' orb LOOK (POWER's own visible change)
    /// resolves off POWER rather than being an authored constant (MV-465 Tier 2) — a live orb's own
    /// resolved renderer bounds (read off the glow's live <see cref="Renderer"/> after
    /// <see cref="OrbitalsVisuals.Tick"/> has scaled it, not the float that produced the scale), at
    /// POWER levels 1 and 5 (s_odm's own 1..5 range).
    ///
    /// Proven to fail on base commit (main HEAD before this ticket): <see cref="OrbitalsVisuals"/> did
    /// not exist at all — this test fails to compile against that commit (CS0246 "The type or
    /// namespace name 'OrbitalsVisuals' could not be found"), the same class of pre-fix evidence
    /// <see cref="MV1236OrbitalsTests"/> documents.
    /// </summary>
    public sealed class MV1241OrbitalsLookTests
    {
        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            // s_odm lives only on rig_board.ship.json — same board selection MV1236OrbitalsTests uses.
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
        public void OrbResolvedBoundsGrowWithPowerLevel()
        {
            AssertOrbDiameterAtPowerLevel(1, 0.35f);
            AssertOrbDiameterAtPowerLevel(5, 0.6f);
        }

        private static void AssertOrbDiameterAtPowerLevel(int powerLevel, float expectedDiameter)
        {
            WeaponSystemState.SecondaryKind = SecondaryKind.Orbitals;
            // ORBS/REACH left at their own default start level (1) — only POWER (s_odm) is moved,
            // same "RestoreSnapshot bypasses the draft/reach gate for test setup" shortcut
            // MV1236OrbitalsTests/MV694ShoulderRackTests/RigStateTests use.
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_odm", powerLevel } },
                new[] { "SECONDARY" });

            var maxGo = new GameObject("Max");
            var orbitals = maxGo.AddComponent<Orbitals>();
            var visuals = maxGo.AddComponent<OrbitalsVisuals>();

            try
            {
                LogAssert.ignoreFailingMessages = true; // VfxMaterials' one-time shader-resolve log line
                visuals.Tick();
                LogAssert.ignoreFailingMessages = false;

                Assert.Greater(orbitals.OrbCount, 0, "test precondition: at least one orb must be live");

                float resolvedDiameter = visuals.ResolvedOrbBounds(0).size.x;
                Assert.AreEqual(expectedDiameter, resolvedDiameter, 0.03f,
                    $"POWER {powerLevel}'s resolved renderer-bounds diameter must be ~{expectedDiameter}m, " +
                    $"was {resolvedDiameter:0.000}m");
            }
            finally
            {
                Object.DestroyImmediate(maxGo);
            }
        }
    }
}
