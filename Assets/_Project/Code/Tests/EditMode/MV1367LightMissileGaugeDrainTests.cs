using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1367 — World 5's LIGHT MISSILE tank regenerated between every volley (55/s against 2.7/ball),
    /// so the gauge over Max never visibly drained. Resolved values only (Tier 2): the gauge reading
    /// after held fire, the time to empty for both loadouts, and the gauge fill colour.
    ///
    /// Fails on base commit ea74fe6: after 10 s of held fire at the starting loadout
    /// <c>PlayerHealth.PrimaryEnergyNormalized()</c> reads ~1.0 (regen out-paces the spend), so the
    /// "at or below 0.75" assertion fails.
    /// </summary>
    public sealed class MV1367LightMissileGaugeDrainTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo PlayerControllerAwake = typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo LightMissileAwake = typeof(LightMissile).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo LightMissileTick = typeof(LightMissile).GetMethod("Tick", NonPublicInstance);

        [SetUp] public void SetUp() { WeaponSystemState.Reset(); DevTuning.Reset(); DevMode.Reset(); }
        [TearDown] public void TearDown() { WeaponSystemState.Reset(); RigState.Reset(); RigBoard.ResetForTests(); DevTuning.Reset(); DevMode.Reset(); }

        [Test]
        public void LightMissileGaugeDrainsUnderHeldFire_MV1367()
        {
            GameObject go = null;
            try
            {
                go = new GameObject("MV1367 Player", typeof(CharacterController), typeof(PlayerController));
                PlayerControllerAwake.Invoke(go.GetComponent<PlayerController>(), null);
                var lm = go.GetComponent<LightMissile>();
                WeaponSystemState.ApplyLoadout(0, WeaponCatalog.PrimaryKind.LightMissile, SecondaryKind.WaterBalloon);
                LightMissileAwake.Invoke(lm, null);
                var health = go.AddComponent<PlayerHealth>();
                health.Initialize();

                DevMode.Enabled = true;
                DevMode.InfiniteEnergy = false;
                DevMode.AutoFire = true;
                for (int i = 0; i < 100; i++) LightMissileTick.Invoke(lm, new object[] { 0.1f });

                Assert.That(health.PrimaryEnergyNormalized(), Is.LessThanOrEqualTo(0.75f),
                    "10 s of held fire at the starting loadout must drain the gauge to <= 0.75");

                // Resolved gauge colour: the ball's base halo colour while LIGHT MISSILE is active.
                Color want = LightMissileBall.BaseHaloColor;
                Color got = PlayerHealth.ResolvedPrimaryGaugeColor();
                Assert.That(got.r, Is.EqualTo(want.r).Within(0.02f), $"gauge r {got} vs halo {want}");
                Assert.That(got.g, Is.EqualTo(want.g).Within(0.02f), $"gauge g {got} vs halo {want}");
                Assert.That(got.b, Is.EqualTo(want.b).Within(0.02f), $"gauge b {got} vs halo {want}");

                // Time to empty (from ticks), starting loadout: 25 s +/- 5.
                float tStart = 10f + SecondsToEmpty(lm);   // the first 10 s were already ticked above
                Assert.That(tStart, Is.EqualTo(25f).Within(5f), "starting loadout time-to-empty");

                // Full loadout (COUNT 5, RATE 5, CAPACITY 5): 10 s +/- 3.
                RigState.AcquireCap("p_cnt");
                RigState.AcquireCap("p_cap");
                RigState.AcquireCap("p_rof");
                for (int i = 0; i < 4; i++) { RigState.RaiseLevel("p_cnt"); RigState.RaiseLevel("p_rof"); RigState.RaiseLevel("p_cap"); }
                Assert.That(new[] { RigState.Level("p_cnt"), RigState.Level("p_rof"), RigState.Level("p_cap") },
                    Is.EqualTo(new[] { 5, 5, 5 }), "precondition: full loadout levels p_cnt/p_rof/p_cap");
                LightMissileAwake.Invoke(lm, null);
                float tFull = SecondsToEmpty(lm);
                Assert.That(tFull, Is.EqualTo(10f).Within(3f), "full loadout time-to-empty");
                Debug.Log($"MV-1367 time-to-empty: start={tStart:F1}s full={tFull:F1}s");
            }
            finally { if (go != null) Object.DestroyImmediate(go); }
        }

        /// <summary>Holds fire in 0.05 s ticks until the tank can no longer pay for a volley (or 60 s).</summary>
        private static float SecondsToEmpty(LightMissile lm)
        {
            float t = 0f;
            while (t < 60f && lm.EnergyNormalized * lm.EffectiveMaxEnergy >= LightMissile.VolleyCostFor(lm.BallsPerVolley))
            {
                LightMissileTick.Invoke(lm, new object[] { 0.05f });
                t += 0.05f;
            }
            return t;
        }
    }
}
