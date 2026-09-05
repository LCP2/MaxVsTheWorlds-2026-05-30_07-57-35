using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-510 round 2 (Lee, 2026-08-21) - the ticket's own named defect: "the cell symbol overlaps
    /// with the cell count." Root cause was a pivot bug in HudController.BuildPowerCellCounter:
    /// the cell icon's RectTransform pivot was left-edge (0, 0.5) but its anchoredPosition.x was
    /// computed as though the pivot were centred (8 + CellCounterIconSize * 0.5f), so the icon's
    /// actual left edge landed 22px further right than intended and ate into the text's reserved
    /// inset - worse still on a pickup, when UpdateDrops scales the icon up to 1.35x and, because
    /// the pivot was the left edge, it grew rightward, straight through the digits. The fix centres
    /// the pivot (so anchoredPosition.x means what the original author intended) and derives the
    /// text's reserved inset from the icon's own max pop-scale geometry instead of a fixed constant.
    /// This is the sole new test this round adds (MV-465 Rule 1) - both the resting and full-pop
    /// states are asserted, since testing only the resting state is exactly what let the original
    /// bug ship unnoticed.
    /// </summary>
    public sealed class HudCellIconPivotOverlapTests
    {
        [Test]
        public void CellIconAndCellCountNeverOverlapAtRestOrFullPop()
        {
            PickupWallet.Reset();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                PickupWallet.SetPowerCells(12); // distinctive two-digit value, matches the ticket's own repro

                var iconRect = FindImage(hudGo, "Part Icon").rectTransform;
                var textRect = FindText(hudGo, "Parts").rectTransform;

                AssertNoHorizontalOverlap(iconRect, textRect, "at rest (_cellPop = 0)");

                // Full pop: UpdateDrops scales the icon to 1 + CellIconPopScaleDelta (1.35x) when
                // _cellPop is freshly banked (1f). Drive it directly - LateUpdate never fires in
                // EditMode - the same private-field/private-method pattern this suite already uses
                // elsewhere for per-frame HUD state.
                SetPrivateField(hud, "_cellPop", 1f);
                InvokeLifecycleWithArg(hud, "UpdateDrops", 0f);

                AssertNoHorizontalOverlap(iconRect, textRect, "at full pop (_cellPop = 1, icon scaled 1.35x)");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                PickupWallet.Reset();
            }
        }

        private static void AssertNoHorizontalOverlap(RectTransform icon, RectTransform text, string when)
        {
            var iconCorners = new Vector3[4];
            var textCorners = new Vector3[4];
            icon.GetWorldCorners(iconCorners);
            text.GetWorldCorners(textCorners);

            float iconMaxX = iconCorners[2].x; // top-right
            float textMinX = textCorners[0].x; // bottom-left

            Assert.That(iconMaxX, Is.LessThanOrEqualTo(textMinX),
                $"the cell icon's world rect must not overlap the cell-count text's world rect {when} - " +
                $"icon max x {iconMaxX}, text min x {textMinX}");
        }

        private static Image FindImage(GameObject go, string name)
        {
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                if (img.name == name) return img;
            Assert.Fail($"no Image named '{name}' found");
            return null;
        }

        private static Text FindText(GameObject go, string parentName)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name != parentName) continue;
                var t = rt.GetComponentInChildren<Text>();
                if (t != null) return t;
            }
            Assert.Fail($"no Text found under a rect named '{parentName}'");
            return null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(f, Is.Not.Null, $"expected a private field '{fieldName}'");
            f.SetValue(target, value);
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }

        private static void InvokeLifecycleWithArg(Object component, string methodName, object arg)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, new[] { arg });
        }
    }
}
