using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1398 — THE RIG's shape sprites were fixed-size bitmaps (e.g. <c>Ring(96, ...)</c>) stretched
    /// over larger rects, so on a 2556x1179 iPhone they rendered blurred. Every shape sprite must now be
    /// generated at (at least) the pixel size it is drawn at. Sole guard on that fix; do not cull.
    /// Asserts resolved values only: each Image's sprite texel rect against its resolved on-screen pixel
    /// rect (world corners with the canvas scaleFactor pinned to the real 1179/1080).
    /// </summary>
    public sealed class MV1398RigCrispPrimitivesTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            _go = new GameObject("WeaponsScreen");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
            CrispUi.Resolution = 1;
        }

        [Test]
        public void EveryRigShapeSpriteIsAtLeastNinetyPercentOfItsDrawnPixelSize_At2556x1179()
        {
            var screen = _go.AddComponent<WeaponsScreen>();
            screen.Open();
            const int w = 2556, h = 1179;
            screen.ApplyBoardScale((float)w / h, h);

            var canvas = screen.RootCanvas;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = h / 1080f;   // what match-by-height resolves to at this size

            var bad = new StringBuilder();
            int checkedCount = 0;
            var corners = new Vector3[4];
            foreach (var img in canvas.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || img.name == "Hit") continue;
                if (img.type != Image.Type.Simple && img.type != Image.Type.Filled) continue;
                var texel = img.sprite.rect;
                if (texel.width < 8f || texel.height < 8f) continue;   // 1-colour fills (Solid) carry no edge
                if (img.GetComponentInParent<Canvas>() != canvas) continue;
                if (!IsUnder(img.transform, "Board Root")) continue;

                img.rectTransform.GetWorldCorners(corners);
                float drawnW = Vector3.Distance(corners[0], corners[3]);
                float drawnH = Vector3.Distance(corners[0], corners[1]);
                if (drawnW < 4f || drawnH < 4f) continue;
                checkedCount++;

                float rw = texel.width / drawnW, rh = texel.height / drawnH;
                if (rw < 0.9f || rh < 0.9f)
                    bad.AppendLine($"{PathOf(img.transform)}: texture {texel.width}x{texel.height} vs drawn {drawnW:F0}x{drawnH:F0}px (ratio {Mathf.Min(rw, rh):F2})");
            }

            Assert.Greater(checkedCount, 10, "expected the RIG board to hold many shape sprites to measure");
            Assert.That(bad.ToString(), Is.Empty, "shape sprites drawn larger than their own texture:\n" + bad);
        }

        private static bool IsUnder(Transform t, string ancestorName)
        {
            for (var p = t; p != null; p = p.parent) if (p.name == ancestorName) return true;
            return false;
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; i < 3 && t != null; i++, t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
    }
}
