using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-613: a boss authored smaller than the rig's legacy 6x6 dimensions rendered unchanged — the
    /// rig's fixed metre-scale parts followed the boss's POSITION (<see cref="BigBermudaRig.Follow"/>)
    /// but never its authored SIZE, while <see cref="BigBermudaBoss.FitColliderToRenderedBody"/> (the
    /// Awake-time fit) sized the CharacterController to the hidden placeholder cube instead — so a boss
    /// authored 3x3 fought with 3 m of physics under a still-6 m mower, on device (Lee, 2026-08-28,
    /// screenshot MV-613.png): it looked unchanged, glided over dressing and wedged in doorways it had
    /// physically passed.
    ///
    /// <see cref="BigBermudaRig.Bind"/> now scales the rig root to (authored boss width / 6) and refits
    /// the boss's CharacterController to what the bound rig actually renders. Both assertions read
    /// RESOLVED values — <c>transform.lossyScale</c> and the CharacterController fields Bind() actually
    /// wrote — never an authored constant, per the project's three-tier testing rule.
    /// </summary>
    public sealed class MV613BossRigScaleTests
    {
        // Awake isn't reliably invoked for AddComponent outside Play mode (same note
        // MV590BossWallSteeringTests/MV548MobileShedTests carry) — drive it directly so _cc actually
        // exists (and is fitted to the placeholder cube) before BigBermudaRig.Bind overrides it.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [TestCase(3f, 0.5f)]   // today's world1_config bosses (MV-589)
        [TestCase(6f, 1.0f)]   // the legacy size the rig's parts were authored for — must stay unscaled
        public void RigBoundToBoss_ScalesToAuthoredWidth_AndColliderTracksTheRenderedBody(
            float authoredWidth, float expectedScale)
        {
            GameObject bossGo = null;
            BigBermudaRig rig = null;
            try
            {
                bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var stray = bossGo.GetComponent<BoxCollider>();
                if (stray != null) Object.DestroyImmediate(stray);
                bossGo.transform.localScale = new Vector3(authoredWidth, 3f, authoredWidth);

                BigBermudaBoss boss = bossGo.AddComponent<BigBermudaBoss>();
                InvokeAwake(boss);
                rig = BigBermudaRig.CreateFor(boss);

                Assert.IsNotNull(rig, "binding a rig to a freshly authored boss must never fail");

                // --- the rig root scales to the boss's authored width, uniformly ---
                Vector3 lossy = rig.transform.lossyScale;
                Assert.AreEqual(expectedScale, lossy.x, 0.01f, "rig root X scale");
                Assert.AreEqual(expectedScale, lossy.y, 0.01f, "rig root Y scale");
                Assert.AreEqual(expectedScale, lossy.z, 0.01f, "rig root Z scale");

                // --- the collider tracks what the rig actually renders, not the placeholder cube ---
                var renderers = rig.GetComponentsInChildren<MeshRenderer>();
                Assert.IsNotEmpty(renderers, "a bound rig must have built a visible body");

                Bounds renderedWorld = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) renderedWorld.Encapsulate(renderers[i].bounds);

                CharacterController cc = bossGo.GetComponent<CharacterController>();
                float worldHeight = cc.height * boss.transform.lossyScale.y;
                float worldRadius = cc.radius *
                    Mathf.Max(boss.transform.lossyScale.x, boss.transform.lossyScale.z);

                float expectedRadius = Mathf.Max(renderedWorld.extents.x, renderedWorld.extents.z);
                Assert.That(worldHeight, Is.EqualTo(renderedWorld.size.y).Within(10).Percent,
                    $"collider world height {worldHeight:0.00} does not track the rendered body " +
                    $"({renderedWorld.size.y:0.00}) within 10%");
                Assert.That(worldRadius, Is.EqualTo(expectedRadius).Within(10).Percent,
                    $"collider world radius {worldRadius:0.00} does not track the rendered body " +
                    $"({expectedRadius:0.00}) within 10%");
            }
            finally
            {
                if (rig != null) Object.DestroyImmediate(rig.gameObject);
                if (bossGo != null) Object.DestroyImmediate(bossGo);
            }
        }
    }
}
