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

        // Replicators (MV-703/MV-706), tracked separately from MowerHutch above because a gate's
        // `replicators-destroyed:<areas>` condition needs to ask "destroyed in THESE areas", not just
        // "destroyed overall" — an MowerHutch shed condition never needed to ask that.
        private static readonly List<Replicator> ReplicatorsStanding = new List<Replicator>(4);
        private static readonly List<Replicator> ReplicatorsRegistered = new List<Replicator>(4);
        private static readonly Dictionary<Replicator, string> ReplicatorArea = new Dictionary<Replicator, string>(4);

        /// <summary>Every factory in the run, in the order the map placed them. The first is the one
        /// nearest the start of the level, which is what the tyre tracks and the mission line want.</summary>
        public static IReadOnlyList<MowerHutch> All => Registered;

        public static int Total => Registered.Count;
        public static int Destroyed => Registered.Count - Standing.Count;

        /// <summary>How many registered Replicators are down (MV-948) — the Replicator counterpart to
        /// <see cref="Destroyed"/>, so a caller that needs "every shed-equivalent factory destroyed this
        /// run" regardless of which world it's in can add the two without reaching into either list
        /// directly.</summary>
        public static int ReplicatorsDestroyed => ReplicatorsRegistered.Count - ReplicatorsStanding.Count;

        /// <summary>True once every factory this run has is down. False in a level with none — an
        /// empty arena has not been cleared, it just never had a source to break.</summary>
        public static bool AllDown => Registered.Count > 0 && Standing.Count == 0;

        /// <summary>The last factory has fallen. Fires once per run.</summary>
        public static event Action Cleared;

        /// <summary>MV-950: fired once, at the end of <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/>,
        /// after both <see cref="ApplyCheckpointDestroyedIds"/> and <see cref="ApplyCheckpointDestroyedShedIds"/>
        /// have moved every checkpoint-recorded Replicator/shed out of <see cref="ReplicatorsStanding"/>/
        /// <see cref="Standing"/> — <see cref="Destroyed"/> and <see cref="ReplicatorsDestroyed"/> already
        /// read correctly by this point, so a listener reacts by re-reading them, not by replaying the
        /// live-kill signal (that would re-drop loot/VFX for a kill the player already banked). A RESUME
        /// never reloads the scene, so this is what tells the HUD's already-alive <c>HudModel</c> and
        /// <c>RunTracker</c>'s already-alive <c>RunStats</c> to catch up — both otherwise stay at zero
        /// since neither is constructed fresh by a restore. Never fires for a fresh PLAY (<c>RestoreCheckpoint</c>
        /// returns before reaching here when the slot holds no checkpoint).</summary>
        public static event Action CheckpointRestored;

        /// <summary>Raise <see cref="CheckpointRestored"/> — the public trigger <c>SaveSystem</c> (a
        /// different namespace) needs, since a C# event can only be invoked from its declaring type.</summary>
        public static void RaiseCheckpointRestored() => CheckpointRestored?.Invoke();

        /// <summary>Wipe the census. Called when a level starts building (the map engine), so a scene
        /// loaded a second time — in the game or in a test run — counts its own factories and not the
        /// previous level's ghosts.</summary>
        public static void Reset()
        {
            Registered.Clear();
            Standing.Clear();
            ReplicatorsRegistered.Clear();
            ReplicatorsStanding.Clear();
            ReplicatorArea.Clear();
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

        /// <summary>The stable ids (<see cref="MowerHutch.Id"/>) of every registered shed this run has
        /// already reported destroyed (MV-922) — what a mid-run checkpoint capture persists so a resume
        /// can re-apply the exact same destroyed set onto the fresh instances a level rebuild creates.
        /// Same shape as <see cref="DestroyedReplicatorIds"/>. A shed whose <see cref="MowerHutch.Id"/>
        /// was never stamped (a hand-built test fixture that skips <see cref="MowerHutch.SetId"/>) is
        /// simply left out.</summary>
        public static string[] DestroyedShedIds()
        {
            var ids = new List<string>();
            foreach (MowerHutch h in Registered)
                if (h != null && !Standing.Contains(h) && !string.IsNullOrEmpty(h.Id))
                    ids.Add(h.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded shed destruction (MV-922) onto whichever
        /// currently-registered, still-alive sheds carry a matching <see cref="MowerHutch.Id"/> — called
        /// once by <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME, after the level's
        /// own sheds have already registered themselves alive (the ordinary build). Same shape as
        /// <see cref="ApplyCheckpointDestroyedIds"/>. A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointDestroyedShedIds(IReadOnlyList<string> destroyedIds)
        {
            if (destroyedIds == null || destroyedIds.Count == 0) return;

            foreach (MowerHutch h in Registered)
            {
                if (h == null || !h.IsAlive || string.IsNullOrEmpty(h.Id)) continue;
                for (int i = 0; i < destroyedIds.Count; i++)
                {
                    if (destroyedIds[i] != h.Id) continue;
                    h.ApplyCheckpointDestroyed();
                    break;
                }
            }
        }

        /// <summary>Register a Replicator by the id of the area it was authored into (MV-703) —
        /// <see cref="MaxWorlds.Arena.WorldRunner"/> calls this once per built Replicator, the same
        /// "known by the time anything reads it" ordering <see cref="Register"/> above guarantees for
        /// sheds.</summary>
        public static void RegisterReplicator(Replicator replicator, string areaId)
        {
            if (replicator == null || ReplicatorsRegistered.Contains(replicator)) return;
            ReplicatorsRegistered.Add(replicator);
            ReplicatorsStanding.Add(replicator);
            ReplicatorArea[replicator] = areaId;
        }

        /// <summary>Report a Replicator destroyed. Idempotent, same shape as <see cref="ReportDestroyed"/>.</summary>
        public static void ReportReplicatorDestroyed(Replicator replicator)
        {
            if (replicator == null) return;
            ReplicatorsStanding.Remove(replicator);
        }

        /// <summary>Every Replicator registered this run, standing or destroyed (MV-855) — what a
        /// <c>replicators-destroyed:all</c> gate's own "counts every Replicator" rule actually means:
        /// whichever ones THIS run registered, not every area a design file happens to author one into
        /// (a fixture/test census that only registers a subset must resolve "all" against that same
        /// subset, or a partial-census test could never isolate one gate's condition from another's).</summary>
        public static IReadOnlyList<Replicator> RegisteredReplicators => ReplicatorsRegistered;

        /// <summary>The area id <paramref name="replicator"/> was registered into (MV-855) — the same id
        /// <see cref="MaxWorlds.Arena.WorldRunner"/> passed to <see cref="RegisterReplicator"/>, exposed
        /// so a caller (the map screen, resolving a marker's blink state) can ask "which area is this
        /// live Replicator in" without re-deriving it from the entity id string. Null if never registered.</summary>
        public static string AreaIdOf(Replicator replicator) =>
            replicator != null && ReplicatorArea.TryGetValue(replicator, out string areaId) ? areaId : null;

        /// <summary>How many Replicators are standing right now (MV-774) — what
        /// <see cref="MaxWorlds.Arena.StormdrainFlood"/>'s own "every live Replicator adds to the rate"
        /// rule reads, so the "REPLICATORS n/25" counter finally means something beyond a label.</summary>
        public static int ReplicatorsAlive => ReplicatorsStanding.Count;

        /// <summary>True once every Replicator this run has is down. False in a run with none — same
        /// "never cleared, just never had a source" convention as <see cref="AllDown"/> (MV-703 AC1).</summary>
        public static bool AllReplicatorsDestroyed =>
            ReplicatorsRegistered.Count > 0 && ReplicatorsStanding.Count == 0;

        /// <summary>True once every Replicator authored into each of <paramref name="areaIds"/> is
        /// destroyed. An EMPTY list resolves TRUE (MV-703 — the opposite of the shed helpers' "nothing to
        /// clear" trap: <see cref="MapValidation"/> only lets an empty list ship if the design meant it,
        /// so there is nothing here to wait on). Ignores any standing replicator outside the named areas.</summary>
        public static bool ReplicatorsDestroyedInAreas(IReadOnlyList<string> areaIds)
        {
            if (areaIds == null || areaIds.Count == 0) return true;

            foreach (string areaId in areaIds)
                foreach (Replicator r in ReplicatorsStanding)
                    if (ReplicatorArea.TryGetValue(r, out string a) && a == areaId) return false;

            return true;
        }

        /// <summary>The stable ids (<see cref="Replicator.Id"/>) of every registered Replicator this run
        /// has already reported destroyed (MV-776) — what a mid-run checkpoint capture persists so a
        /// resume can re-apply the exact same destroyed set onto the fresh instances a level rebuild
        /// creates. A Replicator whose <see cref="Replicator.Id"/> was never stamped (a hand-built test
        /// fixture that skips <see cref="Replicator.SetId"/>) is simply left out.</summary>
        public static string[] DestroyedReplicatorIds()
        {
            var ids = new List<string>();
            foreach (Replicator r in ReplicatorsRegistered)
                if (r != null && !ReplicatorsStanding.Contains(r) && !string.IsNullOrEmpty(r.Id))
                    ids.Add(r.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded destruction (MV-776) onto whichever
        /// currently-registered, still-alive Replicators carry a matching <see cref="Replicator.Id"/> —
        /// called once by <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME, after the
        /// level's own Replicators have already registered themselves alive (the ordinary build). Silent
        /// by design (<see cref="Replicator.ApplyCheckpointDestroyed"/>): this is restoring history, not
        /// scoring a fresh kill, so no pickup drop or HUD signal fires. A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointDestroyedIds(IReadOnlyList<string> destroyedIds)
        {
            if (destroyedIds == null || destroyedIds.Count == 0) return;

            foreach (Replicator r in ReplicatorsRegistered)
            {
                if (r == null || !r.IsAlive || string.IsNullOrEmpty(r.Id)) continue;
                for (int i = 0; i < destroyedIds.Count; i++)
                {
                    if (destroyedIds[i] != r.Id) continue;
                    r.ApplyCheckpointDestroyed();
                    break;
                }
            }
        }
    }
}
