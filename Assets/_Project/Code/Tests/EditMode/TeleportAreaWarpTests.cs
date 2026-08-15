using NUnit.Framework;
using UnityEngine;

using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-393: Teleport must cross a wall/area boundary when the arena beyond it is already reachable
    /// (its connecting gate open) — "I should teleport over walls... any arena in range where there's an
    /// open gate, even if I've not actually been inside that arena yet" — and must NOT when it is not
    /// (a still-shut or locked gate), so a genuinely closed-off area can never be blinked into.
    ///
    /// Exercises <see cref="PlayerAbilities.CanWarpAcrossAreas"/> directly against the shipped map, the
    /// same pure-data shape <see cref="MapRoutesTests"/> already tests robot routing with — no scene, no
    /// live gate GameObject, no CharacterController.
    /// </summary>
    public sealed class TeleportAreaWarpTests
    {
        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        // area1 (x0,z0, 14x10) sits directly south of area2 (x0,z17, 26x24) across gate1, at z = 5.
        private static readonly Vector3 InArea1 = new Vector3(0f, 0f, 2f);
        private static readonly Vector3 JustInsideArea2 = new Vector3(0f, 0f, 6f);

        [Test]
        public void OpenConnectingGateAllowsTheWarp()
        {
            MapData map = Shipped();

            bool canWarp = PlayerAbilities.CanWarpAcrossAreas(map, InArea1, JustInsideArea2, _ => true);

            Assert.That(canWarp, Is.True,
                "an open gate1 must let Teleport land in area2 even though Max is still standing in area1");
        }

        [Test]
        public void ShutConnectingGateRefusesTheWarp()
        {
            MapData map = Shipped();

            bool canWarp = PlayerAbilities.CanWarpAcrossAreas(map, InArea1, JustInsideArea2, _ => false);

            Assert.That(canWarp, Is.False,
                "a still-shut gate1 must NOT let Teleport skip straight into area2 — the caller should " +
                "fall back to a normal collision-respecting move instead");
        }

        [Test]
        public void LandingInsideTheSameAreaNeverWarps()
        {
            MapData map = Shipped();
            var stillInArea1 = new Vector3(3f, 0f, 4f);

            bool canWarp = PlayerAbilities.CanWarpAcrossAreas(map, InArea1, stillInArea1, _ => true);

            Assert.That(canWarp, Is.False,
                "a blink that never leaves Max's own room has no boundary to cross, so it stays a normal " +
                "collision-respecting move regardless of gate state");
        }

        [Test]
        public void LandingOutsideEveryZoneNeverWarps()
        {
            MapData map = Shipped();
            var theVoid = new Vector3(9999f, 0f, 9999f);

            bool canWarp = PlayerAbilities.CanWarpAcrossAreas(map, InArea1, theVoid, _ => true);

            Assert.That(canWarp, Is.False, "there is nothing to warp into outside every authored room");
        }

        [Test]
        public void NoMapLoadedNeverWarps()
        {
            bool canWarp = PlayerAbilities.CanWarpAcrossAreas(null, InArea1, JustInsideArea2, _ => true);

            Assert.That(canWarp, Is.False, "a bare fixture with no level loaded has no room graph to ask");
        }
    }
}
