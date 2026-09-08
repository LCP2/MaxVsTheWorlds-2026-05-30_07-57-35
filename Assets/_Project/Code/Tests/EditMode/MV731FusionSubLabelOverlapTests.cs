using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-731 — sole guard on the FORGE row's adjacent fusion sub-label boxes overlapping. Confirmed
    /// pre-existing and independent of MV-729: <see cref="RigBoardLayout"/>'s column-layout pass can
    /// place two adjacent fusions' resolved X far closer together than <c>rig_board.json</c>'s raw
    /// authored x values suggest (measured f_ovc/f_skr ~139px apart at standard aspect) while each
    /// sub-label box was a flat 280px wide, overlapping by ~141px. The fixed box width never depends on
    /// fusion state (<c>RefreshFusionNode</c> only ever mutates <c>.text</c>/colour, never
    /// <c>sizeDelta</c>), so a single state's boxes stand in for all three (locked/eligible/forged)
    /// without needing to drive every category's own unlock path through this test.
    /// Testing policy (MV-465): one new test, proven to fail on base commit 91d7173 (main HEAD before
    /// this ticket) — on that commit every fusion's sub-label box is a fixed 280px regardless of its
    /// neighbour's resolved X, so this test's own Rect.Overlaps assertion for the f_ovc/f_skr pair fails
    /// there; the fix comment quotes that failure.
    /// </summary>
    public sealed class MV731FusionSubLabelOverlapTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
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
        }

        [Test]
        public void AdjacentForgeFusionSubLabelBoxesNeverOverlap()
        {
            _screen.Open();

            var fusions = RigBoardLayout.Fusions;
            Assert.That(fusions.Count, Is.GreaterThan(1), "fixture: FORGE row must carry more than one fusion to exercise adjacency");

            for (int i = 0; i < fusions.Count; i++)
            {
                var subA = _screen.FusionNodeSub(fusions[i].Id);
                Assert.That(subA, Is.Not.Null, $"{fusions[i].Id} built no sub-label");
                Rect rectA = SubLabelRect(fusions[i], subA);

                for (int j = i + 1; j < fusions.Count; j++)
                {
                    var subB = _screen.FusionNodeSub(fusions[j].Id);
                    Assert.That(subB, Is.Not.Null, $"{fusions[j].Id} built no sub-label");
                    Rect rectB = SubLabelRect(fusions[j], subB);

                    Assert.That(rectA.Overlaps(rectB), Is.False,
                        $"{fusions[i].Id}'s sub-label box ({rectA}) overlaps {fusions[j].Id}'s ({rectB}) — " +
                        $"resolved X gap is {Mathf.Abs(fusions[j].X - fusions[i].X):0.0}px");
                }
            }
        }

        /// <summary>The resolved box in <see cref="RigBoardLayout"/>'s own board-frame coordinates — a
        /// fusion node's pivot is anchored at exactly (<c>fusion.X</c>, <c>-fusion.Y</c>) and the
        /// sub-label is centred under it with no horizontal offset, so the sub-label's own resolved
        /// centre X equals <c>fusion.X</c> exactly.</summary>
        private static Rect SubLabelRect(RigFusionLayout fusion, UnityEngine.UI.Text sub)
        {
            float w = sub.rectTransform.sizeDelta.x;
            float h = sub.rectTransform.sizeDelta.y;
            return new Rect(fusion.X - w * 0.5f, fusion.Y, w, h);
        }
    }
}
