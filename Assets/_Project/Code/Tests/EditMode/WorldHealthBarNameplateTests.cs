using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-312: the Gunner's nameplate shipped reading "RUSHER". The root cause was never a wrong name
    /// mapping — <see cref="MaxWorlds.Enemies.RobotEnemy.ReadoutName"/> already switched on
    /// <c>Kind</c> correctly. It was that <see cref="WorldHealthBar"/> read that name exactly ONCE, in
    /// <c>Build()</c>, which runs from <c>RobotEnemy.Awake()</c> — before <c>EnemySpawner</c> calls
    /// <c>Apply()</c> to stamp the real archetype. Every freshly created robot's bar froze on whatever
    /// kind Awake saw, which is always the Rusher default, and a pooled body never got a second chance
    /// to fix it. This pins the fix: the name has to be RE-READ, not just correctly computed.
    /// </summary>
    public sealed class WorldHealthBarNameplateTests
    {
        private sealed class FakeUnit : MonoBehaviour, IHealthReadout
        {
            public string Name = "RUSHER";
            public float HealthNormalized => 1f;
            public float HealthCurrent => 10f;
            public string ReadoutName => Name;
            public bool IsAlive => true;
        }

        private static void Refresh(WorldHealthBar bar)
        {
            var m = typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(bar, null);
        }

        private static UnityEngine.UI.Text NameTextOf(WorldHealthBar bar)
        {
            var f = typeof(WorldHealthBar).GetField("_nameText", BindingFlags.NonPublic | BindingFlags.Instance);
            return (UnityEngine.UI.Text)f.GetValue(bar);
        }

        [Test]
        public void ANameplate_UpdatesWhenTheUnitsKindChangesAfterAttach()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var unit = go.AddComponent<FakeUnit>();
                unit.Name = "RUSHER";   // Kind's default — what Awake sees before Apply() runs
                var bar = WorldHealthBar.Attach(go, unit, heightAboveCentre: 1.15f, worldWidth: 1.1f,
                                                alwaysShow: true);

                Assert.AreEqual("RUSHER", NameTextOf(bar).text);

                // Simulate EnemySpawner.CreateInstance calling Apply() right after Awake already
                // attached the bar — the exact sequence that shipped a Gunner reading "RUSHER".
                unit.Name = "GUNNER";
                Refresh(bar);

                Assert.AreEqual("GUNNER", NameTextOf(bar).text,
                    "the nameplate is still frozen on the kind Awake saw at Build() time — a Gunner " +
                    "pulled from the pool would read as a Rusher forever, which is MV-312's bug.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
