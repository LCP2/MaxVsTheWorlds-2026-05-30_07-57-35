using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1358 AC1+AC2: a Shielded elite facing north with Max 180 degrees behind it (south) has
    /// turned 60 +/- 2 degrees after 1.0 s of simulated ticks, whether it only runs its facing tick
    /// or ALSO movement-turns (RotateToward) toward Max every tick; its plate still faces away from
    /// Max (full damage from Max's side); after 3.0 s it faces Max.
    ///
    /// Fails on the base commit (935cbdb): ShieldedFaceTurnRateDegPerSec is 180, so the facing-only
    /// robot is at 180 degrees after 1.0 s (expected 60 +/- 2), and RotateToward adds up to 540 deg/s.
    /// </summary>
    public sealed class MV1358ShieldedSlowTurnTests
    {
        [Test]
        public void ShieldedElite_TurnsNoFasterThanSixtyDegPerSec_FromFacingOrMovement_MV1358()
        {
            var root = new GameObject("MV1358 Probe Root");
            try
            {
                var max = new GameObject("Max") { tag = "Player" };
                max.transform.SetParent(root.transform, false);
                max.transform.position = new Vector3(0f, 0f, -10f);

                EnemyArchetype shielded = EnemyArchetype.Of(EnemyKind.Rusher).WithElite(EliteAffix.Shielded);
                const float dt = 1f / 60f;

                foreach (bool alsoMoves in new[] { false, true })
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = Vector3.zero;
                    go.AddComponent<CharacterController>();
                    RobotEnemy robot = go.AddComponent<RobotEnemy>();
                    robot.Apply(shielded);
                    go.transform.rotation = Quaternion.identity; // facing north (+Z)

                    for (int i = 0; i < 60; i++)
                    {
                        robot.TickShieldedFacing(dt);
                        if (alsoMoves) robot.RotateToward(Vector3.back, dt);
                    }

                    float turned = Mathf.Abs(Mathf.DeltaAngle(0f, go.transform.eulerAngles.y));
                    Assert.AreEqual(60f, turned, 2f, "after 1.0 s turned degrees (alsoMoves=" + alsoMoves + ")");

                    float before = robot.HealthCurrent;
                    robot.TakeDamage(new DamageInfo(1f, go.transform.position, Vector3.forward, Team.Player));
                    Assert.AreEqual(1f, before - robot.HealthCurrent, 1e-3f,
                        "a hit from Max's side after 1.0 s must take full damage (alsoMoves=" + alsoMoves + ")");

                    for (int i = 0; i < 120; i++)
                    {
                        robot.TickShieldedFacing(dt);
                        if (alsoMoves) robot.RotateToward(Vector3.back, dt);
                    }
                    Assert.AreEqual(180f, Mathf.Abs(Mathf.DeltaAngle(0f, go.transform.eulerAngles.y)), 2f,
                        "after 3.0 s it faces Max (alsoMoves=" + alsoMoves + ")");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
