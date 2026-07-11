using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>The game's elements. Gadgets, enemy variants and world themes all key off these.</summary>
    public enum Element
    {
        Neutral,
        Water,
        Fire,
        Grass,
        Ice,
        Electric,
        Void,
    }

    /// <summary>
    /// The elemental recolour system (YT-50).
    ///
    /// The problem this exists to prevent: the game ships 13 worlds and elemental enemy/gadget
    /// variants. Hand-authoring a material per (thing x element) is a combinatorial mess that
    /// nobody wants to maintain, and it's exactly the kind of work that doesn't survive contact
    /// with an AI-generated asset pipeline. So a variant is not a new asset — it's a *function*
    /// of a base colour and an element.
    ///
    /// <see cref="Recolor"/> keeps the source colour's luminance and pushes its hue/saturation
    /// toward the element. That means a variant preserves the original's light-and-shade
    /// (a dark panel stays dark, a bright highlight stays bright) instead of flattening it into
    /// a single flat elemental colour — which is what makes recoloured variants read as the
    /// same object in a different element, rather than as a differently-painted blob.
    ///
    /// See docs/ELEMENTAL_VARIANTS.md for how to use it.
    /// </summary>
    public static class ElementPalette
    {
        /// <summary>The identity colour of each element (Art Bible biome/element palette).</summary>
        public static Color ColorOf(Element e)
        {
            switch (e)
            {
                case Element.Water: return new Color(0.31f, 0.76f, 0.97f);   // #4FC3F7
                case Element.Fire: return new Color(1f, 0.34f, 0.13f);       // #FF5722
                case Element.Grass: return new Color(0.49f, 0.70f, 0.26f);   // #7CB342
                case Element.Ice: return new Color(0.88f, 0.97f, 0.98f);     // #E0F7FA
                case Element.Electric: return new Color(0.96f, 0.79f, 0.22f);
                case Element.Void: return new Color(0.61f, 0.15f, 0.69f);    // #9C27B0
                default: return Color.white;
            }
        }

        /// <summary>
        /// Recolour <paramref name="source"/> toward <paramref name="element"/>.
        /// <paramref name="strength"/> 0 leaves it untouched, 1 fully adopts the element's hue.
        /// Luminance is preserved, so shading survives the recolour.
        /// </summary>
        public static Color Recolor(Color source, Element element, float strength = 0.85f)
        {
            if (element == Element.Neutral || strength <= 0f) return source;

            float lum = Luminance(source);
            Color target = ColorOf(element);
            float targetLum = Luminance(target);

            // Rescale the element's colour to the source's brightness before blending, so a dark
            // source doesn't get brightened just because its element happens to be a pale one.
            Color matched = targetLum > 0.001f ? target * (lum / targetLum) : target;
            Color blended = Color.Lerp(source, matched, Mathf.Clamp01(strength));

            return new Color(
                Mathf.Clamp01(blended.r),
                Mathf.Clamp01(blended.g),
                Mathf.Clamp01(blended.b),
                source.a);
        }

        /// <summary>Perceptual (Rec. 709) luminance — the value <see cref="Recolor"/> preserves.</summary>
        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
