using UnityEngine;
using UnityEngine.InputSystem;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Turns <see cref="DevMode"/> on and drives it (YT-60).
    ///
    /// Why this exists: the slice kills Max in ~10-15 seconds with zero robots destroyed, which
    /// makes the entire art queue unreviewable — you die before the effects you're supposed to be
    /// judging have happened. This removes the survival pressure so the VFX can actually be seen
    /// and filmed.
    ///
    /// OFF by default. Two ways in:
    ///   * add <c>?dev=1</c> to the WebGL URL, or
    ///   * press Ctrl+Shift+D (deliberately obscure — not something a player finds by accident).
    ///
    /// With it off, nothing here changes any behaviour: the guards in PlayerHealth and WaterBlaster
    /// read false and the game plays exactly as it shipped.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevModeController : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<DevModeController>() != null) return;
            new GameObject("DevMode").AddComponent<DevModeController>();
        }

        private void Awake()
        {
            DevMode.Reset();
            if (UrlRequestsDevMode()) Enable("URL ?dev=1");
        }

        private void OnDestroy() => DevMode.Reset();

        /// <summary>WebGL hands us the page URL, so the query string is the natural switch — Lee can
        /// turn this on from the play link without a rebuild.</summary>
        private static bool UrlRequestsDevMode()
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return false;
            url = url.ToLowerInvariant();
            return url.Contains("dev=1") || url.Contains("dev=true");
        }

        private static void Enable(string why)
        {
            DevMode.Enabled = true;
            Debug.Log($"[DevMode] ON ({why}) — Max is invincible, blaster energy is infinite.");
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            bool chord = (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) &&
                         (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

            if (chord && kb.dKey.wasPressedThisFrame)
            {
                if (DevMode.Enabled) { DevMode.Reset(); Debug.Log("[DevMode] OFF"); }
                else Enable("Ctrl+Shift+D");
            }

            if (!DevMode.Enabled) return;

            if (kb.f2Key.wasPressedThisFrame) DevMode.AutoFire = !DevMode.AutoFire;
            if (kb.f3Key.wasPressedThisFrame) DevMode.PauseSpawns = !DevMode.PauseSpawns;
            if (kb.f4Key.wasPressedThisFrame) ClearEnemies();

            ApplySpawnPause();
        }

        /// <summary>The spawner is a plain MonoBehaviour, so pausing it needs nothing from the
        /// gameplay stream — just switch it off.</summary>
        private void ApplySpawnPause()
        {
            foreach (var spawner in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
            {
                spawner.enabled = !DevMode.IsSpawnPaused;
            }
        }

        /// <summary>Clear the field so a single effect can be filmed against an empty arena.
        /// Deactivates rather than kills: a kill would fire the whole XP/score/VFX chain and taint
        /// whatever you were trying to look at.</summary>
        private void ClearEnemies()
        {
            int n = 0;
            foreach (var e in FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                e.gameObject.SetActive(false);
                n++;
            }
            Debug.Log($"[DevMode] cleared {n} enemies");
        }

        private void OnGUI()
        {
            if (!DevMode.Enabled) return;

            const float w = 430f;
            var rect = new Rect(Screen.width - w - 12f, 12f, w, 96f);
            GUI.color = new Color(1f, 0.9f, 0.3f);
            GUI.Box(rect, "");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, w - 20f, 22f),
                "DEV MODE — invincible, infinite energy");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 28f, w - 20f, 22f),
                $"F2 auto-fire: {(DevMode.AutoFire ? "ON" : "off")}   " +
                $"F3 spawns: {(DevMode.PauseSpawns ? "PAUSED" : "on")}   F4 clear");
            GUI.Label(new Rect(rect.x + 10f, rect.y + 50f, w - 20f, 22f),
                "Ctrl+Shift+D to turn off");
        }
    }
}
