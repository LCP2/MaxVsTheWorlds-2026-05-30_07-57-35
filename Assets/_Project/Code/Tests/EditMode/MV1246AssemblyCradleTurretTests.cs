using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1246 — the Mothership Assembly Cradle's two turret housings (MV-1227's own look) were drawn
    /// but <see cref="AssemblyCradle.Tick"/> never drove any weapon from them. Fails to compile on base
    /// commit 2d4e353: <see cref="AssemblyCradle"/> carries no <c>SetTurretKind</c>. Tier 2 (resolved
    /// values): asserts the damage a real <see cref="IDamageable"/> stand-in for Max actually receives
    /// and the resolved <see cref="DamageInfo.Point"/>'s distance to a real turret housing transform,
    /// never an authored constant.
    /// </summary>
    public sealed class MV1246AssemblyCradleTurretTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-73456f, 0f, 29012f);

        private GameObject _cradleGo;
        private GameObject _maxGo;

        private sealed class FakeMax : MonoBehaviour, IDamageable
        {
            public float TotalDamage;
            public Vector3? LastShotOrigin;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info)
            {
                TotalDamage += info.Amount;
                LastShotOrigin = info.Point;
            }
        }

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void Tick_TurretsFireOnMaxInRange_FromATurretHousing_AndStopOnceCradleDestroyed()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.name = "AssemblyCradle";
            _cradleGo.transform.position = RigOrigin;
            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build(); // AddComponent's own Awake never runs outside Play mode
            cradle.SetMaxInArea(true);
            // Laser: TickBeam applies damage directly off resolved beam geometry, no BolterBolt/
            // HomingMissile projectile-flight Update loop to simulate under EditMode's frozen frame loop.
            cradle.SetTurretKind(ShedFittingKind.Laser);

            _maxGo = new GameObject("MV1246 Max") { tag = "Player" };
            _maxGo.transform.position = RigOrigin + new Vector3(6f, 0f, 0f); // 6 m, inside Laser's 12 m range
            var max = _maxGo.AddComponent<FakeMax>();

            const float dt = 0.1f;
            const int steps = 50; // 5.0 simulated seconds
            for (int i = 0; i < steps; i++) cradle.Tick(dt);

            Assert.That(max.TotalDamage, Is.GreaterThan(0f),
                "a cradle with Max in range for 5s on a real tier must damage Max at least once");

            Assert.IsTrue(max.LastShotOrigin.HasValue, "a shot landed but recorded no origin point");
            float distToHousing0 = Vector3.Distance(max.LastShotOrigin.Value, cradle.TurretHousings[0].position);
            float distToHousing1 = Vector3.Distance(max.LastShotOrigin.Value, cradle.TurretHousings[1].position);
            Assert.That(Mathf.Min(distToHousing0, distToHousing1), Is.LessThanOrEqualTo(0.3f),
                "the shot must originate within 0.3 m of a turret housing");

            // Destroy the cradle, then keep ticking — no further damage must land.
            cradle.TakeDamage(new DamageInfo(999999f, cradle.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(cradle.IsAlive, "lethal damage must destroy the cradle");

            float damageAtDestruction = max.TotalDamage;
            for (int i = 0; i < steps; i++) cradle.Tick(dt);
            Assert.AreEqual(damageAtDestruction, max.TotalDamage,
                "once the cradle is destroyed its turrets must fire no further shots");
        }
    }
}
