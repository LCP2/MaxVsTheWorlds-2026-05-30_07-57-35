using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1005: Sentinels and Max had no body feedback on a landed hit (the robots already flash
    /// white — <see cref="RobotRig"/>). <see cref="DamageFeedbackVfx"/> adds a red hit flash plus a
    /// low-HP smoke gate to both. Testing policy Rule 1 — one new test, pinning both RESOLVED values
    /// the spec calls out: the property-block tint strength (Tier 2 — read straight off the renderer,
    /// not a mirrored field) and the smoke emitter's active state either side of the 35% threshold.
    ///
    /// Fails on the base commit (8c86feb): <c>DamageFeedbackVfx</c> does not exist there at all, so
    /// every one of these assertions fails to even compile — a Sentinel/Max hit tints nothing.
    /// </summary>
    public sealed class MV1005DamageFeedbackVfxTests
    {
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private GameObject _sentinelGo;
        private GameObject _playerGo;
        private GameObject _lowSentinelGo;
        private GameObject _healthySentinelGo;
        private GameObject _lowPlayerGo;
        private GameObject _healthyPlayerGo;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            Sentinel.ResetRegistry();
            foreach (var go in new[]
                     {
                         _sentinelGo, _playerGo, _lowSentinelGo, _healthySentinelGo, _lowPlayerGo, _healthyPlayerGo,
                     })
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        private static Sentinel NewSentinel(string name, float maxHp, out GameObject go)
        {
            go = new GameObject(name);
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(Vector3.zero, maxHp, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
            return sentinel;
        }

        private static PlayerHealth NewPlayer(string name, out GameObject go)
        {
            go = new GameObject(name, typeof(CharacterController)) { tag = "Player" };
            go.AddComponent<PlayerController>();

            // Unlike Sentinel (which builds its own body in Init), Max's visual body (MaxRig) is built
            // by a separate spawner this bare fixture never runs — so give it one bare renderer to tint,
            // the same minimum a real Max always has by the time a hit can land on him.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(go.transform);
            bodyGo.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            bodyGo.AddComponent<MeshRenderer>();

            var health = go.AddComponent<PlayerHealth>();
            health.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly
            return health;
        }

        /// <summary>Reads the RESOLVED tint strength straight off the first body renderer's property
        /// block — never a mirrored private field — matching the AC's own wording.</summary>
        private static float ResolvedFlashStrength(GameObject owner)
        {
            var renderer = owner.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(renderer, $"{owner.name} built no renderer to read the hit tint from");
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            return mpb.GetColor(EmissionId).r; // HitTintColor.r == 1.0, so the red channel IS the strength
        }

        /// <summary>Drives <see cref="DamageFeedbackVfx"/>'s decay with an explicit dt via reflection —
        /// same "reflect into the private per-frame tick" idiom <c>RobotRigTests</c> uses — so the test
        /// never depends on a live <c>Time.deltaTime</c> in EditMode.</summary>
        private static void Tick(DamageFeedbackVfx vfx, float dt)
        {
            MethodInfo tick = typeof(DamageFeedbackVfx).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(tick, "DamageFeedbackVfx must expose a private Tick(float) for deterministic EditMode ticking");
            tick.Invoke(vfx, new object[] { dt });
        }

        [Test]
        public void OnHit_ResolvesTheSpecTintStrengthAndDecay_AndGatesSmokeAtTheThirtyFivePercentLine()
        {
            // --- hit flash: Sentinel ---
            Sentinel sentinel = NewSentinel("MV1005 Sentinel", maxHp: 100f, out _sentinelGo);
            var sentinelVfx = sentinel.GetComponent<DamageFeedbackVfx>();
            Assert.IsNotNull(sentinelVfx, "Sentinel.Init must attach a DamageFeedbackVfx (MV-1005)");

            sentinel.TakeDamage(new DamageInfo(10f, sentinel.transform.position, Vector3.forward, Team.Enemy));
            Assert.AreEqual(0.65f, ResolvedFlashStrength(_sentinelGo), 0.02f,
                "a landed Sentinel hit must resolve the property-block tint strength to 0.65");
            Tick(sentinelVfx, 0.2f);
            Assert.LessOrEqual(ResolvedFlashStrength(_sentinelGo), 0.01f,
                "the Sentinel's hit flash must have decayed to ~0 after 0.2s of ticks (0.15s decay)");

            // --- hit flash: Max ---
            PlayerHealth playerHealth = NewPlayer("MV1005 Player", out _playerGo);
            var playerVfx = playerHealth.GetComponent<DamageFeedbackVfx>();
            Assert.IsNotNull(playerVfx, "PlayerHealth.Initialize must attach a DamageFeedbackVfx (MV-1005)");

            playerHealth.TakeDamage(new DamageInfo(10f, playerHealth.transform.position, Vector3.forward, Team.Enemy));
            Assert.AreEqual(0.65f, ResolvedFlashStrength(_playerGo), 0.02f,
                "a landed hit on Max must resolve the property-block tint strength to 0.65");
            Tick(playerVfx, 0.2f);
            Assert.LessOrEqual(ResolvedFlashStrength(_playerGo), 0.01f,
                "Max's hit flash must have decayed to ~0 after 0.2s of ticks (0.15s decay)");

            // --- low-HP smoke gate: Sentinel, 30% (active) vs 40% (inactive) ---
            Sentinel lowSentinel = NewSentinel("MV1005 Sentinel Low", maxHp: 100f, out _lowSentinelGo);
            lowSentinel.TakeDamage(new DamageInfo(
                lowSentinel.HealthMax * 0.70f, lowSentinel.transform.position, Vector3.forward, Team.Enemy)); // -> 30%
            Assert.IsTrue(lowSentinel.GetComponent<DamageFeedbackVfx>().SmokeActive,
                "a Sentinel at 30% HP (below 35%) must show the low-HP smoke");

            Sentinel healthySentinel = NewSentinel("MV1005 Sentinel Healthy", maxHp: 100f, out _healthySentinelGo);
            healthySentinel.TakeDamage(new DamageInfo(
                healthySentinel.HealthMax * 0.60f, healthySentinel.transform.position, Vector3.forward, Team.Enemy)); // -> 40%
            Assert.IsFalse(healthySentinel.GetComponent<DamageFeedbackVfx>().SmokeActive,
                "a Sentinel at 40% HP (at/above 35%) must not show the low-HP smoke");

            // --- low-HP smoke gate: Max, 30% (active) vs 40% (inactive) ---
            PlayerHealth lowPlayer = NewPlayer("MV1005 Player Low", out _lowPlayerGo);
            lowPlayer.TakeDamage(new DamageInfo(
                lowPlayer.Max * 0.70f, lowPlayer.transform.position, Vector3.forward, Team.Enemy)); // -> 30%
            Assert.IsTrue(lowPlayer.GetComponent<DamageFeedbackVfx>().SmokeActive,
                "Max at 30% HP (below 35%) must show the low-HP smoke");

            PlayerHealth healthyPlayer = NewPlayer("MV1005 Player Healthy", out _healthyPlayerGo);
            healthyPlayer.TakeDamage(new DamageInfo(
                healthyPlayer.Max * 0.60f, healthyPlayer.transform.position, Vector3.forward, Team.Enemy)); // -> 40%
            Assert.IsFalse(healthyPlayer.GetComponent<DamageFeedbackVfx>().SmokeActive,
                "Max at 40% HP (at/above 35%) must not show the low-HP smoke");
        }
    }
}
