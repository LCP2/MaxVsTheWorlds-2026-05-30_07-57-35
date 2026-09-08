using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The robot's tell, as a pure function (YT-96).
    ///
    /// The eye is how a small enemy telegraphs — a colour tell on a twenty-pixel body does not carry, so
    /// the one bright thing on it does the work. This is the same read the boss's eyes carry and the same
    /// word the ground rings use: gold at rest, the game's warn orange winding up, white on a hit. Pure,
    /// so the guarantee can be checked without driving a whole enemy into its lunge.
    /// </summary>
    public sealed class RobotRigTests
    {
        [Test]
        public void AtRest_TheEyeIsWarmGold_NotTheWarnColour()
        {
            Color idle = RobotRig.TellColorFor(windup: 0f, flash: 0f);

            // Gold: warm, bright, and low in blue — but distinctly NOT the red-orange of the wind-up, or
            // a robot merely standing there would look like one about to hit you.
            Assert.Greater(idle.r, 0.5f, "the resting eye is too dark to read as lit.");
            Assert.Greater(idle.g, 0.5f, "the resting eye has no gold in it — gold is green-warm, not red.");
            Assert.Less(idle.b, 0.4f, "the resting eye is too blue to read as gold.");
        }

        [Test]
        public void WindingUp_TheEyeHeatsTowardTheWarnColour()
        {
            Color idle = RobotRig.TellColorFor(0f, 0f);
            Color windup = RobotRig.TellColorFor(1f, 0f);

            // Warmer, and specifically REDDER: the gap between red and green opens up as it commits.
            Assert.Greater(windup.r - windup.g, idle.r - idle.g + 0.2f,
                "the wind-up does not read hotter than idle. It is the one telegraph that costs you " +
                "health to miss, so it has to be unmistakable from doing nothing.");
            Assert.Greater(windup.r, windup.b + 0.4f, "the wind-up is not warm.");
        }

        [Test]
        public void AHit_WhitesTheEyeOut()
        {
            Color flash = RobotRig.TellColorFor(0f, 1f);

            // Neutral white, never a saturated hue — a hit flash must never be mistakable for a render
            // error (CharacterSkin makes the same promise).
            Assert.AreEqual(flash.r, flash.g, 1e-3f, "the flash is not neutral...");
            Assert.AreEqual(flash.g, flash.b, 1e-3f, "...i.e. white, not a colour.");
            Assert.Greater(flash.r, 0.9f, "the flash is not bright enough to read as a hit.");
        }

        // ------------------------------------------------------------------ MV-723

        /// <summary>
        /// MV-723: contact robots hit Max with no visible action — <see cref="RobotRig"/> now pulls the
        /// whole body back through the wind-up and punches it forward the instant Lunge lands, easing
        /// back to rest. Drives a Rusher through Telegraph into Lunge with an explicit dt (the same
        /// reflection idiom <c>MV428MeleeReadabilityTests</c> already uses — Update()/LateUpdate() never
        /// run outside Play mode) and asserts the RESOLVED local offset of the built model — never an
        /// authored constant — differs between the wind-up and the strike, and settles back to exactly
        /// rest once the punch has decayed.
        /// </summary>
        [Test]
        public void ContactStrike_PullsBackOnWindup_PunchesForwardOnLunge_ThenReturnsToRest()
        {
            // Isolation from any other test in the same run — LungeTokenPool/the active registry/dev
            // overrides are all static, same reason MV428MeleeReadabilityTests resets them in Setup/TearDown.
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();

            var playerGo = new GameObject("Player") { tag = "Player" };
            var go = new GameObject("Enemy Rusher");
            go.transform.position = new Vector3(1f, 0f, 0f);
            var cc = go.AddComponent<CharacterController>();
            var enemy = go.AddComponent<RobotEnemy>();
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(enemy, cc);
            enemy.Apply(EnemyArchetype.Rusher); // stamps stats, re-runs ResetState (finds the tagged Player)

            var rig = go.AddComponent<RobotRig>();
            InvokeEnsureBuilt(rig);

            try
            {
                // Rest, before any wind-up: the body must start exactly at its authored pose.
                InvokeUpdateStrike(rig, 0.02f);
                Assert.AreEqual(0f, ModelLocalZ(rig), 1e-4f, "the body must be at rest before any wind-up begins");

                // Sight, then chase into Telegraph (a Rusher within lungeRange commits on its own).
                enemy.Sight.Tick(true, playerGo.transform.position, 0.02f);
                InvokeTickChase(enemy, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Telegraph, enemy.Current, "setup: must be mid wind-up");

                // Halfway through the wind-up (Rusher's telegraphTime is 0.55s).
                SetStateTimer(enemy, 0.275f);
                InvokeUpdateStrike(rig, 0.02f);
                float windupZ = ModelLocalZ(rig);
                Assert.Less(windupZ, -0.01f, "the wind-up must pull the body back, not leave it at rest");

                // Commit: push the wind-up all the way through, into Lunge.
                SetStateTimer(enemy, 999f);
                InvokeTickTelegraph(enemy, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Lunge, enemy.Current, "setup: must have committed to Lunge");

                // The instant Lunge begins is the instant the strike must land.
                InvokeUpdateStrike(rig, 0.02f);
                float strikeZ = ModelLocalZ(rig);
                Assert.Greater(strikeZ, 0.05f, "the strike must punch the body forward, past rest");
                Assert.Greater(strikeZ, windupZ, "the strike and the wind-up must read as different phases");

                // Ride the punch's own decay to zero — a 1.0 punch at 5 units/s takes 0.2s, so 10 ticks
                // of 0.02s lands exactly on rest, not just close to it (MoveTowards-style linear decay).
                for (int i = 0; i < 10; i++) InvokeUpdateStrike(rig, 0.02f);
                Assert.AreEqual(0f, ModelLocalZ(rig), 1e-4f, "the body must return to rest once the strike decays");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(playerGo);
                RobotEnemy.ResetRegistry();
                LungeTokenPool.Reset();
                DevTuning.Reset();
            }
        }

        private static float ModelLocalZ(RobotRig rig)
        {
            var model = (Transform)typeof(RobotRig)
                .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rig);
            return model.localPosition.z;
        }

        private static void InvokeUpdateStrike(RobotRig rig, float dt) =>
            typeof(RobotRig).GetMethod("UpdateStrike", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, new object[] { dt });

        /// <summary>Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (same note
        /// as RobotSkinDiagnosticsTests) — drive the private build step directly, and swallow whatever
        /// it logs: a build in this test environment can warn about a missing character shader, which
        /// Unity's default test rules would otherwise count as a failure.</summary>
        private static void InvokeEnsureBuilt(RobotRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void InvokeTickChase(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickTelegraph(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickTelegraph", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void SetStateTimer(RobotEnemy e, float value) =>
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(e, value);
    }
}
