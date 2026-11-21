using NUnit.Framework;
using TMPro;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1405 — sole guard on the TMP foundation. The SDF font asset made from the game's UI font
    /// must resolve, and its material's shader must be a runtime TMP SDF shader (not the editor-only
    /// "TMP_SDF Internal Editor") that is referenced from a Resources asset, so it ships in players.
    /// Resolved values only.
    /// </summary>
    public sealed class MV1405TmpFontAssetTests
    {
        [Test]
        public void HudSdfFont_UsesRuntimeShader_ReferencedFromResources()
        {
            var font = CrispUi.TmpFont();
            Assert.IsNotNull(font, "SDF font asset must resolve");
            Assert.IsNotNull(font.material, "SDF font asset must have a material");
            string shader = font.material.shader.name;
            StringAssert.StartsWith("TextMeshPro/", shader);
            StringAssert.DoesNotContain("Internal Editor", shader);

            var shipped = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Assert.IsNotNull(shipped, "TMP Essential Resources must be under a Resources folder");
            Assert.AreEqual(shader, shipped.material.shader.name,
                "shader must be the one a Resources asset references (so the player includes it)");
        }
    }
}
