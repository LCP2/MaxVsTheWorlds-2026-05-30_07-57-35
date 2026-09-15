using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-804 — Lee, on the build: "something weird is happening with max — on occasion he's
    /// arching backwards." Root cause in <c>MaxRig.TickRun</c>: the Shock lean was multiplied
    /// into the persistent <c>_body.localRotation</c> field, which the movement lean's own Slerp
    /// then read back and smoothed from next frame — so the Shock lean re-applied itself on top
    /// of its own accumulated effect every frame it ran, instead of kicking once. Fixed by
    /// holding the movement lean in its own field (<c>_moveLean</c>) and composing the Shock
    /// contribution on top of it fresh every frame, so nothing is ever read back.
    ///
    /// EditMode, same reflection idiom <see cref="MV678StrideDirectionTests"/> already uses for
    /// this rig: Awake is not called automatically outside Play Mode, so it is invoked directly,
    /// and <c>TickRun</c> — private, driven every frame from <c>LateUpdate</c> — is invoked the
    /// same way. Reads the exposed <see cref="MaxRig.BodyLeanPitchDegrees"/> property, never an
    /// internal field.
    ///
    /// Ticks at DOUBLE the reference frame rate (1/120 s) for the primary run, because the defect
    /// is a per-frame accumulation, not a per-second one — more frames in the same wall-clock
    /// window make it worse, not better.
    ///
    /// Must fail on base commit f89c4e7: quoted in the fix comment.
    /// </summary>
    public sealed class MV804MaxLeanTests
    {
        private const float LeanAngle = 9f;
        private const float ShockLeanAngle = 6f;
        private const float ShockSequenceDuration = 0.15f; // 0.03s kick + 0.12s return

        [Test]
        public void ShockLeanKicksOnceInsteadOfRatchetingIntoAnEverDeepeningArch()
        {
            var (maxAbsPitch120, pitchAfterSettle120) = RunBackwardShockLean(1f / 120f);

            Assert.That(maxAbsPitch120, Is.LessThanOrEqualTo(LeanAngle + ShockLeanAngle + 1f),
                $"Max's pitch reached {maxAbsPitch120:F1} degrees during the Shock lean — it is " +
                "being re-applied on top of itself every frame instead of composing once.");

            Assert.That(pitchAfterSettle120, Is.EqualTo(-LeanAngle).Within(1f),
                $"0.3s after the Shock sequence completed, Max's pitch was {pitchAfterSettle120:F1} " +
                $"degrees instead of settling back to the pure movement lean ({-LeanAngle} degrees).");

            var (maxAbsPitch60, _) = RunBackwardShockLean(1f / 60f);

            Assert.That(maxAbsPitch60, Is.EqualTo(maxAbsPitch120).Within(1f),
                $"the peak lean is frame-rate dependent: {maxAbsPitch60:F1} degrees at 1/60s " +
                $"steps vs {maxAbsPitch120:F1} degrees at 1/120s steps — the accumulation bug " +
                "gets worse with more frames in the same wall-clock window.");
        }

        /// <summary>Builds a fresh rig, holds a constant backwards stick, fires one Shock lean, and
        /// ticks for 0.5s at <paramref name="dt"/>, returning the largest absolute pitch reached
        /// and the pitch 0.3s after the Shock sequence (0.15s) completes.</summary>
        private static (float maxAbsPitch, float pitchAfterSettle) RunBackwardShockLean(float dt)
        {
            var playerGo = new GameObject("Player-MV804-Test", typeof(CharacterController));
            var player = playerGo.AddComponent<PlayerController>();
            Invoke(player, "Awake");

            var rigGo = new GameObject("MaxRig-MV804-Test");
            var rig = rigGo.AddComponent<MaxRig>();
            Invoke(rig, "Awake");

            SetMoveInput(player, 0f, -1f); // constant backwards travel

            HudSignals.EmitShockPulseLanded(Vector3.zero);

            const float duration = 0.5f;
            const float settleAt = ShockSequenceDuration + 0.3f;

            float maxAbsPitch = 0f;
            float pitchAfterSettle = 0f;
            bool capturedSettle = false;
            float t = 0f;

            while (t < duration - 1e-5f)
            {
                Invoke(rig, "TickRun", dt);
                t += dt;

                float pitch = rig.BodyLeanPitchDegrees;
                maxAbsPitch = Mathf.Max(maxAbsPitch, Mathf.Abs(pitch));

                if (!capturedSettle && t >= settleAt)
                {
                    pitchAfterSettle = pitch;
                    capturedSettle = true;
                }
            }

            Object.DestroyImmediate(rigGo);
            Object.DestroyImmediate(playerGo);

            return (maxAbsPitch, pitchAfterSettle);
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
