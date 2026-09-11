using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-769: a Sludge Drone's death puddle (<see cref="SludgePuddle"/>) did no damage at all — its
    /// only effect was <see cref="SludgePuddle.SpeedMultiplierAt"/>, consumed as a slow — and rendered
    /// as a literal <c>PrimitiveType.Cylinder</c> disc. Fails to COMPILE on the pre-fix base commit
    /// (36c31e6): at that commit <c>SludgePuddle.Spawn</c> takes exactly 3 arguments (no <c>seed</c>)
    /// and carries no <c>BuildFanMesh</c>/<c>DamagePerSecond</c> members, so this file does not build
    /// (CS1501 "no overload for method 'Spawn' takes 4 arguments", CS0117 "'SludgePuddle' does not
    /// contain a definition for 'BuildFanMesh'/'DamagePerSecond'") before a single assertion runs.
    /// </summary>
    public sealed class MV769SludgePuddleTests
    {
        private GameObject _playerGo;
        private PlayerHealth _playerHealth;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            SludgePuddle.ResetRegistry();

            _playerGo = new GameObject("Player", typeof(CharacterController)) { tag = "Player" };
            _playerGo.AddComponent<PlayerController>();
            _playerHealth = _playerGo.AddComponent<PlayerHealth>();
            _playerHealth.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly

            foreach (var stray in Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            SludgePuddle.ResetRegistry();
            DevTuning.Reset();
            foreach (var p in Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        // ------------------------------------------------------------------ AC1: damage-over-time

        [Test]
        public void Tick_DamagesAnIDamageableInside_ButNotOneOutsideTheRadius()
        {
            _playerGo.transform.position = Vector3.zero;
            SludgePuddle puddle = SludgePuddle.Spawn(Vector3.zero, radius: 2f, duration: 10f);
            try
            {
                float before = _playerHealth.Current;
                puddle.Tick(2f); // simulated 2 seconds standing in the puddle
                float lost = before - _playerHealth.Current;

                // The ticket's own authored rate is EXACTLY 6 damage/second, so 2s inside must resolve
                // to 12 — hardcoded from the spec, not read back off SludgePuddle.DamagePerSecond,
                // so a future change to the rate constant alone still fails this test honestly.
                Assert.AreEqual(12f, lost, 0.01f,
                    "an IDamageable standing in the puddle for 2s at the ticket's authored 6 dmg/s must lose 12 HP, not 0");

                _playerHealth.Revive();
                _playerGo.transform.position = new Vector3(50f, 0f, 0f); // well outside the 2 m radius
                float beforeOutside = _playerHealth.Current;
                puddle.Tick(2f);

                Assert.AreEqual(beforeOutside, _playerHealth.Current, 0.001f,
                    "an IDamageable standing outside the puddle's radius must take no damage");
            }
            finally
            {
                Object.DestroyImmediate(puddle.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC2: not a disc

        [Test]
        public void BuildFanMesh_HasMoreThanEightDistinctVertexDistancesFromCentre()
        {
            Mesh mesh = SludgePuddle.BuildFanMesh(2f, seed: 7);

            var distances = new HashSet<int>();
            foreach (Vector3 v in mesh.vertices)
                distances.Add(Mathf.RoundToInt(v.magnitude * 1000f)); // quantised to kill float noise

            Assert.Greater(distances.Count, 8,
                $"a disc has effectively 2 distinct vertex distances (0 at the centre, one radius " +
                $"repeated at the rim); the fan must have more than 8, found {distances.Count}");
        }

        // ------------------------------------------------------------------ AC3: seeded, not Random

        [Test]
        public void BuildFanMesh_SameSeedIsIdentical_DifferentSeedIsDifferent()
        {
            Mesh a1 = SludgePuddle.BuildFanMesh(2f, seed: 3);
            Mesh a2 = SludgePuddle.BuildFanMesh(2f, seed: 3);
            Mesh b = SludgePuddle.BuildFanMesh(2f, seed: 9);

            Assert.AreEqual(a1.vertices, a2.vertices,
                "the same seed must produce the identical outline every time (never Random)");

            bool anyDifferent = false;
            for (int i = 0; i < a1.vertices.Length; i++)
            {
                if (a1.vertices[i] != b.vertices[i]) { anyDifferent = true; break; }
            }
            Assert.IsTrue(anyDifferent, "two different seeds must produce different outlines");
        }

        // ------------------------------------------------------------------ AC4: hitbox stays round

        [Test]
        public void SpeedMultiplierAt_StillUsesTheCircularRadius_UnaffectedByTheIrregularVisual()
        {
            SludgePuddle puddle = SludgePuddle.Spawn(new Vector3(5f, 0f, 5f), radius: 3f, duration: 10f, seed: 42);
            try
            {
                Vector3 insidePoint = new Vector3(5f + 2f, 0f, 5f); // 2 m from centre, inside the 3 m circular radius
                Assert.AreEqual(SludgePuddle.SpeedMultiplier, SludgePuddle.SpeedMultiplierAt(insidePoint), 0.001f,
                    "a point inside the circular radius must still resolve to the authored slow multiplier, " +
                    "regardless of the irregular fan visual");
            }
            finally
            {
                Object.DestroyImmediate(puddle.gameObject);
            }
        }
    }
}
