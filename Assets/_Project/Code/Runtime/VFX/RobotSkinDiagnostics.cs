using System.Text;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-350 hunt — diagnostic only, no fix in this file.
    ///
    /// Five explanations have each been checked against a real deployed build and ruled out: archetype
    /// colour constants (three tickets), lighting/exposure, two separate spawn paths (the "other" path
    /// is dead code), the rig building before <see cref="RobotEnemy.Kind"/> was corrected, and director
    /// execution order. Round 1 of this diagnostic (<see cref="RobotRig.CurrentBodyColor"/>) then
    /// proved the rig computes and STORES the right colour in <c>_bodyMat</c> — and proved nothing about
    /// what actually reaches the screen, because that colour is read off the material OBJECT the rig
    /// owns, never off a renderer. The bug lives somewhere between that material and the pixel: a part
    /// renderer whose <c>sharedMaterial</c> got reassigned, a <c>MaterialPropertyBlock</c> overriding
    /// <c>_BaseColor</c> on top of a correct material, the greybox primitive re-enabling itself and
    /// drawing through the rig, or the character shader failing to resolve on Lee's GPU (the deployed
    /// console did report unsupported <c>Hidden/*</c> shaders) and falling back to a shader that reads
    /// colour differently.
    ///
    /// Round 2 (this file) reports what is actually on the RENDERERS, not on the material the rig
    /// thinks it built. Logs once when the robot is switched on (a fresh spawn or a pooled respawn —
    /// <see cref="OnEnable"/> fires for both), once more a second later with the existing one-line
    /// summary unchanged, and — new — a single grouped block at that same +1s mark naming every
    /// renderer under the robot: its material, its shader, its base colour, and whether a property
    /// block is sitting on top of it overriding what the material says. Also logs once at boot whether
    /// the hand-written character shader resolved at all. Unconditional — no dev flag, no build define
    /// — because this is a hunt, not a shipping feature, and Lee should not have to go find a switch to
    /// read it. Delete this file once the actual bug is found and fixed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotSkinDiagnostics : MonoBehaviour
    {
        /// <summary>How long after spawn the follow-up line fires — enough to catch a rig that built
        /// correctly on frame one and lost its material afterward, without flooding the console.</summary>
        private const float FollowUpDelay = 1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private RobotEnemy _enemy;
        private RobotRig _rig;
        private RobotSpawnSource _source;
        private float _spawnedAt;
        private bool _followUpLogged;

        /// <summary>Fires once, before the first scene loads, so the answer is on the console even if
        /// the very first robot to spawn is one of the affected ones. Reports whether
        /// <see cref="MaterialLibrary.Character()"/> — the material every robot body asks for — actually
        /// resolved its hand-written shader in this build, or fell back to null (which
        /// <see cref="RobotRig"/> then covers for with a plain lit shader that does not read colour the
        /// same way). Round 1's data cannot tell "shader missing" apart from "renderer lost its
        /// material somewhere else" — this closes that gap.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LogShaderBootState() => Debug.Log(DescribeShaderBootState());

        /// <summary>The boot line's text, pulled out so an EditMode test can check its shape without
        /// waiting for <c>RuntimeInitializeOnLoadMethod</c> to fire (it never does outside Play mode).</summary>
        public static string DescribeShaderBootState()
        {
            Material mat = MaterialLibrary.Character();
            string shaderName = mat != null ? mat.shader.name : "NONE";
            return $"[MV-350 skin] boot characterMaterial={(mat == null ? "NULL" : "ok")} shader={shaderName}";
        }

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
            LogRenderers();
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

        /// <summary>The +1s renderer block: fires the actual Debug.Log for <see cref="BuildRendererReport"/>.</summary>
        private void LogRenderers() => Debug.Log(BuildRendererReport());

        /// <summary>
        /// One grouped, multi-line block naming every <see cref="MeshRenderer"/> under this robot — the
        /// root's own (disabled) greybox included — with exactly the facts round 1 could not see: what
        /// material and shader the renderer is ACTUALLY drawing with, its material's own base colour,
        /// and whether a <see cref="MaterialPropertyBlock"/> is sitting on top overriding it (a block
        /// beats the material, and nothing about <c>_bodyMat</c> can reveal that). Also reports whether
        /// each part carries <see cref="SelfDrivenTint"/> (the marker that is supposed to keep
        /// <c>CharacterSkinDirector</c> off it) and, if a stray <see cref="CharacterSkin"/> ended up on
        /// it anyway, that skin's role and body colour — a second skin claiming a part it shouldn't own
        /// is exactly the kind of second writer this hunt is looking for.
        ///
        /// Pulled out from <see cref="LogRenderers"/> so an EditMode test can check its content directly
        /// instead of parsing a captured <c>Debug.Log</c> call.
        /// </summary>
        public string BuildRendererReport()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            var mpb = new MaterialPropertyBlock();

            var sb = new StringBuilder();
            sb.Append($"[MV-350 skin] +1s renderers id={GetInstanceID()} name={name} count={renderers.Length}");

            foreach (MeshRenderer r in renderers)
            {
                Material mat = r.sharedMaterial;
                string matName = mat != null ? mat.name : "NULL";
                string shaderName = mat != null ? mat.shader.name : "n/a";
                string baseColor = mat != null && mat.HasProperty(BaseColorId)
                    ? "#" + ColorUtility.ToHtmlStringRGB(mat.GetColor(BaseColorId))
                    : "n/a";

                bool hasBlock = r.HasPropertyBlock();
                string blockColor = "none";
                if (hasBlock)
                {
                    r.GetPropertyBlock(mpb);
                    blockColor = "#" + ColorUtility.ToHtmlStringRGB(mpb.GetColor(BaseColorId));
                }

                bool selfDrivenTint = r.GetComponent<SelfDrivenTint>() != null;
                var skin = r.GetComponent<CharacterSkin>();
                string skinInfo = skin != null
                    ? $"role={skin.Role} bodyColor=#{ColorUtility.ToHtmlStringRGB(skin.BodyColor)}"
                    : "none";

                sb.Append($"\n  part={r.name} enabled={r.enabled} material={matName} shader={shaderName} " +
                          $"baseColor={baseColor} propertyBlock={blockColor} selfDrivenTint={selfDrivenTint} " +
                          $"characterSkin={skinInfo}");
            }

            return sb.ToString();
        }
    }
}
