using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1107 — Lee's device report: a World 2 replicator's entry ramp and doorway come out of the
    /// side a large pipe runs along, ramp and pipe interpenetrating. Root cause (read in code):
    /// <c>FactoryDoorway.ChooseFace</c> picked a face with one <c>Physics.Raycast</c> per face and a
    /// tie-break toward wherever the player happened to stand at build time — decorative dressing (a
    /// pipe run, a kerb) carries no collider at all, so the probe reported a pipe-occupied face clear,
    /// and the facing Lee actually drew for each box in the design workbook (<c>MapEntity.facing</c>,
    /// "S" when unauthored) was never consulted by that choice either.
    ///
    /// Fails to even COMPILE on base commit d30d293: <see cref="FactoryDoorway.RampBounds"/>,
    /// <see cref="FactoryDoorway.RampFootWorld"/>, <see cref="FactoryDoorway.DoorFor"/> and
    /// <see cref="IFactoryDoorFacing"/> do not exist on that tree at all — the same "fails because the
    /// surface under test doesn't exist yet" shape this suite's other root-cause tickets already carry
    /// (<c>ReplicatorTests</c>, <c>MV1093LiveShedProductionTests</c>).
    ///
    /// ONE test (MV-465 Rule 1), driving the real entry points: <see cref="WorldLibrary.Load"/>/
    /// <see cref="WorldMapLoader.TryLoad"/>/<see cref="MapRuntime.Build"/> for every shipped world,
    /// World 2's own real dressing pass (<see cref="StormdrainDressing.Dress"/> — the actual generator
    /// of the colliderless pipe runs Lee's screenshot shows; Worlds 1 and 3 dress their walls
    /// differently and have nothing analogous), then <see cref="FactoryDoorway.Install"/> — the same
    /// entry point a real scene boot fires at <c>AfterSceneLoad</c>. Every sub-clause the ticket's own
    /// AC lists is a sub-assertion inside this one test, over every replicator in the real World 2
    /// build and every shed in Worlds 1 and 3.
    ///
    /// Tier 2 (resolved values): every assertion reads a built <see cref="FactoryDoorway"/>'s own
    /// resolved <see cref="FactoryDoorway.RampBounds"/> / <see cref="FactoryDoorway.RampFootWorld"/> /
    /// <see cref="FactoryDoorway.OutwardDirection"/>, another renderer's own resolved
    /// <c>Renderer.bounds</c>/<c>Renderer.enabled</c> after the whole build pipeline has run, or
    /// <see cref="MapData.IsWalkable"/> — never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV1107FactoryRampClearanceTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>(4);

        [TearDown]
        public void TearDown()
        {
            // Doors are built as independent scene roots, never parented under the body they belong to
            // (see FactoryDoorway.InstallFor) — they need their own teardown pass, not just the map
            // roots'. Only a safety net: the test itself tears each world down as it finishes with it.
            foreach (FactoryDoorway d in new List<FactoryDoorway>(FactoryDoorway.AllDoors))
                if (d != null) Object.DestroyImmediate(d.gameObject);

            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void EveryFactoryRamp_ClearsEveryOtherEnabledRenderer_AndLandsOnWalkableFloor()
        {
            // BuildBody's collider-strip [Error] every Replicator test carries, plus this ticket's own
            // config-error path (FactoryDoorway logs one if a ramp genuinely overlaps a wall/authored
            // cover) — neither is what this test is about.
            LogAssert.ignoreFailingMessages = true;

            int doorsChecked = 0;
            var violations = new List<string>();

            foreach (string worldKey in WorldLibrary.Keys)
            {
                WorldConfig cfg = WorldLibrary.Load(worldKey);
                Assert.IsNotNull(cfg, $"setup failure: {worldKey} did not load");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason),
                    $"setup failure: {worldKey} did not build a map: {loadReason}");

                var root = new GameObject($"MV1107 root {worldKey}");
                _spawned.Add(root);
                MapBuild built = MapRuntime.Build(map, root.transform);

                // World 2's own real dressing pass — the actual source of the colliderless pipe runs
                // Lee's screenshot shows (StormdrainKit.DressWallFace hangs a kerb/pipe bank/lamp on
                // every wall face that faces a room, independent of where any factory sits).
                if (worldKey == WorldLibrary.World2)
                    StormdrainDressing.Dress(root.transform, map, built.Cover);

                FactoryDoorway.Install();

                foreach (MowerHutch hutch in built.Factories)
                    CheckDoor(hutch, hutch.name, worldKey, map, ref doorsChecked, violations);

                foreach (Replicator replicator in built.Replicators)
                    CheckDoor(replicator, replicator.name, worldKey, map, ref doorsChecked, violations);

                // Torn down before the next world builds — every world's own map starts from the same
                // origin-centred coordinates, so leaving this one standing would let an unrelated
                // world's geometry spuriously "overlap" the next world's ramps purely from sharing that
                // coordinate space, not from anything either world actually built wrong.
                foreach (FactoryDoorway d in new List<FactoryDoorway>(FactoryDoorway.AllDoors))
                    if (d != null) Object.DestroyImmediate(d.gameObject);
                Object.DestroyImmediate(root);
                _spawned.Remove(root);
            }

            if (violations.Count > 0)
                Assert.Fail(string.Join("\n", violations));

            Assert.Greater(doorsChecked, 0, "setup failure: no factory doors were found to check at all");
        }

        private static void CheckDoor(Component body, string id, string worldKey, MapData map, ref int doorsChecked, List<string> violations)
        {
            FactoryDoorway door = FactoryDoorway.DoorFor(body);
            if (door == null) { violations.Add($"{worldKey}/{id}: no door was installed for this factory"); return; }
            string label = $"{worldKey}/{id}";
            doorsChecked++;

            // === where the config authors a facing, the door's outward direction equals it ===
            if (body is IFactoryDoorFacing authoredFacing && authoredFacing.AuthoredDoorOutward.HasValue
                && authoredFacing.AuthoredDoorOutward.Value != door.OutwardDirection)
            {
                violations.Add($"{label}: outward {door.OutwardDirection} != authored facing {authoredFacing.AuthoredDoorOutward.Value}");
            }

            // A ramp FactoryDoorway's own fix-up pass already disabled (see RampVisible's own doc) lost
            // its argument to a later-built neighbour landing in the same footprint — it draws nothing,
            // so there is no visible ramp left here for anything else to clear, and no foot point a
            // robot could ever stand on. Nothing further to assert for this door.
            if (!door.RampVisible) return;

            // === the ramp's resolved bounds overlap no other enabled renderer (outside the building's ===
            // === own hierarchy and the floor) by more than 0.05 m ===
            Bounds rampBounds = door.RampBounds;
            float groundY = door.GroundY;
            foreach (Renderer other in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (other == null || !other.enabled) continue;
                if (other.bounds.max.y <= groundY + 0.05f) continue; // the floor itself, not an obstacle
                if (other.transform == body.transform || other.transform.IsChildOf(body.transform)) continue;
                if (other.transform == door.transform || other.transform.IsChildOf(door.transform)) continue;

                if (ReallyOverlaps(rampBounds, other.bounds, 0.05f))
                    violations.Add($"{label}: ramp overlaps enabled renderer '{other.name}' rampBounds=[{rampBounds.min},{rampBounds.max}] otherBounds=[{other.bounds.min},{other.bounds.max}]");
            }

            // === the ramp's foot point is over walkable surface ===
            if (!map.IsWalkable(body.transform.position, door.RampFootWorld))
                violations.Add($"{label}: ramp foot {door.RampFootWorld} is not over walkable floor");
        }

        /// <summary>Same "genuine interpenetration, not a flush touch" test <c>FactoryDoorway</c>'s own
        /// fix-up pass uses — two boxes resting exactly against each other (a ramp's sill meeting
        /// ordinary floor) must not read as a 0.05 m violation.</summary>
        private static bool ReallyOverlaps(Bounds a, Bounds b, float epsilon)
        {
            return a.min.x < b.max.x - epsilon && a.max.x > b.min.x + epsilon
                && a.min.y < b.max.y - epsilon && a.max.y > b.min.y + epsilon
                && a.min.z < b.max.z - epsilon && a.max.z > b.min.z + epsilon;
        }
    }
}
