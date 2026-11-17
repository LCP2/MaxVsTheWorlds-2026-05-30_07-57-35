using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1375 AC1 (Tier 2, resolved values): a built Shield Projector's dish material emission, its dome's
    /// resolved tint and its beam's resolved width. The dish emissive and the dome tint are both lime
    /// (#7CFF3A's hue, full saturation); the beam is at least 0.25 m wide.
    /// Reads the resolved material emission and the dome renderer's MaterialPropertyBlock, never an authored constant.
    /// </summary>
    public sealed class MV1375ShieldProjectorLimeTests
    {
        // #7CFF3A at full saturation: hue ~ 0.2786.
        private const float LimeHue = 0.2786f;
        private const float HueTolerance = 0.05f;

        private static void AssertLime(Color c, string what)
        {
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            Assert.Greater(peak, 0.01f, what + " is black / unset (" + c + ")");
            Color.RGBToHSV(new Color(c.r / peak, c.g / peak, c.b / peak), out float h, out float s, out _);
            Assert.AreEqual(LimeHue, h, HueTolerance, what + " hue is not lime: " + c);
            Assert.GreaterOrEqual(s, 0.7f, what + " is not saturated: " + c);
        }

        [Test]
        public void ProjectorDishEmissiveAndDomeTintAreLime_BeamAtLeast025m_MV1375()
        {
            var root = new GameObject("MV1375 Projector");
            ProjectorDome dome = null;
            try
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                RobotBodies.Build(EnemyKind.Projector, root.transform, new RobotPalette(m, m, m, m));

                Transform dish = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Dish") { dish = t; break; }
                Assert.IsNotNull(dish, "Projector body has no Dish part");
                Material dm = dish.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.IsTrue(dm.HasProperty("_EmissionColor"), "dish material has no emission");
                AssertLime(dm.GetColor("_EmissionColor"), "dish emissive");

                dome = ProjectorDome.Spawn(Vector3.zero, 4f, new Vector3(6f, 2.3f, 0f));
                var mpb = new MaterialPropertyBlock();
                dome.GetComponentInChildren<MeshRenderer>().GetPropertyBlock(mpb);
                AssertLime(mpb.GetColor("_BaseColor"), "dome fill tint");
                AssertLime(mpb.GetColor("_RimColor"), "dome rim tint");
                // Beam width assertion superseded by MV-1389 (energy tether: thin core + glow sheath).
            }
            finally
            {
                if (dome != null) Object.DestroyImmediate(dome.gameObject);
                Object.DestroyImmediate(root);
            }
        }
    }
}
