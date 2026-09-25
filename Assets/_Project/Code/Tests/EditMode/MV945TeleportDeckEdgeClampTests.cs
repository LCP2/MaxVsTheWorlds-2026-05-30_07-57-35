using System.Reflection;
using NUnit.Framework;
using UnityEngine;

using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-945, Lee (TestFlight 0.9.9): Max and Sentinels are still leaving the upper walkway — a
    /// screenshot on the a12/a13 Up walkway shows a Sentinel floating outside the parapet. The ticket's
    /// diagnosis comment found <see cref="PlayerAbilities.ResolveSameRoomLanding"/> only ever tested a
    /// same-room blink's landing point for overlap against <see cref="CoverLayer"/>
    /// (<c>PlayerAbilities.CapsuleFitsAt</c>) — and <c>MapRuntime</c>'s own parapet colliders are
    /// deliberately kept OFF <see cref="CoverLayer"/> (they block <c>CharacterController.Move</c>
    /// directly; see <c>MapRuntime.BuildParapetSpan</c>'s own doc comment), so a blink aimed past a
    /// deck's edge finds nothing solid at the open-air landing point and lands exactly there — the same
    /// class of bug MV-864 already fixed for Sentinel deploy, never carried over to Teleport. Fails on
    /// the pre-fix base commit: the blink lands at the raw aim (Origin.x + 4), 2.5m past the deck's own
    /// [-1.5, +1.5] edge, not inside its rect.
    ///
    /// Fixture mirrors <see cref="MV864SentinelDeckPlacementTests"/>: a hand-built <see cref="MapData"/>
    /// (a level-0 floor zone and its level&gt;0 deck overlay sharing one footprint, with a single 3m-wide
    /// Deck entity at its centre) seeded into <see cref="EnemyNavigation.Map"/> through a bare
    /// <see cref="BackyardPath"/>, the same reflection idiom that test and MV670/MV847's teleport tests
    /// already use. Asserts a RESOLVED value (testing policy MV-465, Tier 2): Max's actual
    /// <c>transform.position</c> after the blink, never an authored constant.
    /// </summary>
    public sealed class MV945TeleportDeckEdgeClampTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        // Awake isn't reliably invoked for AddComponent outside Play mode (MV590BossWallSteeringTests,
        // MV670TeleportPassesDecorativeCollidersTests) — drive it directly so PlayerAbilities' own _cc
        // field actually exists before TryTeleport is exercised.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset —
        // a distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry
        // (same reasoning as MV864SentinelDeckPlacementTests.Origin).
        private static readonly Vector3 Origin = new Vector3(-8123f, 2.5f, 91044f);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            EnemyNavigation.Reset();
        }

        [Test]
        public void BlinkAimedOffADecksEdgeLandsOnTheDeckNotMidAir()
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
                pathGo = new GameObject("MV945-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                // MOVE (m_tp's category) starts locked after Reset() — unlock every category so
                // Acquire(AbilityKind.Teleport) below succeeds, the same idiom MV670/MV847's teleport
                // tests already use.
                foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
                DevTuning.TeleportBaseDistance = 4f;
                DevTuning.TeleportDistancePerLevel = 0f;

                maxGo = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
                var cc = maxGo.GetComponent<CharacterController>();
                cc.center = Vector3.up * 1f;
                cc.height = 2f;
                cc.radius = 0.4f;
                maxGo.transform.position = Origin; // standing on the 3m-wide deck's own centre

                var abilities = maxGo.GetComponent<PlayerAbilities>();
                if (abilities == null) abilities = maxGo.AddComponent<PlayerAbilities>();
                InvokeAwake(abilities);
                WeaponSystemState.Acquire(AbilityKind.Teleport);

                // Aimed 4m along +X: 2.5m past the deck's own edge at x = Origin.x + 1.5, into open air.
                bool blinked = abilities.TryTeleport(Vector3.right);

                Assert.That(blinked, Is.True, "precondition: an acquired, off-cooldown Teleport must fire");
                Vector3 landed = maxGo.transform.position;
                Assert.That(landed.x, Is.InRange(Origin.x - 1.5f, Origin.x + 1.5f),
                    "MV-945: a blink aimed off a deck's edge must land inside the deck's own rect, not " +
                    "past it in open air");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
            }
        }
    }
}
