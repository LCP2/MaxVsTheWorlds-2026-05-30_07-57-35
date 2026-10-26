using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1283 AC1 (Testing policy Rule 1 — one new test): a Molten elite moved 5 m over 3 s (five
    /// direct <see cref="RobotEnemy.TickMolten"/> calls at the ticket's own 0.6 s cadence, each preceded
    /// by a 1 m manual move — <c>TickMolten</c> touches only the trail timer/position bookkeeping, never
    /// gravity/CharacterController, so this needs no floor under the test capsule, unlike driving the
    /// full <see cref="RobotEnemy.Tick"/>) leaves between 3 and 6 live patches; a Rusher standing in one
    /// of those patches for 1 s loses 0 health and keeps its full move speed (the patch never registers
    /// with <see cref="MaxWorlds.Arena.MapSlowZones"/>); Max standing in one for 1 s loses 10 +/- 1.
    ///
    /// Fails to compile on b5e050c (the commit before this ticket): <c>RobotEnemy.TickMolten</c>,
    /// <c>RobotEnemy.MoltenPatchCount</c> and the <c>MoltenPatch</c> type do not exist there at all —
    /// same "brand new member, fails to compile" fail-first proof <c>MV1279ShieldProjectorHangBackTests</c>
    /// and <c>MV1244WelderRepairTests</c> already use for their own brand-new kinds.
    /// </summary>
    public sealed class MV1283MoltenEliteTests
    {
        [Test]
        public void MoltenElite_DropsPatchesWhileMoving_PatchDamagesOnlyMax_NeverSlows_MV1283()
        {
            DevTuning.Reset();

            GameObject eliteGo = null, playerGo = null, rusherGo = null;
            MoltenPatch patch = null;
            try
            {
                // --- AC1 part 1: moved 5 m over 3 s (five 0.6 s drops) leaves between 3 and 6 patches. ---
                eliteGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                eliteGo.AddComponent<CharacterController>();
                RobotEnemy elite = eliteGo.AddComponent<RobotEnemy>();
                elite.Apply(EnemyArchetype.Of(EnemyKind.Rusher).WithElite(EliteAffix.Molten));

                for (int i = 0; i < 5; i++)
                {
                    eliteGo.transform.position += new Vector3(1f, 0f, 0f);
                    elite.TickMolten(0.6f);
                }

                Assert.That(elite.MoltenPatchCount, Is.InRange(3, 6),
                    "a Molten elite moved 5m over 3s must leave between 3 and 6 live patches");

                // --- AC1 part 2: a patch damages only Max (10 +/- 1 over 1s), never a robot, never
                // slows anyone. Spawned directly (MoltenPatch's own public API), same "test the hazard
                // in isolation" idiom MV924SludgeDamageRetuneTests uses for SludgePuddle. ---
                playerGo = new GameObject("Player", typeof(CharacterController)) { tag = "Player" };
                playerGo.AddComponent<PlayerController>();
                PlayerHealth playerHealth = playerGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly
                playerGo.transform.position = Vector3.zero;

                rusherGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                rusherGo.AddComponent<CharacterController>();
                RobotEnemy rusher = rusherGo.AddComponent<RobotEnemy>();
                rusher.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
                rusherGo.transform.position = Vector3.zero;

                patch = MoltenPatch.Spawn(Vector3.zero);

                float playerBefore = playerHealth.Current;
                float rusherHealthBefore = rusher.HealthCurrent;
                float rusherSpeedBefore = rusher.EffectiveMoveSpeed;

                patch.Tick(1f); // simulated 1 second standing in the patch

                Assert.AreEqual(10f, playerBefore - playerHealth.Current, 1f,
                    "MV-1283: Max standing in a molten patch for 1s must lose 10 +/- 1 health");
                Assert.AreEqual(rusherHealthBefore, rusher.HealthCurrent, 0.001f,
                    "MV-1283: a robot standing in a molten patch for 1s must take no damage from it");
                Assert.AreEqual(rusherSpeedBefore, rusher.EffectiveMoveSpeed, 0.001f,
                    "MV-1283: a robot standing in a molten patch must keep its full move speed");
            }
            finally
            {
                if (patch != null) Object.DestroyImmediate(patch.gameObject);
                // The elite's own trail (part 1) is never a child of eliteGo — same free-flying-hazard
                // idiom as SludgePuddle/CorrosionPuddle — so destroying eliteGo alone leaks it.
                foreach (var stray in Object.FindObjectsByType<MoltenPatch>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(stray.gameObject);
                if (eliteGo != null) Object.DestroyImmediate(eliteGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (rusherGo != null) Object.DestroyImmediate(rusherGo);
            }
        }
    }
}
