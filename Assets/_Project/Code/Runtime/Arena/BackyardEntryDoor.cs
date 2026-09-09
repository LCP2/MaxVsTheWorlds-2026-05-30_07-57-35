using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// A permanently-shut door in the entry area's WEST boundary wall (MV-718).
    ///
    /// <see cref="BackyardHomeShed"/> stands off that wall, its own door facing the run — but the wall
    /// itself was unbroken, so the fiction the shed exists to sell (Max walked out of it into the yard)
    /// was contradicted by the geometry: there was no opening anywhere near it. The opening cinematic
    /// (MV-710) ends with Max leaving the shed and the door closing behind him; this object makes the
    /// playable world agree with that ending. It is a one-way door in FICTION ONLY — it never opens, no
    /// matter what breaks or gets destroyed, and it carries no collider of its own. The wall's existing
    /// collision is what actually stops the player and is completely untouched by this object's
    /// presence.
    ///
    /// Built the same code-only, collider-less way <see cref="BackyardHomeShed.BuildShed"/> builds the
    /// shed's own door. <see cref="AreaGate"/> was considered and rejected (ticket's own hypothesis): it
    /// is wired for a room-to-room <see cref="MapLink"/> (opensWith, EnemyNavigation.RegisterGate) this
    /// boundary has none of, and its leaf always carries a live collider (MV-386) — the opposite of what
    /// AC4 asks for here. A backdrop door that never opens has nothing to gain from either mechanic.
    /// Self-installing and code-only, like <see cref="BackyardHomeShed"/> itself — no scene wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardEntryDoor : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardEntryDoor>() != null) return;
            if (FindFirstObjectByType<BackyardPath>() == null) return;   // no level loaded at all yet
            new GameObject("BackyardEntryDoor").AddComponent<BackyardEntryDoor>();
        }

        /// <summary>How much of the wall's own thickness the door's slab fills — deliberately less
        /// than the whole thickness so both faces sit inset from the wall's own faces, reading as a
        /// door recessed into a fence line rather than a doorway with a gap (AC4's "slightly
        /// recessed").</summary>
        private const float ThicknessFraction = 0.7f;

        /// <summary>How tall the slab stands against the wall's own height — shorter than the wall,
        /// the same proportion the shed's own door is shorter than its walls.</summary>
        private const float HeightFraction = 0.84f;

        private Transform _root;

        /// <summary>Where the door ended up. Only meaningful once <see cref="Built"/> is true.</summary>
        public Vector3 Center { get; private set; }

        public bool Built { get; private set; }

        /// <summary>Always true. This door never opens — not on combat, not on any
        /// <see cref="GateCondition"/>, not ever. It is a rendered backdrop object, not a mechanic.</summary>
        public bool IsClosed => true;

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null) return;

            MapData map = path.Map;
            if (map == null)
            {
                Debug.LogWarning("[BackyardEntryDoor] no map loaded — nothing for Max to have shut behind him.");
                return;
            }

            // MV-750: this door exists only to complete BackyardHomeShed's fiction (Max shutting his
            // shed door behind him) — with no home shed in a non-garden world, it has nothing to pair
            // with and no wall opening to explain.
            if (!map.WantsGardenDressing) return;

            Center = PlaceFor(map);

            _root = new GameObject("EntryDoor").transform;
            _root.SetParent(transform, false);
            _root.gameObject.AddComponent<KeepsOwnMaterial>();

            BuildDoor(_root, Center, map.wallHeight, map.wallThickness);

            StaticBatchingUtility.Combine(_root.gameObject);
            Built = true;
        }

        /// <summary>Where the door stands: centred in the WEST wall's own thickness band, at the same
        /// Z as <see cref="BackyardHomeShed.PlaceFor"/> — so it lines up with the shed's own door on
        /// the other side of the wall, whichever way the run happens to be laid out.</summary>
        public static Vector3 PlaceFor(MapData map)
        {
            float x = map.Bounds().xMin - map.wallThickness * 0.5f;
            float z = BackyardHomeShed.PlaceFor(map).z;
            return new Vector3(x, 0f, z);
        }

        // --- building it -------------------------------------------------------------------------

        private static void BuildDoor(Transform root, Vector3 center, float wallHeight, float wallThickness)
        {
            Material plankDark = BackyardHomeShed.DoorMaterial();

            float thickness = wallThickness * ThicknessFraction;
            float height = wallHeight * HeightFraction;

            Part(root, "Door", center + new Vector3(0f, height * 0.5f, 0f),
                new Vector3(thickness, height, BackyardHomeShed.DoorWidth), plankDark);
        }

        private static void Part(Transform root, string name, Vector3 at, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            go.isStatic = true;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // A door the player must never collide with (the wall's own collider is what actually
            // stops them) — same Play-mode-vs-Edit-mode split GroundRing.Create uses, so an EditMode
            // test can assert "no collider" synchronously instead of racing a deferred Destroy.
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }
    }
}
