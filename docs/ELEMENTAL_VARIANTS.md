# Elemental recolour variants — the pattern

**A variant is a function, not an asset.**

The game ships toward 13 worlds with elemental enemy, gadget and world-theme variants. Hand-authoring a material per (thing × element) is a combinatorial mess to maintain, and it is exactly the kind of work that does not survive an AI-generated asset pipeline — every time a model is regenerated, every hand-painted variant of it is invalidated.

So we never author a recolour. We compute one.

## Using it

```csharp
using MaxWorlds.Rendering;

// The stylised material for a surface in the current biome:
var ground = MaterialLibrary.Surface(SurfaceKind.Ground);

// The same surface, themed to an element:
var frozen = MaterialLibrary.Variant(SurfaceKind.Ground, Element.Ice);

// Or recolour any colour directly (enemy tints, HUD accents, VFX):
Color iceBot = ElementPalette.Recolor(baseColor, Element.Ice);
```

Variants are cached per `(kind, element)`, so asking for one repeatedly is a dictionary lookup, not an allocation.

## Why `Recolor` preserves luminance

`ElementPalette.Recolor` keeps the **luminance** of the source colour and pushes only its hue and saturation toward the element:

```csharp
Color Recolor(Color source, Element element, float strength = 0.85f)
```

The naive version — lerping the source toward the element's flat colour — flattens the object. A model's dark panels and bright highlights all collapse toward one elemental colour, and the result reads as a differently-painted blob rather than as the same object in a different element.

By rescaling the element's colour to the source's brightness *before* blending, shading survives: a dark panel stays dark, a highlight stays bright, and the silhouette still reads. That is what makes a Fire Bot look like the same robot as a Water Bot.

`strength` is the dial: `0` leaves the colour untouched, `1` fully adopts the element's hue. The default (`0.85`) leaves a trace of the original so materials don't go monochrome.

## Biome tint

`BiomePalette.Tint` is a single colour that multiplies every surface in the biome at once — the one knob for pushing a whole arena warmer, cooler or darker without touching any individual surface:

```csharp
var p = BiomePalette.Backyard;
p.Tint = new Color(0.9f, 0.95f, 1.1f);   // the whole Backyard, cooler
FindFirstObjectByType<WorldMaterials>().Apply(p);
```

Setting `MaterialLibrary.Palette` clears the material cache, so the next fetch rebuilds against the new palette.

## Adding an element

Add it to the `Element` enum and give it an identity colour in `ElementPalette.ColorOf`. Nothing else changes — every existing call site can now produce that variant.

## What this does not do

It recolours. It does not restyle: it will not add ice crystals to a robot or make a fire enemy glow. Those are mesh/VFX concerns (see `MaxWorlds.VFX`). This system is the cheap 80% that makes a roster of elemental variants viable at all, and it is deliberately parameter-driven so the expensive 20% can be added per-element later without re-authoring the base.
