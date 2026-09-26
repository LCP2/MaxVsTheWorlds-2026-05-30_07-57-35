using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-973: the game has no Rigidbodies, no triggers and no gameplay <c>FixedUpdate</c> — movement
    /// is <see cref="CharacterController.Move"/> in <c>Update</c>, and every physics query
    /// (raycast/overlap) is immediate. Left on <c>SimulationMode.FixedUpdate</c> (Unity's default),
    /// the engine still runs a physics step every 0.02s of simulated time regardless, up to 5 per
    /// frame when frames run slow (<c>Time.maximumDeltaTime</c>, MV-883) — work that resolves nothing,
    /// because nothing here is a Rigidbody for it to move.
    ///
    /// <see cref="Install"/> switches to <see cref="SimulationMode.Script"/> (call once, Bootstrap.Awake),
    /// which stops that automatic stepping outright. <see cref="Tick"/> (call once per rendered frame,
    /// Bootstrap.Update, before any gameplay system runs its own queries) replaces what the automatic
    /// step used to provide for free: a sync of the physics engine's internal state against whatever
    /// plain <c>Transform</c> writes happened since the last frame — necessary because
    /// <c>DynamicsManager.asset</c>'s own <c>m_AutoSyncTransforms</c> is already 0 project-wide, so
    /// without either the automatic step or this explicit call, a moved object's collider would never
    /// resync at all. <see cref="RequiresManualSimulate"/> is the single boolean a future ticket flips
    /// the moment a real Rigidbody enters the game — today it stays false, and <see cref="Tick"/> costs
    /// one no-op branch instead of a physics step that does no gameplay work.
    /// </summary>
    public static class PhysicsSimulationDriver
    {
        /// <summary>Whether anything in the current build actually needs the physics engine to step
        /// (a live Rigidbody, a trigger). False today — see the class comment.</summary>
        public static bool RequiresManualSimulate;

        /// <summary>Idempotent; call once (Bootstrap.Awake). No-ops outside a real Play session —
        /// <c>Awake</c> can be invoked directly by an EditMode test with no scene ever entering Play
        /// mode (<c>BootstrapMaximumDeltaTimeTests</c> and its siblings already do exactly this via
        /// reflection), and <see cref="Physics.simulationMode"/> is a live EDITOR setting, not one
        /// scoped to the Play session — changing it in Edit mode leaks straight into
        /// <c>ProjectSettings/DynamicsManager.asset</c> on disk instead of resetting when the test's
        /// GameObject is destroyed (confirmed: running this project's EditMode suite once dirtied that
        /// file with no guard here). Same class of test-pollution the render-scale EditMode tests
        /// already guard against for the URP asset (restoring it in a <c>finally</c>) — this asset has
        /// no per-test caller to hand that restore to, so the fix is not touching it outside Play mode
        /// at all.</summary>
        public static void Install()
        {
            if (!Application.isPlaying) return;
            Physics.simulationMode = SimulationMode.Script;
        }

        /// <summary>Call once per rendered frame, before the first system that raycasts/overlaps this
        /// frame's moved geometry.</summary>
        public static void Tick(float deltaTime)
        {
            Physics.SyncTransforms();
            if (RequiresManualSimulate) Physics.Simulate(deltaTime);
        }
    }
}
