using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1097 (Lee, device, World 1 area 18): "The shed turrets are clearly doing damage but there is
    /// nothing coming out of them to tell Max what's happening" — his health dropped 217 -&gt; 98 beside a
    /// hutch with two red laser turrets and no shot, beam or flash on screen. Root cause (read in code):
    /// <c>ShedFitting.TickBeam</c> applied <c>LaserDps * dt</c> to whatever the target's LIVE position
    /// was every tick, with no beam geometry test at all and nothing ever drawn — undodgeable, and
    /// invisible.
    ///
    /// This one test (CC_AUTONOMY Rule 1) builds a real shed with two Laser fittings through the real map
    /// path (<c>MapRuntime.Build</c>) and ticks each independently through a full attack, pinning every
    /// AC1 sub-check in one method: the Telegraph-phase aim line's resolved width and endpoints, the
    /// Beam-phase line's resolved width, the line disabling once Beam ends, full damage to a target that
    /// stays on the locked line, and zero damage to a target that side-steps 2 m off the LOCKED line the
    /// instant Beam begins (proving the fix is a committed beam geometry test, not a re-aimed one).
    ///
    /// Must fail on base commit d30d293, where <c>ShedTurretRig</c> never built or drew anything for a
    /// Laser fitting (so no LineRenderer exists to read) and <c>TickBeam</c> re-read the target's live
    /// position every tick (so the side-step would still take full damage, not zero).
    /// </summary>
    public sealed class MV1097ShedLaserBeamTests
    {
        private const float Dt = 1f / 60f;

        private static WorldConfig TwoLaserFittingsWorld() => new WorldConfig
        {
            world = "Test World",
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", role = "shed", hasShed = true,
                    origin = new WorldAreaOrigin { x = -15f, z = 0f },
                    size = new WorldAreaSize { w = 30f, d = 30f },
                    shedFittings = "laser",
                    shedFittingCount = 2,
                    sheds = new[] { new WorldShed { x = 0f, z = 15f } },
                },
                new WorldArea
                {
                    id = "boss", role = "boss+exit",
                    origin = new WorldAreaOrigin { x = -15f, z = 30f },
                    size = new WorldAreaSize { w = 30f, d = 20f },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                },
            },
        };

        /// <summary>Minimal stub so damage can be observed without a live PlayerController — same idiom
        /// <c>Mv912ShedDamagesSentinelsTests.DamageRecorder</c> uses.</summary>
        private sealed class DamageRecorder : IDamageable
        {
            public float TotalDamage;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => TotalDamage += info.Amount;
        }

        /// <summary>Ticks <paramref name="fitting"/> until its own LineRenderer shows the Beam-phase full
        /// width, asserting the Telegraph-phase line's resolved width/endpoints the first time it is seen
        /// at telegraph width; then lets the caller react to the just-started beam
        /// (<paramref name="onBeamJustStarted"/>) and ticks until the line disables again. A safety-capped
        /// poll rather than a precomputed tick count, so it is robust to exactly where the ticket's own
        /// 0.5 s / 1.1 s timings land relative to <see cref="Dt"/>.</summary>
        private static void AdvanceThroughLaserAttack(ShedFitting fitting, Vector3 targetPosDuringTelegraph,
            System.Action onBeamJustStarted)
        {
            LineRenderer line = null;
            bool telegraphChecked = false;
            bool beamStarted = false;
            int guard = 0;

            while (!beamStarted)
            {
                Assert.Less(guard++, 200, "the laser fitting never reached Beam phase within a generous tick budget");
                fitting.Tick(Dt);
                line ??= fitting.LaserBeamLineForTests;
                if (line == null) continue;

                if (!telegraphChecked && line.enabled && line.widthMultiplier < 0.15f)
                {
                    telegraphChecked = true;
                    Vector3? barrelTip = fitting.LaserBarrelTipForTests;
                    Assert.IsTrue(barrelTip.HasValue, "the fitting built no barrel tip to measure the aim line from");
                    Assert.That(line.widthMultiplier, Is.InRange(0.03f, 0.08f),
                        $"Telegraph aim line resolved {line.widthMultiplier:F3} m wide, outside the ticket's 0.03-0.08 m window");
                    Assert.That(Vector3.Distance(line.GetPosition(0), barrelTip.Value), Is.LessThanOrEqualTo(0.2f),
                        "Telegraph aim line must start within 0.2 m of the barrel tip");
                    Assert.That(Vector3.Distance(line.GetPosition(1), targetPosDuringTelegraph), Is.LessThanOrEqualTo(0.3f),
                        "Telegraph aim line must end within 0.3 m of the target");
                }

                if (line.enabled && line.widthMultiplier >= 0.18f)
                {
                    beamStarted = true;
                    Assert.That(line.widthMultiplier, Is.GreaterThanOrEqualTo(0.18f),
                        $"Beam-phase line resolved only {line.widthMultiplier:F3} m wide, under the ticket's 0.18 m floor");
                }
            }
            Assert.IsTrue(telegraphChecked, "the Telegraph-phase aim line was never observed enabled at telegraph width");

            onBeamJustStarted?.Invoke();

            guard = 0;
            while (line.enabled)
            {
                Assert.Less(guard++, 200, "the beam line never disabled after Beam phase should have ended");
                fitting.Tick(Dt);
            }
        }

        [Test]
        public void LaserTurret_DrawsTelegraphAndBeam_DamagesOnTheLockedLine_MissesASidestep()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(TwoLaserFittingsWorld(), out MapData map, out string reason), reason);

            var root = new GameObject("MV1097 Probe Root");
            var onLineTargetGo = new GameObject("OnLineTarget");
            var sidestepTargetGo = new GameObject("SidestepTarget");
            try
            {
                // MowerHutch.Build runs in Awake, which AddComponent never fires in Edit mode — same
                // precedent as MV547ShedFittingTests/Mv912ShedDamagesSentinelsTests/MV1058ShedTurretTests.
                // Without this, MowerHutch.IsAlive reads false (its _health is still null) and every
                // fitting's own Tick() would KillWithShed() itself before ever attacking.
                LogAssert.ignoreFailingMessages = true;

                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("a1_shed", out GameObject shedGo), "the shed actor did not build");

                var hutch = shedGo.GetComponent<MowerHutch>();
                Assert.IsNotNull(hutch, "the shed carries no MowerHutch");
                typeof(MowerHutch).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(hutch, null);

                ShedFitting[] fittings = shedGo.GetComponentsInChildren<ShedFitting>();
                Assert.AreEqual(2, fittings.Length, "expected two laser fittings to drive independently");
                Vector3 hutchPos = shedGo.transform.position;

                // --- Scenario 1 (AC1): a stationary target on the locked line takes the full 18 x 1.1 s
                // dose, +-10%. ---
                ShedFitting onLineFitting = fittings[0];
                Vector3 away1 = onLineFitting.transform.position - hutchPos; away1.y = 0f;
                away1 = away1.sqrMagnitude > 1e-6f ? away1.normalized : Vector3.forward;
                onLineTargetGo.transform.position = onLineFitting.transform.position + away1 * 5f;

                var onLineRecorder = new DamageRecorder();
                onLineFitting.SetTarget(onLineTargetGo.transform, onLineRecorder);
                AdvanceThroughLaserAttack(onLineFitting, onLineTargetGo.transform.position, onBeamJustStarted: null);

                const float expectedDose = 18f * 1.1f;
                Assert.That(onLineRecorder.TotalDamage, Is.EqualTo(expectedDose).Within(expectedDose * 0.1f),
                    $"a stationary target on the locked beam must take ~{expectedDose:F2} damage (+-10%), took {onLineRecorder.TotalDamage:F2}");

                // --- Scenario 2 (AC1): a target that steps 2 m off the LOCKED line the instant Beam
                // begins must take nothing, even though it is still in range and sighted — proves the
                // damage test is against the committed geometry, not a re-aimed live direction. ---
                ShedFitting sidestepFitting = fittings[1];
                Vector3 away2 = sidestepFitting.transform.position - hutchPos; away2.y = 0f;
                away2 = away2.sqrMagnitude > 1e-6f ? away2.normalized : Vector3.forward;
                sidestepTargetGo.transform.position = sidestepFitting.transform.position + away2 * 5f;

                var sidestepRecorder = new DamageRecorder();
                sidestepFitting.SetTarget(sidestepTargetGo.transform, sidestepRecorder);
                AdvanceThroughLaserAttack(sidestepFitting, sidestepTargetGo.transform.position, onBeamJustStarted: () =>
                {
                    Vector3 lockedDir = sidestepFitting.LockedBeamDirectionForTests;
                    Vector3 perpendicular = Vector3.Cross(Vector3.up, lockedDir).normalized;
                    sidestepTargetGo.transform.position += perpendicular * 2f;
                });

                Assert.That(sidestepRecorder.TotalDamage, Is.EqualTo(0f),
                    "a target that side-steps 2 m off the locked beam the instant Beam begins must take no damage");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(onLineTargetGo);
                Object.DestroyImmediate(sidestepTargetGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
