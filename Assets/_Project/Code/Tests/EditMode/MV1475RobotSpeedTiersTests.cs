using System;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1475: robot speeds fall in three tiers (FAST melee rushers 1.7R, MEDIUM ranged 1.0R, SLOW
    /// tanks 0.5R) and the Settings "Robot move speed" knob is a MULTIPLIER on each kind's own speed.
    /// Asserts RESOLVED <see cref="RobotEnemy.EffectiveMoveSpeed"/> (no slow zone, non-elite).
    ///
    /// Fails on 00746e8 (the base): Rusher/Gunner/Bruiser resolve 0.93 : 1.00 : 0.46 (ratio
    /// 0.93 : 1 : 0.46, not 1.7 : 1 : 0.5), City's Bolter override pins 3.5 m/s, and
    /// <c>DevTuning.RobotMoveSpeedMultiplier</c> does not exist (the knob was one absolute m/s).
    /// </summary>
    public sealed class MV1475RobotSpeedTiersTests
    {
        private static float Speed(GameObject root, EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<CharacterController>();
            var r = go.AddComponent<RobotEnemy>();
            r.Apply(a);
            return r.EffectiveMoveSpeed;
        }

        [Test]
        public void SpeedsFormThreeTiers_AndSliderIsAMultiplier_MV1475()
        {
            var root = new GameObject("MV1475 Probe Root");
            DevTuning.Reset();
            try
            {
                // AC1: City Courier : Marksman : Bruiser = 1.7 : 1.0 : 0.5.
                WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
                float courier = Speed(root, EnemyArchetype.For(EnemyKind.Rusher, city));
                float marksman = Speed(root, EnemyArchetype.For(EnemyKind.Bolter, city));
                float cityBruiser = Speed(root, EnemyArchetype.For(EnemyKind.Bruiser, city));
                Assert.AreEqual(1.7f, courier / marksman, 0.05f, "City Courier : Marksman");
                Assert.AreEqual(0.5f, cityBruiser / marksman, 0.05f, "City Bruiser : Marksman");

                // Worlds 4 (Reef) and 5 (Mothership): Rusher : Laser : Bruiser skins.
                foreach (string key in new[] { WorldLibrary.World3, WorldLibrary.Mothership })
                {
                    WorldConfig w = WorldLibrary.Load(key);
                    float ru = Speed(root, EnemyArchetype.For(EnemyKind.Rusher, w));
                    float la = Speed(root, EnemyArchetype.For(EnemyKind.Gunner, w));
                    float br = Speed(root, EnemyArchetype.For(EnemyKind.Bruiser, w));
                    Assert.AreEqual(1.7f, ru / la, 0.05f, key + " Rusher : Laser");
                    Assert.AreEqual(0.5f, br / la, 0.05f, key + " Bruiser : Laser");
                }

                // AC2: slider x1.5 multiplies every kind's own speed exactly; kinds stay distinct.
                var baseSpeeds = new System.Collections.Generic.Dictionary<EnemyKind, float>();
                foreach (EnemyKind k in Enum.GetValues(typeof(EnemyKind)))
                    baseSpeeds[k] = Speed(root, EnemyArchetype.Of(k));
                DevTuning.RobotMoveSpeedMultiplier = 1.5f;
                foreach (EnemyKind k in Enum.GetValues(typeof(EnemyKind)))
                    Assert.AreEqual(baseSpeeds[k] * 1.5f, Speed(root, EnemyArchetype.Of(k)), 1e-4f, k + " at x1.5");
                float fast = Speed(root, EnemyArchetype.Of(EnemyKind.Rusher));
                float med = Speed(root, EnemyArchetype.Of(EnemyKind.Gunner));
                float slow = Speed(root, EnemyArchetype.Of(EnemyKind.Bruiser));
                Assert.Greater(fast, med * 1.5f);
                Assert.Greater(med, slow * 1.5f);
            }
            finally
            {
                DevTuning.Reset();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
