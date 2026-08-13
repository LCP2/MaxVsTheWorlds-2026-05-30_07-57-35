using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Gives every Backyard robot a real body (YT-96).
    ///
    /// The robots are pooled and spawned over the course of a run — a director sweep is how they get
    /// dressed the moment they exist, exactly as <see cref="CharacterSkinDirector"/> colours them and
    /// <see cref="GroundAnchorVfx"/> rings them. A once-and-done pass at load would miss every robot the
    /// Mower Hutch has not emitted yet.
    ///
    /// It includes INACTIVE robots on purpose: a pooled robot sits deactivated between lives, and
    /// catching it there means its model is built and standing before it is ever switched on — no frame
    /// of greybox capsule as it charges out of the shed. The build itself happens once per GameObject
    /// (<see cref="RobotRig.Built"/>); a robot pooled as a bruiser is always a bruiser (YT-66), so the
    /// body a rig builds is the body that object wears for good.
    ///
    /// Reads the roster, writes nothing back to it.
    ///
    /// MV-350: a robot created because a spawner's per-kind pool was empty is built AND switched on
    /// in the same synchronous call (<see cref="MaxWorlds.Enemies.EnemySpawner.SpawnKind"/> and
    /// <see cref="MaxWorlds.Enemies.AreaAccumulationDirector.Spawn"/> both do this) — it never gets
    /// the "sits inactive in the pool, already dressed before it's ever switched on" runway this
    /// class's own doc comment above promises. Left at Unity's default execution order, whether this
    /// director's own <see cref="Update"/> reaches that robot before or after the frame renders came
    /// down to unspecified script-order luck, and a robot that lost that race rendered one frame of
    /// its raw greybox — the primitive's own untinted material, not an archetype colour — before the
    /// next sweep caught it. Running deliberately LAST (same convention as
    /// <see cref="MaxWorlds.Feel.ScreenShake"/>) guarantees every spawner's Update has already run
    /// this frame by the time this one does, so a robot spawned-and-shown in the same frame it's
    /// created is still dressed before that frame ever reaches the screen — not "next frame", never.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class RobotRigDirector : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<RobotRigDirector>() != null) return;
            new GameObject("RobotRigs").AddComponent<RobotRigDirector>();
        }

        private void Update()
        {
            foreach (var enemy in FindObjectsByType<RobotEnemy>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (enemy.GetComponent<RobotRig>() == null)
                    enemy.gameObject.AddComponent<RobotRig>();

                // MV-350 diagnostic — see RobotSkinDiagnostics. Same "add once, let OnEnable fire per
                // spawn" shape as RobotRig above, so it rides the exact same sweep and never lags a
                // robot that skips the "sits inactive, already dressed" runway.
                if (enemy.GetComponent<RobotSkinDiagnostics>() == null)
                    enemy.gameObject.AddComponent<RobotSkinDiagnostics>();
            }
        }
    }
}
