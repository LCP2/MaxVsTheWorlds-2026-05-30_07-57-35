using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-758 — the LPPE's muzzle flash, impact flash/sparks, and the 4th-hit Shock beat.
    ///
    /// One [Test] method per CC_AUTONOMY.md's testing policy ("at most one new test per ticket"),
    /// covering the ticket's four ACs as separate assertions inside it — the same "one method,
    /// several assertions" shape MV600AsciiOnlyPlayerFacingTextTests already established for a
    /// multi-AC ticket: AC1 (one FireTick emits exactly one, short-lived muzzle flash), AC2 (the
    /// Shock hit's impact is a structurally DIFFERENT VfxBurst instance, not merely a bigger one),
    /// AC3 (sustained fire allocates nothing once warm), and AC4 (every magnitude LppeVfx introduces
    /// is a CombatVfxTuning value, never a literal above 1.0 in the emit path).
    ///
    /// Fails to compile before MV-758: <see cref="LppeVfx"/> and
    /// <see cref="CombatVfxTuning.LppeMuzzle"/>/<see cref="CombatVfxTuning.LppeImpact"/>/
    /// <see cref="CombatVfxTuning.LppeShockImpact"/> do not exist on that commit.
    /// </summary>
    public sealed class Mv758LppeVfxTests
    {
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserFireTick =
            typeof(PulseLaser).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LppeMuzzleFlashField =
            typeof(LppeVfx).GetField("_muzzleFlash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LppeImpactFlashField =
            typeof(LppeVfx).GetField("_impactFlash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LppeShockFlashField =
            typeof(LppeVfx).GetField("_shockFlash", BindingFlags.NonPublic | BindingFlags.Instance);

        [Test]
        public void OneFireTick_EmitsAShortMuzzleFlash_ShockImpactIsAStructurallyDistinctBurst_NothingAllocatesSustained_AndEveryMagnitudeIsTunable()
        {
            var laserGo = new GameObject("MV-758 PulseLaser probe");
            try
            {
                PulseLaser laser = laserGo.AddComponent<PulseLaser>();
                PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode

                var vfx = laserGo.GetComponent<LppeVfx>();
                Assert.IsNotNull(vfx,
                    "PulseLaser.Awake must attach an LppeVfx, the same 'resolve-or-attach' shape WaterBlaster uses for WaterVfx");

                // --- AC1: a FireTick produces exactly one muzzle VFX instance, lifetime <= 0.08s ---
                PulseLaserFireTick.Invoke(laser, null);
                Assert.AreEqual(1, vfx.MuzzleFlashEmitCount, "one FireTick must emit exactly one muzzle flash");

                var muzzleBurst = (VfxBurst)LppeMuzzleFlashField.GetValue(vfx);
                var muzzlePs = muzzleBurst.GameObject.GetComponent<ParticleSystem>();
                var muzzleParticles = new ParticleSystem.Particle[muzzlePs.particleCount];
                int muzzleCount = muzzlePs.GetParticles(muzzleParticles);
                Assert.That(muzzleCount, Is.GreaterThan(0), "the muzzle burst must actually emit a particle to read back");
                // MV-770 raised the muzzle flash from 0.08s to 0.14s alongside the bolt/rocket weight
                // pass — the invariant this guards (never smear into the next shot) is against the
                // FASTEST the LPPE can fire, PulseLaser.DefaultRateFloorInterval (a maxed RATE track),
                // not the old hardcoded 0.08s literal.
                for (int i = 0; i < muzzleCount; i++)
                {
                    Assert.That(muzzleParticles[i].startLifetime, Is.LessThanOrEqualTo(PulseLaser.DefaultRateFloorInterval),
                        "the muzzle flash must punctuate even the fastest (maxed RATE) cadence, not smear into the next shot");
                }

                // --- AC2: the Shock-carrying 4th hit's impact must be a structurally DIFFERENT
                // instance from a normal hit's impact, not merely a bigger burst on the same one. ---
                vfx.Impact(Vector3.zero, damage: 9f, isShockHit: false);
                vfx.Impact(Vector3.zero, damage: 9f, isShockHit: true);

                var normalFlash = (VfxBurst)LppeImpactFlashField.GetValue(vfx);
                var shockFlash = (VfxBurst)LppeShockFlashField.GetValue(vfx);
                Assert.AreNotSame(normalFlash.GameObject, shockFlash.GameObject,
                    "the Shock hit's flash must be its own instance, not the same burst a normal impact uses");

                var normalPs = normalFlash.GameObject.GetComponent<ParticleSystem>();
                var normalBuf = new ParticleSystem.Particle[normalPs.particleCount];
                int normalCount = normalPs.GetParticles(normalBuf);
                var shockPs = shockFlash.GameObject.GetComponent<ParticleSystem>();
                var shockBuf = new ParticleSystem.Particle[shockPs.particleCount];
                int shockCount = shockPs.GetParticles(shockBuf);
                Assert.That(normalCount, Is.GreaterThan(0), "the normal impact flash must actually emit a particle to read back");
                Assert.That(shockCount, Is.GreaterThan(0), "the Shock impact flash must actually emit a particle to read back");
                Assert.That(shockBuf[0].startSize, Is.GreaterThan(normalBuf[0].startSize),
                    "the Shock flash must read visibly BIGGER than a normal impact flash, not just differently instanced");

                // --- AC3: sustained fire for 300 frames allocates nothing after warm-up. ---
                vfx.Muzzle(Vector3.zero, Vector3.forward); // warm-up, outside the measured block
                vfx.Impact(Vector3.zero, 9f, false);
                vfx.Impact(Vector3.zero, 9f, true);

                // Fully qualified rather than "using UnityEngine.TestTools.Constraints;" — that
                // namespace also declares an "Is" that collides with NUnit.Framework.Is used
                // throughout this file (CS0104). Must be a void statement lambda, not a
                // value-returning one — see Mv537PerfOverlayTests's identical note.
                Assert.That(() =>
                {
                    for (int i = 0; i < 300; i++)
                    {
                        vfx.Muzzle(Vector3.zero, Vector3.forward);
                        vfx.Impact(Vector3.zero, 9f, (i % 4) == 3);
                    }
                }, UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not));

                // --- AC4: every magnitude LppeVfx introduces is reachable from CombatVfxTuning; no
                // literal above 1.0 appears in the emit path. ---
                string vfxPath = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime", "VFX", "LppeVfx.cs");
                string source = File.ReadAllText(vfxPath);
                // "//[^\r\n]*" (not ".*") for the line-comment branch -- under Singleline (needed so
                // the block-comment branch's ".*?" can span multiple lines), a plain ".*" would match
                // through every newline to the end of the file, stripping all the real code after the
                // first "//" along with the comment.
                source = Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline);

                var emitCalls = Regex.Matches(source, @"\.Emit\((?<args>[^;]*?)\);", RegexOptions.Singleline);
                Assert.That(emitCalls.Count, Is.GreaterThan(0), "no .Emit( calls found -- the scan itself is broken");

                var numberInArgs = new Regex(@"(?<![\w.])(\d+\.\d+|\d+)f?(?![\w])");
                foreach (Match call in emitCalls)
                {
                    foreach (Match numberMatch in numberInArgs.Matches(call.Groups["args"].Value))
                    {
                        float value = float.Parse(numberMatch.Value.TrimEnd('f'), CultureInfo.InvariantCulture);
                        Assert.LessOrEqual(value, 1.0f,
                            $"literal '{numberMatch.Value}' found inside an Emit() call in LppeVfx.cs -- every " +
                            "magnitude above 1.0 must come from CombatVfxTuning, not a buried literal");
                    }
                }

                Assert.That(CombatVfxTuning.LppeImpact(20f).SparkCount,
                    Is.GreaterThanOrEqualTo(CombatVfxTuning.LppeImpact(1f).SparkCount),
                    "a bigger hit should throw at least as many sparks, not fewer");
                Assert.That(CombatVfxTuning.LppeShockImpact().FlashSize,
                    Is.GreaterThan(CombatVfxTuning.LppeImpact(9f).FlashSize),
                    "the Shock flash's own tuning must be bigger than a normal hit's by design, not just by luck of the damage value");
            }
            finally
            {
                Object.DestroyImmediate(laserGo);
            }
        }
    }
}
