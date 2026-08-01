using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Water Balloon's landing splash (WV-241, spec §6a): "the splash should read as a satisfying
    /// water burst" and be sized so its ground ring genuinely shows the area it covers, not a fixed
    /// picture unrelated to the ability's real numbers.
    /// </summary>
    public sealed class WaterBalloonSplashVfxTests
    {
        [Test]
        public void EverySystemHasAMaterialAssigned_AndDoesNotSelfStart()
        {
            var go = new GameObject("splash-vfx-test");
            try
            {
                var vfx = go.AddComponent<WaterBalloonSplashVfx>();
                vfx.Init(radius: 1.1f);

                // The burst systems are deliberately unparented (so a splash stays put in the
                // world while Max keeps moving — same reasoning as WaterVfx's own splash/flash),
                // so they can't be found via GetComponentsInChildren; search the scene instead.
                var systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                    .Where(ps => ps.name.StartsWith("WaterBalloon")).ToArray();
                Assert.That(systems.Length, Is.GreaterThanOrEqualTo(2),
                    "expected at least the droplet burst and the impact flash");

                foreach (var ps in systems)
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    Assert.IsNotNull(r.sharedMaterial,
                        $"'{ps.name}' has no material — it would draw nothing in a build");
                    Assert.IsFalse(ps.main.playOnAwake, $"'{ps.name}' must not self-start");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void PlayingItShowsTheGroundRing()
        {
            var go = new GameObject("splash-vfx-ring-test");
            try
            {
                var vfx = go.AddComponent<WaterBalloonSplashVfx>();
                vfx.Init(radius: 1.1f);
                vfx.Play(new Vector3(3f, 0f, 2f));

                Assert.Greater(vfx.CurrentRingRadius, 0f,
                    "landing didn't show a ground ring — the splash's true extent isn't telegraphed");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SafeToPlayWithoutCallingInitFirst()
        {
            // Whoever wires the throw (WV-240) should not have to remember an init order.
            var go = new GameObject("splash-vfx-lazy-test");
            try
            {
                var vfx = go.AddComponent<WaterBalloonSplashVfx>();
                Assert.DoesNotThrow(() => vfx.Play(Vector3.zero));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SplashRadiusMatchesTheAbilitysRealSplashSize()
        {
            // Spec §6a: "an area ≈ 2× the large robot's footprint" — the VFX must actually be built at
            // that radius, not an arbitrary fixed size that happens to look OK.
            float radius = MaxWorlds.Weapons.AbilityTuning.WaterBalloonSplashRadius(
                MaxWorlds.Enemies.EnemyArchetype.Bruiser.ColliderRadius,
                MaxWorlds.Weapons.AbilityTuning.DefaultWaterBalloonSplashMult);

            var go = new GameObject("splash-vfx-size-test");
            try
            {
                var vfx = go.AddComponent<WaterBalloonSplashVfx>();
                vfx.Init(radius);
                vfx.Play(Vector3.zero);

                Assert.That(vfx.CurrentRingRadius, Is.LessThanOrEqualTo(radius + 1e-3f),
                    "the ring must never exceed the splash's authored radius");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
