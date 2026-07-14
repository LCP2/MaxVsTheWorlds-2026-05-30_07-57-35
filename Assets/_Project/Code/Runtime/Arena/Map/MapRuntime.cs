using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>What a map build produced. The dressing pass needs the cover it actually got, not the
    /// cover that was authored — those differ the moment a piece fails validation.</summary>
    public sealed class MapBuild
    {
        public readonly List<CoverPiece> Cover = new List<CoverPiece>(8);
        public readonly Dictionary<string, GameObject> Actors = new Dictionary<string, GameObject>();
    }

    /// <summary>
    /// Builds a playable arena out of a <see cref="MapData"/> (YT-89): the floor, the walls, the cover
    /// and props — and then puts the actors where the map says they go, and wires them to each other
    /// the way the map says they connect.
    ///
    /// That second half is the point. The level's shape used to live in the scene YAML and the actors
    /// standing in it were placed separately by hand, so the two drifted apart and an editor scaffold
    /// existed for the sole purpose of shoving them back into agreement every time a number moved
    /// (Stage68). Now the map is the only thing that says where the factory stands, where the gate is,
    /// and which factory opens which gate — so there is nothing left to keep in sync.
    ///
    /// Actors are ADOPTED, not created: the scene owns one of each (a factory, a gate, a boss, Max),
    /// and the map moves and wires them. It does not spawn a second factory from data — when a map
    /// wants that, this is the method that grows, and nothing else has to.
    ///
    /// Only shapes are set here. The stylised look is applied automatically by the rendering layer
    /// (flat → ground, tall → wall, short → prop), so nothing here touches a material — and so nothing
    /// here can ship magenta.
    /// </summary>
    public static class MapRuntime
    {
        public static MapBuild Build(MapData map, Transform parent)
        {
            var built = new MapBuild();
            if (map == null) return built;

            RetireLegacyGround();

            var root = new GameObject($"Map: {map.name}").transform;
            root.SetParent(parent, false);

            FloorSlab floor = MapGeometry.Floor(map);
            // blocksSight: false — you cannot hide behind the ground, and a ground collider on the
            // cover layer would have every sight-line ray graze it.
            Box(root, "Map Floor", floor.Center, floor.Size, blocksSight: false, isStatic: true);

            foreach (WallSegment w in MapGeometry.Walls(map))
                Box(root, w.Name, w.Center, w.Size, blocksSight: true, isStatic: true);

            BuildProps(map, root, built);
            PlaceActors(map, built);
            WireGates(map, built);

            return built;
        }

        /// <summary>Everything the map creates from nothing: cover to fight around, and scenery with a
        /// body.</summary>
        private static void BuildProps(MapData map, Transform root, MapBuild built)
        {
            bool saidPickup = false;

            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;

                switch (e.Kind)
                {
                    case EntityKind.Cover:
                        built.Cover.Add(BuildCover(root, e));
                        break;

                    case EntityKind.Prop:
                        // Scenery with a body: it reads, and it breaks a sight-line, but the fight is
                        // not designed around it (the posts flanking the shed).
                        Box(root, e.id, e.GroundedCenter, e.Size, blocksSight: true, isStatic: true);
                        break;

                    case EntityKind.Pickup:
                        // The format carries pickups so a map can be authored for them, but the slice
                        // has no pickup system yet. Say so once — do not spawn a lie.
                        if (!saidPickup)
                        {
                            Debug.Log("[MapRuntime] this map authors pickups, but the slice has no " +
                                      "pickup system yet — skipping them.");
                            saidPickup = true;
                        }
                        break;
                }
            }
        }

        private static CoverPiece BuildCover(Transform root, MapEntity e)
        {
            ArenaCover cover = e.ToCover();

            // The cylinder mesh is 2 units tall, so half its height goes into the Y scale.
            Vector3 scale = cover.Shape == CoverShape.Cylinder
                ? new Vector3(cover.Size.x, cover.Size.y * 0.5f, cover.Size.z)
                : cover.Size;

            // Left non-static, unlike the walls: the ambience layer gently sways anything prop-sized.
            GameObject body = Spawn(root, e.id,
                cover.Shape == CoverShape.Cylinder ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                cover.Center, scale);

            // This is the line that turns a prop from scenery into a mechanic (YT-83).
            CoverLayer.Assign(body);

            return new CoverPiece(cover, body);
        }

        /// <summary>Move the scene's actors to where the map says they stand. Their Y is left alone —
        /// the map authors a floor plan, not heights.</summary>
        private static void PlaceActors(MapData map, MapBuild built)
        {
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;

                switch (e.Kind)
                {
                    case EntityKind.PlayerSpawn:
                        Adopt(e, built, GameObject.FindGameObjectWithTag("Player"));
                        break;

                    case EntityKind.Factory:
                        Adopt(e, built, Find<MowerHutch>());
                        break;

                    case EntityKind.Gate:
                        GameObject gate = Adopt(e, built, Find<SubZoneGate>());
                        // A gate is exactly as wide as the doorway it fills, plus the wall it seals
                        // against each side. Its width is NOT authored — it is read off the link, so a
                        // widened doorway can never leave a gap beside its gate.
                        if (gate != null)
                            gate.transform.localScale = new Vector3(SealWidth(map, e), e.height, e.depth);
                        break;

                    case EntityKind.Boss:
                        Adopt(e, built, Find<BigBermudaBoss>());
                        break;
                }
            }
        }

        private static GameObject Adopt(MapEntity e, MapBuild built, GameObject actor)
        {
            if (actor == null)
            {
                // Normal in a geometry-only test scene; a real problem in the game scene, so say it.
                Debug.LogWarning($"[MapRuntime] the map places '{e.id}' ({e.kind}), but the scene has " +
                                 "no such actor to place.");
                return null;
            }

            // A CharacterController caches its own position and will happily undo a teleport, so it
            // has to be switched off across the move (this is how Max gets to the map's start).
            var cc = actor.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            Vector3 at = actor.transform.position;
            actor.transform.position = new Vector3(e.x, at.y, e.z);

            if (cc != null) cc.enabled = was;

            built.Actors[e.id] = actor;
            return actor;
        }

        /// <summary>Hand every gate the factory that opens it. "Kill the source and the way opens" is
        /// now a property of the level data (<c>opensOn</c>) rather than a slot a human dragged an
        /// object into — a slot that silently comes undone the next time the object is rebuilt.</summary>
        private static void WireGates(MapData map, MapBuild built)
        {
            foreach (MapEntity gate in MapValidation.Kind(map, EntityKind.Gate))
            {
                if (!built.Actors.TryGetValue(gate.id, out GameObject gateGo) || gateGo == null) continue;
                if (!built.Actors.TryGetValue(gate.opensOn, out GameObject factoryGo) || factoryGo == null) continue;

                var hutch = factoryGo.GetComponent<MowerHutch>();
                var door = gateGo.GetComponent<SubZoneGate>();
                if (hutch != null && door != null) hutch.Bind(door);
            }
        }

        /// <summary>Width a gate needs to seal its doorway: the opening, plus the wall thickness each
        /// side, so there is no sliver to squeeze through.</summary>
        public static float SealWidth(MapData map, MapEntity gate)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null || link.gate != gate.id) continue;
                    if (MapGeometry.Doorway(map, link, out _, out _, out Span hole))
                        return hole.Length + map.wallThickness * 2f;
                }
            }
            return gate.width;   // an unlinked gate falls back to its authored width
        }

        /// <summary>The slice scene still carries a hand-placed 30 m ground plane from the very first
        /// scaffold. The map owns its floor now, and two coplanar floors at y=0 z-fight into a
        /// shimmering mess — so the old one stands down.</summary>
        private static void RetireLegacyGround()
        {
            GameObject ground = GameObject.Find("Ground");
            if (ground != null) ground.SetActive(false);
        }

        private static GameObject Find<T>() where T : Component
        {
            var found = Object.FindFirstObjectByType<T>();
            return found == null ? null : found.gameObject;
        }

        private static void Box(Transform root, string name, Vector3 center, Vector3 size,
                                bool blocksSight, bool isStatic)
        {
            GameObject go = Spawn(root, name, PrimitiveType.Cube, center, size);
            go.isStatic = isStatic;
            if (blocksSight) CoverLayer.Assign(go);
        }

        private static GameObject Spawn(Transform root, string name, PrimitiveType type,
                                        Vector3 center, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = center;
            go.transform.localScale = scale;
            return go;
        }
    }
}
