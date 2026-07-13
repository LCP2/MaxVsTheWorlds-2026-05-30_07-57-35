using UnityEngine;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Tells the lawn where the machine is (YT-79).
    ///
    /// The ground shader paints the yard's wear itself — the ruts the robo-mowers have worn driving
    /// out of the Hutch, the dead apron where they swing round to line up on Max, the oil under the
    /// machine — and every bit of that is a function of one thing: where the Hutch stands.
    ///
    /// It goes in as a shader GLOBAL rather than a material property, because the ground material is
    /// built by MaterialLibrary long before anything knows where the factory ended up, and because one
    /// material is shared by the lawn, the arena floor and the surround beyond the fence. One answer,
    /// one place to put it.
    ///
    /// Why read it from the scene at all, rather than drawing the tracks where they look nice: the
    /// Hutch's position is GAMEPLAY's. YT-38 put it where the fight wanted it and YT-70 moved Max away
    /// from it. If gameplay moves the factory tomorrow the tracks move with it — because they are the
    /// tracks of THAT factory, not a decal somebody once placed on a lawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardWear : MonoBehaviour
    {
        /// <summary>xy = where the machine stands; z = whether there is one at all.</summary>
        public const string GlobalName = "_MowerWear";

        private static readonly int MowerWearId = Shader.PropertyToID(GlobalName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardWear>() != null) return;
            new GameObject("BackyardWear").AddComponent<BackyardWear>();
        }

        private void Awake() => Apply();

        /// <summary>Point the lawn's wear at the factory. False if there isn't one — in which case the
        /// lawn stays pristine, which is the right answer for a yard with no machine in it.</summary>
        public bool Apply()
        {
            // Cleared first, and that is not belt-and-braces: a shader global outlives the scene that
            // set it. Without this, a test fixture that builds a bare arena after the Backyard has run
            // inherits the Backyard's tyre tracks through its origin.
            Clear();

            var hutch = FindFirstObjectByType<MowerHutch>();
            if (hutch == null) return false;

            Vector3 p = hutch.transform.position;
            Shader.SetGlobalVector(MowerWearId, new Vector4(p.x, p.z, 1f, 0f));
            return true;
        }

        /// <summary>No factory, no tracks.</summary>
        public static void Clear() => Shader.SetGlobalVector(MowerWearId, Vector4.zero);
    }
}
