using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1441: a SfxDirector torn down the way an EditMode test tears down (DestroyImmediate, no
    /// OnDisable) must not throw MissingReferenceException when a later test raises HudSignals cues.
    /// Fail-first: on 4236c9a-descended main the static HudSignals handlers survive the destroyed
    /// director and PlayCue/transform access throws.
    /// </summary>
    public sealed class MV1441AudioTeardownTests
    {
        private static void Invoke(object o, string m) =>
            typeof(SfxDirector).GetMethod(m, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null);

        private static SfxDirector Make()
        {
            var d = new GameObject("SfxDirector(MV1441)").AddComponent<SfxDirector>();
            Invoke(d, "Awake"); Invoke(d, "OnEnable");   // EditMode runs neither on AddComponent
            return d;
        }

        [Test]
        public void RaisingCuesAfterTeardown_DoesNotThrow()
        {
            // Case 1: director destroyed with its pool, still subscribed (no OnDisable in EditMode).
            var dead = Make();
            Object.DestroyImmediate(dead.gameObject);

            // Case 2: director alive but its pooled sources destroyed under it.
            var live = Make();
            foreach (var s in live.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(s);

            try
            {
                Assert.DoesNotThrow(() =>
                {
                    HudSignals.EmitBossEngaged("Test", 1);   // ducking cue, uses transform.position
                    HudSignals.EmitBossDefeated();
                    for (int i = 0; i < 10; i++)            // DamageDealt cap is 6: past the voice limit
                    {
                        HudSignals.EmitDamage(Vector3.zero, 1f);
                        Thread.Sleep(35);                    // clear the 30 ms retrigger gate
                    }
                    Invoke(live, "Update");
                });
            }
            finally
            {
                Invoke(live, "OnDisable");
                Object.DestroyImmediate(live.gameObject);
                AudioBus.ApplyDuck(0f);
            }
        }
    }
}
