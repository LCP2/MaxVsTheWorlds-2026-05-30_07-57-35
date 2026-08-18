using System;

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
    /// </summary>
    public static class PendingMorphingModule
    {
        private static string[] s_candidateIds;

        /// <summary>A 2-3 candidate draft is banked and waiting for the player to open WEAPONS.</summary>
        public static bool HasPending => s_candidateIds != null && s_candidateIds.Length > 0;

        /// <summary>Fired whenever a draft is banked, taken, or reset.</summary>
        public static event Action Changed;

        /// <summary>Bank a drawn candidate set (must be 2-3; callers with 0-1 candidates should resolve
        /// those directly instead of banking them).</summary>
        public static void Set(string[] candidateIds)
        {
            s_candidateIds = candidateIds;
            Changed?.Invoke();
        }

        /// <summary>Hands the banked candidates to the board and clears the pending state — the caller
        /// (<see cref="MaxWorlds.UI.WeaponsScreen"/>) takes ownership of the ids from here. Returns
        /// null if nothing was pending.</summary>
        public static string[] Take()
        {
            var ids = s_candidateIds;
            s_candidateIds = null;
            Changed?.Invoke();
            return ids;
        }

        /// <summary>Back to a fresh run's baseline: nothing pending. Test isolation and a new run.</summary>
        public static void Reset()
        {
            if (s_candidateIds == null) return;
            s_candidateIds = null;
            Changed?.Invoke();
        }
    }
}
