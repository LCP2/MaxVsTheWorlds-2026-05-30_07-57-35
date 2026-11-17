using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1388 (the one new test): on the W4 (Reef) RIG board the CAPTURE branch's child node p_tcap
    /// (how many robots one capture converts / the live ally cap) was labelled "HOLD"; it must read
    /// "SLOTS", like the Sentinels' node. Asserts the resolved on-screen label text of the built board.
    /// </summary>
    public sealed class MV1388ReefCaptureNodeLabelTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
        }

        [Test]
        public void ReefBoard_TcapNodeLabelReadsSlots()
        {
            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Reef));
            RigState.RestoreSnapshot(
                new Dictionary<string, int> { { "p_trp", 1 }, { "p_tcap", 1 } },
                new[] { "PRIMARY" });
            _go = new GameObject("WeaponsScreen");
            var screen = _go.AddComponent<WeaponsScreen>();
            screen.Open();
            screen.RebuildBoard();

            var label = screen.NodeLabel("p_tcap");
            Assert.That(label, Is.Not.Null, "fixture: p_tcap node must be built on the Reef board");
            Assert.That(label.text, Is.EqualTo("SLOTS"));
        }
    }
}
