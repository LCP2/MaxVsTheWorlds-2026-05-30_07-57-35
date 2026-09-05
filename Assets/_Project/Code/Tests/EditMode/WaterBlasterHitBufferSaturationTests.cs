using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-666 — <c>WaterBlaster._hits</c> was a fixed <c>Collider[32]</c> buffer feeding
    /// <c>Physics.OverlapSphereNonAlloc</c>, which silently TRUNCATES once 32+ colliders on
    /// <c>hitMask</c> sit within the blaster's reach, with no ordering guarantee over which 32
    /// survive. a30's 45-robot garrison plus its two bosses in one room is exactly this case — the
    /// query never returns every collider, so a boss the query drops is never even tested for
    /// <c>IsAlive</c>, let alone damaged. This places more damageables than the ORIGINAL buffer
    /// length within the spray's reach and cone (dead ahead, angle 0, so cone width is not what's
    /// under test) and asserts every one of them resolved a hit — the resolved outcome, not an
    /// authored constant.
    /// </summary>
    public sealed class WaterBlasterHitBufferSaturationTests
    {
        private const int OriginalBufferLength = 32;
        private const int TargetCount = OriginalBufferLength + 8;

        private sealed class CountingDamageable : MonoBehaviour, IDamageable
        {
            public int HitCount;
            public bool IsAlive => true;
            public Team Team => Team.Enemy;
            public void TakeDamage(in DamageInfo info) => HitCount++;
        }

        private static void InvokeFireTick(WaterBlaster blaster)
        {
            typeof(WaterBlaster).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(blaster, null);
        }

        [Test]
        public void MoreDamageablesThanOriginalBuffer_EveryOneInConeStillTakesDamage()
        {
            var blasterBody = new GameObject("wb_saturation_test");
            var targets = new GameObject[TargetCount];
            var damageables = new CountingDamageable[TargetCount];
            try
            {
                blasterBody.transform.position = Vector3.zero;
                blasterBody.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                var blaster = blasterBody.AddComponent<WaterBlaster>();

                // Dead ahead (angle 0 from the aim axis) always clears the spray cone regardless of
                // its authored half-angle, and every position here is well within the default 5 m
                // range — the only thing under test is whether the physics query itself returns
                // every one of these colliders, not the cone/reach math.
                for (int i = 0; i < TargetCount; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.position = new Vector3(0f, 0f, 2f + i * 0.05f);
                    go.transform.localScale = Vector3.one * 0.1f;
                    targets[i] = go;
                    damageables[i] = go.AddComponent<CountingDamageable>();
                }

                // autoSyncTransforms is off project-wide -- every transform set above needs an
                // explicit sync before the physics query FireTick runs (same as
                // WaterBlasterGateDamageTests).
                Physics.SyncTransforms();
                InvokeFireTick(blaster);

                int hitCount = 0;
                for (int i = 0; i < TargetCount; i++)
                {
                    if (damageables[i].HitCount > 0) hitCount++;
                }

                Assert.AreEqual(TargetCount, hitCount,
                    $"only {hitCount}/{TargetCount} in-cone damageables took damage -- " +
                    "WaterBlaster._hits truncated the physics query before every one of them was " +
                    "resolved (MV-666: a fixed Collider[32] buffer with no saturation handling).");
            }
            finally
            {
                Object.DestroyImmediate(blasterBody);
                for (int i = 0; i < TargetCount; i++)
                {
                    if (targets[i] != null) Object.DestroyImmediate(targets[i]);
                }
            }
        }
    }
}
