using System;

namespace MaxWorlds.Save
{
    /// <summary>
    /// One profile's payload (YT-218). A slot is a PLAYER, not a paused game: no mid-run state
    /// (position, installed parts, wallet contents) survives between runs — picking a profile
    /// always starts a fresh run. What persists is the player's identity and their best result,
    /// so two brothers sharing a device each keep their own progress. <c>[Serializable]</c> and
    /// fields-only so <c>JsonUtility</c> can round-trip it with no custom converter.
    ///
    /// Seams left deliberately unbuilt for the economy tickets: banked power cells and owned
    /// weapons will live here once those systems exist — don't add fields for them yet.
    /// </summary>
    [Serializable]
    public sealed class SaveSlotData
    {
        /// <summary>False for an untouched slot — the Home screen shows "Empty" and offers New Game.</summary>
        public bool HasData;

        /// <summary>The profile's own name, shown on its slot card. Defaults to a per-slot label
        /// until a rename UI exists.</summary>
        public string DisplayName = string.Empty;

        /// <summary>The best peak Domination % (0..1, <c>DifficultyDirector.Normalized</c>) this
        /// profile has ever reached across any run — the score hooks arrive with YT-209.</summary>
        public float PersonalBestNormalized;
    }
}
