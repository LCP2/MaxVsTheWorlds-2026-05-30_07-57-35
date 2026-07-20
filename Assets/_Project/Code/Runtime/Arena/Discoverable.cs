using System;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// A landmark that the player has to FIND (YT-107). The factories and the boss used to be on the
    /// minimap from the first frame, with the hutch's name floating over the shed roofline before Max
    /// had left the patio — so the level had no secrets and exploring it was a formality.
    ///
    /// One flag, one direction: not found → found, and never back. "Once seen, they stay revealed" is
    /// the whole contract, and making it a one-way latch means no second writer can un-find something
    /// the player has already been shown. (That is not hypothetical here: dev mode re-asserts
    /// component <c>enabled</c> every frame, which is how a destroyed factory went back to producing
    /// robots in YT-100. Owned sticky state is the shape that survives that.)
    ///
    /// Nothing about WHERE the landmark is, or what hiding it looks like, lives here. This is the
    /// record; <see cref="Discovery"/> decides when it flips, and each landmark decides what it hides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Discoverable : MonoBehaviour
    {
        /// <summary>True once the player has laid eyes on this. Never goes back to false.</summary>
        public bool Found { get; private set; }

        /// <summary>Fired the once, on the frame it is first seen.</summary>
        public event Action Revealed;

        public void Reveal()
        {
            if (Found) return;
            Found = true;
            Revealed?.Invoke();
        }

        /// <summary>
        /// Has this thing been found? Answers for a landmark that has no
        /// <see cref="Discoverable"/> at all with <c>true</c> — deliberately.
        ///
        /// The alternative (unmarked ⇒ hidden) hides things nobody meant to hide: a factory built by
        /// a test fixture, or by some future path that doesn't go through the map, would vanish from
        /// the map with nothing on screen to explain why. Failing OPEN means the worst a missing
        /// marker can do is show something early, which is visible the moment you look at the build.
        /// <c>MapDiscoveryTests</c> asserts the shipped map marks every factory, the boss and the
        /// gate, so failing open never becomes the quiet default.
        /// </summary>
        public static bool FoundOn(Component landmark)
        {
            if (landmark == null) return false;
            var mark = landmark.GetComponent<Discoverable>();
            return mark == null || mark.Found;
        }
    }
}
