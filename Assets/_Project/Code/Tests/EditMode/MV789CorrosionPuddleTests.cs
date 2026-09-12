using UnityEngine;
using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-789: the Pipe Turret's <see cref="CorrosionPuddle"/> — not <see cref="SludgePuddle"/>, which
    /// MV-769 already fixed but which Area 1's Pipe Turret never spawns — was still a literal
    /// <c>PrimitiveType.Cylinder</c> doing no damage at all. Fails to COMPILE on the pre-fix base
    /// commit (a820a89): at that commit <c>CorrosionPuddle</c> carries no <c>BuildFanMesh</c>/
    /// <c>DamagePerSecond</c> members, so this file does not build (CS0117 "'CorrosionPuddle' does not
    /// contain a definition for 'BuildFanMesh'/'DamagePerSecond'") before a single assertion runs.
    /// </summary>
    public sealed class MV789CorrosionPuddleTests
    {
        private GameObject _playerGo;
        private PlayerHealth _playerHealth;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();

            _playerGo = new GameObject("Player", typeof(CharacterController)) { tag = "Player" };
            _playerGo.AddComponent<PlayerController>();
            _playerHealth = _playerGo.AddComponent<PlayerHealth>();
            _playerHealth.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly

            foreach (var stray in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            foreach (var p in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        // ------------------------------------------------------------------ AC1 + AC5: not a disc

        [Test]
        public void BuildFanMesh_HasNinePerimeterVertices_WithAtLeastOnePointSevenRimRatio_AndNoRendererIsACylinder()
        {
            Mesh mesh = CorrosionPuddle.BuildFanMesh(2f, seed: 4);

            // Vertex 0 is the centre; the rest are the rim.
            Assert.AreEqual(10, mesh.vertexCount, "a centre vertex plus 9 rim vertices must be exactly 10");

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 1; i < mesh.vertexCount; i++)
            {
                float r = mesh.vertices[i].magnitude;
                min = Mathf.Min(min, r);
                max = Mathf.Max(max, r);
            }
            Assert.GreaterOrEqual(max / min, 1.7f,
                $"the ticket's own 0.68-1.30x rim jitter must swing at least 1.7x on this seed (a cylinder's is 1.0x); found {max / min:F3}");

            CorrosionPuddle puddle = CorrosionPuddle.Spawn(Vector3.zero, radius: 2f, duration: 5f);
            try
            {
                foreach (var mf in puddle.GetComponentsInChildren<MeshFilter>())
                {
                    Assert.AreNotEqual("Cylinder", mf.sharedMesh.name,
                        "no renderer under the puddle may still be Unity's built-in Cylinder primitive mesh");
                }
            }
            finally
            {
                Object.DestroyImmediate(puddle.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC2: seeded from impact position

        [Test]
        public void Spawn_SeedsTheOutlineFromImpactPosition_SamePositionIdentical_DifferentPositionDifferent()
        {
            CorrosionPuddle a1 = CorrosionPuddle.Spawn(Vector3.zero, radius: 2f, duration: 5f);
            CorrosionPuddle a2 = CorrosionPuddle.Spawn(Vector3.zero, radius: 2f, duration: 5f);
            CorrosionPuddle b = CorrosionPuddle.Spawn(new Vector3(5f, 0f, 3f), radius: 2f, duration: 5f);
            try
            {
                Vector3[] verticesA1 = a1.transform.Find("Puddle Rim").GetComponent<MeshFilter>().sharedMesh.vertices;
                Vector3[] verticesA2 = a2.transform.Find("Puddle Rim").GetComponent<MeshFilter>().sharedMesh.vertices;
                Vector3[] verticesB = b.transform.Find("Puddle Rim").GetComponent<MeshFilter>().sharedMesh.vertices;

                Assert.AreEqual(verticesA1, verticesA2,
                    "the same impact position must draw the identical outline every time (never Random)");

                bool anyDifferent = false;
                for (int i = 0; i < verticesA1.Length; i++)
                {
                    if (verticesA1[i] != verticesB[i]) { anyDifferent = true; break; }
                }
                Assert.IsTrue(anyDifferent, "two different impact positions must draw different outlines");
            }
            finally
            {
                Object.DestroyImmediate(a1.gameObject);
                Object.DestroyImmediate(a2.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC3: damage-over-time

        [Test]
        public void Tick_DamagesAnIDamageableInside_ButNotOneOutsideTheRadius()
        {
            _playerGo.transform.position = Vector3.zero;
            CorrosionPuddle puddle = CorrosionPuddle.Spawn(Vector3.zero, radius: 1.5f, duration: 10f);
            try
            {
                float before = _playerHealth.Current;
                puddle.Tick(1.0f); // 4 ticks at the authored 0.25s cadence
                float lost = before - _playerHealth.Current;

                Assert.AreEqual(6f, lost, 0.5f,
                    "an IDamageable standing in the puddle for 1.0s at the ticket's authored 6 dmg/s must lose ~6 HP");

                _playerHealth.Revive();
                _playerGo.transform.position = new Vector3(50f, 0f, 0f); // well outside the 1.5 m radius
                float beforeOutside = _playerHealth.Current;
                puddle.Tick(1.0f);

                Assert.AreEqual(beforeOutside, _playerHealth.Current, 0.001f,
                    "an IDamageable standing outside the puddle's radius must take no damage");
            }
            finally
            {
                Object.DestroyImmediate(puddle.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC4: CorrodedStatus unchanged, still refreshed

        [Test]
        public void Tick_StillRefreshesCorrodedStatus_AtTheUnchangedDurationAndMultiplier()
        {
            CorrosionPuddle puddle = CorrosionPuddle.Spawn(_playerHealth.transform.position, radius: 1.5f, duration: 3f);
            try
            {
                Assert.IsFalse(_playerHealth.IsCorroded, "must not be corroded before the puddle ticks");

                puddle.Tick(0.01f); // one evaluation: Max is standing exactly at the impact point

                Assert.IsTrue(_playerHealth.IsCorroded, "standing in the puddle must still apply CORRODED");
                Assert.AreEqual(CorrodedStatus.Duration, _playerHealth.CorrodedRemaining, 0.02f,
                    "a fresh CORRODED application must still read close to the full, unchanged duration");
                Assert.AreEqual(1.25f, _playerHealth.DamageTakenMultiplier, 1e-4f,
                    "the resolved multiplier must still be the unchanged 1.25x, not a hardcoded 1x");

                float before = _playerHealth.Current;
                _playerHealth.TakeDamage(new DamageInfo(20f, _playerHealth.transform.position, Vector3.forward, Team.Enemy));
                float applied = before - _playerHealth.Current;

                Assert.AreEqual(25f, applied, 1e-3f,
                    "a 20-damage hit while CORRODED must still resolve to 25 (20 x 1.25), unchanged by MV-789's own damage tick");
            }
            finally
            {
                Object.DestroyImmediate(puddle.gameObject);
            }
        }
    }
}
