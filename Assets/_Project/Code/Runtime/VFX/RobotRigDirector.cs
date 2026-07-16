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
    /// </summary>
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
            }
        }
    }
}
