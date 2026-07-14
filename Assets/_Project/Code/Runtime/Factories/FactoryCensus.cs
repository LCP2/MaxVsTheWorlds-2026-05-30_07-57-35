using System;
using System.Collections.Generic;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// How many factories this run has, and whether they are all down (YT-92).
    ///
    /// The slice used to have exactly one, so "a factory died" and "the run's sources are all gone"
    /// were the same event and everything downstream could listen to the same identity-less signal.
    /// With two of them those are different questions, and the answer to the second one has to live
    /// somewhere: the boss must stay asleep until the LAST factory falls, and the HUD must count
    /// "1 / 2", not "1 / 1". Both used to work it out for themselves off the same signal — which was
    /// fine while the answer was always "one".
    ///
    /// So there is one place that knows. A factory registers itself when it wakes and reports itself
    /// when it dies; <see cref="Cleared"/> fires exactly once, on the death of the last one standing.
    ///
    /// Registration happens in <c>Awake</c>, which is what makes the count trustworthy: the map builds
    /// its factories inside <c>BackyardPath.Awake</c>, so every factory in the level has registered
    /// before the first <c>Start</c> runs — and <c>Start</c> is where the HUD and the boss read it.
    /// </summary>
    public static class FactoryCensus
    {
        private static readonly List<MowerHutch> Standing = new List<MowerHutch>(4);
        private static readonly List<MowerHutch> Registered = new List<MowerHutch>(4);

        /// <summary>Every factory in the run, in the order the map placed them. The first is the one
        /// nearest the start of the level, which is what the tyre tracks and the mission line want.</summary>
        public static IReadOnlyList<MowerHutch> All => Registered;

        public static int Total => Registered.Count;
        public static int Destroyed => Registered.Count - Standing.Count;

        /// <summary>True once every factory this run has is down. False in a level with none — an
        /// empty arena has not been cleared, it just never had a source to break.</summary>
        public static bool AllDown => Registered.Count > 0 && Standing.Count == 0;

        /// <summary>The last factory has fallen. Fires once per run.</summary>
        public static event Action Cleared;

        /// <summary>Wipe the census. Called when a level starts building (the map engine), so a scene
        /// loaded a second time — in the game or in a test run — counts its own factories and not the
        /// previous level's ghosts.</summary>
        public static void Reset()
        {
            Registered.Clear();
            Standing.Clear();
        }

        public static void Register(MowerHutch hutch)
        {
            if (hutch == null || Registered.Contains(hutch)) return;
            Registered.Add(hutch);
            Standing.Add(hutch);
        }

        /// <summary>
        /// This factory no longer exists — its GameObject went away (a scene torn down, a test fixture
        /// cleaned up). NOT the same thing as the player destroying it: a level being unloaded has not
        /// been cleared, and nothing is raised here.
        ///
        /// Without this, a run's factories would outlive their level as dead references in a static
        /// list, and the next level would start with sources it could never break.
        /// </summary>
        public static void Forget(MowerHutch hutch)
        {
            Registered.Remove(hutch);
            Standing.Remove(hutch);
        }

        /// <summary>Report a factory destroyed. Raises <see cref="Cleared"/> when it was the last one
        /// standing. Idempotent — a factory that reports twice does not clear the run twice.</summary>
        public static void ReportDestroyed(MowerHutch hutch)
        {
            if (hutch == null || !Standing.Remove(hutch)) return;
            if (AllDown) Cleared?.Invoke();
        }
    }
}
