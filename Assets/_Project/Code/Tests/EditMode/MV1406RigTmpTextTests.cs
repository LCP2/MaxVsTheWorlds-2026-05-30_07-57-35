using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1406: every text component THE RIG builds (at 2556x1179) is a TextMeshProUGUI with the SDF font
    /// asset, never a legacy UnityEngine.UI.Text. Resolved-type assertion only. Sole guard on that swap; do not cull.
    /// </summary>
    public sealed class MV1406RigTmpTextTests
    {
        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
            CrispUi.Resolution = 1;
        }

        [Test]
        public void NoRigTextIsLegacyText_AndEveryRigTmpTextUsesTheSdfFont_At2556x1179()
        {
            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                screen.Open();
                const int w = 2556, h = 1179;
                screen.ApplyBoardScale((float)w / h, h);

                var bad = new StringBuilder();
                int legacy = 0, tmp = 0;
                foreach (var t in screen.RootCanvas.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                { legacy++; bad.AppendLine("legacy Text: " + t.name); }
                foreach (var t in screen.RootCanvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    tmp++;
                    if (t.font != CrispUi.TmpFont()) bad.AppendLine("TMP without HudFont SDF: " + t.name);
                }
                Assert.That(legacy, Is.EqualTo(0), bad.ToString());
                Assert.That(tmp, Is.GreaterThan(10), "the RIG must carry its text as TMP");
                Assert.That(bad.ToString(), Is.Empty);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
