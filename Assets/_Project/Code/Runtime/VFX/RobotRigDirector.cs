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
    /// It sweeps every <see cref="ScanIntervalSeconds"/>, not every frame (YT-186).
    /// <c>FindObjectsByType&lt;RobotEnemy&gt;(Include inactive)</c> walks every robot the run has ever
    /// pooled, active or not — a number that only grows over a run — and doing that 60 times a second
    /// was one of the biggest single frame costs behind the 60→30fps regression. A rig is built once
    /// per GameObject and never rebuilt, so a robot a few frames late to its first build is invisible;
    /// the interval is short enough that nothing ever charges out of the shed as a bare capsule.
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

        /// <summary>Seconds between roster sweeps (YT-186) — short enough that a freshly-created robot
        /// gets its rig well within the time it takes to notice, far cheaper than every frame.</summary>
        private const float ScanIntervalSeconds = 0.15f;
        private float _scanTimer;

        private void OnEnable()
        {
            _scanTimer = 0f;
            RigRobots(); // catch whatever already exists the instant this director wakes up
        }

        private void Update()
        {
            _scanTimer += Time.deltaTime;
            if (_scanTimer < ScanIntervalSeconds) return;
            _scanTimer = 0f;
            RigRobots();
        }

        private void RigRobots()
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
