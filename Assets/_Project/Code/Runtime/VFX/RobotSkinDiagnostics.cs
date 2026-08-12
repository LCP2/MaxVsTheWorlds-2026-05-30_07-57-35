using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-350 hunt — diagnostic only, no fix in this file.
    ///
    /// Five explanations have each been checked against a real deployed build and ruled out: archetype
    /// colour constants (three tickets), lighting/exposure, two separate spawn paths (the "other" path
    /// is dead code), the rig building before <see cref="RobotEnemy.Kind"/> was corrected, and director
    /// execution order. Every round failed the same way — nobody could see what an affected robot was
    /// actually wearing at runtime, because EditMode with <c>-nographics</c> can prove a code path ran
    /// but never what colour ended up on screen. This is the observation tool Lee asked for instead of
    /// a sixth guess: log what a robot actually is, at the two moments that matter, and read the answer
    /// off the deployed WebGL console.
    ///
    /// Logs once when the robot is switched on (a fresh spawn or a pooled respawn — <see cref="OnEnable"/>
    /// fires for both) and once more a second later, so a build-then-drift bug and a build-then-never
    /// bug leave different traces. Unconditional — no dev flag, no build define — because this is a
    /// hunt, not a shipping feature, and Lee should not have to go find a switch to read it. Delete this
    /// file once the actual bug is found and fixed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotSkinDiagnostics : MonoBehaviour
    {
        /// <summary>How long after spawn the follow-up line fires — enough to catch a rig that built
        /// correctly on frame one and lost its material afterward, without flooding the console.</summary>
        private const float FollowUpDelay = 1f;

        private RobotEnemy _enemy;
        private RobotRig _rig;
        private RobotSpawnSource _source;
        private float _spawnedAt;
        private bool _followUpLogged;

        private void OnEnable()
        {
            _enemy = GetComponent<RobotEnemy>();
            _rig = GetComponent<RobotRig>();
            _source = GetComponent<RobotSpawnSource>();
            _spawnedAt = Time.time;
            _followUpLogged = false;
            Log("spawn");
        }

        private void Update()
        {
            if (_followUpLogged) return;
            if (Time.time - _spawnedAt < FollowUpDelay) return;
            _followUpLogged = true;
            Log("+1s");
        }

        /// <summary>One console line: who this robot is, what it SHOULD be wearing, and what it
        /// ACTUALLY is wearing right now, per <see cref="RobotRig.CurrentBodyColor"/>. Public and
        /// static-callable-shaped (an instance method taking no args beyond <paramref name="when"/>) so
        /// an EditMode test can drive it directly — the same reflection-driven pattern
        /// <c>RobotSkinSpawnPathTests</c> already uses for this component's neighbours, since Awake/
        /// OnEnable are not reliable outside Play mode.</summary>
        private void Log(string when)
        {
            if (_enemy == null) return;

            CharacterRole role = CharacterSkin.RoleFor(_enemy.Kind);
            Color expected = CharacterSkin.BaseColorFor(role);

            bool rigBuilt = _rig != null && _rig.Built;
            int rigBuildCount = _rig != null ? _rig.BuildCount : 0;
            string actual = rigBuilt ? ColorUtility.ToHtmlStringRGB(_rig.CurrentBodyColor) : "NO-RIG";
            string source = _source != null ? _source.Source : "unknown";

            Debug.Log($"[MV-350 skin] {when} id={GetInstanceID()} name={name} source={source} " +
                       $"kind={_enemy.Kind} expected=#{ColorUtility.ToHtmlStringRGB(expected)} " +
                       $"actual=#{actual} rigBuilt={rigBuilt} rigBuildCount={rigBuildCount}");
        }
    }
}
