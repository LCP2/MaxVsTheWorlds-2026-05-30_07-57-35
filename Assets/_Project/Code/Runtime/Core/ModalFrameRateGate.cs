using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-574: the shared reference count every pause-on-open screen (HomeScreen, MapScreen,
    /// SettingsPanel, UpgradeScreen, WeaponsScreen, ResultScreen, WorldRunner's death freeze) enters on
    /// open and exits on close. <see cref="Time.timeScale"/> going to 0 stops the simulation but nothing
    /// stops rendering — the world, and any live portrait-stage camera, keeps drawing at full frame rate
    /// behind the modal. While at least one modal is open this halves that cost by dropping
    /// <see cref="Application.targetFrameRate"/> to <see cref="IdleFrameRate"/>; it restores to
    /// <see cref="ActiveFrameRate"/> only once every modal has exited.
    ///
    /// Reference-counted so two overlapping modals (e.g. WeaponsScreen open behind an UpgradeScreen
    /// draft-pick, MV-383) don't have the first Close() prematurely restore full rate while the second
    /// is still up. Same precedent as MV-506: always restore to the authored constant, never to a saved
    /// value — a screen destroyed mid-open (a scene swap, a test) must still leave the rate sane, which
    /// is why every call site's own <c>_open</c>-guarded <c>OnDestroy</c> also calls <see cref="Exit"/>.
    /// </summary>
    public static class ModalFrameRateGate
    {
        public const int ActiveFrameRate = 60;
        public const int IdleFrameRate = 30;

        private static int _openCount;

        public static void Enter()
        {
            _openCount++;
            Application.targetFrameRate = IdleFrameRate;
        }

        public static void Exit()
        {
            if (_openCount == 0) return;   // an unmatched Exit (e.g. a defensive OnDestroy) is a no-op
            _openCount--;
            if (_openCount == 0) Application.targetFrameRate = ActiveFrameRate;
        }

        /// <summary>Test isolation only — a real modal always balances its own Enter/Exit, so production
        /// code never calls this (zeroing mid-session would restore full rate while a real modal is
        /// still open). Many EditMode tests open a screen without going through its own Close()/
        /// OnDestroy tidy-up, which would otherwise leak this count upward across a whole batch run.</summary>
        public static void ResetForTests()
        {
            _openCount = 0;
            Application.targetFrameRate = ActiveFrameRate;
        }
    }
}
