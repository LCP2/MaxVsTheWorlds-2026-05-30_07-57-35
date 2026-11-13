using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1346 AC1 — the ORBITAL has no legs and hovers: ticking the hover over one 2.2 s period moves the
    /// drum's local Y through 0.16 m (+-0.02) while the collider stays fixed. Resolved values only; the hover
    /// is looked up by name/type name so the base commit (legs, no hover) fails by assertion, not compile.
    /// </summary>
    public sealed class MV1346OrbitalHoverTests
    {
        [Test]
        public void OrbitalBody_HasNoLegs_AndHoverBobsDrumThrough016m_WithColliderFixed()
        {
            var m = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var root = new GameObject("RawOrbitalBody");
            var col = root.AddComponent<CapsuleCollider>();
            try
            {
                RobotBodies.Build(EnemyKind.Orbital, root.transform, new RobotPalette(m, m, m, m));

                string[] legs = root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.Contains("Leg") || t.name.Contains("Knee")).Select(t => t.name).ToArray();
                Assert.AreEqual(0, legs.Length, "ORBITAL still has leg parts: " + string.Join(",", legs));

                Transform hover = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "OrbitalHover");
                Assert.IsNotNull(hover, "the ORBITAL body has no OrbitalHover pivot");
                var motion = hover.GetComponents<MonoBehaviour>().FirstOrDefault(c => c.GetType().Name == "OrbitalHoverMotion");
                Assert.IsNotNull(motion, "the hover pivot has no OrbitalHoverMotion");
                var advance = motion.GetType().GetMethod("Advance");

                Bounds before = col.bounds;
                float lo = float.MaxValue, hi = float.MinValue;
                const float dt = 0.01f;
                for (int i = 0; i < 220; i++)
                {
                    advance.Invoke(motion, new object[] { dt });
                    float y = hover.localPosition.y;
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
                Assert.AreEqual(0.16f, hi - lo, 0.02f, "drum local Y range over one 2.2 s bob");
                Assert.AreEqual(before, col.bounds, "the collider moved with the bob");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(m); }
        }
    }
}
