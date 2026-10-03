using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1071 — Lee, on v0.11.5: "Damage effects are not significant enough. I can see a tiny splat
    /// of red." Root cause: <see cref="DamageFeedbackVfx"/> only ever tints the renderers under the
    /// <see cref="PlayerHealth"/> object it is attached to — the greybox capsule <see cref="MaxRig"/>
    /// disables — never Max's actual visible body, which lives on a separate scene-root object.
    ///
    /// Testing policy Rule 1 — one new test, on RESOLVED values (Tier 2): a real hit must raise a live
    /// MaxRig body renderer's resolved emission red channel, and the raise must fully reverse once the
    /// flash decays back to exactly the pre-hit value — not stomp whatever baseline emission
    /// <see cref="MaxRig.WorldCompensationEmission"/> (MV-857) had already baked into that renderer's
    /// material.
    ///
    /// Fails on base `99fccd2`: <c>MaxRig</c> exposes no live-instance registry and
    /// <c>DamageFeedbackVfx</c> has no external-renderer hook, so a hit on Max never touches a single
    /// MaxRig renderer and the resolved emission never moves.
    /// </summary>
    public sealed class MV1071MaxRigDamageFlashTests
    {
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private GameObject _playerGo;
        private GameObject _rigGo;

        [TearDown]
        public void TearDown()
        {
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static void InvokeAwake(object target) =>
            target.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        /// <summary>Same reflected-Tick idiom <see cref="MV1005DamageFeedbackVfxTests"/> uses, so the
        /// decay can be driven deterministically without a live <c>Time.deltaTime</c>.</summary>
        private static void Tick(DamageFeedbackVfx vfx, float dt)
        {
            MethodInfo tick = typeof(DamageFeedbackVfx).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(tick, "DamageFeedbackVfx must expose a private Tick(float) for deterministic EditMode ticking");
            tick.Invoke(vfx, new object[] { dt });
        }

        /// <summary>The resolved value a renderer actually draws: a property-block override when one has
        /// been written, the renderer's own material colour otherwise — never a mirrored field.</summary>
        private static Color ResolvedEmission(Renderer r)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            return mpb.isEmpty ? r.sharedMaterial.GetColor(EmissionId) : mpb.GetColor(EmissionId);
        }

        [Test]
        public void HitOnMax_FlashesALiveMaxRigRenderer_AndFullyReversesOnDecay()
        {
            _playerGo = new GameObject("Player-MV1071-Test", typeof(CharacterController)) { tag = "Player" };
            var player = _playerGo.AddComponent<PlayerController>();
            InvokeAwake(player);

            _rigGo = new GameObject("MaxRig-MV1071-Test");
            var rig = _rigGo.AddComponent<MaxRig>();
            InvokeAwake(rig);

            Assert.AreSame(rig, MaxRig.Instance,
                "a built MaxRig must publish itself so damage feedback can find it");
            Assert.IsNotNull(rig.BodyRenderers, "MaxRig must expose its body renderers for the hit flash");
            Assert.IsNotEmpty(rig.BodyRenderers, "MaxRig built no body renderers to flash");

            Renderer bodyRenderer = rig.BodyRenderers[0];
            Color before = ResolvedEmission(bodyRenderer);

            var health = _playerGo.AddComponent<PlayerHealth>();
            health.Initialize();
            var vfx = health.GetComponent<DamageFeedbackVfx>();

            health.TakeDamage(new DamageInfo(10f, _playerGo.transform.position, Vector3.forward, Team.Enemy));

            Color afterHit = ResolvedEmission(bodyRenderer);
            Assert.GreaterOrEqual(afterHit.r - before.r, 0.5f,
                "a landed hit on Max must raise a live MaxRig renderer's resolved emission red channel by at least 0.5");

            Tick(vfx, 0.3f); // past the new 0.25s HitFlashDecaySeconds

            Color afterDecay = ResolvedEmission(bodyRenderer);
            Assert.AreEqual(before.r, afterDecay.r, 0.001f,
                "once the flash decays, the MaxRig renderer's resolved emission must land back on its exact pre-hit value, not zero");
            Assert.AreEqual(before.g, afterDecay.g, 0.001f);
            Assert.AreEqual(before.b, afterDecay.b, 0.001f);
        }
    }
}
