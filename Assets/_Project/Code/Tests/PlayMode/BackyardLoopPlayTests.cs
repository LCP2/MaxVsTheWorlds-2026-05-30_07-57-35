using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Bosses;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Proves the core slice loop wiring (YT-38 QA): destroying the Mower Hutch must fire the HUD
    /// factory-destroyed signal (which ticks FACTORIES → arena → boss on the real HUD), open the
    /// boss gate it references, and engage Big Bermuda. Builds the real gameplay components and the
    /// factory→gate reference exactly as the scene does, but without loading the full scene (a
    /// scene load leaves RuntimeInitialize singletons behind that break other PlayMode tests).
    /// </summary>
    public sealed class BackyardLoopPlayTests
    {
        private GameObject _hutchGo, _gateGo, _bossGo;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _hutchGo, _gateGo, _bossGo })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingFactory_FiresHudSignal_OpensGate_EngagesBoss()
        {
            // Gate with a real collider (what physically blocks the corridor).
            _gateGo = new GameObject("SubZone Gate", typeof(BoxCollider), typeof(SubZoneGate));
            var gate = _gateGo.GetComponent<SubZoneGate>();

            // Boss — dormant until the factory dies (subscribes to FactoryDestroyed in OnEnable).
            _bossGo = new GameObject("Big Bermuda", typeof(BigBermudaBoss));
            var boss = _bossGo.GetComponent<BigBermudaBoss>();

            // Factory, with its gate reference wired the way the scaffold wires it.
            _hutchGo = new GameObject("Mower Hutch", typeof(MowerHutch));
            var hutch = _hutchGo.GetComponent<MowerHutch>();
            typeof(MowerHutch).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(hutch, gate);

            yield return null; // Awake/OnEnable/Start across all three

            bool factoryDestroyed = false;
            string engagedBoss = null;
            System.Action<Vector3> onFactory = _ => factoryDestroyed = true;
            System.Action<string, int> onBoss = (n, _) => engagedBoss = n;
            HudSignals.FactoryDestroyed += onFactory;
            HudSignals.BossEngaged += onBoss;

            try
            {
                Assert.IsTrue(hutch.IsAlive, "factory should start alive");

                hutch.TakeDamage(new DamageInfo(100000f, hutch.transform.position, Vector3.forward, Team.Player));
                for (int i = 0; i < 3; i++) yield return null;

                Assert.IsFalse(hutch.IsAlive, "factory should be destroyed by a lethal hit");
                Assert.IsTrue(factoryDestroyed, "FactoryDestroyed (the signal that ticks the HUD FACTORIES counter) never fired");
                Assert.AreEqual("BIG BERMUDA", engagedBoss, "boss did not engage after the factory fell");

                var col = _gateGo.GetComponent<Collider>();
                Assert.IsFalse(col.enabled, "boss gate collider should be disabled (open) after the factory dies");
            }
            finally
            {
                HudSignals.FactoryDestroyed -= onFactory;
                HudSignals.BossEngaged -= onBoss;
            }
        }
    }
}
