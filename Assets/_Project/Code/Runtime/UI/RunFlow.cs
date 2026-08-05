using UnityEngine;
using UnityEngine.SceneManagement;
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
    }
}
