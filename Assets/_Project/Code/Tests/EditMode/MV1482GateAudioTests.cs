using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1482: gates were silent. One test (Rule 1), resolved counts only (Tier 2): counts the HudSignals gate cue requests
    /// SfxDirector maps 1:1 onto Cue.GateHit / GateStrained / GateOpened.
    ///   AC1 - 1.0 s of primary fire (0.1 s ticks) on an AreaGate requests GateHit 5-9 times (throttled to 0.12 s).
    ///   AC2 - over a full break GateStrained fires exactly twice (below 66% and below 33%) and GateOpened exactly once.
    ///   AC3 - a SubZoneGate opening requests GateOpened once.
    /// Fails on the base commit: the gate signals do not exist (the test does not compile).
    /// </summary>
    public sealed class MV1482GateAudioTests
    {
        private int _hit, _strained, _opened;
        private void OnHit(Vector3 p) => _hit++;
        private void OnStrained(Vector3 p) => _strained++;
        private void OnOpened(Vector3 p) => _opened++;

        [Test]
        public void GatesRequestHitStrainedAndOpenedCues()
        {
            HudSignals.GateHit += OnHit;
            HudSignals.GateStrained += OnStrained;
            HudSignals.GateOpened += OnOpened;
            var go = new GameObject("MV1482 AreaGate");
            var sub = new GameObject("MV1482 SubZoneGate");
            try
            {
                var gate = go.AddComponent<AreaGate>();
                typeof(AreaGate).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gate, null);

                var info = new DamageInfo(4f, go.transform.position, Vector3.forward, Team.Player, false, DamageSource.PrimaryWeapon);
                for (int i = 0; i < 10; i++) { gate.TakeDamage(info); gate.Tick(0.1f); }
                Assert.That(_hit, Is.InRange(5, 9), "GateHit requests over 1.0 s of fire");
                Assert.IsTrue(gate.IsAlive, "the gate must still be intact after 1 s");

                for (int i = 0; i < 200 && gate.IsAlive; i++) { gate.TakeDamage(info); gate.Tick(0.1f); }
                Assert.IsTrue(gate.IsOpen);
                Assert.AreEqual(2, _strained, "GateStrained over a full break");
                Assert.AreEqual(1, _opened, "GateOpened over a full break");

                _opened = 0;
                var door = sub.AddComponent<SubZoneGate>();
                door.Open();
                door.Open();
                Assert.AreEqual(1, _opened, "SubZoneGate opening requests GateOpened once");
            }
            finally
            {
                HudSignals.GateHit -= OnHit;
                HudSignals.GateStrained -= OnStrained;
                HudSignals.GateOpened -= OnOpened;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(sub);
            }
        }
    }
}
