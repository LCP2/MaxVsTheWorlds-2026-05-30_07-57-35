using UnityEngine;
using UnityEngine.SceneManagement;
using MaxWorlds.Arena;
using MaxWorlds.Save;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Abandon the live run and return to the Home/save-slot screen (MV-257) — the same effect the
    /// HUD's HOME button (YT-191) already had, now shared so every pause-style screen (Settings,
    /// Weapons) can offer the same one-tap way out, not just the HUD underneath them. A profile
    /// carries no mid-run state, so this just drops the active slot and reloads the scene; the
    /// reload re-runs <see cref="MaxWorlds.Core.SceneInstallers"/>, which reopens Home since it now
    /// finds no active slot. The personal best is unaffected — it only banks on a run actually
    /// finishing, not on bailing out early.
    /// </summary>
    public static class RunFlow
    {
        public static void QuitToMenu()
        {
            SaveSystem.ActiveSlot = -1;
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        /// <summary>NEXT WORLD (MV-687): the active slot's <c>WorldIndex</c> has already been advanced
        /// by <see cref="MaxWorlds.Save.SaveSystem.RecordResult"/> by the time this is wired up (the
        /// Result screen only shows after a Victory seals), so reloading the scene is enough —
        /// <see cref="MaxWorlds.Arena.BackyardPath"/> resolves the new world from the save on its own
        /// next <c>Awake</c>.
        ///
        /// MV-964: the door/corridor walk itself has already happened by the time the Result card (and
        /// this button) ever shows — <see cref="MaxWorlds.VFX.WorldFinaleGate"/> opens the door and
        /// <see cref="MaxWorlds.Intro.WorldJoinSequence"/> walks Max through it the instant the world's
        /// last boss dies, well before Victory seals. All this does is tell the DESTINATION world's own
        /// boot that an arrival shell/walk-in is pending, then reload — exactly the plain reload every
        /// other advance already used, generalised off <see cref="WorldTransitions"/> instead of a
        /// World-1-only hardcode (MV-845's <c>LeavingWorldOneForTwo</c>).</summary>
        public static void StartNextWorld()
        {
            Time.timeScale = 1f;
            // MV-841: the Result screen's clock/kill count cover one world each — the next world
            // starts its own tally, not a carry from the one just cleared.
            RunProgressState.Reset();

            int slot = SaveSystem.ActiveSlot;
            if (slot >= 0)
            {
                int fromWorld = SaveSystem.Load(slot).WorldIndex - 1;
                if (WorldTransitions.For(fromWorld) != null) WorldTransitions.PendingArrivalFrom = fromWorld;
            }

            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }
    }
}
