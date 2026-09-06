using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-681 — the Water Balloon Auto-Fire toggle used to differ ON vs OFF only by alpha on
    /// WaterBalloonColor (1.0 vs 0.4), so it read as a shade of the joystick beneath it rather than
    /// its own switch. This asserts the resolved size and the resolved, opaque ON/OFF background
    /// colors and label font size, driven through WeaponSystemState.WaterBalloonAutoFireEnabled
    /// rather than duplicating the literal colors. Sole guard on this fix; do not cull (MV-465).
    /// </summary>
    public sealed class MV681WaterBalloonAutoFireToggleTests
    {
        [Test]
        public void ToggleIsEnlargedWithDistinctOpaqueOnOffColors()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();

            // Water Balloon + its Auto-Fire upgrade (s_aut) acquired so the toggle is visible.
            // RestoreSnapshot bypasses the draft/reach gating RigState.AcquireCap enforces — the
            // same shortcut MV645HudLeftColumnTests/MV676HudPhoneAspectMarginTests use for fixtures.
            RigState.RestoreSnapshot(new Dictionary<string, int>
            {
                { "s_bal", 1 },
                { "s_aut", 1 },
            }, System.Array.Empty<string>());
            WeaponSystemState.RebuildAcquiredFromRigState();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            // OnEnable just subscribed OnAbilitiesChanged — fire it again so the toggle picks up
            // the snapshot above and shows itself.
            WeaponSystemState.RebuildAcquiredFromRigState();

            try
            {
                var root = FindRect(hudGo, "Water Balloon Auto-fire Toggle");
                Assert.That(root, Is.Not.Null, "fixture: the auto-fire toggle must exist");
                Assert.That(root.gameObject.activeInHierarchy, Is.True,
                    "fixture: the toggle must be visible once Auto-Fire is acquired");

                var bg = FindImage(root.gameObject, "BG");
                var label = root.GetComponentInChildren<Text>(true);
                Assert.That(bg, Is.Not.Null, "fixture: the toggle's BG image must exist");
                Assert.That(label, Is.Not.Null, "fixture: the toggle's label must exist");

                // AC1: enlarged pill.
                Assert.That(root.sizeDelta, Is.EqualTo(new Vector2(168f, 52f)),
                    "the toggle's resolved size must grow from 140x44 to 168x52");

                // AC4: label font size.
                Assert.That(label.fontSize, Is.EqualTo(20),
                    "the label's resolved font size must grow from 18 to 20");

                // AC2: ON-state background is opaque green, not an alpha-blended WaterBalloonColor.
                Assert.That(WeaponSystemState.WaterBalloonAutoFireEnabled, Is.True,
                    "fixture: auto-fire defaults to enabled");
                Assert.That(bg.color, Is.EqualTo(new Color(0.30f, 0.85f, 0.35f)).Using(ColorComparer.Instance),
                    "ON-state background must be opaque bright green, not WaterBalloonColor with alpha 1");
                Assert.That(label.text, Is.EqualTo("AUTO ON"));

                // AC3: OFF-state background is opaque muted red/grey.
                WeaponSystemState.WaterBalloonAutoFireEnabled = false;
                Assert.That(bg.color, Is.EqualTo(new Color(0.55f, 0.20f, 0.20f)).Using(ColorComparer.Instance),
                    "OFF-state background must be opaque muted red/grey, not WaterBalloonColor at alpha 0.4");
                Assert.That(label.text, Is.EqualTo("AUTO OFF"));
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                WeaponSystemState.Reset();
                RigState.Reset();
                RigFusionState.Reset();
                PickupWallet.Reset();
            }
        }

        private sealed class ColorComparer : IEqualityComparer<Color>
        {
            public static readonly ColorComparer Instance = new ColorComparer();
            public bool Equals(Color a, Color b) =>
                Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f &&
                Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - 1f) < 0.01f;
            public int GetHashCode(Color c) => 0;
        }

        private static Image FindImage(GameObject go, string name)
        {
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                if (img.name == name) return img;
            return null;
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
