using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>What a map build produced. The dressing pass needs the cover it actually got, not the
    /// cover that was authored — those differ the moment a piece fails validation.</summary>
    public sealed class MapBuild
    {
        public readonly List<CoverPiece> Cover = new List<CoverPiece>(8);
        public readonly Dictionary<string, GameObject> Actors = new Dictionary<string, GameObject>();

        /// <summary>The factories this map built, in the order it authored them.</summary>
        public readonly List<MowerHutch> Factories = new List<MowerHutch>(2);
    }

    /// <summary>
    /// Builds a playable arena out of a <see cref="MapData"/> (YT-89): the floor, the walls, the cover
    /// and props — and then puts the actors where the map says they go, and wires them to each other
    /// the way the map says they connect.
    ///
    /// That second half is the point. The level's shape used to live in the scene YAML and the actors
    /// standing in it were placed separately by hand, so the two drifted apart and an editor scaffold
    /// existed for the sole purpose of shoving them back into agreement every time a number moved
    /// (Stage68). Now the map is the only thing that says where the factories stand, where the gate is,
    /// and which factories open it — so there is nothing left to keep in sync.
    ///
    /// Two kinds of actor, and the difference is exactly the one the level cares about:
    ///
    ///   * The ONE-OF actors — Max, the gate, the boss — are ADOPTED. The scene owns one of each and
    ///     the map moves and wires it.
    ///
    ///   * FACTORIES ARE BUILT (YT-92). A level can have as many as it likes, so there is nothing for
    ///     the scene to own one of, and the hand-placed hutch it used to own stands down. This is not
    ///     just plumbing for a second factory: the scene's copy of the hutch had been quietly carrying
    ///     a body colour from three tickets ago, overriding the code's, which is precisely the failure
    ///     mode the code-driven-scenes rule exists to stop. Built factories cannot disagree with each
    ///     other or with the code, because there is only one recipe.
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

            // Before anything registers: the scene's hand-placed hutch stands down, and the census
            // forgets whatever it may already have counted (its Awake may or may not have run — the
            // order of two Awakes in one scene load is nobody's to promise).
            RetireSceneFactories();
            FactoryCensus.Reset();

            // A new level is a new Invasion Level clock (YT-181) — a run's escalation must not carry
            // over from whatever the last level (or the last test) left it at.
            DifficultyDirector.Reset();

            // A new level is an empty field (YT-186) — the global live-robot count must start at zero,
            // not wherever the last level (or the last test) left it.
            EnemyCensus.Reset();

            // A new level is a new set of directions. The robots cache the map they navigate (YT-93),
            // and a cache that outlives its level would route this yard's robots around the last one's
            // walls.
            EnemyNavigation.Reset();

            var root = new GameObject($"Map: {map.name}").transform;
            root.SetParent(parent, false);

            FloorSlab floor = MapGeometry.Floor(map);
            // blocksSight: false — you cannot hide behind the ground, and a ground collider on the
            // cover layer would have every sight-line ray graze it.
            Box(root, "Map Floor", floor.Center, floor.Size, blocksSight: false, isStatic: true);

            foreach (WallSegment w in MapGeometry.Walls(map))
                Box(root, w.Name, w.Center, w.Size, blocksSight: true, isStatic: true);

            BuildProps(map, root, built);
            PlaceActors(map, root, built);
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

        /// <summary>Put the actors where the map says they stand: adopt the one-of ones, build the
        /// factories. An adopted actor's Y is left alone — the map authors a floor plan, not heights —
        /// while a built one gets a Y from its own body, so it can never be authored half-buried.</summary>
        private static void PlaceActors(MapData map, Transform root, MapBuild built)
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
                        built.Factories.Add(BuildFactory(e, root, built));
                        break;

                    case EntityKind.Gate:
                        GameObject gate = MarkDiscoverable(Adopt(e, built, Find<SubZoneGate>()));
                        // A gate is exactly as wide as the doorway it fills, plus the wall it seals
                        // against each side. Its width is NOT authored — it is read off the link, so a
                        // widened doorway can never leave a gap beside its gate.
                        if (gate != null)
                        {
                            gate.transform.localScale = new Vector3(SealWidth(map, e), e.height, e.depth);

                            // A shut gate stops sight, not just footsteps (YT-107). The walls around
                            // it already block (Box(blocksSight: true)), but the gate filling the
                            // doorway did not — so the boss could be discovered straight through a
                            // door that has never been opened, and a robot could watch Max through it.
                            // Nothing has to close this again: SubZoneGate.Open disables its collider
                            // the instant it starts sinking, so the sight-line opens with the gate.
                            CoverLayer.Assign(gate);
                        }
                        break;

                    case EntityKind.Boss:
                        MarkDiscoverable(Adopt(e, built, Find<BigBermudaBoss>()));
                        break;
                }
            }
        }

        /// <summary>
        /// Say that this landmark has to be found before the map will admit it exists (YT-107).
        ///
        /// Here rather than in each landmark's own Awake because THIS is the code that knows what
        /// kind of thing it is placing — the boss and the gate are adopted from the scene, and asking
        /// them to mark themselves would mean a scene-authored gate quietly behaves differently from
        /// a map-authored one.
        /// </summary>
        private static GameObject MarkDiscoverable(GameObject landmark)
        {
            if (landmark != null && landmark.GetComponent<Discoverable>() == null)
                landmark.AddComponent<Discoverable>();
            return landmark;
        }

        /// <summary>
        /// A Mower Hutch, from data (YT-92). Every factory in every map is this one recipe, so a
        /// level's second factory cannot be a slightly different machine from its first.
        ///
        /// The body is built and SIZED first, and only then given its components, because
        /// <see cref="MowerHutch"/> reads its own scale in Awake to size a health bar in metres
        /// (YT-71) — and AddComponent runs that Awake there and then. Add the script to a
        /// default-sized cube and you get a bar built for a 1 m machine on a 3 m one.
        ///
        /// Nothing here sets a material. The body is damageable, so the rendering layer skins it as a
        /// Structure exactly as it skins the hutch the scene used to hold, and both directors leave a
        /// damageable renderer alone — which is what keeps a code-built factory off the magenta path.
        /// </summary>
        private static MowerHutch BuildFactory(MapEntity e, Transform root, MapBuild built)
        {
            GameObject body = Spawn(root, e.id, PrimitiveType.Cube, e.GroundedCenter, e.Size);

            // Before the hutch, not after: MowerHutch.Awake runs inside the AddComponent below and
            // asks whether it has been found yet, so that the name badge and the glowing core are
            // never built visible and hidden a frame later. A one-frame flash of "MOWER HUTCH" on
            // the horizon is exactly the telegraph this ticket exists to remove.
            MarkDiscoverable(body);

            // RequireComponent brings the EnemySpawner with it — the factory's mouth is part of what a
            // factory IS, not something a scene has to remember to bolt on.
            var hutch = body.AddComponent<MowerHutch>();

            built.Actors[e.id] = body;
            return hutch;
        }

        /// <summary>
        /// The hand-placed Mower Hutch the slice scene has carried since the first scaffold stands
        /// down. The map owns the factories now, and a scene copy is not a spare — it is a second
        /// factory standing in the wrong room with its own stale serialized numbers.
        ///
        /// Inactive rather than destroyed, and BEFORE anything else runs: deactivating an object whose
        /// Awake has not fired yet means it never fires, so the retired hutch cannot register itself,
        /// emit a signal, or spawn a single robot.
        /// </summary>
        private static void RetireSceneFactories()
        {
            foreach (var hutch in Object.FindObjectsByType<MowerHutch>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hutch != null) hutch.gameObject.SetActive(false);
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

        /// <summary>Hand every gate the factories that open it. "Kill the sources and the way opens" is
        /// a property of the level data (<c>opensOn</c>) rather than a slot a human dragged an object
        /// into — a slot that silently comes undone the next time the object is rebuilt.
        ///
        /// A gate may name more than one factory (YT-92), and it opens on the LAST of them. Each key
        /// is announced to the gate as it is bound, so the gate knows how many it is waiting on before
        /// the player has broken any of them.</summary>
        private static void WireGates(MapData map, MapBuild built)
        {
            foreach (MapEntity gate in MapValidation.Kind(map, EntityKind.Gate))
            {
                if (!built.Actors.TryGetValue(gate.id, out GameObject gateGo) || gateGo == null) continue;

                var door = gateGo.GetComponent<SubZoneGate>();
                if (door == null) continue;

                foreach (string key in gate.Keys)
                {
                    if (!built.Actors.TryGetValue(key, out GameObject factoryGo) || factoryGo == null) continue;

                    var hutch = factoryGo.GetComponent<MowerHutch>();
                    if (hutch != null) hutch.Bind(door);   // Bind tells the gate it has another key
                }
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
