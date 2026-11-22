using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-962: THE RIG's top bar used to lay out right-to-left CLOSE, QUIT TO MENU, PARTS — QUIT sat
    /// directly beside CLOSE, so a tap reaching for CLOSE risked hitting QUIT (abandons the run) instead.
    /// This is the ticket's own dedicated test file (testing policy MV-465, Rule 1) and must fail on the
    /// base commit, where QUIT is still CLOSE's immediate left neighbour and CLOSE still resolves at
    /// 104x56, not 220x92 — NOT YET RUN against the base commit: see the MV-962 hand-off comment for why
    /// (`cc-verify`/Unity batchmode could not be run to completion in this session).
    ///
    /// MV-1408 re-orders the bar to the approved phone mockup — left to right PARTS, settings gear, QUIT,
    /// CLOSE (X) — so QUIT is CLOSE's neighbour again by design; the test now pins that order, and the
    /// gap between the two is the spacing MV-1408's own test measures.
    /// </summary>
    public sealed class MV962RigTopBarOrderTests
    {
        [Test]
        public void TopBarOrderIsPartsGearClose()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();

            var go = new GameObject("WeaponsScreen");
            var screen = go.AddComponent<WeaponsScreen>();
            try
            {
                screen.Open();

                var close = FindRect(go, "Close Button");
                var parts = FindRect(go, "Parts Chip");
                var gear = FindRect(go, "Settings Button");
                Assert.That(gear, Is.Not.Null, "fixture: the settings gear must exist");
                Assert.That(close, Is.Not.Null, "fixture: CLOSE must exist");
                Assert.That(parts, Is.Not.Null, "fixture: the PARTS chip must exist");
                Assert.That(FindRect(go, "Quit Button"), Is.Null, "MV-1415: QUIT is no longer in the top bar");

                Rect closeRect = WorldRect(close);
                Rect partsRect = WorldRect(parts);

                Rect gearRect = WorldRect(gear);
                Assert.That(partsRect.xMax, Is.LessThanOrEqualTo(gearRect.xMin), "PARTS must sit entirely left of the settings gear");
                Assert.That(gearRect.xMax, Is.LessThanOrEqualTo(closeRect.xMin), "the settings gear must sit entirely left of CLOSE");
            }
            finally
            {
                Object.DestroyImmediate(go);
                PickupWallet.Reset();
                RigFusionState.Reset();
                RigState.Reset();
                WeaponSystemState.Reset();
            }
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
