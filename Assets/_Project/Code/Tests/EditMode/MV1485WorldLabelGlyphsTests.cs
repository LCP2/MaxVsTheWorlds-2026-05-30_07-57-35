using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1485 - sole guard on world labels showing rows of white boxes. Legacy TextMesh on a dynamic
    /// font kept stale UVs after an atlas rebuild. Every world label must be a TextMeshPro on the CrispUi
    /// SDF font, and that font must resolve a glyph for every string the labels can show.
    /// </summary>
    public sealed class MV1485WorldLabelGlyphsTests
    {
        private static readonly string[] Strings =
        {
            "SEALED", "SEALED \u00B7 DESTROY THE SOURCE", "SEALED \u00B7 1 ROBOT LEFT", "SEALED \u00B7 12 ROBOTS LEFT",
            "UNSEALED \u00B7 SHOOT TO OPEN", "1 LOCK LEFT", "3 LOCKS LEFT", "VAULT", "OPEN", "RACK FULL", "x10",
        };

        [Test]
        public void WorldLabels_AreTextMeshPro_AndFontResolvesEveryGlyph()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.AreEqual(typeof(TextMeshPro), typeof(PartsCache).GetField("_label", flags).FieldType, "PartsCache label type");
            Assert.AreEqual(typeof(TextMeshPro), typeof(Vault).GetField("_label", flags).FieldType, "Vault label type");
            Assert.AreEqual(typeof(TextMeshPro), typeof(Pickup).GetField("_rackFullLabel", flags).FieldType, "Pickup label type");

            // The biome sweep repaints any MeshRenderer it counts as a world surface, flattening glyph quads to boxes.
            var go = new GameObject("MV1485Label");
            try
            {
                CrispUi.WorldText(go, "SEALED", 100, 0.06f, Color.white);
                Assert.IsFalse(WorldMaterials.IsWorldSurface(go.GetComponent<MeshRenderer>()),
                    "world label must be exempt from the biome surface sweep");
                // CharacterSkinDirector skips any renderer carrying SelfDrivenTint (RoleOf); under a damageable body the label turned teal.
                Assert.IsNotNull(go.GetComponent<MaxWorlds.Core.SelfDrivenTint>(), "label must opt out of the character re-skin");
            }
            finally { Object.DestroyImmediate(go); }

            var missing = new List<char>();
            var font = CrispUi.TmpFont();
            foreach (string s in Strings)
                foreach (char c in s)
                    if (!font.HasCharacter(c, true, true) && !missing.Contains(c)) missing.Add(c);
            Assert.IsEmpty(missing, "SDF font has no glyph for: " + string.Join(",", missing));
        }
    }
}
