using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-451 — before this, <c>RobotRig.BuildModel</c> dispatched Heavy and Brute to
    /// <c>BuildRusher</c> (neither had a body of its own), and <c>ParentScale.MakeMetreSpace</c>
    /// cancelled the archetype's <c>BodyScale</c> before any of it was placed. Together those meant
    /// Rusher, Heavy and Brute built the exact same body — same method, same hard-coded numbers, no
    /// scale ever reaching the mesh — despite three different <c>BodyScale</c> values (commit
    /// f2aab92). One test, per MV-465: the three kinds' built heights must be pairwise distinct.
    /// </summary>
    public sealed class RobotBodySizeTests
    {
        private static RobotRig BuildRig(EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            var go = GameObject.CreatePrimitive(
                archetype.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.AddComponent<CharacterController>();

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);

            var rig = go.AddComponent<RobotRig>();

            // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode — the same
            // reflection-driven pattern RobotSkinSpawnPathTests established for exactly this reason.
            // RobotRig strips a Collider off each generated part via the play-mode-correct Destroy(),
            // a logged error in Edit mode; ignore just that.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            return rig;
        }

        [Test]
        public void RusherHeavyAndBrute_BuildPairwiseDistinctlySizedBodies()
        {
            float rusher = 0f, heavy = 0f, brute = 0f;

            foreach (var (kind, setHeight) in new (EnemyKind, System.Action<float>)[]
            {
                (EnemyKind.Rusher, h => rusher = h),
                (EnemyKind.Heavy,  h => heavy = h),
                (EnemyKind.Brute,  h => brute = h),
            })
            {
                var rig = BuildRig(kind);
                try { setHeight(CombinedBoundsHeight(rig)); }
                finally
                {
                    LogAssert.ignoreFailingMessages = true;
                    try { Object.DestroyImmediate(rig.gameObject); }
                    finally { LogAssert.ignoreFailingMessages = false; }
                }
            }

            // A shared build method (the f2aab92 defect) doesn't produce three SIMILAR heights, it
            // produces three IDENTICAL ones — no scale ever reaches the mesh, so the margin here only
            // has to clear floating-point noise, not stylistic variance between three real bodies.
            const float minGap = 0.05f;
            Assert.That(Mathf.Abs(rusher - heavy), Is.GreaterThan(minGap),
                $"Rusher ({rusher:0.00} m) and Heavy ({heavy:0.00} m) are the same height — Heavy has " +
                "fallen through to the Rusher's body again.");
            Assert.That(Mathf.Abs(rusher - brute), Is.GreaterThan(minGap),
                $"Rusher ({rusher:0.00} m) and Brute ({brute:0.00} m) are the same height — Brute has " +
                "fallen through to the Rusher's body again.");
            Assert.That(Mathf.Abs(heavy - brute), Is.GreaterThan(minGap),
                $"Heavy ({heavy:0.00} m) and Brute ({brute:0.00} m) are the same height — one of them " +
                "has fallen through to the other's body.");
        }

        /// <summary>Combined world-space bounds of every built part, excluding the disabled greybox
        /// stand-in — the same exclusion <see cref="RobotSkinSpawnPathTests"/> uses.</summary>
        private static float CombinedBoundsHeight(RobotRig rig)
        {
            var greybox = rig.GetComponent<MeshRenderer>();
            Bounds? combined = null;
            foreach (var r in rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == greybox) continue;
                if (combined == null) { combined = r.bounds; continue; }
                var b = combined.Value;
                b.Encapsulate(r.bounds);
                combined = b;
            }
            Assert.IsNotNull(combined, $"{rig.name} built no visible parts at all");
            return combined.Value.size.y;
        }
    }
}
