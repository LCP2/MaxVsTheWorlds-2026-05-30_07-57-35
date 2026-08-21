using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-510 AC2 — the ticket's own named defect (its whole Observation): the banked power-cell value
    /// rendered in two places on the gameplay HUD at once, the top-centre "4/20" readout
    /// (<c>BuildPowerCellCounter</c>) and a bare "4" chip under THE WEAPONS button's mark
    /// (the old <c>BuildRigCounter(..., above: false, ...)</c>). MV-510 deletes the bare chip and
    /// moves the icon+total readout into its slot instead, so the value now renders exactly once.
    /// </summary>
    public sealed class HudCellReadoutDedupeTests
    {
        [Test]
        public void ExactlyOneHudElementRendersThePowerCellValue()
        {
            PickupWallet.Reset();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                PickupWallet.SetPowerCells(17); // distinctive, unlikely to coincide with any other HUD default
                int cells = PickupWallet.PowerCells;
                int capacity = PickupWallet.Capacity;
                string bare = cells.ToString();
                string withCapacity = $"{cells}/{capacity}";

                var matches = hudGo.GetComponentsInChildren<Text>(true)
                    .Where(t => t.gameObject.activeInHierarchy && (t.text == bare || t.text == withCapacity))
                    .ToList();

                Assert.That(matches.Count, Is.EqualTo(1),
                    "the banked power-cell value must render in exactly one HUD element — found: " +
                    string.Join(", ", matches.Select(t => $"{(t.transform.parent != null ? t.transform.parent.name : "?")}/'{t.text}'")));
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                PickupWallet.Reset();
            }
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
