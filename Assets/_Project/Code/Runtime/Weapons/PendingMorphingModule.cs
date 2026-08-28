using System;
using System.Collections.Generic;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// A Morphing Module drawn with 2-3 candidates (MV-424) no longer force-opens THE RIG at pickup
    /// time — that ambushed the player with a full-screen, game-pausing modal mid-fight, the exact
    /// problem <see cref="AbilityCreditBank"/> was introduced to solve for shed credits (MV-358,
    /// "correcting MV-357's mid-fight modal"). MV-425 applies the same fix here: the candidates wait
    /// here until the player taps WEAPONS on their own schedule, and the HUD's cyan "module captured"
    /// badge (<see cref="MaxWorlds.UI.HudController"/>) is what tells them one is waiting. 0/1-candidate
    /// draws are unaffected — those never showed a screen anyway, so there's nothing to bank.
    /// Static/event-driven, same shape as <see cref="AbilityCreditBank"/>.
    ///
    /// MV-605 AC7: a FIFO queue, not a single slot — a second module collected before the first is
    /// opened used to silently overwrite it (<c>Set</c> replaced the one field outright). Now every
    /// bank enqueues, so two modules collected back to back both survive and <see cref="WeaponsScreen"/>
    /// runs a ceremony for each in turn, oldest first, without either one going missing.
    /// </summary>
    public static class PendingMorphingModule
    {
        private static readonly Queue<string[]> s_queue = new Queue<string[]>();

        /// <summary>At least one draft is banked and waiting for the player to open WEAPONS.</summary>
        public static bool HasPending => s_queue.Count > 0;

        /// <summary>How many separate draws are banked right now — test-only access, same idiom as
        /// <see cref="HasPending"/>.</summary>
        public static int PendingCount => s_queue.Count;

        /// <summary>Fired whenever a draft is banked, taken, or reset.</summary>
        public static event Action Changed;

        /// <summary>Bank a drawn candidate set (must be 2-3; callers with 0-1 candidates should resolve
        /// those directly instead of banking them). Enqueues — an already-banked draft is never
        /// overwritten.</summary>
        public static void Set(string[] candidateIds)
        {
            s_queue.Enqueue(candidateIds);
            Changed?.Invoke();
        }

        /// <summary>Hands the OLDEST banked candidates to the board and dequeues it — the caller
        /// (<see cref="MaxWorlds.UI.WeaponsScreen"/>) takes ownership of the ids from here. Returns
        /// null if nothing was pending.</summary>
        public static string[] Take()
        {
            if (s_queue.Count == 0) return null;
            var ids = s_queue.Dequeue();
            Changed?.Invoke();
            return ids;
        }

        /// <summary>Back to a fresh run's baseline: nothing pending. Test isolation and a new run.</summary>
        public static void Reset()
        {
            if (s_queue.Count == 0) return;
            s_queue.Clear();
            Changed?.Invoke();
        }
    }
}
