using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1413: every kind that can roll Shielded shows its plate in front of its own body mesh, in the
    /// production order (rig built at Awake, THEN the affix stamped by Apply), and a pooled reuse that is
    /// not Shielded shows none. Guards MV-1413 (invisible Shielded plate).
    /// </summary>
    public sealed class MV1413ShieldedPlateVisibleTests
    {
        private static void Tick(RobotRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, null);
                typeof(RobotRig).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static Renderer ActivePlate(RobotRig rig)
        {
            foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(true))
                if (r.name == "ShieldedPlate" && r.enabled && r.gameObject.activeInHierarchy) return r;
            return null;
        }

        private static float BodyFront(RobotRig rig)
        {
            float front = float.NegativeInfinity;
            foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(false))
                if (r.name != "ShieldedPlate") front = Mathf.Max(front, r.bounds.max.z);
            return front;
        }

        [Test]
        public void ShieldedPlateShowsInFrontOfBody_ForEveryKind_AndGoesOnPoolReuse_MV1413()
        {
            var root = new GameObject("MV1413 Probe Root");
            var failures = new List<string>();
            try
            {
                foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
                {
                    if (kind == EnemyKind.Blinker || kind == EnemyKind.Projector) continue;   // never roll Shielded
                    EnemyArchetype plain = EnemyArchetype.Of(kind);

                    var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<CharacterController>();
                    RobotEnemy enemy = go.AddComponent<RobotEnemy>();
                    enemy.Apply(plain);
                    RobotRig rig = go.AddComponent<RobotRig>();
                    Tick(rig);                                   // built un-Shielded, as a pooled prefab is
                    enemy.Apply(plain.WithElite(EliteAffix.Shielded));   // affix stamped after the build
                    Tick(rig);

                    Renderer plate = ActivePlate(rig);
                    if (plate == null) failures.Add(kind + ": no active ShieldedPlate");
                    else if (plate.bounds.max.z <= BodyFront(rig))
                        failures.Add(kind + ": plate front " + plate.bounds.max.z.ToString("0.000") + " not in front of body " + BodyFront(rig).ToString("0.000"));

                    enemy.Apply(plain);                          // pooled reuse, not Shielded
                    Tick(rig);
                    if (ActivePlate(rig) != null) failures.Add(kind + ": plate still active after un-Shielded reuse");
                }
                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
