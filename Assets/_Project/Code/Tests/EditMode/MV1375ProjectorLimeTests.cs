using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1375 (Rule 1, Tier 2): a Shield Projector's dish emissive, its dome's resolved tint and its
    /// beam's resolved width. The dish's emissive and the dome's tint must both be #7CFF3A's hue at
    /// full saturation (each channel normalised by the peak, within 0.05), and the beam at least 0.25 m.
    /// Fails on base: the dish uses the shared cool material (no emission), the dome tint is white-blue
    /// and the beam is 0.025 m wide.
    /// </summary>
    public sealed class MV1375ProjectorLimeTests
    {
        private static Color Normalised(Color c)
        {
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return peak <= 0f ? Color.black : new Color(c.r / peak, c.g / peak, c.b / peak);
        }

        private static void AssertLime(Color c, string what)
        {
            Color n = Normalised(c);
            Color want = Normalised(new Color(0x7C / 255f, 1f, 0x3A / 255f));
            Assert.AreEqual(want.r, n.r, 0.05f, what + " R");
            Assert.AreEqual(want.g, n.g, 0.05f, what + " G");
            Assert.AreEqual(want.b, n.b, 0.05f, what + " B");
        }

        [Test]
        public void ProjectorDishEmissive_DomeTint_AndBeamWidth_AreLime_MV1375()
        {
            var root = new GameObject("MV1375Root");
            ProjectorDome dome = null;
            try
            {
                var m = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Sprites/Default"));
                RobotBodies.Build(EnemyKind.Projector, root.transform, new RobotPalette(m, m, m, m));

                Transform dish = null;
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Dish") dish = t;
                Assert.IsNotNull(dish, "Projector body has a Dish part");

                Material dm = dish.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.IsTrue(dm.HasProperty("_EmissionColor"), "dish material has an emission slot");
                AssertLime(dm.GetColor("_EmissionColor"), "dish emissive");

                dome = ProjectorDome.Spawn(Vector3.zero, 4f, Vector3.up * 2.3f);
                AssertLime(dome.ResolvedFillTint, "dome tint");
                Assert.GreaterOrEqual(dome.ResolvedBeamWidth, 0.25f, "beam width (m)");
            }
            finally
            {
                if (dome != null) Object.DestroyImmediate(dome.gameObject);
                Object.DestroyImmediate(root);
            }
        }
    }
}
