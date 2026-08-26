using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-548 (shed roadmap stage 3): a mobile shed (<see cref="MaxWorlds.Arena.Map.WorldShed.mobile"/>,
    /// MV-562) stays grounded until triggered, then rises to a 0.75 m hover over 2.5 s and pursues Max
    /// at 60% of his walk speed, stopping at a 2 m standoff — via
    /// <see cref="CharacterControllerMotion.SafeMove"/> (MV-386), so it can never tunnel through a wall
    /// the way a raw <c>cc.Move()</c> could.
    /// </summary>
    public sealed class MV548MobileShedTests
    {
        // Awake isn't reliably invoked for AddComponent outside Play mode (same note
        // MV456ShedFaucetTests carries) — drive it directly so the hutch's DestructibleHealth actually
        // exists before TickMobility/TakeDamage are exercised.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private const float BodyWidth = 2.25f;   // MV-541's shed footprint
        private const float BodyHeight = 1.5f;
        private const float Dt = 1f / 60f;

        // EditMode tests all share one physics scene for the whole cc-verify run (1593 test cases) with
        // no per-test scene reset — a rig built near the world origin, where several other fixtures also
        // build theirs, can start out overlapping a previous test's leftover collider and see a bogus
        // depenetration "correction" get folded into the very first CharacterController.Move() (this
        // failed against the full suite, reproducibly, before this offset; it passed every time run
        // filtered to just this class alone). A distinctive, far-off origin sidesteps that entirely.
        // Every assertion below is relative (deltas/distances), so the offset changes nothing it checks.
        private static readonly Vector3 RigOrigin = new Vector3(48213f, 0f, 91027f);

        /// <summary>Builds a mobile shed exactly the way <c>MapRuntime.BuildFactory</c> does for a
        /// <c>WorldShed.mobile == true</c> entity: the primitive's stray BoxCollider stripped, a
        /// CharacterController added, then <see cref="MowerHutch.ConfigureMobility"/> engaged.</summary>
        private static (GameObject go, MowerHutch hutch) BuildMobileShed(Vector3 groundedCenter)
        {
            var go = new GameObject("Mobile Hutch");
            go.transform.position = groundedCenter;
            go.transform.localScale = new Vector3(BodyWidth, BodyHeight, BodyWidth);

            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var cc = go.AddComponent<CharacterController>();
            // LOCAL (unscaled) unit-cube extents, exactly like MapRuntime.BuildFactory/BigBermudaBoss —
            // CharacterController.height/radius are local-space and Unity scales them by
            // transform.lossyScale automatically; passing the WORLD body size here double-scales into
            // an oversized, geometrically invalid capsule (2*radius > height).
            cc.center = Vector3.zero;
            cc.height = 1f;
            cc.radius = 0.5f;

            var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
            InvokeAwake(hutch);
            hutch.ConfigureMobility(true);

            // autoSyncTransforms is off project-wide (DynamicsManager.asset) — make the freshly placed
            // CharacterController visible to its very first physics query (MV-386 precedent, see
            // CharacterControllerMotionTunnelingTests). Without this, SafeMove's first Move() call sees
            // a stale broadphase position and "corrects" with a huge one-off displacement.
            Physics.SyncTransforms();
            return (go, hutch);
        }

        [Test]
        public void MobileShed_LiftsOffThenPursuesMaxAtStandoffWithoutPenetratingAWall()
        {
            // MowerHutch.BuildCore destroys the primitive core's stock collider via Object.Destroy,
            // which is edit-mode-illegal and logs an [Error] regardless of who calls Awake — same shape
            // as MV456ShedFaucetTests' cleanup.
            LogAssert.ignoreFailingMessages = true;

            Vector3 groundedCenter = RigOrigin + new Vector3(0f, BodyHeight * 0.5f, 0f);
            (GameObject go, MowerHutch hutch) = BuildMobileShed(groundedCenter);
            try
            {
                // --- Grounded: Max far away, no damage taken — must not move or leave Grounded. ---
                Vector3 farMax = groundedCenter + Vector3.forward * 50f;
                hutch.TickMobility(1f, farMax, 6f);
                Assert.AreEqual(MowerHutch.ShedMobility.Grounded, hutch.MobilityState,
                    "a mobile shed must stay grounded until its trigger fires");
                Assert.AreEqual(groundedCenter.y, go.transform.position.y, 0.001f,
                    "a grounded shed must not have risen at all");

                // --- Trigger: Max closes to within the 10 m radius — must enter LiftOff. ---
                Vector3 nearMax = groundedCenter + Vector3.forward * 8f;
                hutch.TickMobility(0f, nearMax, 6f);
                Assert.AreEqual(MowerHutch.ShedMobility.LiftOff, hutch.MobilityState,
                    "Max within 10 m of a grounded mobile shed must trigger lift-off");

                // --- Lift-off: 2.5 s of ticks must resolve to a 0.75 m hover, then hand off to Pursuit. ---
                for (float elapsed = 0f; elapsed < 2.5f; elapsed += Dt)
                    hutch.TickMobility(Dt, nearMax, 6f);
                Assert.AreEqual(MowerHutch.ShedMobility.Pursuit, hutch.MobilityState,
                    "2.5 s of lift-off ticks must complete into Pursuit");
                Assert.AreEqual(groundedCenter.y + 0.75f, go.transform.position.y, 0.02f,
                    "lift-off must resolve to exactly a 0.75 m hover above the shed's own grounded Y");

                // --- Pursuit, open ground: must close at 60% of Max's 6 m/s walk speed (3.6 m/s) and
                // hold exactly the 2 m standoff, never colliding with Max or stalling short of it. ---
                Vector3 openMax = new Vector3(RigOrigin.x, go.transform.position.y, RigOrigin.z + 20f);
                for (int i = 0; i < 900; i++) // 15 s -- closes the ~18 m gap to standoff in ~5 s at 3.6 m/s
                    hutch.TickMobility(Dt, openMax, 6f);

                float planarDistance = new Vector2(
                    go.transform.position.x - openMax.x, go.transform.position.z - openMax.z).magnitude;
                Assert.That(planarDistance, Is.EqualTo(2f).Within(0.25f),
                    "pursuit must close in and hold the 2 m standoff from Max, not collide with or stall short of him");

                // --- The shed's own transform — which just moved under TickMobility — is exactly what
                // a real robot emergence doors out from (EnemySpawner.SpawnKind calls
                // FactoryMouth.DoorPoint(transform.position, ...) live, every spawn): the door point
                // resolved off the CURRENT position must differ substantially from the one the ORIGINAL
                // grounded spot would have given. ---
                Vector3 dir = Vector3.forward;
                Vector3 doorAtOriginal = FactoryMouth.DoorPoint(groundedCenter, dir, go.transform.lossyScale, 0.5f, 3.5f, 1f);
                Vector3 doorAtCurrent = FactoryMouth.DoorPoint(go.transform.position, dir, go.transform.lossyScale, 0.5f, 3.5f, 1f);
                Assert.Greater(Vector3.Distance(doorAtCurrent, doorAtOriginal), 1f,
                    "a robot emergence during pursuit must door out from the shed's CURRENT position, not its original grounded spot");
            }
            finally
            {
                Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = false;
            }

            // --- Separate rig: a wall standing between the shed and Max must never be penetrated during
            // pursuit — the same MV-386 guarantee CharacterControllerMotionTunnelingTests pins for
            // SafeMove directly, now exercised through MowerHutch's own pursuit path. ---
            LogAssert.ignoreFailingMessages = true;
            Vector3 wallOrigin = RigOrigin + new Vector3(500f, 0f, 0f); // offset from scenario A's own rig
            Vector3 wallShedCenter = wallOrigin + new Vector3(0f, BodyHeight * 0.5f, -4f);
            (GameObject wallGo, MowerHutch wallHutch) = BuildMobileShed(wallShedCenter);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                float wallZ = wallOrigin.z;
                wall.transform.position = new Vector3(wallOrigin.x, wallShedCenter.y, wallZ);
                wall.transform.localScale = new Vector3(20f, 3f, 0.4f);
                // autoSyncTransforms is off project-wide (DynamicsManager.asset) — make the freshly
                // placed wall visible to the very first physics query against it (MV-386 precedent).
                Physics.SyncTransforms();

                Vector3 beyondWallMax = new Vector3(wallOrigin.x, wallShedCenter.y, wallOrigin.z + 20f); // far past the wall

                // Force the trigger via first damage (distance alone won't reach here from -4 to 20).
                wallHutch.TakeDamage(new DamageInfo(1f, Vector3.zero, Vector3.forward, Team.Player));
                wallHutch.TickMobility(0f, beyondWallMax, 6f);
                Assert.AreEqual(MowerHutch.ShedMobility.LiftOff, wallHutch.MobilityState,
                    "first damage must trigger lift-off even with Max well outside the 10 m radius");

                for (float elapsed = 0f; elapsed < 2.5f; elapsed += Dt)
                    wallHutch.TickMobility(Dt, beyondWallMax, 6f);
                Assert.AreEqual(MowerHutch.ShedMobility.Pursuit, wallHutch.MobilityState);

                for (int i = 0; i < 900; i++)
                    wallHutch.TickMobility(Dt, beyondWallMax, 6f);

                Assert.Less(wallGo.transform.position.z, wallZ,
                    "the shed must never penetrate the wall standing between it and Max, even after 15 s " +
                    "of continuous pursuit pressure against it");
            }
            finally
            {
                Object.DestroyImmediate(wallGo);
                Object.DestroyImmediate(wall);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
