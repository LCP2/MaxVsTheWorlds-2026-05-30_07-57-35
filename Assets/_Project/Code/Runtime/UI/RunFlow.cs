using UnityEngine;
using UnityEngine.SceneManagement;
using MaxWorlds.Intro;
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
        /// next <c>Awake</c>. Same reload mechanism as <see cref="QuitToMenu"/>, but the active slot is
        /// left set so the reload drops straight into the next run instead of reopening Home.
        ///
        /// MV-704: a <c>WorldIndex</c> of exactly 1 means the save just advanced OUT of World 1 — the
        /// one transition with a cinematic — so the reload is deferred to
        /// <see cref="WorldTransitionCinematic"/>'s hand-off instead of firing immediately. Every other
        /// advance (2, 3, ...) reloads straight away, same as before this ticket.</summary>
        public static void StartNextWorld()
        {
            Time.timeScale = 1f;
            if (LeavingWorldOneForTwo() && WorldTransitionCinematic.TryPlay(ReloadActiveScene)) return;
            ReloadActiveScene();
        }

        private static bool LeavingWorldOneForTwo()
        {
            int slot = SaveSystem.ActiveSlot;
            return slot >= 0 && SaveSystem.Load(slot).WorldIndex == 1;
        }

        private static void ReloadActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }
    }
}
