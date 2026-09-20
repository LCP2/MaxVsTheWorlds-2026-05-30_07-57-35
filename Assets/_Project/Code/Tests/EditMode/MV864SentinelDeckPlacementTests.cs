using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-864: a Sentinel deployed while Max stands on a raised deck used to be placed exactly at the
    /// raw aimed point with no regard for the deck's own footprint — <c>PlayerAbilities.TryDeploySentinel</c>
    /// wrote whatever the joystick handed it straight to <c>Sentinel.Init</c>, and the joystick's own
    /// reticle clamp (<c>MapZone.Clamp</c>) only ever pulled a point back inside the ROOM a deck
    /// overlays, never the deck's own (narrower) walkable rect. Aiming past a deck's own edge therefore
    /// deployed the sentinel hanging in mid-air beyond it — exactly Lee's report ("hang in mid air and
    /// in/over the walls"). Fails on dd85276 (the commit before this ticket): the sentinel lands at the
    /// raw aim (4.5, 2.5, 0), outside the deck's own [-1.5, 1.5] X span, not inside its rect.
    ///
    /// Fixture: a hand-built <see cref="MapData"/> — a level-0 floor zone and a level&gt;0 deck overlay
    /// sharing its exact footprint (MV-697's own overlay rule), with a single 3m-wide, 6m-deep Deck
    /// entity at its centre — seeded into <see cref="EnemyNavigation.Map"/> through a bare
    /// <see cref="BackyardPath"/>, the same reflection idiom MV-832/MV-837/MV-838's own EditMode tests
    /// already use (Unity does not call <c>Awake</c> for a plain <c>AddComponent</c> outside Play mode,
    /// so the hand-set map is never clobbered by a real world load). Asserts a RESOLVED value (testing
    /// policy MV-465, Tier 2): the deployed Sentinel's actual <c>transform.position</c>, never an
    /// authored constant.
    /// </summary>
    public sealed class MV864SentinelDeckPlacementTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        // EditMode tests share one scene across the whole run with no reset between them (see
        // MV624SentinelCollisionTests's own note) — a far, dedicated coordinate space rules out any
        // leftover geometry/registry entries from an unrelated test tripping IsValidSentinelPlacement's
        // clearance/CoverLayer checks.
        private static readonly Vector3 Origin = new Vector3(6000f, 0f, 6000f);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();   // also calls RigState.Reset() — the category unlock below must come AFTER this
            // This test is about deck-placement resolution once u_sen is owned, not RigState's own
            // shed/category-lock gate — force every category open so u_sen (SUPPORT's own root) stays
            // reached, same setup idiom SentinelPlacementTests uses for the same reason.
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            DevTuning.Reset();
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [Test]
        public void SentinelAimedBeyondADecksEdgeDeploysOntoTheDeckNotMidAir()
        {
            var map = new MapData
            {
                deckHeight = 2.5f,
                zones = new[]
                {
                    new MapZone { id = "floor", x = Origin.x, z = Origin.z, width = 20f, depth = 20f, level = 0 },
                    new MapZone { id = "deck", x = Origin.x, z = Origin.z, width = 20f, depth = 20f, level = 1 },
                },
                entities = new[]
                {
                    new MapEntity { id = "deck1", kind = "deck", x = Origin.x, z = Origin.z, width = 3f, depth = 6f },
                },
            };

            GameObject pathGo = null;
            GameObject maxGo = null;
            try
            {
                Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
                pathGo = new GameObject("MV864-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                WeaponSystemState.Acquire(AbilityKind.Sentinels);
                PickupWallet.SetPowerCells(100);
                PickupWallet.SetPowerCellSecondary(100); // MV-673: a Sentinel deploy spends this bank, not Parts

                maxGo = new GameObject("Max");
                maxGo.transform.position = Origin + new Vector3(0f, 2.5f, 0f); // standing on the 3m-wide deck's own centre
                var abilities = maxGo.AddComponent<PlayerAbilities>();

                // 3m beyond the deck's own edge, at local x = 1.5
                var aimedPoint = Origin + new Vector3(4.5f, 2.5f, 0f);
                Assert.IsTrue(abilities.TryDeploySentinel(aimedPoint), "setup failure: the deploy must be accepted");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(1));

                Vector3 placed = Sentinel.Active[0].transform.position;
                Assert.That(placed.x, Is.InRange(Origin.x - 1.5f, Origin.x + 1.5f),
                    "MV-864: the sentinel must land inside the deck's own 3m-wide rect, not beyond its edge");
                Assert.That(placed.y, Is.EqualTo(2.5f).Within(0.05f),
                    "MV-864: the sentinel must rest at the deck's own height, not the raw aim's");
            }
            finally
            {
                Sentinel.DestroyAllActive();
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
            }
        }
    }
}
