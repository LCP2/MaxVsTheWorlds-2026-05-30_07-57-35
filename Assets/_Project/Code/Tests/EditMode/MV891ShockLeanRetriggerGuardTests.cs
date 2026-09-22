using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-891 — Lee: "Max leans back and the game shudders every few shots," worse at a higher
    /// primary fire rate. <c>PulseLaser.RegisterHit</c> triggers Shock every
    /// <c>PulseLaser.ShockHitInterval</c>-th (4th) pulse landed on the SAME robot — a pure hit
    /// count, unconditional on <c>dt</c> or frame time — so a faster Rate track raises how often
    /// <c>HudSignals.ShockPulseLanded</c> fires per minute exactly as Lee's own report reasoned.
    /// That is the trigger this ticket names for AC1: per-hit, not time-based.
    ///
    /// <see cref="MaxWorlds.Feel.GameFeel"/> already guards its own hit-stop half of this same
    /// signal with a shared cooldown (<c>minStopInterval</c>) specifically so a burst of these
    /// can't "freeze time several times a second" and "stutter, not punch" (its own doc comment).
    /// <see cref="MaxRig.OnShockPulseLanded"/> had no equivalent guard: every qualifying hit threw
    /// away whatever Shock lean was already in flight and started a brand new one from scratch —
    /// so a second Shock landing while the first lean was still mid-kick or mid-return didn't
    /// smoothly continue, it SNAPPED the resolved pitch back toward zero (a new
    /// <c>AnimSequence</c> reads 0 progress at its own time zero) before kicking again, a visible
    /// discontinuity that reads as a shudder rather than one clean punch.
    ///
    /// EditMode, same reflection idiom <see cref="MV804MaxLeanTests"/> already uses for this rig:
    /// Awake is not called automatically outside Play Mode, so it is invoked directly, and
    /// <c>TickRun</c> the same way. Reads <see cref="MaxRig.BodyLeanPitchDegrees"/>, never an
    /// internal field — a resolved value, not an authored constant (Rule 2 / MV-465).
    ///
    /// Must fail on base commit a732985: quoted in the fix comment.
    /// </summary>
    public sealed class MV891ShockLeanRetriggerGuardTests
    {
        private const float ShockLeanAngle = 6f;      // MaxRig.shockLeanAngle (private)
        private const float ShockLeanKickSeconds = 0.03f;

        [Test]
        public void SecondShockWhileFirstLeanStillMidKick_DoesNotSnapPitchBackTowardZero()
        {
            var playerGo = new GameObject("Player-MV891-Test", typeof(CharacterController));
            var player = playerGo.AddComponent<PlayerController>();
            Invoke(player, "Awake");
            SetMoveInput(player, 0f, 0f);   // standing still: isolates the Shock lean from the move lean

            var rigGo = new GameObject("MaxRig-MV891-Test");
            var rig = rigGo.AddComponent<MaxRig>();
            Invoke(rig, "Awake");

            // Land the first Shock and tick two-thirds of the way into its kick — well before
            // either the kick or the return has finished, so the lean is unambiguously "in flight".
            HudSignals.EmitShockPulseLanded(Vector3.zero);
            const float dt = 1f / 480f;   // fine-grained: isolates a single-tick discontinuity
            float tIntoKick = ShockLeanKickSeconds * 2f / 3f;
            for (float t = 0f; t < tIntoKick; t += dt)
            {
                Invoke(rig, "TickRun", dt);
            }
            float pitchBeforeSecondShock = rig.BodyLeanPitchDegrees;

            // A second robot crossing its own 4th-hit threshold a moment later — the exact
            // scenario a crowd or a fast Rate track produces. This must not restart the lean from
            // scratch while the first one is still resolving.
            HudSignals.EmitShockPulseLanded(Vector3.zero);
            Invoke(rig, "TickRun", dt);
            float pitchAfterSecondShock = rig.BodyLeanPitchDegrees;

            Object.DestroyImmediate(rigGo);
            Object.DestroyImmediate(playerGo);

            Assert.That(Mathf.Abs(pitchBeforeSecondShock), Is.GreaterThan(ShockLeanAngle * 0.5f),
                $"test setup: expected the first Shock lean to already be well into its kick " +
                $"({pitchBeforeSecondShock:F2} degrees) before the second Shock lands.");

            // One dt of real easing can only move the pitch a small fraction of the full swing.
            // A restart snaps Progress(0) back to (near) 0 in that same single tick — a jump far
            // bigger than one dt of easing could ever produce on its own.
            float singleTickJump = Mathf.Abs(pitchAfterSecondShock) - Mathf.Abs(pitchBeforeSecondShock);
            Assert.That(singleTickJump, Is.GreaterThan(-1f),
                $"pitch jumped from {pitchBeforeSecondShock:F2} to {pitchAfterSecondShock:F2} degrees " +
                "in a single tick — a second Shock landing while the first lean was still mid-kick " +
                "restarted the AnimSequence from scratch instead of letting the first one resolve, " +
                "snapping the resolved pitch back toward zero instead of continuing it smoothly.");
        }

        private static void Invoke(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static void Invoke(object target, string methodName, float dt) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, new object[] { dt });

        private static void SetMoveInput(PlayerController player, float x, float y) =>
            typeof(PlayerController).GetProperty("MoveInput")
                .GetSetMethod(nonPublic: true)
                .Invoke(player, new object[] { new Vector2(x, y) });
    }
}
