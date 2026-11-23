using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1428 — City enemy shots must be visible. ONE test (MV-465 Rule 1), Tier 2 resolved values: a real
    /// Launcher, Turret, Gunner and Orbital are driven through their own <see cref="RobotEnemy.Tick"/> with
    /// the brightHostileProjectiles dial on, and the shot each one actually spawns is read back (core size,
    /// material colour, trail metres = lifetime x speed; the Gunner's beam width/colour/length off its
    /// LineRenderer). Also: a Launcher in area 10 salvos two missiles and in area 9 fires one; with the dial
    /// off nothing changes. On the base commit <c>RobotEnemy.SetBrightShots</c> does not exist, so this fails
    /// to compile there.
    /// </summary>
    public sealed class MV1428BrightHostileProjectilesTests
    {
        private sealed class StubMax : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RigLateUpdate =
            typeof(RobotRig).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo RigEnsureBuilt =
            typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 0.02f;
        private static readonly Vector3 Origin = new Vector3(-74123f, 0f, 56789f);

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            OrbitalMissile.DestroyPoolForTests();
            CharacterControllerMotion.HasWarnedOversizedMove = true;
        }

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            OrbitalMissile.DestroyPoolForTests();
        }

        private static bool Near(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        private static void AssertRule(HostileShotLook.Resolved look, Color expected, string what)
        {
            Assert.GreaterOrEqual(look.CoreDiameter, 0.25f, $"{what}: core diameter");
            Assert.IsTrue(Near(look.Color, expected), $"{what}: colour was {look.Color}, expected {expected}");
            Assert.GreaterOrEqual(look.TrailLength, 1.0f, $"{what}: trail length");
        }

        /// <summary>One robot of <paramref name="kind"/> on its own ground with Max <paramref name="maxDist"/> m
        /// east of it; ticked until <paramref name="done"/> or 20 s. Runs <paramref name="inspect"/> with the
        /// robot still alive, then tears everything down.</summary>
        private static void Scenario(EnemyKind kind, bool bright, int area, float maxDist,
            Func<RobotEnemy, bool> done, Action<RobotEnemy> inspect)
        {
            GameObject groundGo = null, maxGo = null, spawnerGo = null;
            try
            {
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.transform.position = new Vector3(Origin.x, -1f, Origin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);
                maxGo = new GameObject("MV1428 Max") { tag = "Player" };
                maxGo.AddComponent<StubMax>();
                maxGo.transform.position = Origin + new Vector3(maxDist, 0f, 0f);
                spawnerGo = new GameObject("MV1428 Spawner");
                spawnerGo.transform.position = Origin;
                var sp = spawnerGo.AddComponent<EnemySpawner>();

                RobotEnemy r = sp.SpawnExact(kind, 1)[0];
                r.transform.position = Origin + Vector3.up * EnemyArchetype.Of(kind).SpawnHeight;
                OnEnableMethod.Invoke(r, null);
                r.SetAreaIndex(area);
                r.SetBrightShots(bright);
                r.BeginDormant(true);
                Physics.SyncTransforms();
                r.TakeDamage(new DamageInfo(1f, r.transform.position, Vector3.right, Team.Player));

                for (int i = 0; i < 1000 && !done(r); i++) r.Tick(Dt);
                inspect(r);
            }
            finally
            {
                foreach (var m in UnityEngine.Object.FindObjectsByType<HomingMissile>(FindObjectsSortMode.None))
                    UnityEngine.Object.DestroyImmediate(m.gameObject);
                foreach (var g in UnityEngine.Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                    UnityEngine.Object.DestroyImmediate(g.gameObject);
                OrbitalMissile.DestroyPoolForTests();
                RobotEnemy.ResetRegistry();
                if (spawnerGo != null) UnityEngine.Object.DestroyImmediate(spawnerGo);
                if (maxGo != null) UnityEngine.Object.DestroyImmediate(maxGo);
                if (groundGo != null) UnityEngine.Object.DestroyImmediate(groundGo);
            }
        }

        private static HomingMissile[] Missiles() =>
            UnityEngine.Object.FindObjectsByType<HomingMissile>(FindObjectsSortMode.None);

        /// <summary>Drives a Launcher through one full attack and returns the missiles it left.</summary>
        private static HomingMissile[] LauncherAttack(bool bright, int area)
        {
            HomingMissile[] result = null;
            bool sawLunge = false;
            Scenario(EnemyKind.Launcher, bright, area, 6f,
                r =>
                {
                    if (r.Current == RobotEnemy.State.Lunge) sawLunge = true;
                    return sawLunge && r.Current != RobotEnemy.State.Lunge;
                },
                r => result = Missiles());
            Assert.IsTrue(sawLunge, "the Launcher attacked");
            return result;
        }

        [Test]
        public void CityShots_AreBright_LauncherSalvosFromArea10_AndAreUnchangedWithTheDialOff()
        {
            // ---- Launcher: orange missile, >= 0.25 m core, >= 1 m trail ----
            Scenario(EnemyKind.Launcher, true, 10, 6f,
                r => Missiles().Length > 0,
                r =>
                {
                    HomingMissile[] m = Missiles();
                    Assert.AreEqual(1, m.Length, "first missile out");
                    AssertRule(m[0].ResolvedLook(), HostileShotLook.Orange, "Launcher missile");
                });

            // ---- Launcher salvo: 2 missiles in area 10, 1 in area 9, 1 with the dial off ----
            Assert.AreEqual(2, LauncherAttack(true, 10).Length, "area 10, dial on: salvo of two");
            Assert.AreEqual(1, LauncherAttack(true, 9).Length, "area 9, dial on: a single missile");
            HomingMissile[] off = LauncherAttack(false, 10);
            Assert.AreEqual(1, off.Length, "dial off: a single missile even in area 10");
            Assert.Less(off[0].ResolvedLook().CoreDiameter, 0.25f, "dial off: no bright core");
            Assert.Less(off[0].ResolvedLook().TrailLength, 1.0f, "dial off: the old short smear");

            // ---- Turret glob ----
            Scenario(EnemyKind.Turret, true, 1, 6f,
                r => UnityEngine.Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None).Length > 0,
                r =>
                {
                    var globs = UnityEngine.Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None);
                    Assert.AreEqual(1, globs.Length, "the Turret lobbed a glob");
                    AssertRule(globs[0].ResolvedLook(), HostileShotLook.Orange, "Turret glob");
                });
            Scenario(EnemyKind.Turret, false, 1, 6f,
                r => UnityEngine.Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None).Length > 0,
                r =>
                {
                    var globs = UnityEngine.Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None);
                    Assert.AreEqual(1, globs.Length, "dial off: the Turret lobbed a glob");
                    Assert.IsFalse(Near(globs[0].ResolvedLook().Color, HostileShotLook.Orange), "dial off: still the green glob");
                });

            // ---- Gunner beam: red, >= 0.08 m wide, >= 1 m long ----
            Scenario(EnemyKind.Gunner, true, 1, 5f,
                r => r.Current == RobotEnemy.State.Lunge,
                r =>
                {
                    Assert.AreEqual(RobotEnemy.State.Lunge, r.Current, "the Gunner fired");
                    var rig = r.GetComponent<RobotRig>();
                    RigEnsureBuilt.Invoke(rig, null);   // Awake does not run for AddComponent in EditMode
                    RigLateUpdate.Invoke(rig, null);
                    LineRenderer beam = rig.BeamLineForTests;
                    Assert.IsNotNull(beam, "the beam was drawn");
                    Assert.GreaterOrEqual(beam.widthMultiplier, 0.08f, "beam width");
                    Assert.IsTrue(Near(beam.startColor, HostileShotLook.Red), $"beam colour was {beam.startColor}");
                    Assert.GreaterOrEqual(Vector3.Distance(beam.GetPosition(0), beam.GetPosition(1)), 1.0f, "beam length");
                });

            // ---- Orbital missile: blue-white ----
            Scenario(EnemyKind.Orbital, true, 1, 6f,
                r => r.Orbital != null && r.Orbital.InFlight > 0,
                r =>
                {
                    Assert.Greater(r.Orbital.InFlight, 0, "the Orbital launched");
                    AssertRule(r.Orbital.Live[0].ResolvedLook(), HostileShotLook.BlueWhite, "Orbital missile");
                });
            Scenario(EnemyKind.Orbital, false, 1, 6f,
                r => r.Orbital != null && r.Orbital.InFlight > 0,
                r =>
                {
                    Assert.Greater(r.Orbital.InFlight, 0, "dial off: the Orbital launched");
                    Assert.IsFalse(Near(r.Orbital.Live[0].ResolvedLook().Color, HostileShotLook.BlueWhite),
                        "dial off: still the crimson ball");
                });
        }
    }
}
